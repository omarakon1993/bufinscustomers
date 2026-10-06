using System;
using System.Linq;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Validación y normalización de la página web de una empresa. Solo se aceptan sitios http/https con un
    /// dominio real (con punto y TLD de al menos 2 letras) o una IP; sin espacios ni caracteres que rompan HTML/JS.
    /// La regla del navegador (<c>Views/Configuracion/Empresas.cshtml</c>) replica esta misma lógica.
    /// </summary>
    public static class UrlWebHelper
    {
        public const int LongitudMaxima = 300;

        /// <summary>
        /// Normaliza (recorta; si no trae esquema antepone <c>https://</c>) y valida.
        /// Una entrada vacía es válida y devuelve <c>null</c> (el campo es opcional).
        /// </summary>
        public static bool TryNormalizar(string entrada, out string normalizada)
        {
            normalizada = null;
            if (string.IsNullOrWhiteSpace(entrada)) return true;

            string t = entrada.Trim();
            if (t.Length > LongitudMaxima) return false;
            if (t.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '\'', '"', '<', '>', '\\' }) >= 0) return false;

            if (t.IndexOf("://", StringComparison.Ordinal) < 0) t = "https://" + t;

            if (!Uri.TryCreate(t, UriKind.Absolute, out Uri uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;           // nada de usuario:clave@host

            string host = uri.Host;
            bool esIp = uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6;
            if (!esIp)
            {
                var etiquetas = host.Split('.');
                if (etiquetas.Length < 2) return false;                       // "localhost", "empresa" → no son sitios públicos
                if (etiquetas.Any(e => e.Length == 0 || e.StartsWith("-") || e.EndsWith("-"))) return false;
                if (etiquetas[etiquetas.Length - 1].Length < 2) return false;
            }

            normalizada = t.Length > LongitudMaxima ? null : t;
            return normalizada != null;
        }
    }
}
