using System.Web.Mvc;
using bufinscustomers.Helpers;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Global: si el usuario autenticado tiene pendiente el cambio de contraseña obligatorio
    /// (<c>Usuarios.DebeCambiarClave</c>), solo puede usar la pantalla de cambio, cerrar sesión,
    /// extender la sesión y cambiar el idioma. Todo lo demás lo redirige ahí.
    /// </summary>
    public class CambioClaveObligatorioFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || !usuario.DebeCambiarClave) return;

            string controller = (filterContext.RouteData.Values["controller"] as string ?? "").ToLowerInvariant();
            string action = (filterContext.RouteData.Values["action"] as string ?? "").ToLowerInvariant();

            if (controller == "acceso" &&
                (action == "cambiarclaveinicial" || action == "cerrarsesion" ||
                 action == "extendersesion" || action == "setlanguage"))
                return;
            if (controller == "error") return;

            var url = new UrlHelper(filterContext.RequestContext).Action("CambiarClaveInicial", "Acceso");
            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.HttpContext.Response.StatusCode = 403;
                filterContext.Result = new JsonResult
                {
                    Data = new { success = false, forbidden = true, cambiarClave = true, redirectUrl = url },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet
                };
            }
            else
            {
                filterContext.Result = new RedirectResult(url);
            }
        }
    }
}
