using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
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
        public ActionResult Index()
        {
            var svc      = new WidgetsService();
            var usuario  = UsuarioSesionHelper.UsuarioActual;
            bool esAdmin = UsuarioSesionHelper.EsSuperAdmin();

            var tarjetasConfig = svc.ObtenerActivas();
            var vm = new List<WidgetTarjetaViewModel>();

            foreach (var t in tarjetasConfig)
            {
                var item = new WidgetTarjetaViewModel { Config = t };
                if (t.Tipo == 1 && !string.IsNullOrWhiteSpace(t.ConsultaSQL))
                {
                    int? filtro = esAdmin ? (int?)null : usuario?.IdEmpresa;
                    item.KpiResultados = svc.EjecutarKpi(t.ConsultaSQL, filtro);
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
