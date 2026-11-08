using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Caching.Memory;

namespace bufinscustomers.Compat
{
    // ==================================================================================================
    // Cache clasica de ASP.NET (HttpRuntime.Cache) sobre MemoryCache, con la misma forma de uso.
    // ==================================================================================================

    /// <summary>Constantes de System.Web.Caching.Cache.</summary>
    public static class Cache
    {
        public static readonly TimeSpan NoSlidingExpiration = TimeSpan.Zero;
        public static readonly DateTime NoAbsoluteExpiration = DateTime.MaxValue;
    }

    /// <summary>Reemplazo de System.Web.HttpRuntime.</summary>
    public static class HttpRuntime
    {
        public static readonly CacheCompat Cache = new CacheCompat();
        public static string AppDomainAppPath { get { return AppPaths.Raiz + System.IO.Path.DirectorySeparatorChar; } }
    }

    /// <summary>Reemplazo de System.Web.Hosting.HostingEnvironment.</summary>
    public static class HostingEnvironment
    {
        public static string MapPath(string rutaVirtual) { return AppPaths.Map(rutaVirtual); }
        public static string ApplicationPhysicalPath { get { return AppPaths.Raiz + System.IO.Path.DirectorySeparatorChar; } }
        public static bool IsHosted { get { return true; } }

        /// <summary>Equivale a HostingEnvironment.QueueBackgroundWorkItem: ejecuta el trabajo en segundo plano; nunca lanza hacia el llamador.</summary>
        public static void QueueBackgroundWorkItem(Action<System.Threading.CancellationToken> trabajo)
        {
            System.Threading.Tasks.Task.Run(() => { try { trabajo(System.Threading.CancellationToken.None); } catch (Exception) { } });
        }

        public static void QueueBackgroundWorkItem(Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> trabajo)
        {
            System.Threading.Tasks.Task.Run(async () => { try { await trabajo(System.Threading.CancellationToken.None); } catch (Exception) { } });
        }
    }

    /// <summary>Cache.Insert / indexador / Remove con la firma de System.Web.Caching.Cache (la dependencia de archivo ya no existe: se pasa null).</summary>
    public sealed class CacheCompat
    {
        private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        public object this[string clave]
        {
            get { object v; return _cache.TryGetValue(clave, out v) ? v : null; }
            set { Insert(clave, value); }
        }

        public object Get(string clave) { return this[clave]; }

        public void Insert(string clave, object valor) { _cache.Set(clave, valor); }

        public void Insert(string clave, object valor, object dependencias, DateTime absoluta, TimeSpan deslizante)
        {
            var o = new MemoryCacheEntryOptions();
            if (absoluta != DateTime.MaxValue)
                o.AbsoluteExpiration = new DateTimeOffset(absoluta.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(absoluta, DateTimeKind.Local) : absoluta);
            if (deslizante != TimeSpan.Zero) o.SlidingExpiration = deslizante;
            _cache.Set(clave, valor, o);
        }

        public object Remove(string clave)
        {
            object v; _cache.TryGetValue(clave, out v);
            _cache.Remove(clave);
            return v;
        }
    }

    // ==================================================================================================
    // Cookies, cabeceras y cache de la respuesta (HttpResponseBase de MVC 5)
    // ==================================================================================================

    public class HttpCookie
    {
        public HttpCookie(string name) { Name = name; }
        public HttpCookie(string name, string value) { Name = name; Value = value; }
        public string Name { get; set; }
        public string Value { get; set; }
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public string Path { get; set; } = "/";
        public string Domain { get; set; }
        public DateTime Expires { get; set; } = DateTime.MinValue;
        public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;
    }

    public enum HttpCacheability { NoCache, Private, Public, Server, ServerAndNoCache, ServerAndPrivate }

    /// <summary>Response.Cache de MVC 5: construye la cabecera Cache-Control a partir de las llamadas Set*.</summary>
    public sealed class ResponseCacheCompat
    {
        private readonly HttpResponse _r;
        private string _cacheabilidad;
        private TimeSpan? _maxAge;
        private bool _noStore;
        public ResponseCacheCompat(HttpResponse r) { _r = r; }

