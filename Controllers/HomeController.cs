using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Windows.Media.Media3D;
using bufinscustomers.Permisos;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class HomeController : BaseController
    {
        public ActionResult Index()
        {
            return View();
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