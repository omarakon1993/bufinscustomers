using System;
using System.Security.Cryptography;
using System.Text;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// TOTP (RFC 6238) propio y sin dependencias: HMAC-SHA1, 6 dígitos, intervalo de 30 s —
    /// compatible con Google Authenticator, Microsoft Authenticator, Authy, 1Password, etc.
    /// Solo usa System.Security.Cryptography (idéntico en .NET Framework y .NET moderno).
    /// </summary>
    public static class TotpHelper
    {
        public const int Digitos = 6;
        public const int IntervaloSegundos = 30;
        private const string AlfabetoBase32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static byte[] GenerarSecreto(int bytes = 20)
        {
            var b = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        public static string ABase32(byte[] datos)
        {
            var sb = new StringBuilder((datos.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            foreach (byte x in datos)
            {
                buffer = (buffer << 8) | x; bits += 8;
                while (bits >= 5) { sb.Append(AlfabetoBase32[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(AlfabetoBase32[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static long PasoActual(DateTime? utc = null)
        {
            var t = utc ?? DateTime.UtcNow;
            return (long)Math.Floor((t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds / IntervaloSegundos);
        }

        public static string Codigo(byte[] secreto, long paso)
        {
            var contador = BitConverter.GetBytes(paso);
            if (BitConverter.IsLittleEndian) Array.Reverse(contador);
            byte[] h;
            using (var hmac = new HMACSHA1(secreto)) h = hmac.ComputeHash(contador);
            int o = h[h.Length - 1] & 0x0F;
            int bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
            return (bin % 1000000).ToString("D" + Digitos);
        }

        /// <summary>
        /// Valida el código con ventana ±1 intervalo (desfase de reloj). Devuelve el paso que
        /// coincidió (para guardarlo y no aceptar el mismo código dos veces) o -1 si no coincide.
        /// Comparación en tiempo constante.
        /// </summary>
        public static long Validar(byte[] secreto, string codigo, long? pasoMinimoExclusivo = null)
        {
            codigo = (codigo ?? "").Replace(" ", "").Trim();
            if (codigo.Length != Digitos) return -1;
            long actual = PasoActual();
            long encontrado = -1;
            for (long p = actual - 1; p <= actual + 1; p++)
            {
                bool igual = ComparacionConstante(Codigo(secreto, p), codigo);
                if (igual && (!pasoMinimoExclusivo.HasValue || p > pasoMinimoExclusivo.Value)) encontrado = p;
            }
            return encontrado;
        }

        public static string UriOtpAuth(string emisor, string cuenta, string secretoBase32)
        {
            return "otpauth://totp/" + Uri.EscapeDataString(emisor + ":" + cuenta) +
                   "?secret=" + secretoBase32 + "&issuer=" + Uri.EscapeDataString(emisor) +
                   "&algorithm=SHA1&digits=" + Digitos + "&period=" + IntervaloSegundos;
        }

        private static bool ComparacionConstante(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int r = 0;
            for (int i = 0; i < a.Length; i++) r |= a[i] ^ b[i];
            return r == 0;
        }
    }
}
