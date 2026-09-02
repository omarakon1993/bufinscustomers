using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Módulo unificado de auditoría: una sola pantalla con pestañas — Cambios, Navegación,
    /// Cargues, IA. Cada pestaña incrusta (iframe, <c>?embed=1</c>) la vista existente sin
    /// modificar su lógica; cada una aplica su propio alcance por rol/empresa.
    ///
    /// Gate: <c>EsSuperAdmin() || EsAdminEmpresa()</c> (otros roles → <c>~/Error/Forbidden</c>).
    /// </summary>
    [ValidarSesion]
    public class AuditoriaHubController : BaseController
    {
        public ActionResult Index()
        {
            if (!(UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.EsAdminEmpresa()))
                return new RedirectResult("~/Error/Forbidden");

            return View("~/Views/Informes/AuditoriaHub.cshtml");
        }
    }
}
