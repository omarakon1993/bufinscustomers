using bufinscustomers.Helpers;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Filtro global: si el usuario en sesion tiene pendiente el cambio de contrasena obligatorio (primer inicio de sesion),
    /// bloquea todo salvo la pantalla de cambio, cerrar sesion, extender sesion, cambiar idioma y las paginas de error.
    /// Peticiones AJAX: 403 con JSON y redirectUrl.
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

            var url = filterContext.UrlHelper().Action("CambiarClaveInicial", "Acceso");

            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.HttpContext.Response.StatusCode = 403;
                filterContext.Result = new JsonResult(new { success = false, forbidden = true, cambiarClave = true, redirectUrl = url });
            }
            else
            {
                filterContext.Result = new RedirectResult(url);
            }
        }
    }
}
