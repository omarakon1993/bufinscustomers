using System.Web;
using System.Web.Optimization;

namespace bufinscustomers
{
    public class BundleConfig
    {
        public static void RegisterBundles(BundleCollection bundles)
        {
            // jQuery
            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                        "~/Assets/Bootstrap/jquery/jquery-{version}.js"));  // <-- ajustado

            // jQuery Validate
            bundles.Add(new ScriptBundle("~/bundles/jqueryval").Include(
                        "~/Assets/Scripts/jquery.validate*"));  // <-- si está en Scripts, está bien

            // Modernizr
            bundles.Add(new ScriptBundle("~/bundles/modernizr").Include(
                        "~/Assets/Scripts/modernizr-*"));  // <-- si está en Scripts, está bien

            // Bootstrap JS
            bundles.Add(new ScriptBundle("~/bundles/bootstrap").Include(
                        "~/Assets/Bootstrap/bootstrap/bootstrap.js"));  // <-- ajustado

            // Estilos CSS
            bundles.Add(new StyleBundle("~/Assets/Content/css").Include(
                        "~/Assets/css/bootstrap.css",
                        "~/Assets/css/site.css"));  // <-- esta es válida si los CSS están en /Assets/css
        }


    }
}
