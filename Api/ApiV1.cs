using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace bufinscustomers.Api
{
    /// <summary>
    /// API de ingesta de BUFINS (v1). Son endpoints mínimos aparte de MVC: no pasan por los filtros de sesión, 2FA ni antiforgery.
    /// Autenticación propia por clave de integración (<c>Authorization: Bearer bfk_…</c>); cada integración opera SOLO en su empresa
    /// y sobre sus propios lotes. Toda la lógica de cargue es la de <see cref="CargueOrquestadorService"/> (la misma de la pantalla de Excel).
    /// </summary>
    public static class ApiV1
    {
        private const string ItemIntegracion = "ApiIntegracion";
        private const string ItemUsuarioTecnico = "ApiUsuarioTecnico";

        public static void Mapear(WebApplication app)
        {
            var g = app.MapGroup("/api/v1");

            g.MapGet("/ping", (HttpContext ctx) =>
            {
                var i = Integ(ctx);
                return Ok(new { estado = "ok", version = "1", integracion = i.Nombre, idEmpresa = i.IdEmpresa, empresa = i.NombreEmpresa, permiteConfirmar = i.PermiteConfirmar });
            });

            g.MapGet("/esquemas", (HttpContext ctx) => Ok(new { hojas = ApiEsquemas.Obtener(Integ(ctx).IdEmpresa) }));

            g.MapPost("/cargues/archivo", SubirArchivo);

            g.MapGet("/cargues/{idLote:long}", (HttpContext ctx, long idLote) =>
            {
                var lote = LotePropio(ctx, idLote, out var error);
                if (lote == null) return error;
                ImpactoCargue impacto = null;
                try { impacto = new CargueImpactoService().Analizar(lote); } catch (Exception ex) { AppLogger.Warn("API impacto: " + ex.Message); }
                return Ok(new { lote = LoteDto(lote), impacto = ImpactoDto(impacto) });
            });

            g.MapGet("/cargues/{idLote:long}/hallazgos", (HttpContext ctx, long idLote, int? max, string idioma) =>
            {
                var lote = LotePropio(ctx, idLote, out var error);
                if (lote == null) return error;
                bool en = string.Equals(idioma, "en", StringComparison.OrdinalIgnoreCase);
                int tope = Math.Max(1, Math.Min(max ?? 200, 1000));
                var svc = new CargueStagingService();
                var items = svc.ObtenerHallazgos(idLote, tope).Select(h => new
                {
                    hoja = h.NombreHoja, tabla = h.NombreTabla, fila = h.NumeroFilaExcel, columna = h.Columna,
                    severidad = h.Severidad, regla = h.CodigoRegla,
                    titulo = en ? (h.TituloEn ?? h.Titulo) : h.Titulo,
                    mensaje = en ? (h.MensajeEn ?? h.Mensaje) : h.Mensaje,
                    periodo = h.Periodo, referencia = h.Referencia,
                    detalle = en ? (h.DetalleEn ?? h.Detalle) : h.Detalle,
                    diferencia = h.Diferencia,
                    sugerencia = en ? (h.SugerenciaEn ?? h.Sugerencia) : h.Sugerencia
                }).ToList();
                return Ok(new { idLote, totalesPorRegla = svc.ObtenerTotalesPorRegla(idLote), maxPorRegla = tope, hallazgos = items });
            });

            g.MapPost("/cargues/{idLote:long}/confirmar", (HttpContext ctx, long idLote) =>
            {
                var integ = Integ(ctx);
                if (!integ.PermiteConfirmar)
                    return Error(403, "confirmacion_humana", "Esta integración no puede confirmar cargues: la confirmación la hace una persona en «Revisar cargue».");
                var lote = LotePropio(ctx, idLote, out var error);
                if (lote == null) return error;
                return ResultadoConfirmacion(ctx, new CargueOrquestadorService(Traducir).Confirmar(Tecnico(ctx), idLote, confirmarReemplazo: false));
            });

            g.MapDelete("/cargues/{idLote:long}", (HttpContext ctx, long idLote) =>
            {
                var lote = LotePropio(ctx, idLote, out var error);
                if (lote == null) return error;
                if (lote.Estado == CargueLoteEstado.Confirmado)
                    return Error(409, "ya_confirmado", "El cargue ya fue confirmado: no se puede descartar. Un administrador puede deshacerlo desde el historial de versiones.");
                return new CargueOrquestadorService(Traducir).DescartarCargue(Tecnico(ctx), idLote)
                    ? Ok(new { idLote, estado = CargueLoteEstado.Descartado })
                    : Error(500, "error_interno", "No se pudo descartar el cargue.");
            });
        }

        // ── POST /cargues/archivo ───────────────────────────────────────────────────────

        private static async Task<IResult> SubirArchivo(HttpContext ctx)
        {
            var integ = Integ(ctx);
            var tecnico = Tecnico(ctx);

            if (!ctx.Request.HasFormContentType)
                return Error(400, "formato_invalido", "Envíe multipart/form-data con los campos: archivo, anio, modo (ejecucion|historico) y, opcional, idEscenario y confirmar.");

            var form = await ctx.Request.ReadFormAsync();
            var archivo = form.Files.GetFile("archivo") ?? form.Files.FirstOrDefault();
            if (archivo == null || archivo.Length == 0)
                return Error(400, "archivo_requerido", "Falta el archivo Excel (campo «archivo»).");
            if (!int.TryParse(form["anio"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int anio))
                return Error(400, "anio_invalido", "El campo «anio» es obligatorio y debe ser un número.");
            string modo = (form["modo"].ToString() ?? "").Trim().ToLowerInvariant();
            int idEscenario = 1;
            if (!string.IsNullOrWhiteSpace(form["idEscenario"]) && !int.TryParse(form["idEscenario"], NumberStyles.Integer, CultureInfo.InvariantCulture, out idEscenario))
                return Error(400, "escenario_invalido", "El campo «idEscenario» debe ser un número.");
            bool pideConfirmar = string.Equals(form["confirmar"].ToString(), "true", StringComparison.OrdinalIgnoreCase);

            string claveIdem = ctx.Request.Headers["Idempotency-Key"].ToString().Trim();
            if (claveIdem.Length > 100 || claveIdem.Any(c => char.IsControl(c)))
                return Error(400, "idempotency_key_invalida", "Idempotency-Key admite hasta 100 caracteres sin saltos de línea.");

            var integraciones = new IntegracionesService();
            var staging = new CargueStagingService();

            // Reenvío con la misma clave: no se vuelve a procesar, se devuelve el estado del lote ya creado.
            if (claveIdem.Length > 0)
            {
                var previo = integraciones.BuscarIdempotencia(integ.Id, claveIdem);
                if (previo.HasValue)
                {
                    var l = staging.ObtenerLote(previo.Value);
                    if (l != null) return Ok(new { repetido = true, lote = LoteDto(l) });
                }
            }

            var orq = new CargueOrquestadorService(Traducir);
            var solicitud = new SolicitudCargue
            {
                Usuario = tecnico, IdEmpresa = integ.IdEmpresa, Anio = anio, Modo = modo, IdEscenario = idEscenario,
                NombreArchivo = archivo.FileName, LongitudArchivo = archivo.Length, Archivo = archivo.OpenReadStream()
            };

            var validacion = orq.ValidarSolicitud(solicitud);
            if (!validacion.Ok) return Error(422, "solicitud_invalida", validacion.Mensaje);

            var proceso = orq.ProcesarArchivo(solicitud, idLote =>
            {
                try { integraciones.MarcarLoteApi(idLote, integ.Id); }
                catch (Exception ex) { AppLogger.Error(ex, "API.MarcarLoteApi"); }
            });

            switch (proceso.Estado)
            {
                case ProcesoCargueEstado.LoteActivoExistente:
                    return Results.Json(new { error = new { codigo = "lote_activo", mensaje = proceso.Mensaje, idLote = proceso.IdLote } }, statusCode: 409);

                case ProcesoCargueEstado.PlantillaIncompleta:
                case ProcesoCargueEstado.SinFilasDelAnio:
                    return Error(422, proceso.Estado == ProcesoCargueEstado.PlantillaIncompleta ? "plantilla_incompleta" : "sin_filas_del_anio", proceso.Mensaje);

                case ProcesoCargueEstado.ErrorEnHoja:
                    return Results.Json(new
                    {
                        error = new { codigo = "error_en_hoja", mensaje = proceso.Mensaje },
                        hojas = proceso.Resultado?.DetalleHojas?.Select(h => new { hoja = h.NombreHoja, estado = h.Estado, mensaje = h.MensajeError }).ToList()
                    }, statusCode: 422);

                case ProcesoCargueEstado.ErrorValidacion:
                    AppLogger.Error("API: sp_ValidarCargueStaging falló: " + proceso.Mensaje, contexto: "ApiV1.SubirArchivo");
                    return Error(500, "error_interno", "No se pudo validar el cargue. Inténtelo de nuevo o contacte a soporte.");

                case ProcesoCargueEstado.ErrorInesperado:
                    // El detalle técnico ya quedó en el log; al cliente no se le devuelve (puede incluir nombres internos).
                    return Error(422, "archivo_no_procesable", "No se pudo procesar el archivo. Verifique que sea una plantilla BUFINS válida (.xlsx) o contacte a soporte.");
            }

            // EnRevision
            if (claveIdem.Length > 0) integraciones.GuardarIdempotencia(integ.Id, claveIdem, proceso.IdLote);
            var lote = staging.ObtenerLote(proceso.IdLote);

            new AuditoriaService().RegistrarCambio(
                AuditoriaTipo.Cargues, AuditoriaAccion.Crear, "CargueLote", proceso.IdLote.ToString(),
                $"La integración '{integ.Nombre}' envió por la API el archivo '{proceso.NombreArchivo}' a '{integ.NombreEmpresa}' — año {anio}, {(modo == "ejecucion" ? "ejecución" : "histórico")}, escenario {idEscenario}: {lote?.TotalFilas:N0} fila(s), {lote?.TotalErrores} error(es), {lote?.TotalAdvertencias} advertencia(s). Queda en revisión.",
                null, new { integracion = integ.Nombre, idIntegracion = integ.Id, archivo = proceso.NombreArchivo, anio, modo, escenario = idEscenario, estado = lote?.Estado },
                integ.IdEmpresa, entidadNombre: integ.NombreEmpresa);

            // Confirmación automática: solo si la integración lo permite, el cliente la pidió y el cargue quedó limpio (sin advertencias).
            string confirmacion = pideConfirmar ? "no_permitida" : "no_solicitada";
            object confirmado = null;
            if (pideConfirmar && integ.PermiteConfirmar)
            {
                if (lote != null && lote.Estado == CargueLoteEstado.ValidadoOk)
                {
                    var c = orq.Confirmar(tecnico, proceso.IdLote, confirmarReemplazo: false);
                    confirmacion = c.Confirmado ? "confirmado" : "pendiente_de_revision";
                    if (c.Confirmado) confirmado = new { mensaje = c.Mensaje, filas = c.Lote?.TotalFilas };
                    lote = staging.ObtenerLote(proceso.IdLote) ?? lote;
                }
                else confirmacion = "pendiente_de_revision";
            }

            var cuerpo = new { repetido = false, confirmacion, confirmado, lote = LoteDto(lote), urlRevision = lote == null ? null : $"/Datos/RevisarCargue?idLote={lote.IdLote}" };
            return Results.Json(cuerpo, statusCode: 201);
        }

        private static IResult ResultadoConfirmacion(HttpContext ctx, ResultadoConfirmacionCargue r)
        {
            switch (r.Estado)
            {
                case ConfirmacionCargueEstado.Confirmado:
                    return Ok(new
                    {
                        estado = CargueLoteEstado.Confirmado, mensaje = r.Mensaje, filas = r.Lote?.TotalFilas,
                        hojas = r.Resultado?.DetalleHojas?.Select(h => new { hoja = h.NombreHoja, tabla = h.NombreTabla, filas = h.FilasInsertadas }).ToList()
                    });
                case ConfirmacionCargueEstado.LoteNoExiste: return Error(404, "lote_no_encontrado", "El cargue no existe.");
                case ConfirmacionCargueEstado.SinPermisoPlantilla: return Error(403, "sin_permiso", r.Mensaje);
                case ConfirmacionCargueEstado.NoConfirmable: return Error(409, "no_confirmable", r.Mensaje);
                case ConfirmacionCargueEstado.Bloqueado: return Error(409, "bloqueado_regla_de_oro", r.Mensaje);
                case ConfirmacionCargueEstado.FaltaConfirmarReemplazo: return Error(409, "requiere_revision_humana", r.Mensaje);
                case ConfirmacionCargueEstado.CargaVacia: return Error(422, "sin_filas_del_anio", r.Mensaje);
                case ConfirmacionCargueEstado.LoteNoDisponible: return Error(409, "lote_no_disponible", r.Mensaje);
                default:
                    AppLogger.Error("API: fallo al confirmar: " + r.Mensaje, contexto: "ApiV1.Confirmar");
                    return Error(500, "error_interno", "No se pudo confirmar el cargue. Inténtelo de nuevo o contacte a soporte.");
            }
        }

        // ── Utilidades ─────────────────────────────────────────────────────────────────

        public static Integracion Integ(HttpContext ctx) => (Integracion)ctx.Items[ItemIntegracion];
        public static Usuarios Tecnico(HttpContext ctx) => (Usuarios)ctx.Items[ItemUsuarioTecnico];

        /// <summary>Lote visible para esta integración: solo los que ella misma creó. Si no, 404 (no se revela que existe).</summary>
        private static CargueLote LotePropio(HttpContext ctx, long idLote, out IResult error)
        {
            error = null;
            var integ = Integ(ctx);
            var lote = new CargueStagingService().ObtenerLote(idLote);
            if (lote == null || lote.IdEmpresa != integ.IdEmpresa || !new IntegracionesService().LoteEsDeIntegracion(idLote, integ.Id))
            {
                error = Error(404, "lote_no_encontrado", "El cargue no existe.");
                return null;
            }
            return lote;
        }

        private static object LoteDto(CargueLote l) => l == null ? null : new
        {
            idLote = l.IdLote, estado = l.Estado, anio = l.Anio, modo = l.Modo == 0 ? "ejecucion" : "historico", idEscenario = l.IdEscenario,
            archivo = l.NombreArchivo, totalFilas = l.TotalFilas, totalErrores = l.TotalErrores, totalAdvertencias = l.TotalAdvertencias,
            puedeConfirmar = l.PuedeConfirmar, fechaCreacion = l.FechaCreacion, fechaValidacion = l.FechaValidacion, fechaConfirmacion = l.FechaConfirmacion
        };

        private static object ImpactoDto(ImpactoCargue i) => i == null ? null : new
        {
            filasExistentes = i.TotalExistentes, filasNuevas = i.TotalNuevas, requiereRevisionHumana = i.RequiereConfirmacion, bloqueado = i.Bloqueado,
            tablas = i.Tablas.Select(t => new { tabla = t.Tabla, existentes = t.Existentes, nuevas = t.Nuevas }).ToList()
        };

        /// <summary>Idioma de los mensajes: español salvo que se pida inglés con Accept-Language o ?idioma=en.</summary>
        private static string Traducir(string clave) => Recursos.Obtener(clave);

        public static IResult Ok(object cuerpo) => Results.Json(cuerpo, statusCode: 200);

        public static IResult Error(int status, string codigo, string mensaje) =>
            Results.Json(new { error = new { codigo, mensaje } }, statusCode: status);
    }

    /// <summary>Hojas y columnas que acepta la API para una empresa (las mismas del catálogo de hojas de cargue).</summary>
    public static class ApiEsquemas
    {
        private static readonly HashSet<string> ColumnasDeSistema = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Llave", "IdEmpresa_Log", "IdEscenario", "IdUsuarioCargue_Log", "FechaCargue_Log", "Historico_Log",
            "IdUsuarioEjecucion_Log", "FechaEjecucion_Log", "Observacion_Log", "IdLote", "NumeroFilaExcel"
        };

        public static List<object> Obtener(int idEmpresa)
        {
            var hojas = new List<object>();
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            {
                cn.Open();
                foreach (var par in TablasCargueHelper.MapeoParaEmpresa(idEmpresa))
                {
                    var columnas = new List<object>();
                    using (var cmd = new SqlCommand(@"
                        SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE,
                               COLUMNPROPERTY(OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME)), COLUMN_NAME, 'IsIdentity') AS EsIdentidad
                        FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION", cn))
                    {
                        cmd.Parameters.AddWithValue("@T", par.Value);
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                string nombre = r["COLUMN_NAME"].ToString();
                                if (ColumnasDeSistema.Contains(nombre) || Convert.ToInt32(r["EsIdentidad"] == DBNull.Value ? 0 : r["EsIdentidad"]) == 1) continue;
                                columnas.Add(new
                                {
                                    nombre, tipo = TipoApi(r["DATA_TYPE"].ToString()),
                                    longitudMaxima = r["CHARACTER_MAXIMUM_LENGTH"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["CHARACTER_MAXIMUM_LENGTH"]),
                                    admiteVacio = string.Equals(r["IS_NULLABLE"].ToString(), "YES", StringComparison.OrdinalIgnoreCase)
                                });
                            }
                    }
                    hojas.Add(new { hoja = par.Key, tabla = par.Value, columnas });
                }
            }
            return hojas;
        }

        private static string TipoApi(string tipoSql)
        {
            switch (tipoSql.ToLowerInvariant())
            {
                case "money": case "smallmoney": case "decimal": case "numeric": case "float": case "real":
                case "int": case "bigint": case "smallint": case "tinyint": return "numero";
                case "date": case "datetime": case "datetime2": case "smalldatetime": return "fecha";
                case "bit": return "booleano";
                default: return "texto";
            }
        }
    }

    /// <summary>
    /// Autenticación y límites de la API. Va antes de los endpoints. Cualquier excepción se responde como JSON (nunca la página de error HTML).
    /// Límites (en memoria): 20 claves incorrectas por IP en 10 min → 429; 120 llamadas por integración por minuto → 429.
    /// </summary>
    public class ApiV1Middleware
    {
        private readonly RequestDelegate _next;
        private static readonly ConcurrentDictionary<string, (int n, DateTime inicio)> FallosPorIp = new ConcurrentDictionary<string, (int, DateTime)>();
        private static readonly ConcurrentDictionary<int, (int n, DateTime inicio)> LlamadasPorIntegracion = new ConcurrentDictionary<int, (int, DateTime)>();
        private static bool _disponible; private static DateTime _disponibleHasta = DateTime.MinValue;

        public ApiV1Middleware(RequestDelegate next) { _next = next; }

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (!ctx.Request.Path.StartsWithSegments("/api")) { await _next(ctx); return; }

            ctx.Response.Headers["Cache-Control"] = "no-store";
            try
            {
                if (!ctx.Request.Path.StartsWithSegments("/api/v1"))
                { await Escribir(ctx, 404, "no_encontrado", "Ruta no encontrada. La API vigente es /api/v1."); return; }

                if (!Disponible())
                { await Escribir(ctx, 503, "api_no_configurada", "La API de ingesta aún no está habilitada en este entorno."); return; }

                string ip = ClientIpHelper.ObtenerIp();
                if (Excedido(FallosPorIp, ip, 20, TimeSpan.FromMinutes(10)))
                { ctx.Response.Headers["Retry-After"] = "600"; await Escribir(ctx, 429, "demasiados_intentos", "Demasiados intentos con una clave incorrecta. Espere unos minutos."); return; }

                string auth = ctx.Request.Headers["Authorization"].ToString();
                string clave = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth.Substring(7).Trim() : null;
                if (string.IsNullOrEmpty(clave))
                { Sumar(FallosPorIp, ip, TimeSpan.FromMinutes(10)); await Escribir(ctx, 401, "no_autorizado", "Falta la clave de la integración (Authorization: Bearer bfk_…)."); return; }

                var res = new IntegracionesService().Autenticar(clave, ip);
                if (!res.Ok)
                {
                    Sumar(FallosPorIp, ip, TimeSpan.FromMinutes(10));
                    AppLogger.Warn($"API: autenticación rechazada ({res.Motivo}) desde {ip}", "ApiV1");
                    if (res.IpNoPermitida) { await Escribir(ctx, 403, "ip_no_permitida", "Esta clave no está autorizada para usarse desde esta dirección IP."); return; }
                    await Escribir(ctx, 401, "no_autorizado", "Clave de integración no válida o revocada."); return;
                }

                if (Excedido(LlamadasPorIntegracion, res.Integracion.Id, 120, TimeSpan.FromMinutes(1)))
                { ctx.Response.Headers["Retry-After"] = "60"; await Escribir(ctx, 429, "limite_de_llamadas", "Superó el límite de 120 llamadas por minuto."); return; }
                Sumar(LlamadasPorIntegracion, res.Integracion.Id, TimeSpan.FromMinutes(1));

                ctx.Items["ApiIntegracion"] = res.Integracion;
                ctx.Items["ApiUsuarioTecnico"] = res.UsuarioTecnico;

                // Idioma de los mensajes: inglés si se pide con ?idioma=en o Accept-Language: en*; por defecto español.
                bool en = string.Equals(ctx.Request.Query["idioma"], "en", StringComparison.OrdinalIgnoreCase)
                          || ctx.Request.Headers["Accept-Language"].ToString().StartsWith("en", StringComparison.OrdinalIgnoreCase);
                var cultura = new CultureInfo(en ? "en-US" : "es-CO");
                CultureInfo.CurrentUICulture = cultura; CultureInfo.CurrentCulture = cultura;

                await _next(ctx);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ApiV1");
                if (!ctx.Response.HasStarted)
                    await Escribir(ctx, 500, "error_interno", "Error interno. Si persiste, contacte a soporte indicando la hora de la llamada.");
            }
        }

        private static bool Disponible()
        {
            if (DateTime.Now < _disponibleHasta) return _disponible;
            _disponible = IntegracionesService.Disponible();
            _disponibleHasta = DateTime.Now.AddSeconds(_disponible ? 60 : 15);
            return _disponible;
        }

        private static bool Excedido<T>(ConcurrentDictionary<T, (int n, DateTime inicio)> d, T clave, int max, TimeSpan ventana)
        {
            if (!d.TryGetValue(clave, out var v)) return false;
            if (DateTime.Now - v.inicio > ventana) { d.TryRemove(clave, out _); return false; }
            return v.n >= max;
        }

        private static void Sumar<T>(ConcurrentDictionary<T, (int n, DateTime inicio)> d, T clave, TimeSpan ventana) =>
            d.AddOrUpdate(clave, _ => (1, DateTime.Now), (_, v) => DateTime.Now - v.inicio > ventana ? (1, DateTime.Now) : (v.n + 1, v.inicio));

        private static Task Escribir(HttpContext ctx, int status, string codigo, string mensaje)
        {
            ctx.Response.StatusCode = status;
            return ctx.Response.WriteAsJsonAsync(new { error = new { codigo, mensaje } });
        }
    }
}
