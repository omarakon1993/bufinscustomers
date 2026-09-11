using System;
using System.Collections.Generic;
using System.Runtime.Caching;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Cache de aplicación (no de sesión) para el catálogo de escenarios activos. Mismo patrón
    /// que EmpresaCacheHelper: TTL corto para que un cambio del Super Admin en el gestor de
    /// escenarios se refleje para todos los usuarios ya logueados sin esperar a su próximo login.
    /// </summary>
    public static class EscenarioCacheHelper
    {
        private const string CACHE_KEY = "EscenariosActivos";
        private static readonly TimeSpan TTL = TimeSpan.FromMinutes(5);

        public static List<Escenario> ObtenerEscenariosCacheados()
        {
            var cache = MemoryCache.Default;
            if (cache[CACHE_KEY] is List<Escenario> escenarios)
                return escenarios;

            escenarios = new EscenarioService().ObtenerActivos();
            cache.Set(CACHE_KEY, escenarios, DateTimeOffset.UtcNow.Add(TTL));
            return escenarios;
        }

        public static void Invalidar()
        {
            MemoryCache.Default.Remove(CACHE_KEY);
        }
    }
}
