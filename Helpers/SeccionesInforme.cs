using System;
using System.Linq;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Secciones visibles de un informe (barra «Secciones» de la pantalla: Assets/js/informe-secciones.js). El navegador manda los ids separados por
    /// coma en el filtro <c>Secciones</c>; Excel y correo incluyen solo lo que el usuario tiene a la vista. Vacío o ausente = todas (compatibilidad con
    /// llamadas antiguas y con informes que no usan la barra).
    /// </summary>
    public static class SeccionesInforme
    {
        /// <summary>¿Está seleccionada la sección <paramref name="id"/>? Sin filtro (null/vacío) todas lo están.</summary>
        public static bool Incluye(string secciones, string id)
        {
            if (string.IsNullOrWhiteSpace(secciones)) return true;
            return secciones.Split(',').Any(s => string.Equals(s.Trim(), id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>¿Hay filtro explícito (el usuario eligió qué ver)?</summary>
        public static bool HayFiltro(string secciones) { return !string.IsNullOrWhiteSpace(secciones); }
    }
}
