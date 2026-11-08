using bufinscustomers.Helpers;

namespace bufinscustomers.Permisos
{
    /// <summary>
    /// Restringe una accion o un controlador completo al Super Admin (Admin = 2). Mismo contrato que RequierePermiso:
    /// AJAX recibe JSON { success=false, forbidden=true } con HTTP 403; una peticion normal se redirige a ~/Error/Forbidden.
    /// </summary>
    public class SoloSuperAdminAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var http = filterContext.HttpContext;

                if (http.Request.IsAjaxRequest())
                {
                    http.Response.StatusCode = 403;
                    filterContext.Result = new JsonResult(new
                    {
                        success = false,
                        forbidden = true,
                        message = Recursos.Obtener("Err_403_Json")
                    });
                }
                else
                {
                    filterContext.Result = new RedirectResult("~/Error/Forbidden");
                }
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
