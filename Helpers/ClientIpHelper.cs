using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Web;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Resuelve la IP real del cliente. Por defecto devuelve <see cref="HttpRequestBase.UserHostAddress"/>
    /// (la conexión TCP directa). Si la petición llega a través de un proxy inverso / balanceador /
    /// Cloudflare, ese valor es la IP del proxy, no la del visitante — para recuperar la IP real hay
    /// que leer una cabecera (<c>X-Forwarded-For</c> o <c>CF-Connecting-IP</c>), pero SOLO se puede
    /// confiar en ella cuando la conexión directa proviene de un proxy conocido; de lo contrario un
    /// atacante falsea la cabecera y evade el rate-limit por IP.
    ///
    /// Configuración en Web.config (appSettings):
    ///   TrustedProxies         Lista separada por comas de IPs o rangos CIDR de los proxies de
    ///                          confianza (p. ej. "127.0.0.1, 10.0.0.0/8, 173.245.48.0/20").
    ///                          Vacío (por defecto) = no se confía en ninguna cabecera → comportamiento
    ///                          idéntico al histórico (solo UserHostAddress).
    ///   TrustCloudflareHeader  "true" para leer "CF-Connecting-IP" (además de X-Forwarded-For)
    ///                          cuando el proxy directo es de confianza. Por defecto "false".
    /// </summary>
    public static class ClientIpHelper
    {
        private static readonly Lazy<IpRango[]> _proxiesConfianza =
            new Lazy<IpRango[]>(() => ParsearRangos(ConfigurationManager.AppSettings["TrustedProxies"]));

        private static readonly Lazy<bool> _confiarCloudflare = new Lazy<bool>(() =>
            string.Equals(ConfigurationManager.AppSettings["TrustCloudflareHeader"], "true",
                StringComparison.OrdinalIgnoreCase));

        /// <summary>IP real del cliente para la petición en curso, o "" si no hay contexto HTTP.</summary>
        public static string ObtenerIp()
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx?.Request == null) return "";
                return ObtenerIp(new HttpRequestWrapper(ctx.Request));
            }
            catch { return ""; }
        }

        public static string ObtenerIp(HttpRequestBase request)
        {
            if (request == null) return "";
            string directa = (request.UserHostAddress ?? "").Trim();

            IpRango[] proxies;
            try { proxies = _proxiesConfianza.Value; }
            catch { proxies = new IpRango[0]; }

            // Sin proxies de confianza, o la conexión directa no es uno de ellos: no se
            // confía en ninguna cabecera reenviada.
            if (proxies.Length == 0 || !EsConfianza(directa, proxies))
                return directa;

            // Cloudflare envía una sola IP: la del cliente.
            if (_confiarCloudflare.Value)
            {
                string cf = (request.Headers["CF-Connecting-IP"] ?? "").Trim();
                if (EsIpValida(cf)) return cf;
            }

            // X-Forwarded-For: "cliente, proxy1, proxy2". Se recorre de derecha a izquierda
            // saltando los proxies de confianza; el primer valor que no lo sea es el cliente.
            string xff = request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrWhiteSpace(xff))
            {
                var partes = xff.Split(',')
                                .Select(p => NormalizarIp(p.Trim()))
                                .Where(p => p.Length > 0)
                                .ToArray();
                for (int i = partes.Length - 1; i >= 0; i--)
                {
                    if (!EsIpValida(partes[i])) continue;
                    if (EsConfianza(partes[i], proxies)) continue;
                    return partes[i];
                }
            }

            return directa;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string NormalizarIp(string valor)
        {
            // quita el puerto de "1.2.3.4:5678" (IPv4) y los corchetes de "[::1]:5678" (IPv6)
            if (string.IsNullOrEmpty(valor)) return valor;
            if (valor.StartsWith("["))
            {
                int cierre = valor.IndexOf(']');
                return cierre > 0 ? valor.Substring(1, cierre - 1) : valor;
            }
            int primerDosPuntos = valor.IndexOf(':');
            if (primerDosPuntos > 0 && valor.IndexOf(':', primerDosPuntos + 1) < 0) // un solo ':' → IPv4:puerto
                return valor.Substring(0, primerDosPuntos);
            return valor;
        }

        private static bool EsIpValida(string ip) => !string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out _);

        private static bool EsConfianza(string ip, IpRango[] rangos)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            foreach (var r in rangos)
                if (r.Contiene(addr)) return true;
            return false;
        }

        private static IpRango[] ParsearRangos(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new IpRango[0];
            return csv.Split(',')
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .Select(IpRango.Parsear)
                      .Where(r => r != null)
                      .ToArray();
        }

        /// <summary>Un IP suelto o un rango CIDR, con comparación por prefijo de bits.</summary>
        private sealed class IpRango
        {
            private readonly byte[] _red;
            private readonly int _bits;

            private IpRango(byte[] red, int bits) { _red = red; _bits = bits; }

            public static IpRango Parsear(string texto)
            {
                try
                {
                    int barra = texto.IndexOf('/');
                    string ipParte = barra >= 0 ? texto.Substring(0, barra) : texto;
                    if (!IPAddress.TryParse(ipParte, out var ip)) return null;
                    byte[] bytes = ip.GetAddressBytes();
                    int bits = barra >= 0 ? int.Parse(texto.Substring(barra + 1)) : bytes.Length * 8;
                    if (bits < 0 || bits > bytes.Length * 8) return null;
                    return new IpRango(bytes, bits);
                }
                catch { return null; }
            }

            public bool Contiene(IPAddress ip)
            {
                byte[] bytes = ip.GetAddressBytes();
                if (bytes.Length != _red.Length) return false; // familias distintas (IPv4 vs IPv6)
                int bytesCompletos = _bits / 8;
                for (int i = 0; i < bytesCompletos; i++)
                    if (bytes[i] != _red[i]) return false;
                int restoBits = _bits % 8;
                if (restoBits == 0) return true;
                int mascara = 0xFF << (8 - restoBits) & 0xFF;
                return (bytes[bytesCompletos] & mascara) == (_red[bytesCompletos] & mascara);
            }
        }
    }
}
