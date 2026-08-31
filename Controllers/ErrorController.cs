using System.Net;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Páginas de error con la marca del sitio. Sin <c>[ValidarSesion]</c> (deben
    /// renderizar también para usuarios anónimos). Las vistas usan <c>Layout = null</c>
    /// para no depender del sidebar ni de la sesión.
    ///
    /// Rutas de entrada:
    ///  - <c>customErrors</c> en Web.config: <c>defaultRedirect="~/Error"</c> + <c>&lt;error&gt;</c> 404/403.
    ///  - <c>&lt;httpErrors&gt;</c> en system.webServer para errores que no llegan a MVC.
    ///  - <c>LoggingHandleErrorAttribute</c> registra la excepción; esta acción solo pinta la página.
    /// </summary>
    public class ErrorController : BaseController
    {
        // GET ~/Error  → error genérico / 500
        public ActionResult Index()
        {
            Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            Response.TrySkipIisCustomErrors = true;
            return View();
        }

        // GET ~/Error/NotFound → 404
        public ActionResult NotFound()
        {
            Response.StatusCode = (int)HttpStatusCode.NotFound;
            Response.TrySkipIisCustomErrors = true;
            return View();
        }

        // GET ~/Error/Forbidden → 403 (sin permiso). También destino de RequierePermisoAttribute.
        public ActionResult Forbidden()
        {
            Response.StatusCode = (int)HttpStatusCode.Forbidden;
            Response.TrySkipIisCustomErrors = true;
            return View();
        }
    }
}
