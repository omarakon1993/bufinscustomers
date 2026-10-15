using System.Web;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Decide cuándo invitar al usuario a configurar una passkey (etiqueta «Nuevo» del menú y franja del
    /// Home). Solo aplica si el 2FA y las passkeys están habilitados, el 2FA le aplica al usuario, ya tiene
    /// la app autenticadora activa y todavía no tiene ninguna passkey. El resultado se guarda en la sesión
    /// para no consultar la BD en cada página; <see cref="Invalidar"/> lo refresca (alta/baja de passkey).
    /// </summary>
    public static class PasskeyAvisoHelper
    {
        private const string CLAVE = "PkSinPasskeys";

        public static bool UsuarioSinPasskeys(Usuarios u)
        {
            if (u == null) return false;
            if (!DobleFactorService.Habilitado || !DobleFactorService.PasskeysHabilitadas || !DobleFactorService.AplicaA(u)) return false;

            var session = HttpContext.Current?.Session;
            if (session != null && session[CLAVE] is bool cache) return cache;

            bool resultado = new DobleFactorService().Obtener(u.Id).Activo && new PasskeyService().Listar(u.Id).Count == 0;
            if (session != null) session[CLAVE] = resultado;
            return resultado;
        }

        /// <summary>Etiqueta «Nuevo» junto a «Seguridad» en el menú de usuario.</summary>
        public static bool MostrarEtiquetaNueva() { return UsuarioSinPasskeys(UsuarioSesionHelper.UsuarioActual); }

        /// <summary>Franja del Home: lo anterior y que el usuario no la haya pospuesto ni descartado.</summary>
        public static bool MostrarAvisoHome(Usuarios u)
        {
            return UsuarioSinPasskeys(u) && new PasskeyService().AvisoVigente(u.Id);
        }

        public static void Invalidar()
        {
            var session = HttpContext.Current?.Session;
            if (session != null) session.Remove(CLAVE);
        }
    }
}
