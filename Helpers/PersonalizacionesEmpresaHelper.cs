using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Paquete de personalización del cargue para una o varias empresas: plantilla Excel propia
    /// y hojas <c>Z_</c> adicionales con su tabla <c>Ini_</c> destino (que deben existir en la BD,
    /// junto con su espejo <c>Staging_Ini_</c>).
    /// </summary>
    public sealed class PersonalizacionCargue
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }

        /// <summary>Clave de recurso (Strings.resx) con el nombre visible del paquete (selector de plantilla del Super Admin).</summary>
        public string ClaveNombre { get; set; }

        /// <summary>
        /// Ruta virtual de la plantilla propia (o null = plantilla estándar). Va en App_Data para que
        /// no se pueda descargar por URL directa: solo por DatosController.DescargarPlantilla, que valida acceso.
        /// </summary>
        public string Plantilla { get; set; }

        /// <summary>Hoja <c>Z_</c> adicional → tabla <c>Ini_</c> destino.</summary>
        public IReadOnlyDictionary<string, string> HojasExtra { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Personalizaciones por empresa.
    ///   • QUÉ hace cada paquete se define aquí, en <see cref="Catalogo"/> (código: implica tablas,
    ///     plantilla y a veces SP nuevos, que igual requieren despliegue).
    ///   • A QUIÉN se le aplica se guarda en dbo.EmpresaPersonalizaciones (BD: los Id de empresa son
    ///     datos y cambian entre ambientes; habilitar un paquete existente a otra empresa es un INSERT).
    /// Cache de aplicación de 5 min, igual que <see cref="EmpresaCacheHelper"/>.
    ///
    /// Para AGREGAR una personalización nueva: (1) crear sus tablas Ini_X + Staging_Ini_X,
    /// (2) agregar su entrada al catálogo, (3) asignarla con un INSERT en dbo.EmpresaPersonalizaciones.
    /// El cargue, staging, confirmación, historial/rollback, cierre de año y plantilla con datos la
    /// recogen solos vía <see cref="TablasCargueHelper"/>.
    /// </summary>
    public static class PersonalizacionesEmpresaHelper
    {
        public const string CodigoChuridoHistPrecios = "CHURIDO_HIST_PRECIOS";

        public static readonly IReadOnlyDictionary<string, PersonalizacionCargue> Catalogo =
            new Dictionary<string, PersonalizacionCargue>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    CodigoChuridoHistPrecios,
                    new PersonalizacionCargue
                    {
                        Codigo = CodigoChuridoHistPrecios,
                        Descripcion = "Histórico de precios por tipo de fruta (Churido / Ucrania)",
                        ClaveNombre = "Datos_PlantillaPers_CHURIDO_HIST_PRECIOS",
                        Plantilla = "~/App_Data/PlantillasEmpresa/PlantillaBUFINS_Churido.xlsx",
                        HojasExtra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "Z_HistPrecios_Churido", "Ini_HistPrecios_Churido" },
                        }
                    }
                },
            };

        private const string CACHE_KEY = "EmpresaPersonalizacionesActivas";
        private static readonly TimeSpan TTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TTL_ERROR = TimeSpan.FromMinutes(1);

        /// <summary>Paquetes activos (y presentes en el catálogo) de la empresa, en orden de código.</summary>
        public static List<PersonalizacionCargue> ObtenerParaEmpresa(int idEmpresa)
        {
            if (idEmpresa <= 0) return new List<PersonalizacionCargue>();
            if (!ObtenerAsignaciones().TryGetValue(idEmpresa, out var codigos))
                return new List<PersonalizacionCargue>();

            return codigos
                .Where(Catalogo.ContainsKey)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Select(c => Catalogo[c])
                .ToList();
        }

        public static bool Tiene(int idEmpresa, string codigo) =>
            ObtenerParaEmpresa(idEmpresa).Any(p => string.Equals(p.Codigo, codigo, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Paquetes de la empresa que el usuario puede VER (hojas, plantilla, avisos). Regla más estricta que
        /// <see cref="EmpresaAccesoHelper"/> a propósito: el acceso por grupo empresarial NO basta.
        ///   • Super Admin: todos los de la empresa.
        ///   • Resto: solo los que también tiene asignados SU PROPIA empresa (Usuarios.IdEmpresa).
        /// </summary>
        public static List<PersonalizacionCargue> VisiblesPara(Usuarios usuario, int idEmpresa)
        {
            var paquetes = ObtenerParaEmpresa(idEmpresa);
            if (usuario == null) return new List<PersonalizacionCargue>();
            if (usuario.Admin == 2 || paquetes.Count == 0) return paquetes;

            var propios = new HashSet<string>(
                ObtenerParaEmpresa(usuario.IdEmpresa ?? 0).Select(p => p.Codigo), StringComparer.OrdinalIgnoreCase);
            return paquetes.Where(p => propios.Contains(p.Codigo)).ToList();
        }

        /// <summary>
        /// True si el usuario puede cargar/revisar/confirmar datos de la empresa: ve TODOS sus paquetes.
        /// Si no los viera, el cargue le exigiría hojas que no puede conocer (y borraría datos que no puede reponer).
        /// </summary>
        public static bool PuedeOperarCargue(Usuarios usuario, int idEmpresa) =>
            VisiblesPara(usuario, idEmpresa).Count == ObtenerParaEmpresa(idEmpresa).Count;

        public static void Invalidar() => MemoryCache.Default.Remove(CACHE_KEY);

        private static Dictionary<int, HashSet<string>> ObtenerAsignaciones()
        {
            var cache = MemoryCache.Default;
            if (cache[CACHE_KEY] is Dictionary<int, HashSet<string>> asignaciones)
                return asignaciones;

            try
            {
                asignaciones = new PersonalizacionEmpresaService().ObtenerAsignacionesActivas();
                cache.Set(CACHE_KEY, asignaciones, DateTimeOffset.UtcNow.Add(TTL));
            }
            catch (Exception ex)
            {
                // Sin BD no hay personalizaciones (todas las empresas usan la plantilla estándar);
                // se reintenta pronto en vez de dejar el error cacheado 5 min.
                AppLogger.Error(ex, "PersonalizacionesEmpresaHelper.ObtenerAsignaciones");
                asignaciones = new Dictionary<int, HashSet<string>>();
                cache.Set(CACHE_KEY, asignaciones, DateTimeOffset.UtcNow.Add(TTL_ERROR));
            }
            return asignaciones;
        }
    }
}
