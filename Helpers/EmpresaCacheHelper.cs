using System;
using System.Collections.Generic;
using System.Runtime.Caching;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Cache de aplicación (no de sesión) para la lista completa de empresas, incluyendo
    /// su grupo empresarial. Compartido por EmpresasViewBagFilter y EmpresaAccesoHelper
    /// para que el acceso por grupo se recalcule en cada request sin quedar atado a la
    /// sesión de un usuario ya logueado.
    /// </summary>
    public static class EmpresaCacheHelper
    {
        private const string CACHE_KEY = "EmpresasTodas";
        private static readonly TimeSpan TTL = TimeSpan.FromMinutes(5);

        public static List<Empresas> ObtenerEmpresasCacheadas()
        {
            var cache = MemoryCache.Default;
            if (cache[CACHE_KEY] is List<Empresas> empresas)
                return empresas;

            empresas = new EmpresaService().ObtenerEmpresas();
            cache.Set(CACHE_KEY, empresas, DateTimeOffset.UtcNow.Add(TTL));
            return empresas;
        }

        public static void Invalidar()
        {
            MemoryCache.Default.Remove(CACHE_KEY);
        }
    }
}
