using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;

public class EmpresasViewBagFilter : ActionFilterAttribute
{
    private EmpresaService _empresaService = new EmpresaService();
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        var empresas = _empresaService.ObtenerEmpresas();
        filterContext.Controller.ViewBag.Empresas = empresas;
        base.OnActionExecuting(filterContext);
    }
}
