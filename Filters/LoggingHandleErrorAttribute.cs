using System.Web.Mvc;
using bufinscustomers.Helpers;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Igual que <see cref="HandleErrorAttribute"/> (deja que <c>customErrors</c> muestre
    /// <c>~/Views/Shared/Error.cshtml</c>), pero registra la excepción en <see cref="AppLogger"/>
    /// antes de delegar. Se registra como filtro global en <c>App_Start/FilterConfig.cs</c>
    /// en lugar del <c>HandleErrorAttribute</c> estándar.
    /// </summary>
    public class LoggingHandleErrorAttribute : HandleErrorAttribute
    {
        public override void OnException(ExceptionContext filterContext)
        {
            if (filterContext?.Exception != null && !filterContext.ExceptionHandled)
            {
                string ctx = null;
                var rd = filterContext.RouteData?.Values;
                if (rd != null)
                    ctx = $"{rd["controller"]}/{rd["action"]}";

                AppLogger.Error(filterContext.Exception, ctx);
            }

            base.OnException(filterContext);
        }
    }
}
