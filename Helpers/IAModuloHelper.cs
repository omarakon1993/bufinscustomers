using bufinscustomers.Services;
using System;
using System.Runtime.Caching;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// ¿Debe mostrarse el módulo de IA al usuario actual? Super Admin siempre. Para el resto se oculta si
    /// la empresa tiene la IA apagada (Configuración IA → Por Empresa) o el usuario tiene el acceso a IA
    /// bloqueado. La interfaz usa esto para no mostrar la opción de menú ni las tarjetas de insights; el
    /// servidor sigue rechazando la consulta (IAUsoService) aunque alguien llame al endpoint directo.
    /// El estado de la empresa se cachea 60 s para no consultar la BD en cada página.
    /// </summary>
    public static class IAModuloHelper
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

        public static bool Visible()
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            if (u == null) return false;
            if (UsuarioSesionHelper.EsSuperAdmin()) return true;
            if (u.AccesoConsultasIA == false) return false;
            return EmpresaTieneIA(u.IdEmpresa ?? 0);
        }

        private static bool EmpresaTieneIA(int idEmpresa)
        {
            string key = "ia_habilitada_" + idEmpresa;
            var cache = MemoryCache.Default;
            if (cache[key] is bool habilitada) return habilitada;

            habilitada = new ConfiguracionIAEmpresaService().ObtenerConfig(idEmpresa).IaHabilitada;
            cache.Set(key, habilitada, DateTimeOffset.Now.Add(Ttl));
            return habilitada;
        }

        /// <summary>
        /// Máximo de caracteres de una pregunta de IA (Análisis IA y "Pregúntale a este informe"): parámetro
        /// <c>IA_MaxCaracteresPregunta</c> (50–4000, por defecto 500). Cacheado 60 s.
        /// </summary>
        public static int MaxCharsPregunta()
        {
            const string key = "ia_max_chars_pregunta";
            var cache = MemoryCache.Default;
            if (cache[key] is int cached) return cached;

            int max = 500;
            try
            {
                if (int.TryParse(new ConfiguracionSistemaService().ObtenerValor("IA_MaxCaracteresPregunta"), out int v) && v >= 50 && v <= 4000)
                    max = v;
            }
            catch (Exception) { /* sin configuración: se usa el valor por defecto */ }
            cache.Set(key, max, DateTimeOffset.Now.Add(Ttl));
            return max;
        }

        /// <summary>Descarta el estado cacheado de la empresa (se llama al guardar su configuración).</summary>
        public static void Invalidar(int idEmpresa)
        {
            MemoryCache.Default.Remove("ia_habilitada_" + idEmpresa);
        }
    }
}
