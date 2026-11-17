using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class NotificacionesController(NotificacionesService svc) : BaseController
    {
        [HttpGet]
        public JsonResult Recientes()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return Json(new { noLeidas = 0, items = System.Array.Empty<object>() }, JsonRequestBehavior.AllowGet);

            var (items, noLeidas) = svc.ObtenerRecientes(usuario.Id);
            return Json(new { noLeidas, items }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult MarcarLeida(int id)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            svc.MarcarLeida(id, usuario.Id);
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult MarcarTodasLeidas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            svc.MarcarTodasLeidas(usuario.Id);
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult LimpiarTodas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            svc.LimpiarTodas(usuario.Id);
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Eliminar(int id)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            svc.Eliminar(id, usuario.Id);
            return Json(new { success = true });
        }
    }
}
