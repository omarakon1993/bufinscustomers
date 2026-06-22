using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Helpers;

namespace bufinscustomers.Permisos
{
    public class ValidarSesionAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // ========== VERIFICACIÓN MEJORADA DE AUTENTICACIÓN ==========
            var usuario = UsuarioSesionHelper.UsuarioActual;
            
            if (usuario == null)
            {
                // ========== MANEJO DIFERENCIADO SEGÚN TIPO DE PETICIÓN ==========
                
                // Si es petición AJAX, retornar JSON
                if (filterContext.HttpContext.Request.IsAjaxRequest())
                {
                    // Header que el handler global de jQuery detecta para disparar cerrarSesionAutomatica()
                    filterContext.HttpContext.Response.Headers.Add("X-Session-Expired", "true");
                    filterContext.Result = new JsonResult
                    {
                        Data = new {
                            success        = false,
                            message        = "Tu sesión ha expirado. Por favor, inicia sesión nuevamente.",
                            redirectUrl    = "/Acceso/Login?expired=true",
                            sessionExpired = true
                        },
                        JsonRequestBehavior = JsonRequestBehavior.AllowGet
                    };
                }
                else
                {
                    // Para peticiones normales, redirigir con mensaje
                    var urlHelper = new UrlHelper(filterContext.RequestContext);
                    var loginUrl = urlHelper.Action("Login", "Acceso", new { expired = true });
                    
                    filterContext.Result = new RedirectResult(loginUrl);
                }
            }
            else
            {
                // ========== VERIFICAR SI LA SESIÓN ESTÁ POR EXPIRAR ==========
                var sessionInfo = UsuarioSesionHelper.ObtenerInfoSesion();
                if (sessionInfo?.EstaPorExpirar == true)
                {
                    // Agregar header para notificar al cliente sobre proximidad de expiración
                    filterContext.HttpContext.Response.Headers.Add("X-Session-Warning", "true");
                    filterContext.HttpContext.Response.Headers.Add("X-Minutes-Remaining", sessionInfo.MinutosRestantes.ToString("0"));
                }
            }
            
            base.OnActionExecuting(filterContext);
        }
    }
}