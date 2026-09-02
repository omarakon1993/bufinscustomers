using System.Web;
using System.Web.Mvc;
using bufinscustomers.Filters;

namespace bufinscustomers
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            // Igual que HandleErrorAttribute, pero registra la excepción en AppLogger.
            filters.Add(new LoggingHandleErrorAttribute());
            filters.Add(new EmpresasViewBagFilter()); // <-- Aquí agregas tu filtro
            filters.Add(new RegistroNavegacionFilter()); // registra las visitas a páginas (AuditoriaNavegacion)
        }
    }
}
