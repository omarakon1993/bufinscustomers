#nullable enable
using System;
using System.Collections.Generic;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Mensaje seguro para mostrar al usuario cuando algo falla. Una sola implementación para controladores
    /// (<c>BaseController.MensajeError</c>) y servicios. Siempre registra la excepción completa con un código de referencia.
    /// Si la excepción es TÉCNICA (SQL del sistema, nulos, E/S, tiempos de espera, red...) devuelve un texto genérico traducido con ese
    /// código, para no filtrar nombres de tablas, rutas ni versiones; si es una regla de negocio (mensaje escrito por la aplicación, o un
    /// error de usuario lanzado con THROW/RAISERROR) conserva su mensaje. Nunca se debe mostrar <c>ex.Message</c> directamente.
    /// </summary>
    public static class MensajeErrorHelper
    {
        /// <param name="ex">La excepción capturada.</param>
        /// <param name="contexto">Dónde ocurrió (para el log), p. ej. "CuentasCorreoController.Crear".</param>
        /// <param name="traducir">Traductor de claves de recursos; si es null usa el idioma actual del hilo.</param>
        public static string Crear(Exception? ex, string contexto, Func<string, string>? traducir = null)
        {
            string codigo = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            try { AppLogger.Error(ex!, contexto + " ref=" + codigo); } catch { /* el log nunca debe romper la respuesta */ }

            if (!EsTecnica(ex) && ex != null && !string.IsNullOrWhiteSpace(ex.Message)) return ex.Message;
            Func<string, string> t = traducir ?? Recursos.Obtener;
            string plantilla = t("Err_InternoRef");
            // Un traductor que devuelve la clave tal cual (p. ej. la API sin idioma) no trae el marcador {0}: se usa el recurso del idioma actual.
            if (!plantilla.Contains("{0}")) plantilla = Recursos.Obtener("Err_InternoRef");
            return string.Format(plantilla, codigo);
        }

        /// <summary>true si el error es de infraestructura (no una regla de negocio) y por eso no se muestra su texto.</summary>
        public static bool EsTecnica(Exception? ex)
        {
            Exception? b = ex?.GetBaseException();
            var sql = (b as Microsoft.Data.SqlClient.SqlException) ?? (ex as Microsoft.Data.SqlClient.SqlException);
            if (sql != null)
                return !(sql.Class <= 16 && sql.Number >= 50000); // 50000+ = THROW/RAISERROR escrito por nosotros (regla de negocio)
            return b is NullReferenceException || b is IndexOutOfRangeException || b is InvalidCastException
                || b is KeyNotFoundException || b is FormatException || b is System.IO.IOException
                || b is TimeoutException || b is System.ComponentModel.Win32Exception
                || b is OutOfMemoryException || b is ArgumentOutOfRangeException || b is OverflowException
                || b is DivideByZeroException || b is System.Net.WebException || b is System.Net.Http.HttpRequestException
                || b is System.Threading.Tasks.TaskCanceledException || b is UnauthorizedAccessException;
        }
    }
}
