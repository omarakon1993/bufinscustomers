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

            var tarjetasConfig = await svc.ObtenerActivasAsync();
            var vm = new List<WidgetTarjetaViewModel>();

            foreach (var t in tarjetasConfig)
            {
                var item = new WidgetTarjetaViewModel { Config = t };
                int? filtro = esAdmin ? (int?)null : usuario?.IdEmpresa;

                if (!string.IsNullOrWhiteSpace(t.ConsultaSQL))
                {
                    if (t.Tipo == 1)
                        item.KpiResultados = await svc.EjecutarKpiAsync(t.ConsultaSQL, filtro);
                    else if (t.Tipo == 2)
                        item.GraficoResultados = await svc.EjecutarGraficoAsync(t.ConsultaSQL, filtro);
                }
                vm.Add(item);
            }

            return View(vm);
        }
        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }
        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

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
