using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class AnalisisIAController : BaseController
    {
        private readonly InformeTablasDatosService _service = new InformeTablasDatosService();

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            ViewBag.Tablas = _service.ObtenerTablasDisponibles();

            if (esAdmin)
                ViewBag.Empresas = _service.ObtenerEmpresas();
            else
                ViewBag.Empresas = _service.ObtenerEmpresas().Where(e => e.Id == idEmpresa).ToList();

            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            return View("~/Views/Informes/AnalisisIA.cshtml");
        }
    }
}
