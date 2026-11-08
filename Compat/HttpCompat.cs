using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace bufinscustomers.Compat
{
    /// <summary>
    /// Propiedades de HttpRequestBase / HttpPostedFileBase de ASP.NET 4.x que el codigo existente usa (Request.UserAgent, Request.Url,
    /// archivo.InputStream...), expuestas como propiedades de extension (C# 14) sobre los tipos de ASP.NET Core. Asi no se edita cada uso.
    /// </summary>
    public static class HttpCompat
    {
        extension(HttpRequest r)
        {
            public string UserAgent => r.Headers.UserAgent.ToString();
            public string UserHostAddress => r.HttpContext.Ip();
            public string HttpMethod => r.Method;
            public Uri Url => new Uri(r.Scheme + "://" + r.Host.ToUriComponent() + r.Path.ToUriComponent() + r.QueryString.ToUriComponent());
            public Uri UrlReferrer
            {
                get
                {
                    Uri u;
                    return Uri.TryCreate(r.Headers.Referer.ToString(), UriKind.Absolute, out u) ? u : null;
                }
            }
            public string[] UserLanguages => r.Headers.AcceptLanguage.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            public string RawUrl => r.Path.ToUriComponent() + r.QueryString.ToUriComponent();
            public bool IsSecureConnection => r.IsHttps;
            public IFormFileCollection Files => r.HasFormContentType ? r.Form.Files : new FormFileCollection();
        }

        extension(IFormFile f)
        {
            public Stream InputStream => f.OpenReadStream();
            public int ContentLength => (int)f.Length;
        }
    }

    /// <summary>System.Web.Script.Serialization.JavaScriptSerializer sobre Newtonsoft, con el mismo formato (nombres tal cual, fechas "/Date(ms)/").</summary>
    public class JavaScriptSerializer
    {
        private static readonly JsonSerializerSettings Ajustes = new JsonSerializerSettings
        {
            DateFormatHandling = DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        public int MaxJsonLength { get; set; }
        public JavaScriptSerializer() { MaxJsonLength = 2097152; }

        public string Serialize(object objeto) { return JsonConvert.SerializeObject(objeto, Ajustes); }
        public T Deserialize<T>(string json) { return JsonConvert.DeserializeObject<T>(json, Ajustes); }
        public object DeserializeObject(string json) { return JsonConvert.DeserializeObject(json, Ajustes); }
    }

    /// <summary>Atributo de MVC 5 que ya no existe: la validacion de peticiones desaparece en ASP.NET Core (se conserva solo como marca).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ValidateInputAttribute : Attribute
    {
        public ValidateInputAttribute(bool habilitar) { }
    }
}
