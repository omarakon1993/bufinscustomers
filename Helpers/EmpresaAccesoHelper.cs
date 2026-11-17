using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Control de acceso por empresa. El acceso se amplía automáticamente a las empresas
    /// hermanas del mismo GrupoEmpresarial (ver CLAUDE.md, sección "Multi-Company Group
    /// Access"): un usuario ve y administra su propia empresa (IdEmpresa, la "principal")
    /// y cualquier otra empresa de su mismo grupo exactamente igual.
    /// </summary>
    public static class EmpresaAccesoHelper
    {
        // ¿Puede este usuario ver/modificar la empresa? Super Admin sí; los demás: la suya o una de su mismo grupo empresarial.
        public static bool TieneAcceso(Usuarios usuario, int idEmpresa)
        {
            if (usuario == null)
                return false;

            if (UsuarioSesionHelper.EsSuperAdmin())
                return true;

            if (usuario.IdEmpresa == idEmpresa)
                return true;

            // Sin grupo, solo cuenta la empresa propia (ya descartada arriba).
            var idGrupo = ObtenerIdGrupoDeEmpresa(usuario.IdEmpresa);
            if (idGrupo == null)
                return false;

            var empresaObjetivo = EmpresaCacheHelper.ObtenerEmpresasCacheadas()
                .FirstOrDefault(e => e.Id == idEmpresa);

            return empresaObjetivo != null && empresaObjetivo.IdGrupoEmpresarial == idGrupo;
        }

        /// <summary>
        /// Ids de empresas visibles/administrables para el usuario actual. Null significa
        /// "sin filtro" (Super Admin, ve y administra todas).
        /// </summary>
        public static List<int> ObtenerIdsEmpresasPermitidas(Usuarios usuario)
        {
            if (usuario == null)
                return new List<int>();

            if (UsuarioSesionHelper.EsSuperAdmin())
                return null;

            var todas = EmpresaCacheHelper.ObtenerEmpresasCacheadas();
            var idGrupo = ObtenerIdGrupoDeEmpresa(usuario.IdEmpresa, todas);

            if (idGrupo == null)
                return usuario.IdEmpresa.HasValue ? new List<int> { usuario.IdEmpresa.Value } : new List<int>();

            return todas.Where(e => e.IdGrupoEmpresarial == idGrupo).Select(e => e.Id).ToList();
        }

        // Id del grupo empresarial de una empresa (null si no pertenece a ninguno), leído de la caché de empresas.
        private static int? ObtenerIdGrupoDeEmpresa(int? idEmpresa)
        {
            return ObtenerIdGrupoDeEmpresa(idEmpresa, EmpresaCacheHelper.ObtenerEmpresasCacheadas());
        }

        private static int? ObtenerIdGrupoDeEmpresa(int? idEmpresa, List<Empresas> todas)
        {
            if (idEmpresa == null)
                return null;

            return todas.FirstOrDefault(e => e.Id == idEmpresa)?.IdGrupoEmpresarial;
        }
    }
}
