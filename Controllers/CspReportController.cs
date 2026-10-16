using System;
using System.IO;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using bufinscustomers.Helpers;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Recibe los informes de violaciones de la política CSP en modo solo-reporte
    /// (<c>Content-Security-Policy-Report-Only</c>, ver Web.config). No bloquea nada en el navegador: sirve para
    /// inventariar qué scripts en línea quedan antes de quitar <c>'unsafe-inline'</c> de la política vigente.
    /// Anónimo a propósito (el navegador lo envía solo, sin token), por eso está acotado: cuerpo máximo de 8 KB,
    /// tope por IP y global por hora, y cada violación distinta se registra una sola vez por hora en AppLogger.
    /// </summary>
    public class CspReportController : BaseController
    {
        private const int MaxBytes = 8 * 1024;
        private const int MaxPorIpHora = 120;
        private const int MaxGlobalHora = 2000;

        [HttpPost]
        public ActionResult Recibir()
        {
            try
            {
                if (Request.ContentLength > MaxBytes || !PermitirPeticion())
                    return new HttpStatusCodeResult(204);

                string cuerpo;
                using (var sr = new StreamReader(Request.InputStream))
                {
                    var buf = new char[MaxBytes + 1];
                    int n = sr.Read(buf, 0, buf.Length);
                    if (n > MaxBytes) return new HttpStatusCodeResult(204);
                    cuerpo = new string(buf, 0, n);
                }

                dynamic datos = Newtonsoft.Json.JsonConvert.DeserializeObject(cuerpo);
                dynamic r = datos?["csp-report"];
                if (r == null) return new HttpStatusCodeResult(204);

                string directiva = Limpiar((string)r["effective-directive"] ?? (string)r["violated-directive"], 60);
                string bloqueado = Limpiar((string)r["blocked-uri"], 120);
                string pagina = Limpiar(RutaSola((string)r["document-uri"]), 120);
                string origen = Limpiar(RutaSola((string)r["source-file"]), 120);
                string linea = Limpiar(Convert.ToString((object)r["line-number"]), 10);

                string clave = "csp_" + (directiva + "|" + bloqueado + "|" + pagina + "|" + origen + "|" + linea).GetHashCode();
                if (HttpRuntime.Cache[clave] != null) return new HttpStatusCodeResult(204);
                HttpRuntime.Cache.Insert(clave, 1, null, DateTime.Now.AddHours(1), Cache.NoSlidingExpiration);

                AppLogger.Info("directiva=" + directiva + " bloqueado=" + bloqueado + " pagina=" + pagina +
                               " origen=" + origen + ":" + linea, "CSP-REPORT");
            }
            catch (Exception)
            {
                // Un informe mal formado nunca debe generar errores ni ruido.
            }
            return new HttpStatusCodeResult(204);
        }

        private bool PermitirPeticion()
        {
            return Contar("csp_ip_" + ClientIpHelper.ObtenerIp(), MaxPorIpHora) && Contar("csp_global", MaxGlobalHora);
        }

        private static bool Contar(string clave, int tope)
        {
            lock (typeof(CspReportController))
            {
                int actual = HttpRuntime.Cache[clave] is int ? (int)HttpRuntime.Cache[clave] : 0;
                if (actual >= tope) return false;
                if (actual == 0)
                    HttpRuntime.Cache.Insert(clave, 1, null, DateTime.Now.AddHours(1), Cache.NoSlidingExpiration);
                else
                    HttpRuntime.Cache[clave] = actual + 1; // conserva la expiración original (reemplazo del valor)
                return true;
            }
        }

        private static string RutaSola(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return "";
            Uri u;
            return Uri.TryCreate(uri, UriKind.Absolute, out u) ? u.AbsolutePath : uri;
        }

        private static string Limpiar(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
