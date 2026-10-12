using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Cache de aplicación (5 min, igual que <see cref="EmpresaCacheHelper"/>) del catálogo de hojas del
    /// cargue (dbo.CargueHojas + dbo.CargueHojasEmpresas). El Gestor de hojas de cargue lo invalida al
    /// guardar, así que los cambios se ven al instante en ese servidor y en ≤ 5 min en los demás.
    ///
    /// RESPALDO: si el script Sql/021 aún no se ejecutó (o la BD falla al leer el catálogo), se usa el
    /// catálogo que antes vivía en código — las 9 hojas estándar + Z_HistPrecios_Churido con las empresas
    /// de dbo.EmpresaPersonalizaciones —, de modo que el cargue se comporta exactamente como antes.
    /// </summary>
    public static class CargueHojasCacheHelper
    {
        private const string CACHE_KEY = "CargueHojasCatalogo";
        private static readonly TimeSpan TTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TTL_ERROR = TimeSpan.FromMinutes(1);

        /// <summary>Catálogo completo (activas e inactivas), ordenado por Orden.</summary>
        public static IReadOnlyList<HojaCargue> Todas()
        {
            var cache = MemoryCache.Default;
            if (cache[CACHE_KEY] is List<HojaCargue> hojas)
                return hojas;

            TimeSpan ttl = TTL;
            try
            {
                hojas = new CargueHojasService().ObtenerCatalogo() ?? CatalogoRespaldo(cargarEmpresas: true);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "CargueHojasCacheHelper.Todas");
                hojas = CatalogoRespaldo(cargarEmpresas: true);
                ttl = TTL_ERROR;   // reintentar pronto en vez de dejar el respaldo 5 min
            }

            hojas = hojas.OrderBy(h => h.Orden).ThenBy(h => h.NombreHoja, StringComparer.OrdinalIgnoreCase).ToList();
            cache.Set(CACHE_KEY, hojas, DateTimeOffset.UtcNow.Add(ttl));
            return hojas;
        }

        /// <summary>Hojas activas que se exigen/procesan en el cargue de la empresa (estándar + sus personalizadas).</summary>
        public static List<HojaCargue> ParaEmpresa(int idEmpresa) =>
            Todas().Where(h => h.AplicaA(idEmpresa)).ToList();

        /// <summary>Hojas estándar activas.</summary>
        public static List<HojaCargue> Estandar() =>
            Todas().Where(h => h.Activo && h.EsEstandar).ToList();

        /// <summary>Hojas personalizadas activas asignadas a la empresa.</summary>
        public static List<HojaCargue> PersonalizadasDe(int idEmpresa) =>
            Todas().Where(h => h.Activo && h.EsPersonalizada && h.Empresas.ContainsKey(idEmpresa)).ToList();

        public static HojaCargue PorId(int idHoja) => Todas().FirstOrDefault(h => h.IdHoja == idHoja);

        public static void Invalidar() => MemoryCache.Default.Remove(CACHE_KEY);

        /// <summary>
        /// Catálogo anterior al gestor (antes en TablasCargueHelper.MapeoZaIni y
        /// PersonalizacionesEmpresaHelper.Catalogo). Fechas mínimas en las estándar = el rollback sigue
        /// exigiendo sus snapshots; la de Churido toma la fecha real de su asignación.
        /// </summary>
        private static List<HojaCargue> CatalogoRespaldo(bool cargarEmpresas)
        {
            var estandar = new[]
            {
                ("Z_BalancePrueba",             "Ini_BalancePrueba"),
                ("Z_CteYnoCte",                 "Ini_CteYnoCte"),
                ("Z_PYGDetallado",              "Ini_PYG"),
                ("Z_PYGDetalladoConAjuste",     "Ini_PYGDetalladoConAjuste"),
                ("Z_PresupuestoBalance",        "Ini_PresupuestoBalance"),
                ("Z_PptoPYGDetallado",          "Ini_PptoPYG"),
                ("Z_PptoPYGDetalladoConAjuste", "Ini_PptoPYGConAjuste"),
                ("Z_PCH",                       "Ini_PCH"),
                ("Z_EjecPCH",                   "Ini_EjecPCH"),
            };

            var hojas = estandar.Select((x, i) => new HojaCargue
            {
                IdHoja = -(i + 1),
                NombreHoja = x.Item1,
                TablaIni = x.Item2,
                Alcance = HojaCargue.AlcanceEstandar,
                Activo = true,
                Orden = (i + 1) * 10,
                Origen = HojaCargue.OrigenExistente,
                FechaCreacion = DateTime.MinValue,
                VigenteDesde = DateTime.MinValue
            }).ToList();

            var churido = new HojaCargue
            {
                IdHoja = -100,
                NombreHoja = "Z_HistPrecios_Churido",
                TablaIni = "Ini_HistPrecios_Churido",
                Alcance = HojaCargue.AlcancePersonalizada,
                Activo = true,
                Orden = 100,
                Origen = HojaCargue.OrigenExistente,
                FechaCreacion = DateTime.MinValue,
                VigenteDesde = DateTime.MinValue
            };
            if (cargarEmpresas)
            {
                try { churido.Empresas = new CargueHojasService().ObtenerAsignacionesLegadoChurido(); }
                catch (Exception ex) { AppLogger.Error(ex, "CargueHojasCacheHelper.CatalogoRespaldo"); }
            }
            hojas.Add(churido);
            return hojas;
        }
    }
}
