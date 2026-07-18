using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;

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

        filterContext.Controller.ViewBag.Empresas = empresas;
        base.OnActionExecuting(filterContext);
    }

    public static void Invalidar()
    {
        EmpresaCacheHelper.Invalidar();
    }
}
