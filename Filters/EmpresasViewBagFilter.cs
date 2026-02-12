using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Services;

public class EmpresasViewBagFilter : ActionFilterAttribute
{
    private EmpresaService _empresaService = new EmpresaService();
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        var empresas = _empresaService.ObtenerEmpresas();

        var usuario = UsuarioSesionHelper.UsuarioActual;
        if (usuario != null && !UsuarioSesionHelper.EsSuperAdmin())
        {
            empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();
        }

        filterContext.Controller.ViewBag.Empresas = empresas;
        base.OnActionExecuting(filterContext);
    }
}
