using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Hojas de cargue PERSONALIZADAS por empresa (dbo.CargueHojas con Alcance = 'PERSONALIZADA' y sus
    /// empresas en dbo.CargueHojasEmpresas, administradas desde el Gestor de hojas de cargue).
    /// Aquí solo vive la regla de VISIBILIDAD: quién puede ver/descargar/cargar esas hojas.
    /// </summary>
    public static class PersonalizacionesEmpresaHelper
    {
        /// <summary>Hojas personalizadas activas de la empresa.</summary>
        public static List<HojaCargue> ObtenerParaEmpresa(int idEmpresa) =>
            idEmpresa <= 0 ? new List<HojaCargue>() : CargueHojasCacheHelper.PersonalizadasDe(idEmpresa);

        /// <summary>
        /// Hojas personalizadas de la empresa que el usuario puede VER (hojas, plantilla, avisos). Regla más
        /// estricta que <see cref="EmpresaAccesoHelper"/> a propósito: el acceso por grupo empresarial NO basta.
        ///   • Super Admin: todas las de la empresa.
        ///   • Resto: solo las que también tiene asignadas SU PROPIA empresa (Usuarios.IdEmpresa).
        /// </summary>
        public static List<HojaCargue> VisiblesPara(Usuarios usuario, int idEmpresa)
        {
            var hojas = ObtenerParaEmpresa(idEmpresa);
            if (usuario == null) return new List<HojaCargue>();
            if (usuario.Admin == 2 || hojas.Count == 0) return hojas;

            var propias = new HashSet<int>(ObtenerParaEmpresa(usuario.IdEmpresa ?? 0).Select(h => h.IdHoja));
            return hojas.Where(h => propias.Contains(h.IdHoja)).ToList();
        }

        /// <summary>
        /// True si el usuario puede cargar/revisar/confirmar datos de la empresa: ve TODAS sus hojas personalizadas.
        /// Si no las viera, el cargue le exigiría hojas que no puede conocer (y borraría datos que no puede reponer).
        /// </summary>
        public static bool PuedeOperarCargue(Usuarios usuario, int idEmpresa) =>
            VisiblesPara(usuario, idEmpresa).Count == ObtenerParaEmpresa(idEmpresa).Count;

        /// <summary>Hojas que se le entregan al usuario en una plantilla de la empresa: estándar + personalizadas visibles.</summary>
        public static List<HojaCargue> HojasPlantillaPara(Usuarios usuario, int idEmpresa) =>
            CargueHojasCacheHelper.Estandar()
                .Concat(VisiblesPara(usuario, idEmpresa))
                .OrderBy(h => h.Orden).ThenBy(h => h.NombreHoja, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static void Invalidar() => CargueHojasCacheHelper.Invalidar();
    }
}
