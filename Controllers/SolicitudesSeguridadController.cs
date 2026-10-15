using System;
using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Bandeja de solicitudes de reinicio de métodos de autenticación (Super Admin). Cada solicitud la
    /// crea el usuario desde la pantalla de verificación del 2FA. Aprobar reinicia la app autenticadora
    /// y las passkeys del usuario; antes hay que verificar su identidad por otro canal (llamada o
    /// videollamada). Nadie puede resolver su propia solicitud.
    /// </summary>
    [ValidarSesion]
    [SoloSuperAdmin]
    public class SolicitudesSeguridadController : BaseController
    {
        private readonly SolicitudReinicioAuthService _svc = new SolicitudReinicioAuthService();

        public ActionResult Index()
        {
            ViewBag.Pendientes = _svc.ContarPendientes();
            return View("~/Views/SolicitudesSeguridad/Index.cshtml");
        }

        [HttpGet]
        public JsonResult Listar(string estado)
        {
            if (estado != SolicitudReinicioAuthService.Pendiente && estado != SolicitudReinicioAuthService.Aprobada &&
                estado != SolicitudReinicioAuthService.Rechazada) estado = null;

            var items = _svc.Listar(estado).Select(s => new
            {
                s.Id,
                fecha = s.Fecha.ToString("dd/MM/yyyy HH:mm"),
                s.Estado,
                s.Usuario,
                nombre = s.NombreCompleto,
                s.Correo,
                s.Empresa,
                s.Telefono,
                s.Mensaje,
                s.IP,
                navegador = Navegador(s.UserAgent),
                resuelta = s.FechaResolucion.HasValue ? s.FechaResolucion.Value.ToString("dd/MM/yyyy HH:mm") : "",
                s.ResueltaPor,
                nota = s.NotaResolucion
            });
            return Json(new { ok = true, items, pendientes = _svc.ContarPendientes() }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Aprobar(int id, string nota, bool verificado)
        {
            if (!verificado) return Json(new { ok = false, mensaje = R("SolSeg_Adm_ErrorVerificar") });

            var actual = UsuarioSesionHelper.UsuarioActual;
            var sol = _svc.Obtener(id);
            if (sol == null || sol.Estado != SolicitudReinicioAuthService.Pendiente)
                return Json(new { ok = false, mensaje = R("SolSeg_Adm_YaResuelta") });
            if (actual != null && sol.IdUsuario == actual.Id)
                return Json(new { ok = false, mensaje = R("SolSeg_Adm_Propia") });

            if (!new DobleFactorService().Reiniciar(sol.IdUsuario))
                return Json(new { ok = false, mensaje = R("Usr_2FA_ErrorReiniciar") });
            _svc.Resolver(id, SolicitudReinicioAuthService.Aprobada, actual.Id, nota);

            var destino = new UsuarioService().ObtenerUsuarioPorId(sol.IdUsuario);
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Editar,
                $"Solicitud #{id} aprobada: métodos de autenticación reiniciados para {sol.Usuario} por el Super Admin {actual.Usuario}",
                sol.IdUsuario, sol.NombreCompleto, destino?.IdEmpresa);

            string titulo = R("Notif_SolSeg_AprobadaTitulo"), msg = R("Notif_SolSeg_AprobadaMsg");
            new NotificacionesService().Crear(sol.IdUsuario, titulo, msg, "success");
            AlertaUsuarioCorreo.Enviar(sol.Correo, titulo, msg);
            return Json(new { ok = true, mensaje = R("SolSeg_Adm_Aprobada") });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Rechazar(int id, string nota)
        {
            var actual = UsuarioSesionHelper.UsuarioActual;
            var sol = _svc.Obtener(id);
            if (sol == null || sol.Estado != SolicitudReinicioAuthService.Pendiente)
                return Json(new { ok = false, mensaje = R("SolSeg_Adm_YaResuelta") });
            if (actual != null && sol.IdUsuario == actual.Id)
                return Json(new { ok = false, mensaje = R("SolSeg_Adm_Propia") });

            if (!_svc.Resolver(id, SolicitudReinicioAuthService.Rechazada, actual.Id, nota))
                return Json(new { ok = false, mensaje = R("SolSeg_Adm_YaResuelta") });

            var destino = new UsuarioService().ObtenerUsuarioPorId(sol.IdUsuario);
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Editar,
                $"Solicitud #{id} rechazada para {sol.Usuario} por el Super Admin {actual.Usuario}",
                sol.IdUsuario, sol.NombreCompleto, destino?.IdEmpresa);

            string titulo = R("Notif_SolSeg_RechazadaTitulo"), msg = R("Notif_SolSeg_RechazadaMsg");
            new NotificacionesService().Crear(sol.IdUsuario, titulo, msg, "warning");
            AlertaUsuarioCorreo.Enviar(sol.Correo, titulo, msg);
            return Json(new { ok = true, mensaje = R("SolSeg_Adm_Rechazada") });
        }

        /// <summary>Navegador y sistema de forma legible, a partir del User-Agent (aproximado).</summary>
        private static string Navegador(string ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return "";
            string nav = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera" : ua.Contains("Chrome/") ? "Chrome"
                       : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Safari/") ? "Safari" : "";
            string so = ua.Contains("Windows") ? "Windows" : ua.Contains("Android") ? "Android" : ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS"
                      : ua.Contains("Mac OS") ? "macOS" : ua.Contains("Linux") ? "Linux" : "";
            return (nav + (nav != "" && so != "" ? " · " : "") + so).Trim();
        }
    }
}
