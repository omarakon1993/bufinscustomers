using System.Web.Optimization;

namespace bufinscustomers
{
    public class BundleConfig
    {
        public static void RegisterBundles(BundleCollection bundles)
        {
            // CSS custom — los 4 archivos sin referencias a fuentes externas
            bundles.Add(new StyleBundle("~/bundles/css-custom").Include(
                "~/Assets/css/responsive-custom.css",
                "~/Assets/css/modern-sidebar.css",
                "~/Assets/css/bufins-components.css",
                "~/Assets/css/layout.css",
                "~/Assets/css/modelo-consola.css"));

            // JS global (orden es crítico: jQuery → Bootstrap → plugins → custom)
            bundles.Add(new ScriptBundle("~/bundles/js-global").Include(
                "~/Assets/Bootstrap/jquery/jquery.min.js",
                "~/Assets/Bootstrap/bootstrap/js/bootstrap.bundle.min.js",
                "~/Assets/Bootstrap/jquery-easing/jquery.easing.min.js",
                "~/Assets/js/sb-admin-2.min.js",
                "~/Assets/js/modern-sidebar.js"));

            // Forzar bundling independientemente del modo debug
            BundleTable.EnableOptimizations = true;
        }
    }
}
