using bufinscustomers.Helpers;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Registra en AppLogger toda excepcion no controlada de la tuberia MVC y la deja seguir: la pagina de error de la marca
    /// la muestra el manejador de excepciones (UseExceptionHandler("/Error")). Equivale al HandleErrorAttribute de MVC 5.
    /// </summary>
    public class LoggingHandleErrorAttribute : ExceptionFilterAttribute
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
