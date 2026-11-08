using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Helpers;

namespace bufinscustomers.Permisos
{
    /// <summary>
    /// Exige una sesion valida. Peticiones AJAX: responde JSON con sessionExpired (el JS del layout redirige al login);
    /// peticiones normales: redirige al login. Ademas avisa por cabeceras cuando la sesion esta por expirar.
    /// </summary>
    public class ValidarSesionAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (usuario == null)
            {
                if (filterContext.HttpContext.Request.IsAjaxRequest())
                {
                    filterContext.HttpContext.Response.Headers["X-Session-Expired"] = "true";
                    filterContext.Result = new JsonResult(new
                    {
                        success = false,
                        message = "Tu sesión ha expirado. Por favor, inicia sesión nuevamente.",
                        redirectUrl = "/Acceso/Login?expired=true",
                        sessionExpired = true
                    });
                }
                else
                {
                    var loginUrl = filterContext.UrlHelper().Action("Login", "Acceso", new { expired = true });
                    filterContext.Result = new RedirectResult(loginUrl);
                }
            }
            else
            {
                var sessionInfo = UsuarioSesionHelper.ObtenerInfoSesion();
                if (sessionInfo?.EstaPorExpirar == true)
                {
                    filterContext.HttpContext.Response.Headers["X-Session-Warning"] = "true";
                    filterContext.HttpContext.Response.Headers["X-Minutes-Remaining"] = sessionInfo.MinutosRestantes.ToString("0");
                }
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
