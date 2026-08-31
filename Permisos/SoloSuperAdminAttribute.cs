using bufinscustomers.Helpers;
using System.Web.Mvc;

namespace bufinscustomers.Permisos
{
    /// <summary>
    /// Exige Super Admin (Admin = 2). Reemplaza al check inline repetido
    /// <c>if (!UsuarioSesionHelper.EsSuperAdmin()) return RedirectToAction("Index", "Home");</c>
    /// en los controladores 100% reservados a Super Admin (gestores de menú, categorías, grupos
    /// de menú, widgets, prompts IA, modelos de ejecución, configuración global de IA).
    ///
    /// Se pone a nivel de clase, junto a <c>[ValidarSesion]</c>. Ante falta de permiso:
    ///   - AJAX  → JSON <c>{ success = false, forbidden = true }</c> + HTTP 403.
    ///   - normal → redirección a <c>~/Error/Forbidden</c> (HTTP 403).
    ///
    /// Para gates que además admiten Admin de Empresa o dependen de acceso por empresa/grupo,
    /// NO usar este atributo: esos siguen con lógica inline (es correcto, no es deuda).
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
