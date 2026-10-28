using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Comentarios de gestión de los informes (tarjeta compartida <c>_ComentariosGestion</c>). Los pueden leer y escribir todos los
    /// usuarios con acceso a la empresa (propia o de su grupo); editar y eliminar, solo su autor o un administrador.
    /// </summary>
    [ValidarSesion]
    public class ComentariosGestionController : BaseController
    {
        private readonly ComentariosGestionService _service = new ComentariosGestionService();

        private bool TieneAccesoEmpresa(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return UsuarioSesionHelper.EsSuperAdmin() || EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa);
        }

        private static bool EsAdministrador() => UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.EsAdminEmpresa();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Listar(int idEmpresa, int anio, int mes, string informe)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                if (mes < 1 || mes > 12) return Json(new { success = false, message = R("Com_Invalido") });
                if (!string.IsNullOrEmpty(informe) && !_service.InformeValido(informe)) return Json(new { success = false, message = R("Com_Invalido") });

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var lista = _service.Listar(idEmpresa, anio, mes, string.IsNullOrEmpty(informe) ? null : informe, out bool disponible);
                bool admin = EsAdministrador();
                var comentarios = lista.Select(c => new
                {
                    c.Id, c.Informe, c.Clave, c.Texto, c.NombreUsuario, c.Fecha, c.FechaEdicion,
                    PuedeEditar = admin || c.IdUsuario == usuario.Id
                });
                return Json(new { success = true, disponible, comentarios });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ComentariosGestionController.Listar");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Crear(int idEmpresa, string informe, int anio, int mes, string clave, string texto)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                texto = (texto ?? "").Trim(); clave = (clave ?? "").Trim();
                if (mes < 1 || mes > 12 || !_service.InformeValido(informe) || texto.Length == 0 || texto.Length > ComentariosGestionService.MaxTexto || clave.Length > ComentariosGestionService.MaxClave)
                    return Json(new { success = false, message = string.Format(R("Com_TextoInvalido"), ComentariosGestionService.MaxTexto) });

                var usuario = UsuarioSesionHelper.UsuarioActual;
                string nombre = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                long id = _service.Crear(idEmpresa, informe, anio, mes, clave, texto, usuario.Id, nombre);

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Crear, "ComentarioGestion", id.ToString(),
                    $"Comentario de gestión en {informe} ({mes:00}/{anio})" + (clave.Length > 0 ? $", {clave}" : "") + ".",
                    null, new { informe, anio, mes, clave, texto }, idEmpresa);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ComentariosGestionController.Crear");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Editar(long id, string texto)
        {
            try
            {
                var actual = _service.Obtener(id);
                if (actual == null) return Json(new { success = false, message = R("Com_NoExiste") });
                if (!TieneAccesoEmpresa(actual.IdEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (!EsAdministrador() && actual.IdUsuario != usuario.Id) return Json(new { success = false, message = R("Common_SinPermisos") });

                texto = (texto ?? "").Trim();
                if (texto.Length == 0 || texto.Length > ComentariosGestionService.MaxTexto)
                    return Json(new { success = false, message = string.Format(R("Com_TextoInvalido"), ComentariosGestionService.MaxTexto) });

                _service.Editar(id, texto);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Editar, "ComentarioGestion", id.ToString(),
                    $"Comentario de gestión editado en {actual.Informe} ({actual.Mes:00}/{actual.Anio}).", new { texto = actual.Texto }, new { texto }, actual.IdEmpresa);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ComentariosGestionController.Editar");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Eliminar(long id)
        {
            try
            {
                var actual = _service.Obtener(id);
                if (actual == null) return Json(new { success = false, message = R("Com_NoExiste") });
                if (!TieneAccesoEmpresa(actual.IdEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (!EsAdministrador() && actual.IdUsuario != usuario.Id) return Json(new { success = false, message = R("Common_SinPermisos") });

                _service.Eliminar(id);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Eliminar, "ComentarioGestion", id.ToString(),
                    $"Comentario de gestión eliminado en {actual.Informe} ({actual.Mes:00}/{actual.Anio}).", new { texto = actual.Texto }, null, actual.IdEmpresa);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ComentariosGestionController.Eliminar");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }
    }
}
