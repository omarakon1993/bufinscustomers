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
    /// Gate: cualquier usuario con sesión válida (quién ve la opción se controla por menú/permiso,
    /// no por rol aquí). Usuario Normal solo ve la pestaña «Cambios» (acotada a su propia
    /// auditoría) — Navegación/Cargues/IA siguen siendo exclusivas de Super Admin/Admin de Empresa
    /// y sus propios controladores las rechazan si un Usuario Normal intenta cargarlas.
    /// </summary>
    [ValidarSesion]
    public class AuditoriaHubController : BaseController
    {
        public ActionResult Index()
        {
            if (UsuarioSesionHelper.UsuarioActual == null)
                return new RedirectResult("~/Error/Forbidden");

            ViewBag.EsUsuarioNormal = UsuarioSesionHelper.EsUsuarioNormal();

            return View("~/Views/Informes/AuditoriaHub.cshtml");
        }
    }
}
