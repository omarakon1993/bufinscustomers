using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System.Linq;
using System.Web.Mvc;
using System.Configuration;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class AnalisisIAController : BaseController
    {
        private readonly InformeTablasDatosService _service = new InformeTablasDatosService();

        public ActionResult Index()
        {
            var usuario        = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin   = UsuarioSesionHelper.EsSuperAdmin();
            var esAdminEmpresa = UsuarioSesionHelper.EsAdminEmpresa();
            var idEmpresa      = usuario?.IdEmpresa ?? 0;

            ViewBag.Tablas           = _service.ObtenerTablasDisponibles();
            ViewBag.EsAdmin          = esSuperAdmin;
            ViewBag.EsAdminEmpresa   = esAdminEmpresa;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            if (esSuperAdmin)
                ViewBag.Empresas = _service.ObtenerEmpresas();
            else
                ViewBag.Empresas = _service.ObtenerEmpresas().Where(e => e.Id == idEmpresa).ToList();

            // Modelo IA dinámico desde BD (fallback a Web.config o default)
            var cfgSvc = new ConfiguracionSistemaService();
            string modeloBD = cfgSvc.ObtenerValor("OpenAIModel");
            ViewBag.ModeloIA = !string.IsNullOrWhiteSpace(modeloBD)
                ? modeloBD
                : (ConfigurationManager.AppSettings["OpenAIModel"] ?? "gpt-4o-mini");

            return View("~/Views/Informes/AnalisisIA.cshtml");
        }

    }
}
