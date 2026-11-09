using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace bufinscustomers
{
    /// <summary>
    /// Cabeceras de seguridad (antes en Web.config): HSTS, X-Frame-Options, etc. y la política de contenido (CSP).
    ///
    /// CSP de scripts ESTRICTA (por defecto): <c>script-src 'self' 'nonce-XXXX'</c> con un nonce NUEVO en cada petición. Solo se ejecutan los
    /// archivos del propio sitio y los bloques en línea que lleven ese nonce (los pone <c>NonceScriptTagHelper</c> en todos los
    /// &lt;script&gt; de las vistas). Los atributos de evento (onclick="…") ya no se ejecutan: <c>InlineHandlerTagHelper</c> los convierte
    /// en <c>data-h-*</c> y <c>Assets/js/inline-handlers.js</c> los atiende. Las violaciones se reportan a <c>/CspReport/Recibir</c>.
    ///
    /// Interruptor de emergencia: el parámetro <c>CspScriptEstricto</c> de <c>ConfiguracionSistema</c> en «false» (Configuraciones generales
    /// BUFINS) devuelve la política anterior (<c>'unsafe-inline'</c>) en menos de un minuto, sin republicar.
    /// Si una respuesta ya trae su propia CSP (visor de documentación, descargas) se respeta y no se sobrescribe.
    /// </summary>
    public class CabecerasSeguridad
    {
        private const string ClaveNonce = "csp-nonce";
        private static readonly TimeSpan VigenciaInterruptor = TimeSpan.FromSeconds(60);
        private static DateTime _interruptorLeido = DateTime.MinValue;
        private static bool _estricta = true;
        private static readonly object _candado = new object();

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
        };

        // El resto de la política es igual en las dos variantes; solo cambia script-src.
        private const string PoliticaBase = "default-src 'self'; {SCRIPT}; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: blob:; connect-src 'self' https://open.er-api.com; frame-src https:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'; report-uri /CspReport/Recibir";

        private readonly RequestDelegate _next;
        public CabecerasSeguridad(RequestDelegate next) { _next = next; }

        /// <summary>Nonce de la petición actual (null fuera de una petición o si la CSP estricta está apagada).</summary>
        public static string Nonce(HttpContext ctx)
        {
            return ctx != null && ctx.Items.TryGetValue(ClaveNonce, out var n) ? n as string : null;
        }

        /// <summary>true si la CSP estricta de scripts está vigente (parámetro <c>CspScriptEstricto</c>; por defecto sí). Se lee cada 60 s.</summary>
        public static bool Estricta()
        {
            if (DateTime.UtcNow - _interruptorLeido < VigenciaInterruptor) return _estricta;
            lock (_candado)
            {
                if (DateTime.UtcNow - _interruptorLeido < VigenciaInterruptor) return _estricta;
                bool valor = true;
                try
                {
                    var v = new bufinscustomers.Services.ConfiguracionSistemaService().ObtenerValor("CspScriptEstricto");
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        v = v.Trim().ToLowerInvariant();
                        valor = !(v == "false" || v == "0" || v == "no" || v == "off");
                    }
                }
                catch { /* sin BD: se mantiene la política estricta */ }
                _estricta = valor;
                _interruptorLeido = DateTime.UtcNow;
                return valor;
            }
        }

        public Task InvokeAsync(HttpContext ctx)
        {
            bool estricta = Estricta();
            string nonce = null;
            if (estricta)
            {
                var bytes = RandomNumberGenerator.GetBytes(16);
                nonce = Convert.ToBase64String(bytes);
                ctx.Items[ClaveNonce] = nonce;
            }

            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                foreach (var kv in Cabeceras) h[kv.Key] = kv.Value;
                if (!h.ContainsKey("Content-Security-Policy"))
                {
                    string script = estricta ? "script-src 'self' 'nonce-" + nonce + "'" : "script-src 'self' 'unsafe-inline'";
                    h["Content-Security-Policy"] = PoliticaBase.Replace("{SCRIPT}", script);
                }
                h.Remove("Content-Security-Policy-Report-Only");
                h.Remove("Server");
                return Task.CompletedTask;
            });
            return _next(ctx);
        }
    }
}
