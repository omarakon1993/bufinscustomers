using System;
using bufinscustomers.Helpers;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace bufinscustomers.Filters
{
    /// <summary>
    /// Filtro GLOBAL: cuando el token anti-falsificación de un POST no se puede validar, en vez de la respuesta 400 (que
    /// UseStatusCodePagesWithReExecute pinta como la página «500 Algo salió mal») devuelve al usuario a un sitio útil.
    /// Causa típica: el formulario (sobre todo el login) quedó abierto desde antes de que el sitio reiniciara/reciclara;
    /// en Conexcol las llaves de Data Protection viven en memoria y cada reinicio invalida los formularios abiertos.
    ///
    /// Debe ser global e <see cref="IAlwaysRunResultFilter"/>: [ValidateAntiForgeryToken] es un filtro de AUTORIZACIÓN,
    /// así que al fallar corta la tubería antes de crear el controlador (un filtro implementado en el propio controlador,
    /// como tenía AccesoController en MVC 5, nunca se ejecuta en ASP.NET Core).
    /// </summary>
    public class AntiforgeryFallidoFilter : IAlwaysRunResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (!(context.Result is IAntiforgeryValidationFailedResult)) return;

            var http = context.HttpContext;
            string controller = (context.RouteData.Values["controller"] as string ?? "").ToLowerInvariant();
            string urlLogin = "/Acceso/Login";
            bool autenticado = UsuarioSesionHelper.EstaAutenticado();

            AppLogger.Warn($"Token anti-falsificación inválido en {http.Request.Method} {http.Request.Path} " +
                           $"(ip={ClientIpHelper.ObtenerIp(http.Request)}, sesion={(autenticado ? "activa" : "no")}); " +
                           "se redirige en vez de mostrar error.", "AntiforgeryFallido");

            // AJAX: nunca redirigir (el cliente recibiría HTML). JSON que el JS del layout ya maneja.
            if (http.Request.IsAjaxRequest())
            {
                http.Response.StatusCode = 200;
                context.Result = new JsonResult(new { success = false, sessionExpired = true, message = "", redirectUrl = urlLogin });
                return;
            }

            // Pantallas de acceso (login, 2FA, restablecer clave…) o sin sesión: volver al login con mensaje claro.
            if (controller == "acceso" || !autenticado)
            {
                context.Result = new RedirectResult(urlLogin + "?tokenExpirado=true");
                return;
            }

            // Con sesión activa: volver a la página de origen (solo si es del mismo sitio) con un aviso.
            string destino = "/";
            string referer = http.Request.Headers["Referer"].ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
                && string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                destino = uri.PathAndQuery;
            try
            {
                var tempData = http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
                tempData["ErrorMessage"] = Recursos.Obtener("Common_FormularioExpirado");
                tempData.Save();
            }
            catch (Exception ex) { AppLogger.Warn("AntiforgeryFallido: no se pudo guardar el aviso: " + ex.Message); }
            context.Result = new RedirectResult(destino);
        }

        public void OnResultExecuted(ResultExecutedContext context) { }
    }
}
