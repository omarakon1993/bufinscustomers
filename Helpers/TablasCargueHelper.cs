using System;
using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Fuente ÚNICA de verdad del conjunto de tablas que intervienen en el cargue de datos:
    /// hoja <c>Z_</c> del Excel (nombre normalizado, sin espacios/tildes) → tabla <c>Ini_</c> destino.
    ///
    /// La consumen:
    ///   • <see cref="bufinscustomers.Controllers.DatosController"/> — resolución de hoja, borrado previo y limpieza en error.
    ///   • <see cref="bufinscustomers.Services.HistorialVersionesCarguesService"/> — snapshot, conteo de filas y rollback.
    ///   • <see cref="bufinscustomers.Controllers.ConfiguracionEmpresaController"/> — cierre de año (ejecución → histórico).
    ///
    /// Para AGREGAR o QUITAR una tabla del cargue, edítese SOLO este diccionario: los tres
    /// puntos anteriores quedan sincronizados automáticamente.
    /// </summary>
    public static class TablasCargueHelper
    {
        /// <summary>Nombre de hoja <c>Z_</c> (normalizado) → nombre de tabla <c>Ini_</c> destino.</summary>
        public static readonly IReadOnlyDictionary<string, string> MapeoZaIni =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Z_BalancePrueba",             "Ini_BalancePrueba" },
                { "Z_CteYnoCte",                 "Ini_CteYnoCte" },
                { "Z_EjecPCH",                   "Ini_EjecPCH" },
                { "Z_PCH",                       "Ini_PCH" },
                { "Z_PptoPYGDetallado",          "Ini_PptoPYG" },
                { "Z_PptoPYGDetalladoConAjuste", "Ini_PptoPYGConAjuste" },
                { "Z_PresupuestoBalance",        "Ini_PresupuestoBalance" },
                { "Z_PYGDetallado",              "Ini_PYG" },
                { "Z_PYGDetalladoConAjuste",     "Ini_PYGDetalladoConAjuste" },
            };

        /// <summary>Tablas <c>Z_</c> del cargue (claves del mapeo).</summary>
        public static string[] TablasZ => MapeoZaIni.Keys.ToArray();

        /// <summary>Tablas <c>Ini_</c> destino, sin duplicados (valores del mapeo).</summary>
        public static string[] TablasIni => MapeoZaIni.Values.Distinct().ToArray();
    }
}
