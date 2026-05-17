using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class NotificacionesController : BaseController
    {
        private readonly NotificacionesService _svc = new NotificacionesService();

        [HttpGet]
        public JsonResult Recientes()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return Json(new { noLeidas = 0, items = new object[0] }, JsonRequestBehavior.AllowGet);

            var (items, noLeidas) = _svc.ObtenerRecientes(usuario.Id);
            return Json(new { noLeidas, items }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult MarcarLeida(int id)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            _svc.MarcarLeida(id, usuario.Id);
            return Json(new { success = true });
        }

        [HttpPost]
        public JsonResult MarcarTodasLeidas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            _svc.MarcarTodasLeidas(usuario.Id);
            return Json(new { success = true });
        }

        [HttpPost]
        public JsonResult LimpiarTodas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            _svc.LimpiarTodas(usuario.Id);
            return Json(new { success = true });
        }

        [HttpPost]
        public JsonResult Eliminar(int id)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { success = false });
            _svc.Eliminar(id, usuario.Id);
            return Json(new { success = true });
        }
    }
}
