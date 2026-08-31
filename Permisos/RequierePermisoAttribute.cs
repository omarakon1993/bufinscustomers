using bufinscustomers.Helpers;
using System;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Permisos
{
    /// <summary>
    /// Exige que el usuario tenga al menos uno de los códigos de permiso indicados antes de
    /// ejecutar la acción. Super Admin (Admin = 2) siempre pasa, porque
    /// <see cref="UsuarioSesionHelper.TienePermiso(string)"/> devuelve <c>true</c> para él.
    ///
    /// Reemplaza al check inline repetido
    /// <c>if (!EsSuperAdmin() &amp;&amp; !TienePermiso("CODE")) { ... }</c>: al ser un filtro,
    /// el framework corta la petición antes de entrar a la acción y no depende de que el
    /// desarrollador recuerde escribir el <c>if</c>.
    ///
    /// Uso:
    ///   <c>[RequierePermiso("ADMIN_USUARIOS_GESTOR")]</c>            — un permiso
    ///   <c>[RequierePermiso("ADMIN_EMPRESAS_GESTOR,ADMIN_REPORTES_GESTOR")]</c> — cualquiera de varios
    ///
    /// Respuesta cuando falta el permiso:
    ///   - petición AJAX  → JSON <c>{ success = false, forbidden = true, message }</c> con HTTP 403.
    ///   - petición normal → redirección a <c>~/Error/Forbidden</c> (HTTP 403).
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
                    http.Response.TrySkipIisCustomErrors = true;
                    filterContext.Result = new JsonResult
                    {
                        Data = new
                        {
                            success   = false,
                            forbidden = true,
                            message   = R("Err_403_Json")
                        },
                        JsonRequestBehavior = JsonRequestBehavior.AllowGet
                    };
                }
                else
                {
                    filterContext.Result = new RedirectResult("~/Error/Forbidden");
                }
            }

            base.OnActionExecuting(filterContext);
        }

        private static string R(string key) =>
            System.Web.HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;
    }
}
