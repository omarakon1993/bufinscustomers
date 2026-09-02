using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Política de contraseñas única de la aplicación. Devuelve CLAVES de recurso (no texto ya
    /// traducido) para que cada controlador las resuelva con su helper <c>R(...)</c> y se respete
    /// la cultura activa. La usan AccesoController (registro / restablecer clave) y
    /// UsuarioController (crear usuario / cambiar clave).
    ///
    /// Requisitos: mínimo <see cref="LongitudMinima"/> caracteres, con minúscula, mayúscula,
    /// número y carácter especial; sin 3+ caracteres idénticos seguidos; que no sea una clave de
    /// uso común ni contenga el nombre de usuario; y — salvo que <c>HibpCheckEnabled</c> esté en
    /// "false" — que no aparezca en la base de contraseñas filtradas de Have I Been Pwned
    /// (comprobación k-anonymity: solo se envían los primeros 5 caracteres del SHA-1, la clave
    /// nunca sale de aquí). Ante cualquier fallo de red la comprobación de filtración se omite
    /// ("fail open") para no bloquear al usuario por un problema ajeno.
    /// </summary>
    public static class PoliticaContrasena
    {
        public const int LongitudMinima = 12;

        private static readonly Regex _minuscula = new Regex("[a-z]", RegexOptions.Compiled);
        private static readonly Regex _mayuscula = new Regex("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex _numero    = new Regex("[0-9]", RegexOptions.Compiled);
        private static readonly Regex _especial  = new Regex("[^A-Za-z0-9]", RegexOptions.Compiled);
        private static readonly Regex _repetidos = new Regex(@"(.)\1\1", RegexOptions.Compiled);

        private static readonly HashSet<string> ClavesComunes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "12345678", "123456789", "1234567890", "password", "Password1", "Password123",
            "qwerty123", "qwertyui", "11111111", "00000000", "abcd1234", "abc12345",
            "contraseña", "contrasena", "Contrasena1", "Contraseña1", "admin123", "Admin123",
            "bufins123", "Bufins123", "12345678a", "a12345678", "iloveyou1", "letmein123",
            "welcome123", "changeme1", "usuario123", "colombia1", "Colombia1",
            "Password1!", "Qwerty123!", "Contrasena1!", "Admin123!", "12345678910",
            "abcdefgh1", "Abcdefgh1", "password1!", "P@ssword1", "P@ssw0rd", "Bufins123!",
            "Password123!", "Colombia123", "Colombia123!", "Bienvenido1", "Bienvenido1!"
        };

        private static readonly Lazy<HttpClient> _http = new Lazy<HttpClient>(() =>
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            c.DefaultRequestHeaders.Add("User-Agent", "bufinscustomers-password-policy");
            c.DefaultRequestHeaders.Add("Add-Padding", "true"); // respuesta de tamaño uniforme (HIBP)
            return c;
        });

        /// <summary>
        /// Valida <paramref name="clave"/>. <paramref name="errorKey"/> queda con la clave de
        /// recurso del primer incumplimiento, o <c>null</c> si la clave cumple la política.
        /// </summary>
        public static bool Validar(string clave, string usuario, out string errorKey, bool comprobarFiltracion = true)
        {
            if (string.IsNullOrEmpty(clave) || clave.Length < LongitudMinima) { errorKey = "Pwd_ErrorLongitud"; return false; }
            if (!_minuscula.IsMatch(clave)) { errorKey = "Pwd_ErrorMinuscula"; return false; }
            if (!_mayuscula.IsMatch(clave)) { errorKey = "Pwd_ErrorMayuscula"; return false; }
            if (!_numero.IsMatch(clave))    { errorKey = "Pwd_ErrorNumero";    return false; }
            if (!_especial.IsMatch(clave))  { errorKey = "Pwd_ErrorEspecial";  return false; }
            if (_repetidos.IsMatch(clave))  { errorKey = "Pwd_ErrorRepetidos"; return false; }
            if (ClavesComunes.Contains(clave)) { errorKey = "Pwd_ErrorComun"; return false; }
            if (!string.IsNullOrEmpty(usuario) && clave.IndexOf(usuario, StringComparison.OrdinalIgnoreCase) >= 0)
            { errorKey = "Pwd_ErrorContieneUsuario"; return false; }

            if (comprobarFiltracion && HibpHabilitado() && EstaEnFiltracion(clave))
            { errorKey = "Pwd_ErrorFiltrada"; return false; }

            errorKey = null;
            return true;
        }

        private static bool HibpHabilitado() =>
            !string.Equals(ConfigurationManager.AppSettings["HibpCheckEnabled"], "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Consulta Have I Been Pwned (Pwned Passwords, rango k-anonymity). Solo se transmiten los
        /// 5 primeros caracteres del SHA-1; el sufijo se compara localmente. Devuelve <c>false</c>
        /// ante cualquier error (red, timeout, respuesta no 2xx): no se bloquea al usuario.
        /// </summary>
        public static bool EstaEnFiltracion(string clave)
        {
            try
            {
                string sha1 = Sha1Hex(clave);
                string prefijo = sha1.Substring(0, 5);
                string sufijo  = sha1.Substring(5);

                string cuerpo = Task.Run(async () =>
                {
                    using (var resp = await _http.Value
                        .GetAsync("https://api.pwnedpasswords.com/range/" + prefijo)
                        .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }).GetAwaiter().GetResult();

                if (string.IsNullOrEmpty(cuerpo)) return false;

                foreach (var linea in cuerpo.Split('\n'))
                {
                    int sep = linea.IndexOf(':');
                    if (sep <= 0) continue;
                    if (string.Equals(linea.Substring(0, sep).Trim(), sufijo, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[PoliticaContrasena.EstaEnFiltracion] {0}", ex.Message);
            }
            return false;
        }

        private static string Sha1Hex(string texto)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(texto));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }
    }
}
