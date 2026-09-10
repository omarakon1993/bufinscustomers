using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class InformeTablasDatosController : BaseController
    {
        private InformeTablasDatosService _service = new InformeTablasDatosService();

        // ── Límites del historial de conversación que envía el cliente (S04) ──
        private const int MaxCharsHistorialJson    = 2_000_000; // ~2 MB de JSON crudo antes de parsear
        private const int MaxMensajesHistorial     = 13;        // 1 mensaje de contexto + 6 pares Q&A
        private const int MaxCharsMensajeHistorial = 60_000;    // por mensaje (salvo el de contexto)
        private const int MaxCharsHistorialTotal   = 500_000;   // suma de todos los mensajes

        /// <summary>
        /// Vista principal de consulta de informes de tablas de datos
        /// </summary>
        public ActionResult InformeTablasDatos()
        {
            // Obtener información del usuario actual
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            // Cargar tablas disponibles
            ViewBag.Tablas = _service.ObtenerTablasDisponibles();

            // Cargar empresas (solo para admin)
            if (esAdmin)
            {
                ViewBag.Empresas = _service.ObtenerEmpresas();
            }
            else
            {
                // Para usuarios no admin, mostrar su empresa y las de su mismo grupo empresarial
                var empresas = _service.ObtenerEmpresas();
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                ViewBag.Empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            return View("~/Views/Informes/InformeTablasDatos.cshtml");
        }

        /// <summary>
        /// Obtiene los años disponibles para una tabla específica
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerAnios(string nombreTabla, int? idEmpresa = null)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Si no es admin, permitir su empresa o una del mismo grupo empresarial
                if (!esAdmin)
                {
                    idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                        ? idEmpresa
                        : usuario?.IdEmpresa;
                }

                var anios = _service.ObtenerAñosDisponibles(nombreTabla, idEmpresa);

                return Json(new { success = true, anios }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener años: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Obtiene las variables disponibles para una tabla específica
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerVariables(string nombreTabla, int? idEmpresa = null)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Si no es admin, permitir su empresa o una del mismo grupo empresarial
                if (!esAdmin)
                {
                    idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                        ? idEmpresa
                        : usuario?.IdEmpresa;
                }

                var variables = _service.ObtenerVariablesDisponibles(nombreTabla, idEmpresa);

                return Json(new { success = true, variables }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener variables: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Consulta los datos con filtros aplicados
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarDatos(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin pueden ver su propia empresa o una de su mismo grupo empresarial
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                            return Json(new { success = false, message = "No tiene permisos para consultar datos de otra empresa" });
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Realizar consulta. El acceso a filtros.IdEmpresa ya se validó arriba (empresa
                // propia o de su mismo grupo empresarial); se consulta esa empresa, no la del usuario.
                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);

                // Mapear nombres de columnas a nombres amigables
                var columnasAmigables = resultado.Columnas.Select(c => new
                {
                    nombreTecnico = c,
                    nombreAmigable = _service.ObtenerNombreAmigableColumna(c)
                }).ToList();

                // Formatear los datos para la vista
                var datosFormateados = resultado.Filas.Select(fila =>
                {
                    var filaFormateada = new Dictionary<string, object>();
                    foreach (var kvp in fila)
                    {
                        object valorFormateado = kvp.Value;

                        // Formatear valores según el tipo
                        if (kvp.Value != null)
                        {
                            if (kvp.Value is decimal)
                            {
                                valorFormateado = ((decimal)kvp.Value).ToString("N2");
                            }
                            else if (kvp.Value is double)
                            {
                                valorFormateado = ((double)kvp.Value).ToString("N2");
                            }
                            else if (kvp.Value is DateTime)
                            {
                                // Formatear fechas de ejecución con hora
                                if (kvp.Key.Contains("Ejecucion") || kvp.Key.Contains("FechaEjecucion"))
                                {
                                    valorFormateado = ((DateTime)kvp.Value).ToString("dd/MM/yyyy HH:mm:ss");
                                }
                                else
                                {
                                    valorFormateado = ((DateTime)kvp.Value).ToString("dd/MM/yyyy");
                                }
                            }
                        }
                        else
                        {
                            valorFormateado = "";
                        }

                        filaFormateada[kvp.Key] = valorFormateado;
                    }
                    return filaFormateada;
                }).ToList();

                var jsonResult = Json(new
                {
                    success = true,
                    datos = datosFormateados,
                    columnas = columnasAmigables,
                    totalRegistros = resultado.TotalRegistros,
                    resultadosTruncados = resultado.ResultadosTruncados
                }, JsonRequestBehavior.AllowGet);

                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al consultar datos: {ex.Message}" });
            }
        }

        /// <summary>
        /// Consulta la IA con los datos actuales y una pregunta opcional
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        // historialJson trae el prompt/respuestas de turnos anteriores; puede contener texto con
        // < >, JSON, etc. que la validación de request de ASP.NET marcaría como "peligroso" (HTTP 500).
        // El endpoint solo reenvía ese texto a OpenAI, nunca lo devuelve como HTML.
        [ValidateInput(false)]
        public async Task<JsonResult> ConsultarConIA(FiltrosInformeTablasDatos filtros, string pregunta, string historialJson = null, string modo = null)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin pueden ver su propia empresa o una de su mismo grupo empresarial
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                            return Json(new IAConsultaResponse { Exitoso = false, Error = "No tiene permisos para consultar datos de otra empresa." });
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Año obligatorio para el análisis IA (reduce el volumen de datos)
                if (!filtros.Anio.HasValue)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "Debe seleccionar un Año para el análisis IA." });
                }

                // El acceso a filtros.IdEmpresa ya se validó arriba (empresa propia o de su mismo
                // grupo empresarial). Cada análisis es de UNA empresa: se filtra por la empresa
                // seleccionada, no por la del usuario, para que el filtro de empresa funcione en grupos.
                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = await _service.ConsultarDatosAsync(filtros, esAdmin, idEmpresaConsulta);

                if (resultado.TotalRegistros == 0)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "No hay datos para analizar con los filtros seleccionados." });
                }

                // Deserializar y SANEAR el historial que envía el cliente (no es de confianza):
                // se rechaza un blob desproporcionado antes de parsearlo y luego se acota nº de
                // turnos y tamaño para que un cliente manipulado no infle el coste de cada llamada.
                List<MensajeChatIA> historial = null;
                if (!string.IsNullOrWhiteSpace(historialJson))
                {
                    if (historialJson.Length > MaxCharsHistorialJson)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_HistorialGrandeMensaje") });

                    try { historial = JsonConvert.DeserializeObject<List<MensajeChatIA>>(historialJson); }
                    catch { historial = null; }

                    historial = SanearHistorial(historial);
                }

                // Quota diaria por usuario (super admin queda exento).
                // limiteDiario: valor de Usuarios.LimiteConsultasIA si está definido; si es NULL o
                // hay error de BD se usa el default global (IA_LimiteConsultasDefault, 20 si tampoco
                // está); un 0 explícito en la columna significa "sin acceso al Análisis IA".
                if (!esAdmin)
                {
                    int limiteDiario = ObtenerLimiteConsultasIA(usuario.Id);
                    if (limiteDiario <= 0)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_SinAccesoMensaje") });

                    int consultasHoy = new AuditoriaAnalisisIAService().ContarConsultasHoy(usuario.Id);
                    if (consultasHoy >= limiteDiario)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });
                }

                // Presupuesto mensual de tokens por empresa (S03). 0 = ilimitado. Al agotarse se
                // bloquea a los usuarios no-Super y se avisa a los Super Admin; al 80 % solo se avisa.
                long presupuestoTokens = ObtenerPresupuestoTokensMensual();
                if (presupuestoTokens > 0 && filtros.IdEmpresa.HasValue && filtros.IdEmpresa.Value > 0)
                {
                    long consumidosMes = new AuditoriaAnalisisIAService().SumarTokensMes(filtros.IdEmpresa.Value);
                    if (consumidosMes >= presupuestoTokens)
                    {
                        NotificarPresupuestoIA(filtros.IdEmpresa.Value, consumidosMes, presupuestoTokens, agotado: true);
                        if (!esAdmin)
                            return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_PresupuestoAgotadoMensaje") });
                    }
                    else if (consumidosMes >= (long)(presupuestoTokens * 0.8))
                    {
                        NotificarPresupuestoIA(filtros.IdEmpresa.Value, consumidosMes, presupuestoTokens, agotado: false);
                    }
                }

                // Leer configuración en paralelo
                var cfgSvc     = new ConfiguracionSistemaService();
                var tApiKey    = cfgSvc.ObtenerValorAsync("OpenAIApiKey");
                var tModelo    = cfgSvc.ObtenerValorAsync("OpenAIModel");
                var tMaxTokens = cfgSvc.ObtenerValorAsync("OpenAIMaxTokens");
                var tTemp      = cfgSvc.ObtenerValorAsync("OpenAITemperature");
                await Task.WhenAll(tApiKey, tModelo, tMaxTokens, tTemp);

                string apiKey   = (tApiKey.Result ?? "").Trim();
                string modeloIA = (tModelo.Result ?? "gpt-4o").Trim();
                int maxTokensIA = (int.TryParse(tMaxTokens.Result, out int ptk) && ptk > 0) ? ptk : 8000;
                // Si OpenAITemperature está vacío o no existe → null → no se envía al API
                double? temperatureIA = double.TryParse(
                    tTemp.Result,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double tVal) ? (double?)tVal : null;

                var iaService = new IAService(apiKey);

                var tablaAmigable = _service.ObtenerTablasDisponibles()
                    .FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;

                // Modo de análisis (N06): elige el prompt y el tope de tokens del resumen inicial.
                var cfgModo = ConfigModo(modo);
                var promptConfig = new GestorPromptsService().ObtenerPorCodigo(cfgModo.Codigo);
                string instrucciones = promptConfig?.TextoPrompt ?? cfgModo.Fallback;

                // Para el resumen inicial se usa el tope del modo (acotado por el configurado si es menor);
                // para las preguntas de seguimiento se respeta el máximo configurado.
                int maxTokensLlamada = string.IsNullOrWhiteSpace(pregunta)
                    ? (maxTokensIA > 0 ? Math.Min(maxTokensIA, cfgModo.MaxTokens) : cfgModo.MaxTokens)
                    : maxTokensIA;

                var guardrailConfig = new GestorPromptsService().ObtenerPorCodigo("GUARDRAIL_SISTEMA");
                string guardrail = guardrailConfig?.TextoPrompt;

                // Contexto de negocio de Bufins (Fase A): se aplica siempre, independiente del modo
                // o de si hay pregunta explícita.
                var contextoConfig = new GestorPromptsService().ObtenerPorCodigo("CONTEXTO_NEGOCIO_BUFINS");
                string contextoNegocio = contextoConfig?.TextoPrompt;

                // Datos en CSV compacto (≈ 40 % menos tokens que JSON) acotados por tamaño para no
                // exceder el contexto del modelo. Solo en la primera llamada (los turnos siguientes
                // reutilizan el contexto ya enviado).
                const int MaxCharsData = 200_000;
                string datosCsv = null;
                int filasEnviadas = resultado.TotalRegistros;

                if (historial == null || historial.Count == 0)
                    datosCsv = ConstruirCsv(resultado.Columnas, resultado.Filas, MaxCharsData, out filasEnviadas);

                var request = new IAConsultaRequest
                {
                    Pregunta = string.IsNullOrWhiteSpace(pregunta) ? null : pregunta.Trim(),
                    DatosJson = datosCsv,
                    FormatoDatos = "csv",
                    NombreTabla = tablaAmigable,
                    FiltrosDescripcion = ConstruirDescripcionFiltros(filtros),
                    Historial = historial
                };

                var response = await iaService.ConsultarAsync(request, instrucciones, guardrail, modeloIA, maxTokensLlamada, temperatureIA, contextoNegocio);
                response.FilasEnviadas = filasEnviadas;
                response.TotalFilas = resultado.TotalRegistros;

                // Coste estimado (N14): tarifas USD por 1k tokens desde ConfiguracionSistema (0 = no mostrar).
                var costos = ObtenerCostosIA();
                if ((costos.Item1 > 0 || costos.Item2 > 0) && response.TokensTotal > 0)
                    response.CostoEstimadoUSD = Math.Round(
                        response.TokensPrompt    / 1000m * costos.Item1 +
                        response.TokensRespuesta / 1000m * costos.Item2, 4);

                if (response.Exitoso)
                {
                    try
                    {
                        var empresa = _service.ObtenerEmpresas()
                            .FirstOrDefault(e => e.Id == filtros.IdEmpresa.GetValueOrDefault());
                        response.IdAuditoria = new AuditoriaAnalisisIAService().Registrar(new AuditoriaAnalisisIA
                        {
                            IdUsuario       = usuario.Id,
                            NombreUsuario   = $"{usuario.Nombre} {usuario.Apellidos}".Trim(),
                            IdEmpresa       = filtros.IdEmpresa ?? 0,
                            NombreEmpresa   = empresa?.Nombre ?? "—",
                            NombreTabla     = tablaAmigable,
                            Filtros         = ConstruirDescripcionFiltros(filtros),
                            Pregunta        = request.Pregunta,
                            Respuesta       = response.Respuesta,
                            FechaPregunta   = DateTime.Now,
                            FilasAnalizadas = response.FilasEnviadas,
                            TokensTotal     = response.TokensTotal
                        });
                    }
                    catch (Exception exAud)
                    {
                        // La cuota diaria se deriva de esta tabla: si el INSERT falla, la consulta
                        // se atendió pero no queda contabilizada. Se registra para poder detectarlo.
                        AppLogger.Error(exAud, $"AuditoriaAnalisisIA no registrada (usuario {usuario?.Id}) — la cuota puede quedar descontada de menos");
                    }
                }

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = $"Error al procesar la consulta: {ex.Message}" });
            }
        }

        /// <summary>Valoración 👍/👎 de una respuesta de IA (N02). Solo el autor puede valorarla.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ValorarRespuestaIA(int id, int? valor)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { ok = false });

            int? v = null;
            if (valor.HasValue && (valor.Value == 0 || valor.Value == 1)) v = valor.Value;

            bool ok = new AuditoriaAnalisisIAService().Valorar(id, usuario.Id, v);
            return Json(new { ok });
        }

        // ── Modos de análisis (N06) ────────────────────────────────────────
        // Cada modo elige un prompt de GestorPrompts (por código) y un tope de tokens para el
        // resumen inicial. Si el código no está en GestorPrompts se usa el texto de reserva.

        private const string FALLBACK_DETALLADO =
            "Elabora un análisis financiero detallado y estructurado (6 a 10 párrafos) con subtítulos. " +
            "Cubre desempeño por período, principales partidas, márgenes, variaciones relevantes, causas probables " +
            "y recomendaciones accionables. Usa cifras concretas de los datos.";
        private const string FALLBACK_CIFRAS =
            "Responde SOLO con cifras clave en viñetas y una tabla markdown cuando aplique: totales por período, " +
            "variaciones absolutas y porcentuales y los 5 valores más altos y más bajos. Sin narrativa, sin recomendaciones.";
        private const string FALLBACK_RIESGO =
            "Actúa como auditor: identifica señales de alerta y riesgos en los datos (caídas de ingresos, sobrecostos, " +
            "tensión de liquidez, partidas atípicas, inconsistencias). Devuelve una lista priorizada (alto/medio/bajo) " +
            "con la cifra que la sustenta y una acción sugerida para cada una.";

        private (string Codigo, int MaxTokens, string Fallback) ConfigModo(string modo)
        {
            switch ((modo ?? "").Trim().ToLowerInvariant())
            {
                case "detallado": return ("ANALISIS_DETALLADO", 4000, FALLBACK_DETALLADO);
                case "cifras":    return ("SOLO_CIFRAS",        1200, FALLBACK_CIFRAS);
                case "riesgo":    return ("ALERTAS_RIESGO",     1800, FALLBACK_RIESGO);
                default:          return ("RESUMEN_GERENCIAL",  1800, null); // null → IAService usa su default
            }
        }

        /// <summary>Tarifas USD por 1.000 tokens (prompt, respuesta) desde ConfiguracionSistema. 0 = no calcular coste.</summary>
        private (decimal, decimal) ObtenerCostosIA()
        {
            decimal p = 0m, s = 0m;
            try
            {
                var cfg = new ConfiguracionSistemaService();
                decimal.TryParse(cfg.ObtenerValor("IA_CostoPor1kTokensPrompt"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p);
                decimal.TryParse(cfg.ObtenerValor("IA_CostoPor1kTokensRespuesta"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
            }
            catch (Exception ex) { AppLogger.Error(ex, "InformeTablasDatosController.ObtenerCostosIA"); }
            return (p < 0 ? 0 : p, s < 0 ? 0 : s);
        }

        /// <summary>
        /// Serializa las filas a CSV compacto (separador ';', encabezados con nombres amigables,
        /// números con punto decimal invariante, fechas yyyy-MM-dd). Escribe filas hasta llegar a
        /// <paramref name="maxChars"/> y devuelve por <paramref name="filasEscritas"/> cuántas cupieron.
        /// </summary>
        private string ConstruirCsv(List<string> columnas, List<Dictionary<string, object>> filas,
            int maxChars, out int filasEscritas)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(string.Join(";", columnas.Select(c => CsvCampo(_service.ObtenerNombreAmigableColumna(c))))).Append('\n');

            int n = 0;
            foreach (var fila in filas)
            {
                var linea = string.Join(";", columnas.Select(c =>
                {
                    object v = (fila != null && fila.TryGetValue(c, out var val)) ? val : null;
                    string s;
                    if (v == null || v == DBNull.Value)                 s = "";
                    else if (v is DateTime dt)                          s = dt.ToString("yyyy-MM-dd");
                    else if (v is decimal || v is double || v is float) s = Convert.ToDecimal(v, inv).ToString(inv);
                    else if (v is int || v is long || v is short || v is byte) s = Convert.ToInt64(v).ToString(inv);
                    else                                               s = v.ToString();
                    return CsvCampo(s);
                }));
                if (sb.Length + linea.Length + 1 > maxChars) break;
                sb.Append(linea).Append('\n');
                n++;
            }
            filasEscritas = n;
            return sb.ToString();
        }

        private static string CsvCampo(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return (s.IndexOf(';') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }

        private string ConstruirDescripcionFiltros(FiltrosInformeTablasDatos filtros)
        {
            var partes = new List<string>();

            if (filtros.IdEmpresa.HasValue)
                partes.Add($"Empresa ID {filtros.IdEmpresa}");

            if (filtros.Anio.HasValue)
                partes.Add($"Año {filtros.Anio}");

            if (filtros.Mes.HasValue)
            {
                string[] meses = { "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
                                   "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
                int mes = filtros.Mes.Value;
                partes.Add($"Mes {(mes >= 1 && mes <= 12 ? meses[mes] : mes.ToString())}");
            }

            if (!string.IsNullOrWhiteSpace(filtros.Variable))
                partes.Add($"Variable '{filtros.Variable}'");

            return partes.Count > 0 ? string.Join(", ", partes) : "Sin filtros adicionales";
        }

        /// <summary>
        /// Exporta los datos consultados a Excel
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin solo pueden exportar su propia empresa
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                        {
                            TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                            return RedirectToAction("InformeTablasDatos");
                        }
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Obtener datos. El acceso a filtros.IdEmpresa ya se validó arriba (empresa propia
                // o de su mismo grupo empresarial); se exporta esa empresa, no la del usuario.
                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);

                if (resultado.TotalRegistros == 0)
                {
                    TempData["InfoMessage"] = "No hay datos para exportar con los filtros seleccionados";
                    return RedirectToAction("InformeTablasDatos");
                }

                // Crear archivo Excel
                using (var package = new XLWorkbook())
                {
                    // Obtener nombre amigable de la tabla
                    var tablas = _service.ObtenerTablasDisponibles();
                    var tablaSeleccionada = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
                    string nombreHoja = tablaSeleccionada?.NombreAmigable ?? filtros.NombreTabla;

                    // Limitar el nombre de la hoja a 31 caracteres (límite de Excel)
                    if (nombreHoja.Length > 31)
                        nombreHoja = nombreHoja.Substring(0, 31);

                    var worksheet = package.Worksheets.Add(nombreHoja);

                    // Agregar encabezados con nombres amigables
                    int col = 1;
                    foreach (var nombreColumna in resultado.Columnas)
                    {
                        var cell = worksheet.Cell(1, col);
                        cell.Value = _service.ObtenerNombreAmigableColumna(nombreColumna);

                        // Estilo del encabezado
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241); // Color del tema
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        col++;
                    }

                    // Agregar datos
                    int fila = 2;
                    foreach (var registro in resultado.Filas)
                    {
                        col = 1;
                        foreach (var nombreColumna in resultado.Columnas)
                        {
                            var cell = worksheet.Cell(fila, col);
                            var valor = registro.ContainsKey(nombreColumna) ? registro[nombreColumna] : null;

                            if (valor != null)
                            {
                                // Asignar valor según tipo
                                if (valor is DateTime)
                                {
                                    cell.Value = (DateTime)valor;
                                    cell.Style.NumberFormat.Format = "dd/mm/yyyy";
                                }
                                else if (valor is decimal || valor is double || valor is float)
                                {
                                    ExcelCellHelper.SetValue(cell, valor);
                                    cell.Style.NumberFormat.Format = "#,##0.00";
                                }
                                else if (valor is int || valor is long)
                                {
                                    ExcelCellHelper.SetValue(cell, valor);
                                    cell.Style.NumberFormat.Format = "#,##0";
                                }
                                else
                                {
                                    cell.Value = valor.ToString();
                                }
                            }

                            // Estilo de la celda
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);

                            col++;
                        }
                        fila++;
                    }

                    // Autoajustar columnas
                    worksheet.Columns().AdjustToContents();

                    // Agregar filtros automáticos
                    worksheet.Range(1, 1, 1, resultado.Columnas.Count).SetAutoFilter();

                    // Congelar primera fila
                    worksheet.SheetView.Freeze(1, 0);

                    // Generar nombre de archivo
                    string nombreArchivo = $"Informe_{nombreHoja}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    // Retornar archivo
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("InformeTablasDatos");
            }
        }

        /// <summary>
        /// Exporta datos + análisis de IA en un Excel con dos hojas
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcelConIA(FiltrosInformeTablasDatos filtros, string textoAnalisis)
        {
            try
            {
                var usuario          = UsuarioSesionHelper.UsuarioActual;
                var esAdmin          = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                        {
                            TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                            return RedirectToAction("InformeTablasDatos");
                        }
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Cada exportación es de UNA empresa: la seleccionada (ya validada arriba), no la del usuario.
                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);
                var tablas    = _service.ObtenerTablasDisponibles();
                string nombreTabla = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;
                string hojaNombre  = nombreTabla.Length > 31 ? nombreTabla.Substring(0, 31) : nombreTabla;

                using (var package = new XLWorkbook())
                {
                    // ── Hoja 1: Datos ──────────────────────────────────────────
                    var wsData = package.Worksheets.Add(hojaNombre);
                    int col = 1;
                    foreach (var c in resultado.Columnas)
                    {
                        var cell = wsData.Cell(1, col);
                        cell.Value = _service.ObtenerNombreAmigableColumna(c);
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        col++;
                    }
                    int fila = 2;
                    foreach (var registro in resultado.Filas)
                    {
                        col = 1;
                        foreach (var c in resultado.Columnas)
                        {
                            var cell  = wsData.Cell(fila, col);
                            var valor = registro.ContainsKey(c) ? registro[c] : null;
                            if (valor != null)
                            {
                                if (valor is decimal || valor is double || valor is float)
                                { ExcelCellHelper.SetValue(cell, valor); cell.Style.NumberFormat.Format = "#,##0.00"; }
                                else if (valor is int || valor is long)
                                { ExcelCellHelper.SetValue(cell, valor); cell.Style.NumberFormat.Format = "#,##0"; }
                                else if (valor is DateTime)
                                { cell.Value = (DateTime)valor; cell.Style.NumberFormat.Format = "dd/mm/yyyy"; }
                                else cell.Value = valor.ToString();
                            }
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
                            col++;
                        }
                        fila++;
                    }
                    wsData.Columns().AdjustToContents();
                    wsData.Range(1, 1, 1, resultado.Columnas.Count).SetAutoFilter();
                    wsData.SheetView.Freeze(1, 0);

                    // ── Hoja 2: Análisis IA ────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(textoAnalisis))
                    {
                        string hojaNombreIA = R("IA_Excel_HojaAnalisis") ?? "Análisis IA";
                        var wsIA = package.Worksheets.Add(hojaNombreIA);

                        wsIA.Cell(1, 1).Value = R("IA_PDF_Title") ?? "Análisis Gerencial IA";
                        wsIA.Cell(1, 1).Style.Font.Bold = true;
                        wsIA.Cell(1, 1).Style.Font.FontSize = 14;
                        wsIA.Cell(1, 1).Style.Font.FontColor = XLColor.FromArgb(79, 70, 229);

                        wsIA.Cell(2, 1).Value = ConstruirDescripcionFiltros(filtros);
                        wsIA.Cell(2, 1).Style.Font.Italic = true;
                        wsIA.Cell(2, 1).Style.Font.FontColor = XLColor.FromColor(Color.Gray);

                        wsIA.Cell(3, 1).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                        wsIA.Cell(3, 1).Style.Font.Italic = true;
                        wsIA.Cell(3, 1).Style.Font.FontSize = 9;
                        wsIA.Cell(3, 1).Style.Font.FontColor = XLColor.FromColor(Color.Gray);

                        string textoLimpio = LimpiarMarkdown(textoAnalisis);
                        var parrafos = textoLimpio.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        int filaIA = 5;
                        foreach (var parrafo in parrafos)
                        {
                            string linea = parrafo.Trim();
                            if (string.IsNullOrWhiteSpace(linea)) continue;
                            wsIA.Cell(filaIA, 1).Value      = linea;
                            wsIA.Cell(filaIA, 1).Style.Alignment.WrapText = true;
                            if (linea.StartsWith("•") == false && linea.Length < 100 && !linea.Contains(".") && !linea.Contains(","))
                                wsIA.Cell(filaIA, 1).Style.Font.Bold = true;
                            filaIA++;
                        }
                        wsIA.Column(1).Width = 110;
                    }

                    string archivo = $"AnalisisIA_{hojaNombre}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar: {ex.Message}";
                return RedirectToAction("InformeTablasDatos");
            }
        }

        /// <summary>
        /// Límite diario de consultas IA del usuario. Si <c>Usuarios.LimiteConsultasIA</c> tiene un
        /// valor se devuelve tal cual (incluido <c>0</c> = sin acceso). Si es <c>NULL</c> —o hay un
        /// error de BD— se cae al default global <c>IA_LimiteConsultasDefault</c> de
        /// <c>ConfiguracionSistema</c> (20 si tampoco está configurado).
        /// </summary>
        private int ObtenerLimiteConsultasIA(int idUsuario)
        {
            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(CadenaConexion))
                {
                    var cmd = new System.Data.SqlClient.SqlCommand(
                        "SELECT LimiteConsultasIA FROM Usuarios WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        return Convert.ToInt32(result);   // valor explícito (incl. 0 = sin acceso)
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTablasDatosController.ObtenerLimiteConsultasIA");
            }
            return ObtenerLimiteConsultasIADefault();
        }

        /// <summary>Límite diario por defecto cuando el usuario no tiene uno propio configurado.</summary>
        private int ObtenerLimiteConsultasIADefault()
        {
            try
            {
                var v = new ConfiguracionSistemaService().ObtenerValor("IA_LimiteConsultasDefault");
                if (int.TryParse(v, out int d) && d >= 0)
                    return d;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTablasDatosController.ObtenerLimiteConsultasIADefault");
            }
            return 20;
        }

        /// <summary>Presupuesto mensual de tokens IA por empresa (clave <c>IA_TokensMensualesPorEmpresa</c>). 0 = ilimitado.</summary>
        private long ObtenerPresupuestoTokensMensual()
        {
            try
            {
                var v = new ConfiguracionSistemaService().ObtenerValor("IA_TokensMensualesPorEmpresa");
                if (long.TryParse(v, out long t) && t >= 0) return t;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTablasDatosController.ObtenerPresupuestoTokensMensual");
            }
            return 0;
        }

        /// <summary>Avisa a los Super Admin del consumo de tokens de una empresa. Throttle: ≤ 1 aviso
        /// por empresa y tipo (aviso/agotado) cada 24 h, vía <c>HttpRuntime.Cache</c>.</summary>
        private void NotificarPresupuestoIA(int idEmpresa, long consumidos, long presupuesto, bool agotado)
        {
            string ck = "ia_presup_" + idEmpresa + "_" + (agotado ? "full" : "warn");
            if (System.Web.HttpRuntime.Cache[ck] != null) return;
            System.Web.HttpRuntime.Cache.Insert(ck, 1, null, DateTime.Now.AddHours(24),
                System.Web.Caching.Cache.NoSlidingExpiration);

            try
            {
                string nombreEmpresa = _service.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? ("#" + idEmpresa);
                int pct = presupuesto > 0 ? (int)Math.Min(100, consumidos * 100 / presupuesto) : 0;
                string titulo = R(agotado ? "Notif_IAPresupuestoAgotadoTitulo" : "Notif_IAPresupuestoAvisoTitulo");
                string msg = string.Format(R(agotado ? "Notif_IAPresupuestoAgotadoMsg" : "Notif_IAPresupuestoAvisoMsg"),
                    nombreEmpresa, consumidos.ToString("N0"), presupuesto.ToString("N0"), pct);

                var notif = new NotificacionesService();
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    notif.Crear(idAdmin, titulo, msg, agotado ? "error" : "warning", "/AuditoriaHub");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTablasDatosController.NotificarPresupuestoIA");
            }
        }

        private List<int> ObtenerIdsSuperAdmin()
        {
            var ids = new List<int>();
            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(CadenaConexion))
                using (var cmd = new System.Data.SqlClient.SqlCommand("SELECT Id FROM Usuarios WHERE Admin = 2", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["Id"]));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTablasDatosController.ObtenerIdsSuperAdmin");
            }
            return ids;
        }

        /// <summary>
        /// Acota el historial que llega del cliente (S04): descarta mensajes con forma inválida,
        /// limita el tamaño de cada mensaje (salvo el primero, que es el contexto que generó el
        /// servidor), conserva solo el contexto + los últimos turnos, y aplica un tope de tamaño
        /// total soltando los mensajes intermedios más antiguos.
        /// </summary>
        private static List<MensajeChatIA> SanearHistorial(List<MensajeChatIA> historial)
        {
            if (historial == null || historial.Count == 0) return null;

            var limpio = historial
                .Where(m => m != null
                         && !string.IsNullOrEmpty(m.Contenido)
                         && (m.Rol == "user" || m.Rol == "assistant" || m.Rol == "system"))
                .ToList();
            if (limpio.Count == 0) return null;

            for (int i = 1; i < limpio.Count; i++)
                if (limpio[i].Contenido.Length > MaxCharsMensajeHistorial)
                    limpio[i].Contenido = limpio[i].Contenido.Substring(0, MaxCharsMensajeHistorial);

            if (limpio.Count > MaxMensajesHistorial)
            {
                var recortado = new List<MensajeChatIA> { limpio[0] };
                recortado.AddRange(limpio.Skip(limpio.Count - (MaxMensajesHistorial - 1)));
                limpio = recortado;
            }

            while (limpio.Count > 2 && limpio.Sum(m => (long)m.Contenido.Length) > MaxCharsHistorialTotal)
                limpio.RemoveAt(1);

            return limpio;
        }

        private string LimpiarMarkdown(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return texto;
            return texto
                .Replace("**", string.Empty)
                .Replace("*",  string.Empty)
                .Replace("### ", string.Empty)
                .Replace("## ",  string.Empty)
                .Replace("# ",   string.Empty)
                .Replace("- ",   "• ");
        }
    }
}
