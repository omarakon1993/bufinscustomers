using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Punto único de ejecución de consultas de IA. El controlador solo valida acceso temprano
    /// (<c>BaseController.ValidarAccesoIA</c>), arma los datos y llama a <see cref="EjecutarAsync"/>.
    /// El gateway:
    /// <list type="bullet">
    /// <item>vuelve a evaluar el acceso con la <b>estimación de tokens</b> de la consulta y las consultas
    /// en curso de la empresa (reserva), y aplica la política de la empresa al agotarse el presupuesto
    /// (bloquear / degradar a un modelo económico / sobreconsumo marcado),</item>
    /// <item>resuelve configuración, prompts, modelo (el fijo de la empresa si lo tiene) e idioma,</item>
    /// <item>garantiza que TODA respuesta se mida: <c>AuditoriaAnalisisIA</c> (con <c>TokensTotal</c>, base
    /// del presupuesto), <c>dbo.IAUsoLog</c> y <c>dbo.IAUsoMensual</c>.</item>
    /// </list>
    /// No lee recursos de idioma (los mensajes los traduce <see cref="IASolicitud.Traducir"/>). Los
    /// registros de uso nunca lanzan: ante un fallo se escribe en <see cref="AppLogger"/>.
    /// </summary>
    public class IAGateway : BaseService
    {
        private const string ModeloPorDefecto = "gpt-4o";
        private const string ModeloEconomicoPorDefecto = "gpt-4o-mini";
        private const int MaxTokensPorDefecto = 8000;
        private const int MaxTokensDegradado = 1200;
        private const int TokensSalidaEstimados = 1500;

        // Tokens estimados de las consultas en curso por empresa (aún no están en la auditoría): evita
        // que varias peticiones simultáneas pasen todas el control de presupuesto.
        private static readonly ConcurrentDictionary<int, long> _reservas = new ConcurrentDictionary<int, long>();

        public async Task<IAConsultaResponse> EjecutarAsync(IASolicitud s)
        {
            var reloj = Stopwatch.StartNew();

            // ── Configuración (en paralelo) ─────────────────────────────────────────────
            var cfg = new ConfiguracionSistemaService();
            var tApiKey = cfg.ObtenerValorAsync("OpenAIApiKey");
            var tModelo = cfg.ObtenerValorAsync("OpenAIModel");
            var tMaxTokens = cfg.ObtenerValorAsync("OpenAIMaxTokens");
            var tTemp = cfg.ObtenerValorAsync("OpenAITemperature");
            await Task.WhenAll(tApiKey, tModelo, tMaxTokens, tTemp);

            string apiKey = (tApiKey.Result ?? "").Trim();
            string modelo = string.IsNullOrWhiteSpace(tModelo.Result) ? ModeloPorDefecto : tModelo.Result.Trim();
            int maxTokens = (int.TryParse(tMaxTokens.Result, out int ptk) && ptk > 0) ? ptk : MaxTokensPorDefecto;
            double? temperatura = double.TryParse(tTemp.Result, NumberStyles.Float, CultureInfo.InvariantCulture, out double tv)
                ? (double?)tv : null;

            // El resumen inicial (sin pregunta) usa el tope propio de la función, acotado por el configurado.
            bool esResumen = string.IsNullOrWhiteSpace(s.Request.Pregunta);
            int maxTokensLlamada = (esResumen && s.MaxTokensResumen > 0) ? Math.Min(maxTokens, s.MaxTokensResumen) : maxTokens;

            // ── Control por empresa con la estimación de esta consulta ──────────────────
            long estimados = EstimarTokens(s.Request, maxTokensLlamada);
            _reservas.TryGetValue(s.IdEmpresa, out long reservados);
            var acceso = new IAUsoService().EvaluarAcceso(s.Usuario, s.Usuario?.Admin == 2, s.IdEmpresa, s.Funcion, estimados, reservados);

            if (!acceso.Permitido)
            {
                string clave = IAUsoService.ClaveMensaje(acceso.CodigoError);
                var denegada = new IAConsultaResponse
                {
                    Exitoso = false,
                    Error = s.Traducir != null ? s.Traducir(clave) : acceso.CodigoError
                };
                RegistrarUso(s, denegada, null, (int)reloj.ElapsedMilliseconds, acceso, acceso.CodigoError);
                return denegada;
            }

            // Modelo: el fijo de la empresa (si lo tiene); en modo "Degradar" uno económico y respuestas más cortas.
            if (!string.IsNullOrWhiteSpace(acceso.ModeloEmpresa)) modelo = acceso.ModeloEmpresa.Trim();
            else
            {
                // Enrutamiento por tipo de tarea: las de plantilla fija (resumen sin pregunta, cifras, insights
                // PYG/Balance) no necesitan el modelo grande. Opt-in: sin IA_ModeloSimple el modelo no cambia.
                string simple = (cfg.ObtenerValor("IA_ModeloSimple") ?? "").Trim();
                if (simple.Length > 0 && EsTareaSimple(s, esResumen)) modelo = simple;
            }
            if (acceso.Degradado)
            {
                string economico = cfg.ObtenerValor("IA_ModeloEconomico");
                modelo = string.IsNullOrWhiteSpace(economico) ? ModeloEconomicoPorDefecto : economico.Trim();
                maxTokensLlamada = Math.Min(maxTokensLlamada, MaxTokensDegradado);
            }

            // ── Prompts y conocimiento (GestorPrompts) ──────────────────────────────────
            var prompts = new GestorPromptsService();
            string instrucciones = string.IsNullOrWhiteSpace(s.CodigoPrompt) ? null : prompts.ObtenerPorCodigo(s.CodigoPrompt)?.TextoPrompt;
            if (string.IsNullOrWhiteSpace(instrucciones)) instrucciones = s.PromptPorDefecto;
            if (string.Equals(s.Idioma, "en", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.InstruccionEnIngles))
                instrucciones = (instrucciones ?? "") + Environment.NewLine + Environment.NewLine + s.InstruccionEnIngles;
            string guardrail = prompts.ObtenerPorCodigo("GUARDRAIL_SISTEMA")?.TextoPrompt;
            string contextoNegocio = prompts.ObtenerPorCodigo("CONTEXTO_NEGOCIO_BUFINS")?.TextoPrompt;
            // Contexto propio de la empresa (Configuración IA → Por Empresa): se suma al global.
            string contextoEmpresa = acceso.ContextoEmpresa;
            if (!string.IsNullOrWhiteSpace(contextoEmpresa))
            {
                // Rotulado aparte para que el modelo distinga la actividad propia de la empresa del contexto general de Bufins.
                string bloqueEmpresa = "Contexto de la empresa consultada (a qué se dedica y particularidades de su negocio):" +
                                       Environment.NewLine + contextoEmpresa.Trim();
                contextoNegocio = string.IsNullOrWhiteSpace(contextoNegocio)
                    ? bloqueEmpresa
                    : contextoNegocio + Environment.NewLine + Environment.NewLine + bloqueEmpresa;
            }

            // ── Llamada al modelo (con reserva de tokens mientras dura) ─────────────────
            _reservas.AddOrUpdate(s.IdEmpresa, estimados, (_, v) => v + estimados);
            IAConsultaResponse response;
            try
            {
                response = await new IAService(apiKey).ConsultarAsync(
                    s.Request, instrucciones, guardrail, modelo, maxTokensLlamada, temperatura, contextoNegocio, s.Idioma);
            }
            finally
            {
                _reservas.AddOrUpdate(s.IdEmpresa, 0, (_, v) => Math.Max(0, v - estimados));
            }
            reloj.Stop();

            if (response.CodigoError == "SIN_CREDITO" && s.Traducir != null)
                response.Error = s.Traducir("IA_ProveedorSinCreditoMensaje");

            response.FilasEnviadas = s.FilasAnalizadas;
            response.TotalFilas = s.TotalFilas > 0 ? s.TotalFilas : s.FilasAnalizadas;
            if (string.IsNullOrEmpty(response.Modelo)) response.Modelo = modelo;

            // Costo estimado (tarifas USD por 1k tokens en ConfiguracionSistema; 0 = no calcular).
            decimal? costo = CalcularCosto(cfg, response);
            if (costo.HasValue) response.CostoEstimadoUSD = costo.Value;

            // ── Medición: auditoría + uso ───────────────────────────────────────────────
            if (response.Exitoso)
                response.IdAuditoria = RegistrarAuditoria(s, response);

            RegistrarUso(s, response, costo, (int)reloj.ElapsedMilliseconds, acceso, null);

            return response;
        }

        /// <summary>
        /// true si la consulta es una tarea de plantilla fija que un modelo económico resuelve bien:
        /// insights de PYG/Balance y, en Análisis IA, el resumen gerencial y "solo cifras". Las preguntas
        /// libres, el análisis detallado y el de riesgo siguen con el modelo principal.
        /// </summary>
        private static bool EsTareaSimple(IASolicitud s, bool esResumen)
        {
            if (!esResumen) return false; // cualquier pregunta libre → modelo principal
            if (s.Funcion == IAFuncion.InsightsPYG || s.Funcion == IAFuncion.InsightsBalance) return true;
            return s.Funcion == IAFuncion.Chat
                && (s.CodigoPrompt == "RESUMEN_GERENCIAL" || s.CodigoPrompt == "SOLO_CIFRAS");
        }

        /// <summary>Estimación gruesa de los tokens de la consulta: caracteres/4 de lo que se envía + una salida típica.</summary>
        private static long EstimarTokens(IAConsultaRequest r, int maxTokensSalida)
        {
            long chars = (r?.DatosJson?.Length ?? 0) + (r?.Pregunta?.Length ?? 0) + 2500; // 2500 ≈ prompts/guardrail/contexto
            if (r?.Historial != null)
                foreach (var m in r.Historial) chars += m?.Contenido?.Length ?? 0;
            return chars / 4 + Math.Min(maxTokensSalida, TokensSalidaEstimados);
        }

        private static decimal? CalcularCosto(ConfiguracionSistemaService cfg, IAConsultaResponse r)
        {
            if (r.TokensTotal <= 0) return null;
            try
            {
                // Tarifa propia del modelo si existe ("IA_CostoPor1kTokensPrompt:gpt-4o-mini"); si no, la global.
                // Así el costo es correcto aunque el enrutamiento use dos modelos de precio distinto.
                string m = (r.Modelo ?? "").Trim();
                Func<string, string> valor = clave =>
                {
                    string v = m.Length > 0 ? cfg.ObtenerValor(clave + ":" + m) : null;
                    return string.IsNullOrWhiteSpace(v) ? cfg.ObtenerValor(clave) : v;
                };
                decimal.TryParse(valor("IA_CostoPor1kTokensPrompt"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal p);
                decimal.TryParse(valor("IA_CostoPor1kTokensRespuesta"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal rs);
                if (p <= 0 && rs <= 0) return null;
                // Los tokens de entrada servidos desde la caché de prompts se facturan con descuento
                // (IA_FactorCostoCache, def. 0.5 = gpt-4o/4o-mini; los gpt-5.x cobran ≈0.1; también por modelo).
                decimal factor = 0.5m;
                if (decimal.TryParse(valor("IA_FactorCostoCache"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal f)
                    && f >= 0m && f <= 1m)
                    factor = f;
                int cacheados = Math.Min(Math.Max(r.TokensCacheados, 0), r.TokensPrompt);
                decimal promptEquivalente = (r.TokensPrompt - cacheados) + cacheados * factor;
                return Math.Round(promptEquivalente / 1000m * p + r.TokensRespuesta / 1000m * rs, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "IAGateway.CalcularCosto");
                return null;
            }
        }

        /// <summary>Fila de AuditoriaAnalisisIA (visor + base del presupuesto mensual). Devuelve su Id o 0.</summary>
        private static int RegistrarAuditoria(IASolicitud s, IAConsultaResponse r)
        {
            try
            {
                var u = s.Usuario;
                return new AuditoriaAnalisisIAService().Registrar(new AuditoriaAnalisisIA
                {
                    IdUsuario = u.Id,
                    NombreUsuario = $"{u.Nombre} {u.Apellidos}".Trim(),
                    IdEmpresa = s.IdEmpresa,
                    NombreEmpresa = s.NombreEmpresa ?? "—",
                    NombreTabla = s.NombreTablaAuditoria,
                    Filtros = s.FiltrosDescripcion ?? "",
                    Pregunta = string.IsNullOrWhiteSpace(s.Request.Pregunta) ? null : s.Request.Pregunta,
                    Respuesta = r.Respuesta,
                    FechaPregunta = DateTime.Now,
                    FilasAnalizadas = s.FilasAnalizadas,
                    TokensTotal = r.TokensTotal
                });
            }
            catch (Exception ex)
            {
                // Sin esta fila el consumo no se descuenta del presupuesto mensual: no se silencia.
                AppLogger.Error(ex, $"IAGateway: AuditoriaAnalisisIA no registrada ({s.Funcion}, usuario {s.Usuario?.Id})");
                return 0;
            }
        }

        /// <summary>IAUsoLog (una fila por llamada, también las rechazadas) + IAUsoMensual (acumulado). Nunca lanza.</summary>
        private void RegistrarUso(IASolicitud s, IAConsultaResponse r, decimal? costo, int latenciaMs,
            IAUsoService.ResultadoAcceso acceso, string codigoRechazo)
        {
            try
            {
                string error = r.Exitoso ? null : (codigoRechazo ?? r.Error);
                if (error != null && error.Length > 300) error = error.Substring(0, 300);

                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    InsertarLog(cn, s, r, costo, latenciaMs, error, acceso);

                    if (!r.Exitoso) return; // el acumulado solo cuenta respuestas atendidas

                    using (var cmd = new SqlCommand(@"
                        MERGE dbo.IAUsoMensual WITH (HOLDLOCK) AS t
                        USING (SELECT @IdEmpresa AS IdEmpresa, @Periodo AS Periodo) AS src
                           ON t.IdEmpresa = src.IdEmpresa AND t.Periodo = src.Periodo
                        WHEN MATCHED THEN
                            UPDATE SET Tokens = t.Tokens + @Tokens, Consultas = t.Consultas + 1, CostoUSD = t.CostoUSD + @Costo
                        WHEN NOT MATCHED THEN
                            INSERT (IdEmpresa, Periodo, Tokens, Consultas, CostoUSD) VALUES (@IdEmpresa, @Periodo, @Tokens, 1, @Costo);", cn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", s.IdEmpresa);
                        cmd.Parameters.AddWithValue("@Periodo", DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                        cmd.Parameters.AddWithValue("@Tokens", (long)r.TokensTotal);
                        cmd.Parameters.AddWithValue("@Costo", costo ?? 0m);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                // Típico antes de ejecutar Sql/013: la consulta ya se atendió, solo falta la medición extendida.
                AppLogger.Warn($"IAGateway: IAUsoLog/IAUsoMensual no registrado ({ex.Message})");
            }
        }

        // Nivel de esquema detectado de IAUsoLog: 2 = con TokensCacheados (Sql/018), 1 = con Sobreconsumo/Degradado
        // (Sql/015), 0 = base. Baja solo si la BD aún no tiene las columnas, para no fallar un INSERT en cada llamada.
        private static int _nivelLog = 2;

        private static void InsertarLog(SqlConnection cn, IASolicitud s, IAConsultaResponse r, decimal? costo, int latenciaMs,
            string error, IAUsoService.ResultadoAcceso acceso)
        {
            const string baseCols = "IdEmpresa, IdUsuario, Funcion, Modelo, TokensPrompt, TokensRespuesta, CostoUSD, LatenciaMs, DesdeCache, Exitoso, Error, IdAuditoria";
            const string baseVals = "@IdEmpresa, @IdUsuario, @Funcion, @Modelo, @TP, @TR, @Costo, @Lat, @Cache, @Ok, @Error, @IdAud";

            Func<int, SqlCommand> crear = nivel =>
            {
                string cols = baseCols, vals = baseVals;
                if (nivel >= 1) { cols += ", Sobreconsumo, Degradado"; vals += ", @Sobre, @Degr"; }
                if (nivel >= 2) { cols += ", TokensCacheados"; vals += ", @TC"; }
                var cmd = new SqlCommand($"INSERT INTO dbo.IAUsoLog ({cols}) VALUES ({vals});", cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", s.IdEmpresa);
                cmd.Parameters.AddWithValue("@IdUsuario", s.Usuario?.Id ?? 0);
                cmd.Parameters.AddWithValue("@Funcion", s.Funcion ?? "");
                cmd.Parameters.AddWithValue("@Modelo", (object)r.Modelo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TP", r.TokensPrompt);
                cmd.Parameters.AddWithValue("@TR", r.TokensRespuesta);
                cmd.Parameters.AddWithValue("@Costo", (object)costo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Lat", latenciaMs);
                cmd.Parameters.AddWithValue("@Cache", r.DesdeCache);
                cmd.Parameters.AddWithValue("@Ok", r.Exitoso);
                cmd.Parameters.AddWithValue("@Error", (object)error ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdAud", r.IdAuditoria > 0 ? (object)r.IdAuditoria : DBNull.Value);
                if (nivel >= 1)
                {
                    cmd.Parameters.AddWithValue("@Sobre", acceso != null && acceso.Sobreconsumo && r.Exitoso);
                    cmd.Parameters.AddWithValue("@Degr", acceso != null && acceso.Degradado && r.Exitoso);
                }
                if (nivel >= 2)
                    cmd.Parameters.AddWithValue("@TC", r.TokensCacheados);
                return cmd;
            };

            for (int nivel = _nivelLog; ; nivel--)
            {
                try
                {
                    using (var cmd = crear(nivel)) cmd.ExecuteNonQuery();
                    return;
                }
                catch (SqlException ex) when (ex.Number == 207 && nivel > 0) // columna aún inexistente: bajar de nivel
                {
                    _nivelLog = nivel - 1;
                }
            }
        }
    }
}
