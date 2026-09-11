using System.Web.Mvc;
using bufinscustomers.Helpers;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Filtro global (registrado en FilterConfig) que puebla ViewBag.Escenarios en cada request,
    /// igual que EmpresasViewBagFilter hace con ViewBag.Empresas. El catálogo de escenarios es
    /// el mismo para todas las empresas (sin alcance por empresa/usuario), así que aquí no hay
    /// filtrado adicional — solo se expone la lista cacheada.
    /// </summary>
    public class EscenariosViewBagFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            filterContext.Controller.ViewBag.Escenarios = EscenarioCacheHelper.ObtenerEscenariosCacheados();
            base.OnActionExecuting(filterContext);
        }

        public static void Invalidar()
        {
            EscenarioCacheHelper.Invalidar();
        }
    }
}
