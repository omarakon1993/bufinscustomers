using System;
using System.Linq;
using bufinscustomers.Helpers;

namespace bufinscustomers.Permisos
{
    /// <summary>
    /// Exige que el usuario tenga al menos uno de los codigos de permiso indicados (separados por coma, punto y coma o espacio):
    /// [RequierePermiso("CODE")] o [RequierePermiso("CODE1,CODE2")]. El Super Admin siempre pasa (lo resuelve TienePermiso).
    /// Peticion AJAX: JSON { success=false, forbidden=true } con HTTP 403; peticion normal: redirige a ~/Error/Forbidden.
    /// </summary>
    public class RequierePermisoAttribute : ActionFilterAttribute
    {
        public string[] CodigosPermiso { get; }

        public RequierePermisoAttribute(string codigoPermiso)
        {
            CodigosPermiso = (codigoPermiso ?? "")
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToArray();
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            bool autorizado = CodigosPermiso.Length == 0
                || CodigosPermiso.Any(UsuarioSesionHelper.TienePermiso);

            if (!autorizado)
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
