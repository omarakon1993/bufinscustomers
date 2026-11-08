using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Helpers;
using bufinscustomers.Models;

/// <summary>
/// Filtro global: pone en ViewBag.Empresas las empresas que el usuario puede ver (lo que habilita el acceso por grupo empresarial
/// en todos los desplegables). Super Admin: todas. Sin sesion: todas (p. ej. pantallas de acceso).
/// </summary>
public class EmpresasViewBagFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        var todasLasEmpresas = EmpresaCacheHelper.ObtenerEmpresasCacheadas();
        var usuario = UsuarioSesionHelper.UsuarioActual;

        List<Empresas> empresas;
        if (usuario == null)
        {
            empresas = todasLasEmpresas;
        }
        else
        {
            var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario);
            empresas = idsPermitidos == null
                ? todasLasEmpresas
                : todasLasEmpresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
        }

        var vb = filterContext.ViewBagDe();
        if (vb != null) vb.Empresas = empresas;

        base.OnActionExecuting(filterContext);
    }

    /// <summary>Invalida la caché de empresas (llamar tras crear/editar/eliminar una empresa o cambiar su grupo).</summary>
    public static void Invalidar()
    {
        EmpresaCacheHelper.Invalidar();
    }
}
