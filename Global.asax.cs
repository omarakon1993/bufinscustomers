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
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            Exception ex = Server.GetLastError();
            if (ex == null) return;

            // Desenvuelve HttpUnhandledException para obtener la excepción real
            if (ex is HttpUnhandledException && ex.InnerException != null)
                ex = ex.InnerException;

            // Red de seguridad: excepciones que no pasaron por LoggingHandleErrorAttribute
            // (errores de routing, de la propia vista de error, etc.).
            bufinscustomers.Helpers.AppLogger.Error(ex, "Application_Error");
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
