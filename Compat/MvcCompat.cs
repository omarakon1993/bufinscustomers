using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace bufinscustomers.Compat
{
    /// <summary>Equivale a System.Web.Mvc.JsonRequestBehavior: se conserva para no editar las ~130 llamadas existentes; hoy no cambia nada.</summary>
    public enum JsonRequestBehavior { AllowGet, DenyGet }

    /// <summary>System.Web.Mvc.HttpStatusCodeResult (con descripcion opcional).</summary>
    public class HttpStatusCodeResult : StatusCodeResult
    {
        public string StatusDescription { get; private set; }
        public HttpStatusCodeResult(int statusCode) : base(statusCode) { }
        public HttpStatusCodeResult(int statusCode, string statusDescription) : base(statusCode) { StatusDescription = statusDescription; }
        public HttpStatusCodeResult(System.Net.HttpStatusCode statusCode) : base((int)statusCode) { }
        public HttpStatusCodeResult(System.Net.HttpStatusCode statusCode, string statusDescription) : base((int)statusCode) { StatusDescription = statusDescription; }
    }

    public class HttpNotFoundResult : HttpStatusCodeResult
    {
        public HttpNotFoundResult() : base(404) { }
        public HttpNotFoundResult(string descripcion) : base(404, descripcion) { }
    }

    public class HttpUnauthorizedResult : HttpStatusCodeResult
    {
        public HttpUnauthorizedResult() : base(401) { }
        public HttpUnauthorizedResult(string descripcion) : base(401, descripcion) { }
    }

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
