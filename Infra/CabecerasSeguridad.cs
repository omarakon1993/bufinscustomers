using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace bufinscustomers
{
    /// <summary>
    /// Cabeceras de seguridad que antes vivian en Web.config (system.webServer/httpProtocol/customHeaders), con los mismos valores:
    /// HSTS, CSP (aplicada) y CSP en modo reporte. Se anaden al empezar a responder, asi tambien salen en estaticos, 304 y errores.
    /// </summary>
    public class CabecerasSeguridad
    {
        private static readonly Dictionary<string, string> Cabeceras = new Dictionary<string, string>
        {
            { "X-Content-Type-Options", "nosniff" },
            { "X-Frame-Options", "SAMEORIGIN" },
            { "Referrer-Policy", "strict-origin-when-cross-origin" },
            { "Strict-Transport-Security", "max-age=31536000; includeSubDomains" },
            { "Permissions-Policy", "camera=(), microphone=(), geolocation=()" },
            { "X-XSS-Protection", "0" },
            { "Cross-Origin-Opener-Policy", "same-origin" },
            { "X-Permitted-Cross-Domain-Policies", "none" },
            { "Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: blob:; connect-src 'self' https://open.er-api.com; frame-src https:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'" },
            { "Content-Security-Policy-Report-Only", "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: blob:; connect-src 'self' https://open.er-api.com; frame-src https:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'; report-uri /CspReport/Recibir" },
        };

        private readonly RequestDelegate _next;
        public CabecerasSeguridad(RequestDelegate next) { _next = next; }

        public Task InvokeAsync(HttpContext ctx)
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                foreach (var kv in Cabeceras) h[kv.Key] = kv.Value;
                h.Remove("Server");
                return Task.CompletedTask;
            });
            return _next(ctx);
        }
    }
}
