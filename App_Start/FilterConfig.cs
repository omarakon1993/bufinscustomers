using System.Web;
using System.Web.Mvc;

namespace bufinscustomers
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new EmpresasViewBagFilter()); // <-- Aquí agregas tu filtro
        }
    }
}
