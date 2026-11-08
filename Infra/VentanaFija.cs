using System;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;

namespace bufinscustomers
{
    /// <summary>Contador por clave en ventana fija (límite por IP): reemplaza a HttpRuntime.Cache.Add + Interlocked.</summary>
    public static class VentanaFija
    {
        private class Contador { public int N; }
        private static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions());

        /// <summary>Suma una petición a la clave y devuelve el total de la ventana actual (que dura <paramref name="segundos"/>).</summary>
        public static int Incrementar(string clave, int segundos)
        {
            var c = Cache.GetOrCreate(clave, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(segundos);
                e.Priority = CacheItemPriority.Low;
                return new Contador();
            });
            return Interlocked.Increment(ref c.N);
        }
    }
}
