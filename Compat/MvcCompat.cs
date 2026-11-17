using System.Globalization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace bufinscustomers.Compat
{
    public static class MvcCompatExtensions
    {
        /// <summary>HttpRequestBase.IsAjaxRequest() de MVC 5.</summary>
        public static bool IsAjaxRequest(this HttpRequest request)
        {
            return request != null && request.Headers["X-Requested-With"] == "XMLHttpRequest";
        }

        /// <summary>new UrlHelper(filterContext.RequestContext) de MVC 5.</summary>
        public static IUrlHelper UrlHelper(this ActionContext contexto)
        {
            return contexto.HttpContext.RequestServices.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(contexto);
        }

        /// <summary>Equivale a Controller.ViewBag de un filtro (filterContext.Controller.ViewBag).</summary>
        public static dynamic ViewBagDe(this ActionExecutingContext contexto)
        {
            var c = contexto.Controller as Controller;
            return c == null ? null : c.ViewBag;
        }
    }

    /// <summary>
    /// new ViewDataDictionary { { "clave", valor } } de MVC 5 empezaba vacio; en ASP.NET Core se hereda el ViewData de la pagina y Add falla si la clave
    /// ya existe (p. ej. Title). Esta variante sobrescribe el valor en vez de lanzar.
    /// </summary>
    public class ViewDataDictionaryEx : Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary
    {
        public ViewDataDictionaryEx(Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary origen) : base(origen) { }
        public new void Add(string clave, object valor) { this[clave] = valor; }
    }

    /// <summary>Textos de App_GlobalResources: reemplaza a HttpContext.GetGlobalResourceObject("Strings", clave).</summary>
    public static class Recursos
    {
        public static string Obtener(string clave)
        {
            if (string.IsNullOrEmpty(clave)) return clave;
            var texto = bufinscustomers.App_GlobalResources.Strings.ResourceManager.GetString(clave, CultureInfo.CurrentUICulture);
            return texto ?? clave;
        }
    }
}
