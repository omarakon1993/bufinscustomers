using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class HomeController : BaseController
    {
        public ActionResult Index()
        {
            return View();
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
        /// Alertas del Home, generadas por <c>sp_ObtenerAlertasHome</c> (mismo esquema de hallazgos que
        /// <c>sp_ValidarCargueStaging</c>). Super Admin ve todas las empresas; el resto, su empresa y las de su grupo.
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerAlertas()
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var idsPermitidos = UsuarioSesionHelper.EsSuperAdmin()
                    ? null
                    : (EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>());

                var alertas = new AlertasHomeService().Obtener(idsPermitidos);
                bool ingles = System.Threading.Thread.CurrentThread.CurrentUICulture.Name
                                    .Equals("en-US", StringComparison.OrdinalIgnoreCase);

                return Json(new
                {
                    ok = true,
                    alertas = alertas.Select(a => new
                    {
                        idEmpresa = a.IdEmpresa,
                        empresa = a.NombreEmpresa,
                        severidad = a.Severidad,
                        codigo = a.CodigoRegla,
                        titulo = (ingles && !string.IsNullOrWhiteSpace(a.TituloEn)) ? a.TituloEn : a.Titulo,
                        mensaje = (ingles && !string.IsNullOrWhiteSpace(a.MensajeEn)) ? a.MensajeEn : a.Mensaje
                    })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "HomeController.ObtenerAlertas");
                return Json(new { ok = false }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Accesos rápidos: las opciones de menú que el usuario más visita (últimos 60 días, según
        /// <c>AuditoriaNavegacion</c>), cruzadas con su propio sidebar para respetar permisos. Si aún no hay
        /// historial se completa con las opciones destacadas de su menú.
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerAccesosRapidos()
        {
            const int Maximo = 8;
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                bool iaVisible = IAModuloHelper.Visible();

                // Opciones permitidas = las que ya trae su sidebar (filtrado por permisos).
                var permitidas = (UsuarioSesionHelper.ObtenerMenuSidebar() ?? new List<SidebarCategoriaViewModel>())
                    .SelectMany(c => c.Grupos ?? new List<SidebarGrupoViewModel>())
                    .SelectMany(g => g.Items ?? new List<SidebarItemViewModel>())
                    .Where(i => !string.IsNullOrWhiteSpace(i.Controller) && !string.IsNullOrWhiteSpace(i.Action))
                    .Where(i => iaVisible || !string.Equals(i.Controller, "AnalisisIA", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(i => (i.Controller + "/" + i.Action).ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First());

                var resultado = new List<SidebarItemViewModel>();

                try
                {
                    var visitas = new AuditoriaNavegacionService().ResumenPorPagina(
                        new NavegacionFiltro { IdUsuario = usuario.Id, Desde = DateTime.Today.AddDays(-60) }, 40);
                    foreach (var v in visitas)
                    {
                        var clave = ((v.Controller ?? "") + "/" + (v.Action ?? "")).ToLowerInvariant();
                        if (permitidas.TryGetValue(clave, out var item) && !resultado.Contains(item))
                            resultado.Add(item);
                        if (resultado.Count >= Maximo) break;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("HomeController.ObtenerAccesosRapidos (sin historial): " + ex.Message);
                }

                bool haySugeridos = false;
                if (resultado.Count < 4)
                {
                    foreach (var item in permitidas.Values.Where(i => i.EsDestacado))
                    {
                        if (resultado.Count >= Maximo) break;
                        if (resultado.Contains(item)) continue;
                        resultado.Add(item);
                        haySugeridos = true;
                    }
                }

                return Json(new
                {
                    ok = true,
                    sugeridos = haySugeridos,
                    items = resultado.Select(i => new
                    {
                        nombre = i.Nombre,
                        icono = i.Icono,
                        url = Url.Action(i.Action, i.Controller)
                    })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "HomeController.ObtenerAccesosRapidos");
                return Json(new { ok = false }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
