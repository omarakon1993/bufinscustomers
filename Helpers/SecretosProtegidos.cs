using System;
using System.Text;
using System.Web.Security;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Cifra/descifra secretos de configuración en reposo con la clave de máquina del servidor
    /// (<see cref="MachineKey"/>). Los valores cifrados se guardan con el prefijo <c>enc:v1:</c>
    /// para poder convivir con valores en claro heredados: <see cref="Descifrar"/> devuelve el
    /// valor tal cual si no lleva el prefijo, así que la migración es perezosa — un secreto
    /// queda cifrado la próxima vez que se guarda.
    /// </summary>
    public static class SecretosProtegidos
    {
        private const string Prefijo = "enc:v1:";
        private static readonly string[] Proposito = { "bufins.configuracion.secreto.v1" };

        public static bool EstaCifrado(string valor) =>
            !string.IsNullOrEmpty(valor) && valor.StartsWith(Prefijo, StringComparison.Ordinal);

        /// <summary>Cifra un texto plano. Si ya viene cifrado o está vacío, lo devuelve sin cambios.</summary>
        public static string Cifrar(string textoPlano)
        {
            if (string.IsNullOrEmpty(textoPlano) || EstaCifrado(textoPlano)) return textoPlano;
            try
            {
                byte[] protegido = MachineKey.Protect(Encoding.UTF8.GetBytes(textoPlano), Proposito);
                return Prefijo + Convert.ToBase64String(protegido);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "SecretosProtegidos.Cifrar");
                return textoPlano; // fail-open: peor cifrar-y-fallar que perder la clave
            }
        }

        /// <summary>Descifra un valor almacenado. Si no lleva el prefijo <c>enc:v1:</c> se asume
        /// texto en claro heredado y se devuelve tal cual. Devuelve <c>null</c> si el descifrado falla.</summary>
        public static string Descifrar(string valorAlmacenado)
        {
            if (string.IsNullOrEmpty(valorAlmacenado) || !EstaCifrado(valorAlmacenado))
                return valorAlmacenado;
            try
            {
                byte[] protegido = Convert.FromBase64String(valorAlmacenado.Substring(Prefijo.Length));
                byte[] datos = MachineKey.Unprotect(protegido, Proposito);
                return datos == null ? null : Encoding.UTF8.GetString(datos);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "SecretosProtegidos.Descifrar");
                return null;
            }
        }

        /// <summary>Versión enmascarada para mostrar en pantalla: primeros 3 + puntos + últimos 4.</summary>
        public static string Enmascarar(string textoPlano)
        {
            if (string.IsNullOrEmpty(textoPlano)) return "";
            var t = textoPlano.Trim();
            if (t.Length <= 8) return new string('•', Math.Max(4, t.Length));
            return t.Substring(0, 3) + new string('•', 8) + t.Substring(t.Length - 4);
        }
    }
}
