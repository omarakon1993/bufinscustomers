using System;
using System.Collections.Generic;
using System.Runtime.Caching;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Mapa cacheado <c>controller/action</c> → (código de menú, título). Lo usa
    /// <see cref="Filters.RegistroNavegacionFilter"/> para etiquetar cada visita con el nombre
    /// de la opción de menú a la que corresponde. Cache de aplicación (no de sesión), 10 min.
    /// </summary>
    public static class MenuRutaCacheHelper
    {
        private const string CACHE_KEY = "MenuRutasMap";
        private static readonly TimeSpan TTL = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Devuelve <c>(codigo, titulo)</c> para la ruta dada, o <c>(null, null)</c> si la ruta
        /// no corresponde a ninguna opción de menú. Nunca lanza.
        /// </summary>
        public static (string codigo, string titulo) Resolver(string controller, string action)
        {
            if (string.IsNullOrEmpty(controller) || string.IsNullOrEmpty(action))
                return (null, null);

            try
            {
                var mapa = ObtenerMapa();
                return mapa.TryGetValue(Clave(controller, action), out var v) ? v : (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        public static void Invalidar() => MemoryCache.Default.Remove(CACHE_KEY);

        private static string Clave(string c, string a) =>
            (c ?? "").Trim().ToLowerInvariant() + "/" + (a ?? "").Trim().ToLowerInvariant();

        private static Dictionary<string, (string, string)> ObtenerMapa()
        {
            var cache = MemoryCache.Default;
            if (cache[CACHE_KEY] is Dictionary<string, (string, string)> m)
                return m;

            m = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (var op in new MenuOpcionesService().ObtenerTodas())
            {
                if (string.IsNullOrWhiteSpace(op.Controller) || string.IsNullOrWhiteSpace(op.Action))
                    continue;
                var key = Clave(op.Controller, op.Action);
                if (!m.ContainsKey(key))
                    m[key] = (op.Codigo, op.Nombre);
            }

            cache.Set(CACHE_KEY, m, DateTimeOffset.UtcNow.Add(TTL));
            return m;
        }
    }
}
