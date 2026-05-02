using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace bufinscustomers
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            // Soluci�n al error CS1061: Usar el m�todo correcto para establecer la licencia no comercial
            ExcelPackage.License.SetNonCommercialOrganization("bufinscustomers");
        }

        protected void Application_AcquireRequestState(object sender, EventArgs e)
        {
            string lang = "es-CO";
            var cookie = Request.Cookies["lang"];
            if (cookie != null && (cookie.Value == "es-CO" || cookie.Value == "en-US"))
                lang = cookie.Value;

            var culture = new CultureInfo(lang);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
    }
}
