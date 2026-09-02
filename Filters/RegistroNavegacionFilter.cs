using System;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Registra cada visita a una página real del sistema en <c>AuditoriaNavegacion</c>.
    /// Registrado globalmente en <c>FilterConfig</c>.
    ///
    /// Solo cuenta como "página" un <b>GET no-AJAX que devuelve una vista completa</b>
    /// (<see cref="ViewResult"/>). Eso descarta de forma natural los endpoints JSON, el sondeo
    /// de notificaciones, descargas, redirects y vistas parciales, sin listar nada.
    /// Además:
    ///  - solo usuarios autenticados (las páginas anónimas de acceso quedan fuera),
    ///  - se deduplican las repeticiones inmediatas de la misma ruta por el mismo usuario
    ///    (<see cref="DEDUP_SEG"/> s) para no inflar la tabla con refrescos / dobles clics,
    ///  - la escritura va en segundo plano: cero latencia añadida a la respuesta.
    /// Nunca lanza.
    /// </summary>
    public class RegistroNavegacionFilter : ActionFilterAttribute
    {
        private const int DEDUP_SEG = 15;

        public override void OnResultExecuted(ResultExecutedContext filterContext)
        {
            try
            {
                if (filterContext?.HttpContext?.Request == null) return;
                var req = filterContext.HttpContext.Request;

                if (!string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)) return;
                if (req.IsAjaxRequest()) return;
                if (!(filterContext.Result is ViewResult)) return;   // vista completa, no parcial/JSON/redirect/archivo

                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null) return;

                var rv = filterContext.RouteData?.Values;
                string controller = rv? ["controller"] as string;
                string action     = rv? ["action"] as string;
                if (string.IsNullOrEmpty(controller) || string.IsNullOrEmpty(action)) return;
                if (string.Equals(controller, "Error", StringComparison.OrdinalIgnoreCase)) return;
                // El dashboard es la página de aterrizaje tras cada login/navegación: registrarla
                // generaría muchísimo ruido sin valor. Se excluye a propósito.
                if (string.Equals(controller, "Home", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase)) return;

                // Dedupe: misma ruta + mismo usuario dentro de DEDUP_SEG s → no repetir.
                string dedupeKey = "_nav_" + usuario.Id + "_" +
                                   controller.ToLowerInvariant() + "/" + action.ToLowerInvariant();
                if (HttpRuntime.Cache[dedupeKey] != null) return;
                HttpRuntime.Cache.Insert(dedupeKey, 1, null,
                    DateTime.Now.AddSeconds(DEDUP_SEG), Cache.NoSlidingExpiration);

                var reg = new RegistroNavegacion
                {
                    Fecha         = DateTime.Now,
                    IdUsuario     = usuario.Id,
                    NombreUsuario = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim(),
                    IdEmpresa     = usuario.IdEmpresa,
                    RolUsuario    = usuario.Admin,
                    Controller    = controller,
                    Action        = action,
                    IpAddress     = ClientIpHelper.ObtenerIp(req),
                    UserAgent     = req.UserAgent
                };

                // Resolver el nombre del menú y escribir, todo fuera del hilo de la petición.
                HostingEnvironment.QueueBackgroundWorkItem(_ =>
                {
                    try
                    {
                        var (codigo, titulo) = MenuRutaCacheHelper.Resolver(reg.Controller, reg.Action);
                        reg.CodigoMenu = codigo;
                        reg.TituloPagina = titulo;
                        new AuditoriaNavegacionService().Registrar(reg);
                    }
                    catch { /* nunca romper por telemetría */ }
                });
            }
            catch { /* nunca romper la petición */ }
        }
    }
}
