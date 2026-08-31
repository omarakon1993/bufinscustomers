using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Linq;
using System.Web.Mvc;
using System.Collections.Generic;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Controlador para gestionar permisos de usuarios
    /// Accesible por Super Administradores (Admin = 2) y Admin de Empresa (Admin = 1) para usuarios de su empresa
    /// </summary>
    [ValidarSesion]
    public class PermisosController : BaseController
    {
        private readonly MenuOpcionesService _menuOpcionesService = new MenuOpcionesService();
        private readonly UsuarioService _usuarioService = new UsuarioService();

        /// <summary>
        /// Vista para gestionar permisos de un usuario específico
        /// </summary>
        /// <param name="id">ID del usuario</param>
        public ActionResult Gestionar(int id)
        {
            var usuarioActual = UsuarioSesionHelper.UsuarioActual;

            // Solo Admin 2 o Admin 1 pueden gestionar permisos
            if (!UsuarioSesionHelper.EsSuperAdmin() && !UsuarioSesionHelper.EsAdminEmpresa())
            {
                return RedirectToAction("Index", "Home");
            }

            // Obtener usuario target
            var usuario = _usuarioService.ObtenerUsuarioPorId(id);
            if (usuario == null)
            {
                SetErrorMessage("Usuario no encontrado.");
                return RedirectToAction("Usuarios", "Usuario");
            }

            // No permitir gestionar permisos de Admin 2
            if (usuario.Admin == 2)
            {
                SetErrorMessage("No se pueden gestionar permisos de Super Administradores. Los Super Administradores tienen todos los permisos automáticamente.");
                return RedirectToAction("Usuarios", "Usuario");
            }

            // Admin 1 puede gestionar usuarios de su empresa o de su mismo grupo empresarial
            if (UsuarioSesionHelper.EsAdminEmpresa())
            {
                bool tieneAcceso = usuario.IdEmpresa.HasValue
                    && EmpresaAccesoHelper.TieneAcceso(usuarioActual, usuario.IdEmpresa.Value);
                if (!tieneAcceso)
                {
                    SetErrorMessage("No tienes permisos para gestionar opciones de usuarios de otras empresas.");
                    return RedirectToAction("Usuarios", "Usuario");
                }
            }

            // Obtener opciones de menú con estado (asignado/no asignado)
            var opciones = _menuOpcionesService.ObtenerOpcionesConEstado(id);

            // Filtrar opciones según quién gestiona y a quién se le asignan
            byte nivelAdmin = usuarioActual.Admin ?? 0;
            byte nivelTarget = usuario.Admin ?? 0;

            opciones = FiltrarOpcionesParaAsignacion(opciones, nivelAdmin, nivelTarget);

            var opcionesPorCategoria = opciones.GroupBy(o => o.Categoria).ToList();

            var viewModel = new GestionOpcionesMenuViewModel
            {
                Usuario = usuario,
                OpcionesPorCategoria = opcionesPorCategoria
            };

            return View(viewModel);
        }

        /// <summary>
        /// Filtra las opciones según el nivel del usuario al que se le asignan permisos.
        /// Admin 2 nunca se edita (tiene todo), así que SoloSuperAdmin nunca aparece.
        /// </summary>
        private List<OpcionMenuUsuarioViewModel> FiltrarOpcionesParaAsignacion(
            List<OpcionMenuUsuarioViewModel> opciones, byte nivelAdmin, byte nivelTarget)
        {
            return opciones.Where(o =>
            {
                // SoloSuperAdmin: solo para Admin 2, nunca asignable a Admin 0 ni Admin 1
                if (o.SoloSuperAdmin)
                    return false;

                // SoloAdminEmpresa: solo para Admin 1+, no asignable a Admin 0
                if (o.SoloAdminEmpresa && nivelTarget == 0)
                    return false;

                return true;
            }).ToList();
        }

        /// <summary>
        /// Guarda los permisos asignados a un usuario
        /// </summary>
        /// <param name="idUsuario">ID del usuario</param>
        /// <param name="permisos">Lista de IDs de permisos asignados</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Guardar(int idUsuario, List<int> permisos)
        {
            var usuarioActual = UsuarioSesionHelper.UsuarioActual;

            // Solo Admin 2 o Admin 1 pueden guardar permisos
            if (!UsuarioSesionHelper.EsSuperAdmin() && !UsuarioSesionHelper.EsAdminEmpresa())
            {
                return Json(new { success = false, message = "No tienes permisos para esta acción." });
            }

            try
            {
                // Verificar que el usuario target existe
                var usuarioTarget = _usuarioService.ObtenerUsuarioPorId(idUsuario);
                if (usuarioTarget == null)
                {
                    return Json(new { success = false, message = "Usuario no encontrado." });
                }

                // No permitir gestionar permisos de Admin 2
                if (usuarioTarget.Admin == 2)
                {
                    return Json(new { success = false, message = "No se pueden modificar permisos de Super Administradores." });
                }

                // Admin 1 puede gestionar usuarios de su empresa o de su mismo grupo empresarial
                if (UsuarioSesionHelper.EsAdminEmpresa())
                {
                    bool tieneAcceso = usuarioTarget.IdEmpresa.HasValue
                        && EmpresaAccesoHelper.TieneAcceso(usuarioActual, usuarioTarget.IdEmpresa.Value);
                    if (!tieneAcceso)
                    {
                        return Json(new { success = false, message = "No tienes permisos para gestionar usuarios de otras empresas." });
                    }
                }

                var usuarioAsigno = usuarioActual.Id;

                // Si permisos es null, crear lista vacía (significa que se quitaron todos)
                if (permisos == null)
                {
                    permisos = new List<int>();
                }

                _menuOpcionesService.GuardarOpcionesUsuario(idUsuario, permisos, usuarioAsigno);

                // Invalidar caché de permisos del admin que está guardando
                UsuarioSesionHelper.InvalidarCachePermisos();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Permisos, AuditoriaAccion.Asignar,
                    "UsuarioMenuPermisos", idUsuario.ToString(),
                    $"Permisos de menú actualizados para {usuarioTarget.Correo} ({permisos.Count} opción/es)",
                    null, new { idUsuario, permisos }, idEmpresa: usuarioTarget.IdEmpresa);

                SetSuccessMessage("Opciones de menú actualizadas correctamente.");
                return Json(new { success = true, message = "Opciones de menú actualizadas correctamente." });
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al guardar permisos: " + ex.Message);
                return Json(new { success = false, message = "Error al guardar permisos: " + ex.Message });
            }
        }
    }
}
