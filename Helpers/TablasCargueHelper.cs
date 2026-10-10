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
    ///   • <see cref="bufinscustomers.Services.CargueStagingService"/> — validación en dos pasos (dbo.Staging_Ini_*).
    ///
    /// Para AGREGAR o QUITAR una tabla del cargue ESTÁNDAR (todas las empresas), edítese SOLO este
    /// diccionario: los puntos anteriores quedan sincronizados automáticamente. Para una hoja que solo
    /// aplica a ciertas empresas, no se toca este diccionario: va en un paquete de
    /// <see cref="PersonalizacionesEmpresaHelper"/> y se consume con los métodos <c>*ParaEmpresa</c>.
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

        /// <summary>Nombre de la tabla de staging (dbo.Staging_Ini_*) para una tabla <c>Ini_</c> destino.</summary>
        public static string NombreStaging(string nombreTablaIni) => "Staging_" + nombreTablaIni;

        // ── Personalizaciones por empresa (Helpers/PersonalizacionesEmpresaHelper.cs) ──────────────
        // Operaciones de DATOS de la empresa (hojas exigidas, borrado previo, snapshot/rollback, cierre de
        // año) usan los métodos *ParaEmpresa. Lo que se le MUESTRA o DESCARGA a un usuario (plantilla,
        // plantilla con datos, avisos) usa *ParaPaquetes con PersonalizacionesEmpresaHelper.VisiblesPara
        // (o la elección explícita de un Super Admin): así nadie de otra empresa ve una hoja ajena.

        /// <summary>Plantilla estándar de cargue.</summary>
        public const string PlantillaEstandar = "~/Assets/Plantillas/PlantillaBUFINS.xlsx";

        /// <summary>Mapeo estándar + hojas adicionales de los paquetes indicados.</summary>
        public static IReadOnlyDictionary<string, string> MapeoParaPaquetes(IEnumerable<PersonalizacionCargue> paquetes)
        {
            var mapeo = new Dictionary<string, string>(MapeoZaIni.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.OrdinalIgnoreCase);
            foreach (var p in paquetes ?? Enumerable.Empty<PersonalizacionCargue>())
                foreach (var hoja in p.HojasExtra)
                    mapeo[hoja.Key] = hoja.Value;
            return mapeo;
        }

        /// <summary>Ruta virtual de la plantilla de los paquetes indicados (la del primero que tenga una, o la estándar).</summary>
        public static string PlantillaParaPaquetes(IEnumerable<PersonalizacionCargue> paquetes) =>
            (paquetes ?? Enumerable.Empty<PersonalizacionCargue>())
                .Select(p => p.Plantilla)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
            ?? PlantillaEstandar;

        /// <summary>Mapeo estándar + hojas adicionales de los paquetes activos de la empresa.</summary>
        public static IReadOnlyDictionary<string, string> MapeoParaEmpresa(int idEmpresa) =>
            MapeoParaPaquetes(PersonalizacionesEmpresaHelper.ObtenerParaEmpresa(idEmpresa));

        /// <summary>Tablas <c>Ini_</c> de la empresa (estándar + personalizadas), sin duplicados.</summary>
        public static string[] TablasIniParaEmpresa(int idEmpresa) => MapeoParaEmpresa(idEmpresa).Values.Distinct().ToArray();

        /// <summary>Hojas <c>Z_</c> adicionales de la empresa (vacío si no tiene personalización).</summary>
        public static string[] HojasPersonalizadasParaEmpresa(int idEmpresa) =>
            PersonalizacionesEmpresaHelper.ObtenerParaEmpresa(idEmpresa).SelectMany(p => p.HojasExtra.Keys).Distinct().ToArray();

        /// <summary>
        /// Mapeo estándar + TODAS las hojas personalizadas del catálogo, sin importar la empresa. Solo para
        /// operaciones que van filtradas por IdLote (limpieza/conteo de staging), nunca para decidir qué ve una empresa.
        /// </summary>
        public static IReadOnlyDictionary<string, string> MapeoCompleto
        {
            get
            {
                var mapeo = new Dictionary<string, string>(MapeoZaIni.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.OrdinalIgnoreCase);
                foreach (var p in PersonalizacionesEmpresaHelper.Catalogo.Values)
                    foreach (var hoja in p.HojasExtra)
                        mapeo[hoja.Key] = hoja.Value;
                return mapeo;
            }
        }
    }
}
