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
using System.Net.Http.Json;
using System.Text.Json;
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

            // Contrato OpenAPI: público (no lleva clave) para que el equipo del cliente pueda generar su código o importarlo en Postman.
            g.MapGet("/openapi.json", () =>
            {
                var ruta = AppPaths.Map("~/Api/openapi.v1.json");
                return System.IO.File.Exists(ruta)
                    ? Results.Content(System.IO.File.ReadAllText(ruta, System.Text.Encoding.UTF8), "application/json; charset=utf-8")
                    : Error(404, "no_encontrado", "El contrato OpenAPI no está disponible.");
            });

            // Manual de integración (HTML autocontenido, sin secretos): público para que el equipo técnico del cliente lo consulte y copie los ejemplos.
            g.MapGet("/manual", (string idioma) =>
            {
                bool en = string.Equals(idioma, "en", StringComparison.OrdinalIgnoreCase);
                var ruta = AppPaths.Map("~/App_Data/Documentacion/Tecnica/" + (en ? "Manual_Integracion_API.en.html" : "Manual_Integracion_API.html"));
                if (!System.IO.File.Exists(ruta)) ruta = AppPaths.Map("~/App_Data/Documentacion/Tecnica/Manual_Integracion_API.html");
                return System.IO.File.Exists(ruta)
                    ? Results.Content(System.IO.File.ReadAllText(ruta, System.Text.Encoding.UTF8), "text/html; charset=utf-8")
                    : Error(404, "no_encontrado", "El manual no está disponible.");
            });

            g.MapGet("/ping", (HttpContext ctx) =>
            {
                var i = Integ(ctx);
                return Ok(new { estado = "ok", version = "1", entorno = i.Sandbox ? "pruebas" : "produccion", integracion = i.Nombre, idEmpresa = i.IdEmpresa, empresa = i.NombreEmpresa, permiteConfirmar = i.PermiteConfirmar });
            });

            g.MapGet("/esquemas", (HttpContext ctx) => Ok(new { hojas = ApiEsquemas.Obtener(Integ(ctx).IdEmpresa) }));

            g.MapPost("/cargues/archivo", SubirArchivo);
            g.MapPost("/cargues", IniciarCargue);
            g.MapPost("/cargues/{idLote:long}/hojas/{hoja}", AgregarHoja);
            g.MapPost("/cargues/{idLote:long}/validar", ValidarCargue);

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
                return ResultadoConfirmacion(ctx, new CargueOrquestadorService(Traducir).Confirmar(Tecnico(ctx), idLote, confirmarReemplazo: false, simular: integ.Sandbox));
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
                try { integraciones.MarcarLoteApi(idLote, integ.Id, integ.Sandbox); }
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
            return Finalizar(integ, tecnico, orq, staging, lote, proceso.NombreArchivo, pideConfirmar, 201);
        }

        // ── Envío por hojas en JSON: POST /cargues → POST /cargues/{id}/hojas/{hoja} (n veces) → POST /cargues/{id}/validar ──

        private static async Task<IResult> IniciarCargue(HttpContext ctx)
        {
            var integ = Integ(ctx);
            var tecnico = Tecnico(ctx);

            var body = await LeerJson(ctx);
            if (body == null || body.Value.ValueKind != JsonValueKind.Object)
                return Error(400, "json_invalido", "El cuerpo debe ser un objeto JSON: { \"anio\": 2026, \"modo\": \"ejecucion\" }.");
            var b = body.Value;

            if (!b.TryGetProperty("anio", out var pAnio) || pAnio.ValueKind != JsonValueKind.Number || !pAnio.TryGetInt32(out int anio))
                return Error(400, "anio_invalido", "El campo «anio» es obligatorio y debe ser un número.");
            string modo = b.TryGetProperty("modo", out var pModo) && pModo.ValueKind == JsonValueKind.String ? (pModo.GetString() ?? "").Trim().ToLowerInvariant() : "";
            int idEscenario = 1;
            if (b.TryGetProperty("idEscenario", out var pEsc) && !(pEsc.ValueKind == JsonValueKind.Number && pEsc.TryGetInt32(out idEscenario)))
                return Error(400, "escenario_invalido", "El campo «idEscenario» debe ser un número.");
            string referencia = b.TryGetProperty("referencia", out var pRef) && pRef.ValueKind == JsonValueKind.String ? pRef.GetString() : null;

            string claveIdem = ctx.Request.Headers["Idempotency-Key"].ToString().Trim();
            if (claveIdem.Length > 100 || claveIdem.Any(c => char.IsControl(c)))
                return Error(400, "idempotency_key_invalida", "Idempotency-Key admite hasta 100 caracteres sin saltos de línea.");

            var integraciones = new IntegracionesService();
            var staging = new CargueStagingService();
            if (claveIdem.Length > 0)
            {
                var previo = integraciones.BuscarIdempotencia(integ.Id, claveIdem);
                var l = previo.HasValue ? staging.ObtenerLote(previo.Value) : null;
                if (l != null) return Ok(new { repetido = true, lote = LoteDto(l) });
            }

            var orq = new CargueOrquestadorService(Traducir);
            var solicitud = new SolicitudCargue
            {
                Usuario = tecnico, IdEmpresa = integ.IdEmpresa, Anio = anio, Modo = modo, IdEscenario = idEscenario,
                NombreArchivo = string.IsNullOrWhiteSpace(referencia) ? "API · " + integ.Nombre : referencia
            };
            var validacion = orq.ValidarSolicitud(solicitud, exigirArchivo: false);
            if (!validacion.Ok) return Error(422, "solicitud_invalida", validacion.Mensaje);

            var proceso = orq.IniciarLoteSinArchivo(solicitud, idLote =>
            {
                try { integraciones.MarcarLoteApi(idLote, integ.Id, integ.Sandbox); }
                catch (Exception ex) { AppLogger.Error(ex, "API.MarcarLoteApi"); }
            });
            if (proceso.Estado == ProcesoCargueEstado.LoteActivoExistente)
                return Results.Json(new { error = new { codigo = "lote_activo", mensaje = proceso.Mensaje, idLote = proceso.IdLote } }, statusCode: 409);

            if (claveIdem.Length > 0) integraciones.GuardarIdempotencia(integ.Id, claveIdem, proceso.IdLote);
            var lote = staging.ObtenerLote(proceso.IdLote);
            return Results.Json(new
            {
                repetido = false, lote = LoteDto(lote),
                siguientePaso = $"Envíe cada hoja con POST /api/v1/cargues/{proceso.IdLote}/hojas/{{hoja}} (también las que vayan sin filas) y luego POST /api/v1/cargues/{proceso.IdLote}/validar."
            }, statusCode: 201);
        }

        private static async Task<IResult> AgregarHoja(HttpContext ctx, long idLote, string hoja)
        {
            var lote = LotePropio(ctx, idLote, out var errorLote);
            if (lote == null) return errorLote;

            var body = await LeerJson(ctx);
            if (body == null || body.Value.ValueKind != JsonValueKind.Object || !body.Value.TryGetProperty("filas", out var pFilas) || pFilas.ValueKind != JsonValueKind.Array)
                return Error(400, "json_invalido", "El cuerpo debe ser { \"filas\": [ { \"Columna\": valor, ... } ], \"reemplazar\": false }. «filas» puede ir vacía.");
            bool reemplazar = body.Value.TryGetProperty("reemplazar", out var pRem) && pRem.ValueKind == JsonValueKind.True;

            var filas = new List<IDictionary<string, object>>();
            int n = 0;
            foreach (var fila in pFilas.EnumerateArray())
            {
                n++;
                if (fila.ValueKind != JsonValueKind.Object)
                    return Error(400, "fila_invalida", $"La fila {n} debe ser un objeto { "{ \"Columna\": valor }" }.");
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in fila.EnumerateObject())
                {
                    if (!TryValor(prop.Value, out object v))
                        return Error(400, "valor_no_soportado", $"Fila {n}, columna «{prop.Name}»: solo se admiten número, texto, verdadero/falso o nulo.");
                    d[prop.Name] = v;
                }
                filas.Add(d);
            }

            var r = new CargueOrquestadorService(Traducir).AgregarFilasHoja(Tecnico(ctx), idLote, hoja, filas, reemplazar);
            switch (r.Estado)
            {
                case HojaApiEstado.Ok:
                    return Ok(new { idLote, hoja = r.Hoja, tabla = r.Tabla, filasRecibidas = r.FilasRecibidas, filasGuardadas = r.FilasGuardadas, filasOmitidasPorAnio = r.FilasOmitidasPorAnio, filasEnHoja = r.FilasEnHoja,
                                    aviso = r.FilasOmitidasPorAnio > 0 ? $"{r.FilasOmitidasPorAnio} fila(s) se omitieron porque su año no es {lote.Anio}." : null });
                case HojaApiEstado.LoteNoDisponible: return Error(409, "lote_no_disponible", "El cargue ya fue confirmado o descartado.");
                case HojaApiEstado.HojaNoSoportada: return Error(422, "hoja_no_soportada", $"La hoja «{hoja}» no existe para esta empresa. Consulte GET /api/v1/esquemas.");
                case HojaApiEstado.ColumnaDesconocida: return Error(422, "columna_desconocida", $"La columna «{r.Mensaje}» no existe en la hoja «{r.Hoja}». Consulte GET /api/v1/esquemas.");
                case HojaApiEstado.DemasiadasFilas: return Error(422, "demasiadas_filas", $"Máximo {r.Mensaje} filas por llamada: envíe la hoja en varias tandas.");
                default: return Error(422, "datos_invalidos", r.Mensaje);
            }
        }

        private static async Task<IResult> ValidarCargue(HttpContext ctx, long idLote)
        {
            var integ = Integ(ctx);
            var tecnico = Tecnico(ctx);
            var lote0 = LotePropio(ctx, idLote, out var errorLote);
            if (lote0 == null) return errorLote;

            bool pideConfirmar = false;
            if (ctx.Request.ContentLength > 0)
            {
                var body = await LeerJson(ctx);
                pideConfirmar = body != null && body.Value.ValueKind == JsonValueKind.Object && body.Value.TryGetProperty("confirmar", out var pc) && pc.ValueKind == JsonValueKind.True;
            }

            var orq = new CargueOrquestadorService(Traducir);
            var r = orq.ValidarLoteApi(tecnico, idLote);
            switch (r.Estado)
            {
                case ValidacionApiEstado.LoteNoDisponible:
                    return Error(409, "lote_no_disponible", "El cargue ya fue confirmado o descartado.");
                case ValidacionApiEstado.HojasFaltantes:
                    return Results.Json(new { error = new { codigo = "hojas_faltantes", mensaje = r.Mensaje }, hojasFaltantes = r.HojasFaltantes }, statusCode: 422);
                case ValidacionApiEstado.SinFilasDelAnio:
                    return Error(422, "sin_filas_del_anio", r.Mensaje);
                case ValidacionApiEstado.ErrorValidacion:
                    return Error(500, "error_interno", "No se pudo validar el cargue. Inténtelo de nuevo o contacte a soporte.");
            }
            return Finalizar(integ, tecnico, orq, new CargueStagingService(), r.Lote, r.Lote?.NombreArchivo, pideConfirmar, 200);
        }

        /// <summary>
        /// Cierre común de «archivo» y «validar»: audita, confirma solo si la integración puede, el cliente lo pidió y el cargue quedó
        /// limpio (sin advertencias ni reducción fuerte), y avisa a los revisores si queda esperando a una persona.
        /// </summary>
        private static IResult Finalizar(Integracion integ, Usuarios tecnico, CargueOrquestadorService orq, CargueStagingService staging,
            CargueLote lote, string archivo, bool pideConfirmar, int status)
        {
            new AuditoriaService().RegistrarCambio(
                AuditoriaTipo.Cargues, AuditoriaAccion.Crear, "CargueLote", lote?.IdLote.ToString(),
                $"{(integ.Sandbox ? "[PRUEBAS] " : "")}La integración '{integ.Nombre}' envió por la API '{archivo}' a '{integ.NombreEmpresa}' — año {lote?.Anio}, {(lote?.Modo == 0 ? "ejecución" : "histórico")}, escenario {lote?.IdEscenario}: {lote?.TotalFilas:N0} fila(s), {lote?.TotalErrores} error(es), {lote?.TotalAdvertencias} advertencia(s). Queda en revisión.",
                null, new { integracion = integ.Nombre, idIntegracion = integ.Id, archivo, anio = lote?.Anio, escenario = lote?.IdEscenario, estado = lote?.Estado },
                integ.IdEmpresa, entidadNombre: integ.NombreEmpresa);

            string confirmacion = pideConfirmar ? "no_permitida" : "no_solicitada";
            object confirmado = null;
            if (pideConfirmar && integ.PermiteConfirmar)
            {
                if (lote != null && lote.Estado == CargueLoteEstado.ValidadoOk)
                {
                    var c = orq.Confirmar(tecnico, lote.IdLote, confirmarReemplazo: false, simular: integ.Sandbox);
                    confirmacion = c.Estado == ConfirmacionCargueEstado.Simulado ? "simulado" : c.Confirmado ? "confirmado" : "pendiente_de_revision";
                    if (c.Confirmado || c.Estado == ConfirmacionCargueEstado.Simulado) confirmado = new { mensaje = c.Mensaje, filas = c.Lote?.TotalFilas };
                    lote = staging.ObtenerLote(lote.IdLote) ?? lote;   // tras una simulación el lote de pruebas ya no existe (null → se conserva el anterior)
                }
                else confirmacion = "pendiente_de_revision";
            }

            // Si queda esperando a una persona (o con errores), se le avisa a los administradores de la empresa.
            // En pruebas NO: nadie debe revisar un cargue de prueba.
            if (confirmacion != "confirmado" && confirmacion != "simulado" && !integ.Sandbox) orq.NotificarRevisores(lote, integ.Nombre, tecnico.Id);

            return Results.Json(new
            {
                repetido = false, sandbox = integ.Sandbox, confirmacion, confirmado, lote = LoteDto(lote),
                urlRevision = lote == null ? null : $"/Datos/RevisarCargue?idLote={lote.IdLote}"
            }, statusCode: status);
        }

        private static async Task<JsonElement?> LeerJson(HttpContext ctx)
        {
            try { return await ctx.Request.ReadFromJsonAsync<JsonElement>(); }
            catch (Exception) { return null; }
        }

        private static bool TryValor(JsonElement e, out object v)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number: v = e.GetDouble(); return true;
                case JsonValueKind.String: v = e.GetString(); return true;
                case JsonValueKind.True: v = true; return true;
                case JsonValueKind.False: v = false; return true;
                case JsonValueKind.Null: v = null; return true;
                default: v = null; return false;
            }
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
                case ConfirmacionCargueEstado.Simulado:
                    return Ok(new
                    {
                        estado = "ConfirmadoSimulado", sandbox = true, mensaje = r.Mensaje, filas = r.Lote?.TotalFilas,
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

                if (ctx.Request.Path.Equals("/api/v1/openapi.json", StringComparison.OrdinalIgnoreCase)
                    || ctx.Request.Path.Equals("/api/v1/manual", StringComparison.OrdinalIgnoreCase)) { await _next(ctx); return; }

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

                if (res.Integracion.Sandbox) ctx.Response.Headers["X-Bufins-Entorno"] = "pruebas";
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
