using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NUglify;

namespace bufinscustomers
{
    /// <summary>
    /// Paquetes de CSS/JS (reemplazo mínimo de System.Web.Optimization). Cada paquete concatena y minifica sus archivos,
    /// se sirve en su ruta virtual (/Assets/css/base, /bundles/obs-core…) con una versión (?v=hash) y caché larga.
    /// Se conserva porque el IIS del hosting no comprime los estáticos sueltos: los paquetes sí pasan por la compresión de la aplicación.
    /// El CSS vive bajo ~/Assets/css/ para que sus url(../fonts|../webfonts) sigan valiendo.
    /// </summary>
    public static class Bundles
    {
        public class Paquete
        {
            public string Ruta;        // ruta virtual sin "~", p. ej. /bundles/obs-core
            public bool EsCss;
            public string[] Archivos;  // rutas virtuales con "~/"
        }

        private static readonly Dictionary<string, Paquete> Registro = new Dictionary<string, Paquete>(StringComparer.OrdinalIgnoreCase);
        private class Contenido { public string Texto; public string Version; public string Firma; }
        private static readonly ConcurrentDictionary<string, Contenido> Cache = new ConcurrentDictionary<string, Contenido>(StringComparer.OrdinalIgnoreCase);
        private static bool _minificar = true;

        private static void Css(string ruta, params string[] archivos) { Registro[ruta] = new Paquete { Ruta = ruta, EsCss = true, Archivos = archivos }; }
        private static void Js(string ruta, params string[] archivos) { Registro[ruta] = new Paquete { Ruta = ruta, EsCss = false, Archivos = archivos }; }

        static Bundles()
        {
            // CSS propio del portal (en este orden)
            Css("/bundles/css-custom", "~/Assets/css/responsive-custom.css", "~/Assets/css/modern-sidebar.css", "~/Assets/css/bufins-components.css",
                "~/Assets/css/page-header.css", "~/Assets/css/layout.css", "~/Assets/css/modelo-consola.css", "~/Assets/css/bufins-modal.css");

            // JS global (el orden es critico: jQuery -> Bootstrap -> plugins -> propio)
            Js("/bundles/js-global", "~/Assets/Bootstrap/jquery/jquery.min.js", "~/Assets/Bootstrap/bootstrap/js/bootstrap.bundle.min.js",
                "~/Assets/Bootstrap/jquery-easing/jquery.easing.min.js", "~/Assets/js/sb-admin-2.min.js", "~/Assets/js/modern-sidebar.js", "~/Assets/js/inline-handlers.js");
        }

        public static void Configurar(IHostEnvironment env) { _minificar = true; }   // el sitio anterior forzaba el bundling en cualquier modo

        private static string Firma(Paquete p)
        {
            return string.Join("|", p.Archivos.Select(a =>
            {
                var f = new FileInfo(AppPaths.Map(a));
                return f.Exists ? f.LastWriteTimeUtc.Ticks + ":" + f.Length : "-";
            }));
        }

        private static Contenido Construir(Paquete p)
        {
            var firma = Firma(p);
            Contenido c;
            if (Cache.TryGetValue(p.Ruta, out c) && c.Firma == firma) return c;
            var sb = new StringBuilder();
            foreach (var a in p.Archivos)
            {
                var ruta = AppPaths.Map(a);
                if (!File.Exists(ruta)) continue;
                var texto = File.ReadAllText(ruta, Encoding.UTF8);
                if (_minificar && !ruta.Contains(".min."))
                {
                    var r = p.EsCss ? Uglify.Css(texto) : Uglify.Js(texto);
                    if (!r.HasErrors) texto = r.Code;
                }
                sb.Append(texto).Append(p.EsCss ? "\n" : "\n;\n");
            }
            var t = sb.ToString();
            string hash;
            using (var sha = SHA256.Create())
                hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(t))).Replace('+', '-').Replace('/', '_').TrimEnd('=').Substring(0, 12);
            c = new Contenido { Texto = t, Version = hash, Firma = firma };
            Cache[p.Ruta] = c;
            return c;
        }

        /// <summary>URL del paquete con su versión (?v=hash).</summary>
        public static string Url(string rutaVirtual)
        {
            var ruta = rutaVirtual.TrimStart('~');
            Paquete p;
            if (!Registro.TryGetValue(ruta, out p)) return ruta;
            return ruta + "?v=" + Construir(p).Version;
        }

        public static bool EsCss(string rutaVirtual)
        {
            Paquete p;
            return Registro.TryGetValue(rutaVirtual.TrimStart('~'), out p) && p.EsCss;
        }

        /// <summary>Middleware: responde las rutas de los paquetes con el contenido concatenado y minificado.</summary>
        public static IApplicationBuilder UsarBundles(this IApplicationBuilder app)
        {
            return app.Use(async (ctx, next) =>
            {
                Paquete p;
                if (!(HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) || !Registro.TryGetValue(ctx.Request.Path.Value ?? "", out p)) { await next(); return; }
                var c = Construir(p);
                var etag = "\"" + c.Version + "\"";
                ctx.Response.Headers["ETag"] = etag;
                ctx.Response.Headers["Cache-Control"] = ctx.Request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "no-cache";
                if (ctx.Request.Headers.IfNoneMatch.ToString() == etag) { ctx.Response.StatusCode = 304; return; }
                ctx.Response.ContentType = p.EsCss ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";
                await ctx.Response.WriteAsync(c.Texto, Encoding.UTF8);
            });
        }
    }

    /// <summary>@Scripts.Render("~/bundles/x") como en MVC 5.</summary>
    public static class Scripts
    {
        public static IHtmlContent Render(params string[] rutas)
        {
            return new HtmlString(string.Concat(rutas.Select(r => "<script src=\"" + Bundles.Url(r) + "\"></script>\n")));
        }
    }

    /// <summary>@Styles.Render("~/bundles/x") como en MVC 5.</summary>
    public static class Styles
    {
        public static IHtmlContent Render(params string[] rutas)
        {
            return new HtmlString(string.Concat(rutas.Select(r => "<link href=\"" + Bundles.Url(r) + "\" rel=\"stylesheet\"/>\n")));
        }
    }
}
