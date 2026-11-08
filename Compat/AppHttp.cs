using System;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace bufinscustomers.Compat
{
    /// <summary>
    /// Reemplazo de <c>System.Web.HttpContext.Current</c>: el contexto de la petición en curso (null fuera de una petición,
    /// igual que antes). Se alimenta con IHttpContextAccessor desde Program.cs.
    /// </summary>
    public static class AppHttp
    {
        private static IHttpContextAccessor _accesor;
        public static void Iniciar(IHttpContextAccessor accesor) { _accesor = accesor; }

        /// <summary>Contexto nativo de ASP.NET Core de la petición actual, o null.</summary>
        public static HttpContext Raw { get { return _accesor == null ? null : _accesor.HttpContext; } }

        /// <summary>Equivale a HttpContext.Current: ofrece Session, Request, Response, Items y Server con la forma conocida.</summary>
        public static HttpContextCompat Current
        {
            get
            {
                var raw = Raw;
                if (raw == null) return null;
                var c = raw.Items["__bufins_ctx"] as HttpContextCompat;
                if (c == null) { c = new HttpContextCompat(raw); raw.Items["__bufins_ctx"] = c; }
                return c;
            }
        }
    }

    public sealed class HttpContextCompat
    {
        private readonly HttpContext _raw;
        public HttpContextCompat(HttpContext raw) { _raw = raw; Session = new SesionCompat(raw); }

        public HttpContext Raw { get { return _raw; } }
        public SesionCompat Session { get; private set; }
        public HttpRequest Request { get { return _raw.Request; } }
        public HttpResponse Response { get { return _raw.Response; } }
        public System.Collections.Generic.IDictionary<object, object> Items { get { return _raw.Items; } }
        public System.Security.Claims.ClaimsPrincipal User { get { return _raw.User; } }
        public ServerCompat Server { get { return ServerCompat.Instancia; } }
    }

    /// <summary>Reemplazo de HttpServerUtility (Server.MapPath).</summary>
    public sealed class ServerCompat
    {
        public static readonly ServerCompat Instancia = new ServerCompat();
        public string MapPath(string rutaVirtual) { return AppPaths.Map(rutaVirtual); }
    }
}
