using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using Newtonsoft.Json;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class HomeController : BaseController
    {
        public async Task<ActionResult> Index()
        {
            var svc      = new WidgetsService();
            var usuario  = UsuarioSesionHelper.UsuarioActual;
            bool esAdmin = UsuarioSesionHelper.EsSuperAdmin();

            // Empresa propia y las de su mismo grupo empresarial (solo consulta del dashboard)
            var idsPermitidos = esAdmin ? null : (EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>());

            var tarjetasConfig = await svc.ObtenerActivasAsync();

            // D1: cada widget se resuelve en paralelo (antes: en serie con await dentro del foreach).
            // E2: el resultado crudo de cada consulta SQL se cachea 60 s (compartido entre usuarios);
            //     el filtrado por empresa se aplica por-petición sobre la copia cacheada.
            var tareas = tarjetasConfig.Select(async t =>
            {
                var item = new WidgetTarjetaViewModel { Config = t };

                if (!string.IsNullOrWhiteSpace(t.ConsultaSQL))
                {
                    if (t.Tipo == 1)
                    {
                        var resultados = await WidgetCacheadoAsync("wk:" + t.ConsultaSQL,
                            () => svc.EjecutarKpiAsync(t.ConsultaSQL, null));
                        item.KpiResultados = (idsPermitidos == null || resultados == null)
                            ? resultados
                            : resultados.Where(r => idsPermitidos.Contains(r.IdEmpresa)).ToList();
                    }
                    else if (t.Tipo == 2)
                    {
                        var resultados = await WidgetCacheadoAsync("wg:" + t.ConsultaSQL,
                            () => svc.EjecutarGraficoAsync(t.ConsultaSQL, null));
                        item.GraficoResultados = (idsPermitidos == null || resultados == null)
                            ? resultados
                            : resultados.Where(r => idsPermitidos.Contains(r.IdEmpresa)).ToList();
                    }
                }
                return item;
            });

            var vm = (await Task.WhenAll(tareas)).ToList();
            return View(vm);
        }

        // E2 — caché corta (60 s) del resultado crudo de una consulta de widget.
        private static readonly System.Runtime.Caching.ObjectCache _widgetCache =
            System.Runtime.Caching.MemoryCache.Default;

        private static async Task<T> WidgetCacheadoAsync<T>(string clave, Func<Task<T>> factory) where T : class
        {
            if (_widgetCache.Get(clave) is T hit) return hit;

            var valor = await factory();
            if (valor != null)
            {
                _widgetCache.Set(clave, valor, new System.Runtime.Caching.CacheItemPolicy
                {
                    AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(60)
                });
            }
            return valor;
        }

        [HttpGet]
        public async Task<ActionResult> ObtenerNoticias(bool refresh = false)
        {
            try
            {
                var svc = new IndicadoresFinancierosService();
                var vm  = await svc.ObtenerSoloNoticiasAsync(refresh).ConfigureAwait(false);
                return Json(new { rssConfigurado = vm.RSSConfigurado, feeds = vm.Feeds },
                            JsonRequestBehavior.AllowGet);
            }
            catch
            {
                return Json(new { rssConfigurado = false, noticias = new object[0] },
                            JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Devuelve los nombres amigables de las tablas financieras configuradas para el resumen IA
        /// de la empresa indicada (o la del usuario actual si no es Super Admin).
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerTablasConfiguradasIA(int? idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                int? idEmpresaObjetivo = esAdmin
                    ? idEmpresa
                    : (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value) ? idEmpresa : usuario?.IdEmpresa);
                if (!idEmpresaObjetivo.HasValue)
                    return Json(new { success = true, tablas = new string[0] }, JsonRequestBehavior.AllowGet);

                var tablasAsignadas = new EmpresaTablasResumenIAService().ObtenerTablasAsignadas(idEmpresaObjetivo.Value);
                var tablasDisponibles = new InformeTablasDatosService().ObtenerTablasDisponibles();

                var nombresAmigables = tablasAsignadas
                    .Select(nombreTabla => tablasDisponibles.FirstOrDefault(t => t.NombreTabla == nombreTabla)?.NombreAmigable)
                    .Where(nombre => nombre != null)
                    .ToList();

                return Json(new { success = true, tablas = nombresAmigables }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Genera un resumen ejecutivo IA con los datos financieros más recientes de la empresa,
        /// usando las tablas configuradas en EmpresaTablasResumenIA y el prompt RESUMEN_GERENCIAL.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ObtenerResumenIA(int? idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                int idEmpresaObjetivo;
                if (esAdmin)
                {
                    if (!idEmpresa.HasValue)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("Home_IA_SeleccioneEmpresa") });
                    idEmpresaObjetivo = idEmpresa.Value;
                }
                else if (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                {
                    idEmpresaObjetivo = idEmpresa.Value;
                }
                else
                {
                    if (!usuario.IdEmpresa.HasValue)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("Home_IA_SinEmpresa") });
                    idEmpresaObjetivo = usuario.IdEmpresa.Value;
                }

                var tablasAsignadas = new EmpresaTablasResumenIAService().ObtenerTablasAsignadas(idEmpresaObjetivo);
                if (tablasAsignadas == null || tablasAsignadas.Count == 0)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Home_IA_SinConfigurar") });

                // Cuota diaria por usuario (Super Admin exento) — misma regla que InformeTablasDatosController.ConsultarConIA
                if (!esAdmin)
                {
                    int limiteDiario = ObtenerLimiteConsultasIA(usuario.Id);
                    if (limiteDiario <= 0)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });

                    int consultasHoy = new AuditoriaAnalisisIAService().ContarConsultasHoy(usuario.Id);
                    if (consultasHoy >= limiteDiario)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });
                }

                var tablasService = new InformeTablasDatosService();
                var tablasDisponibles = tablasService.ObtenerTablasDisponibles();

                const int MaxFilasPorTabla = 300;
                var datosPorTabla = new Dictionary<string, object>();
                int totalFilasEnviadas = 0;

                foreach (var nombreTabla in tablasAsignadas)
                {
                    var tablaInfo = tablasDisponibles.FirstOrDefault(t => t.NombreTabla == nombreTabla);
                    if (tablaInfo == null) continue; // fuera del whitelist actual

                    var anios = tablasService.ObtenerAñosDisponibles(nombreTabla, idEmpresaObjetivo);
                    if (anios == null || anios.Count == 0) continue;

                    var filtros = new FiltrosInformeTablasDatos { NombreTabla = nombreTabla, Año = anios[0], IdEmpresa = idEmpresaObjetivo };
                    var resultado = await tablasService.ConsultarDatosAsync(filtros, esAdmin, idEmpresaObjetivo);
                    if (resultado.TotalRegistros == 0) continue;

                    var filas = resultado.Filas.Take(MaxFilasPorTabla).ToList();
                    datosPorTabla[tablaInfo.NombreAmigable] = filas;
                    totalFilasEnviadas += filas.Count;
                }

                if (datosPorTabla.Count == 0)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Home_IA_SinDatos") });

                var cfgSvc     = new ConfiguracionSistemaService();
                var tApiKey    = cfgSvc.ObtenerValorAsync("OpenAIApiKey");
                var tModelo    = cfgSvc.ObtenerValorAsync("OpenAIModel");
                var tMaxTokens = cfgSvc.ObtenerValorAsync("OpenAIMaxTokens");
                var tTemp      = cfgSvc.ObtenerValorAsync("OpenAITemperature");
                await Task.WhenAll(tApiKey, tModelo, tMaxTokens, tTemp);

                string apiKey   = (tApiKey.Result ?? "").Trim();
                string modeloIA = (tModelo.Result ?? "gpt-4o").Trim();
                int maxTokensIA = (int.TryParse(tMaxTokens.Result, out int ptk) && ptk > 0) ? ptk : 8000;
                double? temperatureIA = double.TryParse(
                    tTemp.Result,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double tVal) ? (double?)tVal : null;

                var iaService = new IAService(apiKey);

                var promptConfig = new GestorPromptsService().ObtenerPorCodigo("RESUMEN_GERENCIAL");
                string instrucciones = promptConfig?.TextoPrompt;

                var guardrailConfig = new GestorPromptsService().ObtenerPorCodigo("GUARDRAIL_SISTEMA");
                string guardrail = guardrailConfig?.TextoPrompt;

                // Contexto de negocio de Bufins (Fase A): mismo conocimiento curado que en Análisis IA.
                var contextoConfig = new GestorPromptsService().ObtenerPorCodigo("CONTEXTO_NEGOCIO_BUFINS");
                string contextoNegocio = contextoConfig?.TextoPrompt;

                var empresaInfo = tablasService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresaObjetivo);
                string nombreEmpresa = empresaInfo?.Nombre ?? "—";
                string nombreTablasEnviadas = string.Join(", ", datosPorTabla.Keys);

                var request = new IAConsultaRequest
                {
                    Pregunta = null,
                    DatosJson = JsonConvert.SerializeObject(datosPorTabla),
                    NombreTabla = nombreTablasEnviadas,
                    FiltrosDescripcion = $"Empresa {nombreEmpresa}, año más reciente disponible por tabla"
                };

                var response = await iaService.ConsultarAsync(request, instrucciones, guardrail, modeloIA, maxTokensIA, temperatureIA, contextoNegocio);
                response.FilasEnviadas = totalFilasEnviadas;
                response.TotalFilas = totalFilasEnviadas;

                if (response.Exitoso)
                {
                    try
                    {
                        new AuditoriaAnalisisIAService().Registrar(new AuditoriaAnalisisIA
                        {
                            IdUsuario       = usuario.Id,
                            NombreUsuario   = $"{usuario.Nombre} {usuario.Apellidos}".Trim(),
                            IdEmpresa       = idEmpresaObjetivo,
                            NombreEmpresa   = nombreEmpresa,
                            NombreTabla     = nombreTablasEnviadas,
                            Filtros         = request.FiltrosDescripcion,
                            Pregunta        = null,
                            Respuesta       = response.Respuesta,
                            FechaPregunta   = DateTime.Now,
                            FilasAnalizadas = totalFilasEnviadas
                        });
                    }
                    catch { }
                }

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = $"Error al generar el resumen: {ex.Message}" });
            }
        }

        private int ObtenerLimiteConsultasIA(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("SELECT LimiteConsultasIA FROM Usuarios WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    var result = cmd.ExecuteScalar();
                    return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
            catch { return 0; }
        }

        public ActionResult CerrarSesion()
        {
            Session["usuario"] = null;
            return RedirectToAction("Login", "Acceso");
        }
        public ActionResult CargueExcel()
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Cache.SetExpires(DateTime.UtcNow.AddDays(-1));

            TempData.Keep("Mensaje");
            TempData.Keep("MensajeTipo");

            var tablas = TempData["TablasExcel"] as List<(string nombre, DataTable tabla)>;

            return View(tablas ?? new List<(string, DataTable)>());
        }
    }
}
