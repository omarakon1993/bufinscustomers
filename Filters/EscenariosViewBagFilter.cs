using bufinscustomers.Helpers;

namespace bufinscustomers.Filters
{
    /// <summary>Filtro global: puebla ViewBag.Escenarios (catalogo de escenarios de datos) en cada peticion.</summary>
    public class EscenariosViewBagFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var vb = filterContext.ViewBagDe();
            if (vb != null) vb.Escenarios = EscenarioCacheHelper.ObtenerEscenariosCacheados();
            base.OnActionExecuting(filterContext);
        }

        public static void Invalidar()
        {
            EscenarioCacheHelper.Invalidar();
        }
    }
}
