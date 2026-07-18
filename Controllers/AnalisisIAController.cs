using bufinscustomers.Helpers;
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
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new System.Collections.Generic.List<int>();
                ViewBag.Empresas = _service.ObtenerEmpresas().Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            // Modelo IA activo desde BD (mostrado en el badge del header)
            var cfgSvc = new ConfiguracionSistemaService();
            string modeloBD = cfgSvc.ObtenerValor("OpenAIModel");
            ViewBag.ModeloIA = !string.IsNullOrWhiteSpace(modeloBD) ? modeloBD : "gpt-4o";

            return View("~/Views/Informes/AnalisisIA.cshtml");
        }

    }
}