        public void SetCacheability(HttpCacheability c)
        {
            _cacheabilidad = c == HttpCacheability.NoCache || c == HttpCacheability.ServerAndNoCache ? "no-cache"
                           : c == HttpCacheability.Public ? "public" : "private";
            if (_cacheabilidad == "no-cache")
            {
                _r.Headers["Pragma"] = "no-cache";
                _r.Headers["Expires"] = "-1";
            }
            Aplicar();
        }
        public void SetNoStore() { _noStore = true; Aplicar(); }
        public void SetMaxAge(TimeSpan t) { _maxAge = t; Aplicar(); }
        public void SetETag(string etag) { _r.Headers["ETag"] = etag; }
        public void SetLastModified(DateTime d) { _r.Headers["Last-Modified"] = d.ToUniversalTime().ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
        public void SetExpires(DateTime d) { _r.Headers["Expires"] = d.ToUniversalTime().ToString("R", System.Globalization.CultureInfo.InvariantCulture); }

        private void Aplicar()
        {
            var t = new List<string>();
            if (_cacheabilidad != null) t.Add(_cacheabilidad);
            if (_noStore) t.Add("no-store");
            if (_maxAge.HasValue) t.Add("max-age=" + (long)_maxAge.Value.TotalSeconds);
            if (t.Count > 0) _r.Headers["Cache-Control"] = string.Join(", ", t);
        }
    }

    public static class HttpResponseCompat
    {
        private const string Clave = "__bufins_cachecompat";

        extension(HttpResponse r)
        {
            public ResponseCacheCompat Cache
            {
                get
                {
                    var c = r.HttpContext.Items[Clave] as ResponseCacheCompat;
                    if (c == null) { c = new ResponseCacheCompat(r); r.HttpContext.Items[Clave] = c; }
                    return c;
                }
            }
            /// <summary>No existe en ASP.NET Core (no hay paginas de error de IIS que omitir): se ignora.</summary>
            public bool TrySkipIisCustomErrors { get => true; set { } }
            public void AddHeader(string nombre, string valor) { r.Headers[nombre] = valor; }
            public void AppendHeader(string nombre, string valor) { r.Headers.Append(nombre, valor); }
            public void SetCookie(HttpCookie c) { r.Cookies.Add(c); }
        }

        extension(IResponseCookies cookies)
        {
            public void Add(HttpCookie c)
            {
                var o = new CookieOptions { HttpOnly = c.HttpOnly, Secure = c.Secure, Path = c.Path ?? "/", Domain = c.Domain, SameSite = c.SameSite, IsEssential = true };
                if (c.Expires != DateTime.MinValue)
                    o.Expires = new DateTimeOffset(c.Expires.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(c.Expires, DateTimeKind.Local) : c.Expires);
                cookies.Append(c.Name, c.Value ?? "", o);
            }
        }

        /// <summary>JsonResult.MaxJsonLength de MVC 5: ASP.NET Core no limita el tamano del JSON, asi que se ignora.</summary>
        extension(JsonResult j)
        {
            public int MaxJsonLength { get => int.MaxValue; set { } }
        }

        // ---- ayudantes de vista: Html.AttributeEncode / Html.Encode / Json.Encode de MVC 5 ----
        extension(IHtmlHelper h)
        {
            public string AttributeEncode(string s) { return s == null ? null : System.Web.HttpUtility.HtmlAttributeEncode(s); }
            public string AttributeEncode(object o) { return o == null ? null : System.Web.HttpUtility.HtmlAttributeEncode(o.ToString()); }
            public string Encode(string s) { return s == null ? "" : System.Web.HttpUtility.HtmlEncode(s); }
        }

        extension(IJsonHelper j)
        {
            public string Encode(object o) { return new JavaScriptSerializer().Serialize(o); }
        }
    }
}
