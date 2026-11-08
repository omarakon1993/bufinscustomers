using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace bufinscustomers.Compat
{
    /// <summary>Datos de una sesión de servidor (objetos .NET tal cual, sin serializar, como la sesión InProc de ASP.NET 4.x).</summary>
    public sealed class SesionDatos
    {
        public string Id;
        public readonly ConcurrentDictionary<string, object> Valores = new ConcurrentDictionary<string, object>();
    }

    /// <summary>
    /// Almacén de sesiones en memoria equivalente a <c>sessionState mode="InProc"</c> del sitio anterior: misma cookie
    /// (<c>BUFINS_SessionId</c>), 20 minutos de inactividad (expiración deslizante) y objetos guardados sin serializar.
    /// Al expirar o abandonarse una sesión se invoca <see cref="SesionTerminada"/>, que reemplaza a <c>Session_End</c> de Global.asax
    /// (se usa para descartar los cargues que la sesión dejó en revisión). La expulsión se detecta con un retraso de hasta ~1 minuto.
    /// </summary>
    public static class SesionStore
    {
        public const string NombreCookie = "BUFINS_SessionId";
        public static int TimeoutMinutos = 20;

        /// <summary>Se invoca cuando una sesión termina (expirada o abandonada). Reemplaza a Global.asax Session_End.</summary>
        public static Action<SesionDatos> SesionTerminada;

        private static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromSeconds(30) });

        public static SesionDatos Obtener(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            SesionDatos s;
            return Cache.TryGetValue(id, out s) ? s : null;   // la lectura renueva la expiración deslizante
        }

        public static SesionDatos Crear()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var s = new SesionDatos { Id = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=') };
            var opciones = new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(TimeoutMinutos) };
            opciones.RegisterPostEvictionCallback((clave, valor, razon, estado) =>
            {
                var datos = valor as SesionDatos;
                if (datos == null || razon == EvictionReason.Replaced) return;
                try { SesionTerminada?.Invoke(datos); } catch (Exception) { /* nunca debe tumbar el proceso */ }
            });
            Cache.Set(s.Id, s, opciones);
            return s;
        }

        public static void Abandonar(string id)
        {
            if (!string.IsNullOrEmpty(id)) Cache.Remove(id);
        }
    }

    /// <summary>
    /// Vista de la sesión de la petición actual con la API de <c>HttpSessionState</c> (indexador, Remove, Clear, Abandon, Timeout, SessionID).
    /// La sesión se crea al primer valor escrito, igual que en ASP.NET.
    /// </summary>
    public sealed class SesionCompat
    {
        private readonly HttpContext _ctx;
        private const string ItemKey = "__bufins_sesion";

        public SesionCompat(HttpContext ctx) { _ctx = ctx; }

        private SesionDatos Datos(bool crear)
        {
            var existente = _ctx.Items[ItemKey] as SesionDatos;
            if (existente != null) return existente;

            string id = _ctx.Request.Cookies[SesionStore.NombreCookie];
            var s = SesionStore.Obtener(id);
            if (s == null && crear)
            {
                s = SesionStore.Crear();
                if (!_ctx.Response.HasStarted)
                    _ctx.Response.Cookies.Append(SesionStore.NombreCookie, s.Id, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = _ctx.Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true,
                        Path = "/"
                    });
            }
            if (s != null) _ctx.Items[ItemKey] = s;
            return s;
        }

        public object this[string key]
        {
            get
            {
                var s = Datos(false);
                object v;
                return s != null && s.Valores.TryGetValue(key, out v) ? v : null;
            }
            set
            {
                if (value == null) { Remove(key); return; }
                Datos(true).Valores[key] = value;
            }
        }

        public void Remove(string key)
        {
            var s = Datos(false);
            object ignorado;
            if (s != null) s.Valores.TryRemove(key, out ignorado);
        }

        public void Clear()
        {
            var s = Datos(false);
            if (s != null) s.Valores.Clear();
        }

        public void Abandon()
        {
            var s = Datos(false);
            if (s != null) { SesionStore.Abandonar(s.Id); _ctx.Items[ItemKey] = null; }
        }

        public int Timeout { get { return SesionStore.TimeoutMinutos; } set { SesionStore.TimeoutMinutos = value; } }

        public string SessionID { get { var s = Datos(false); return s == null ? "" : s.Id; } }
    }
}
