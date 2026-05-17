using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Services;

public class EmpresasViewBagFilter : ActionFilterAttribute
{
    private const string CACHE_KEY = "EmpresasTodas";
    private static readonly TimeSpan TTL = TimeSpan.FromMinutes(5);

    private EmpresaService _empresaService = new EmpresaService();

    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        var todasLasEmpresas = ObtenerEmpresasCacheadas();

        var usuario = UsuarioSesionHelper.UsuarioActual;
        var empresas = (usuario != null && !UsuarioSesionHelper.EsSuperAdmin())
            ? todasLasEmpresas.Where(e => e.Id == usuario.IdEmpresa).ToList()
            : todasLasEmpresas;

        filterContext.Controller.ViewBag.Empresas = empresas;
        base.OnActionExecuting(filterContext);
    }

    private List<Empresas> ObtenerEmpresasCacheadas()
    {
        var cache = MemoryCache.Default;
        if (cache[CACHE_KEY] is List<Empresas> empresas)
            return empresas;

        empresas = _empresaService.ObtenerEmpresas();
        cache.Set(CACHE_KEY, empresas, DateTimeOffset.UtcNow.Add(TTL));
        return empresas;
    }

    public static void Invalidar()
    {
        MemoryCache.Default.Remove(CACHE_KEY);
    }
}
