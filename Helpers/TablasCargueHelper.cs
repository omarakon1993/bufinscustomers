using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Fuente ÚNICA del conjunto de tablas que intervienen en el cargue de datos:
    /// hoja <c>Z_</c> del Excel (nombre normalizado, sin espacios/tildes) → tabla <c>Ini_</c> destino.
    ///
    /// Las hojas ya NO se definen en código: viven en dbo.CargueHojas / dbo.CargueHojasEmpresas y se
    /// administran desde el Gestor de hojas de cargue (Super Admin). Este helper las lee de
    /// <see cref="CargueHojasCacheHelper"/> (que trae un respaldo con el catálogo anterior si el script
    /// Sql/021 no se ha ejecutado).
    ///
    /// La consumen:
    ///   • <see cref="bufinscustomers.Controllers.DatosController"/> — hojas exigidas, borrado previo y limpieza en error.
    ///   • <see cref="bufinscustomers.Services.HistorialVersionesCarguesService"/> — snapshot, conteo de filas y rollback.
    ///   • <see cref="bufinscustomers.Controllers.ConfiguracionEmpresaController"/> — cierre de año (ejecución → histórico).
    ///   • <see cref="bufinscustomers.Services.CargueStagingService"/> — validación en dos pasos (dbo.Staging_Ini_*).
    ///   • <see cref="bufinscustomers.Services.PlantillaCargueService"/> — plantilla Excel generada por empresa.
    ///
    /// Operaciones de DATOS de una empresa usan los métodos <c>*ParaEmpresa</c>. Lo que se le MUESTRA o
    /// DESCARGA a un usuario usa <see cref="MapeoParaHojas"/> con las hojas que ese usuario puede ver
    /// (<see cref="PersonalizacionesEmpresaHelper.VisiblesPara"/>).
    /// </summary>
    public static class TablasCargueHelper
    {
        /// <summary>Plantilla base de cargue (hojas estándar con su formato y la hoja auxiliar "Datos").</summary>
        public const string PlantillaEstandar = "~/Assets/Plantillas/PlantillaBUFINS.xlsx";

        /// <summary>Hojas ESTÁNDAR activas: nombre de hoja <c>Z_</c> → tabla <c>Ini_</c>.</summary>
        public static IReadOnlyDictionary<string, string> MapeoZaIni => MapeoParaHojas(CargueHojasCacheHelper.Estandar());

        /// <summary>Hojas <c>Z_</c> estándar activas.</summary>
        public static string[] TablasZ => MapeoZaIni.Keys.ToArray();

        /// <summary>Tablas <c>Ini_</c> de las hojas estándar activas, sin duplicados.</summary>
        public static string[] TablasIni => MapeoZaIni.Values.Distinct().ToArray();

        /// <summary>Nombre de la tabla de staging (dbo.Staging_Ini_*) para una tabla <c>Ini_</c> destino.</summary>
        public static string NombreStaging(string nombreTablaIni) => "Staging_" + nombreTablaIni;

        /// <summary>Mapeo hoja → tabla de las hojas indicadas, en su orden.</summary>
        public static IReadOnlyDictionary<string, string> MapeoParaHojas(IEnumerable<HojaCargue> hojas)
        {
            var mapeo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in hojas ?? Enumerable.Empty<HojaCargue>())
                mapeo[h.NombreHoja] = h.TablaIni;
            return mapeo;
        }

        /// <summary>Hojas activas que la empresa debe cargar (estándar + sus personalizadas), en orden.</summary>
        public static List<HojaCargue> HojasParaEmpresa(int idEmpresa) => CargueHojasCacheHelper.ParaEmpresa(idEmpresa);

        /// <summary>Mapeo de las hojas activas de la empresa (estándar + personalizadas).</summary>
        public static IReadOnlyDictionary<string, string> MapeoParaEmpresa(int idEmpresa) =>
            MapeoParaHojas(HojasParaEmpresa(idEmpresa));

        /// <summary>Tablas <c>Ini_</c> de la empresa (estándar + personalizadas), sin duplicados.</summary>
        public static string[] TablasIniParaEmpresa(int idEmpresa) => MapeoParaEmpresa(idEmpresa).Values.Distinct().ToArray();

        /// <summary>Hojas <c>Z_</c> personalizadas de la empresa (vacío si no tiene).</summary>
        public static string[] HojasPersonalizadasParaEmpresa(int idEmpresa) =>
            CargueHojasCacheHelper.PersonalizadasDe(idEmpresa).Select(h => h.NombreHoja).ToArray();

        /// <summary>
        /// TODAS las hojas del catálogo (activas o no, de cualquier empresa). Solo para operaciones que van
        /// filtradas por IdLote (limpieza/conteo de staging), nunca para decidir qué ve o carga una empresa.
        /// </summary>
        public static IReadOnlyDictionary<string, string> MapeoCompleto => MapeoParaHojas(CargueHojasCacheHelper.Todas());
    }
}
