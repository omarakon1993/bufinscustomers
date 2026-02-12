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
    /// Solo accesible por Super Administradores (Admin = 2)
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
            // Solo Admin 2 puede gestionar permisos
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tienes permisos para acceder a esta función.");
                return RedirectToAction("Index", "Home");
            }

            // Obtener usuario
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

            // Obtener opciones de menú con estado (asignado/no asignado)
            var opciones = _menuOpcionesService.ObtenerOpcionesConEstado(id);
            var opcionesPorCategoria = opciones.GroupBy(o => o.Categoria).ToList();

            var viewModel = new GestionOpcionesMenuViewModel
            {
                Usuario = usuario,
                OpcionesPorCategoria = opcionesPorCategoria
            };

            return View(viewModel);
        }

        /// <summary>
        /// Guarda los permisos asignados a un usuario
        /// </summary>
        /// <param name="idUsuario">ID del usuario</param>
        /// <param name="permisos">Lista de IDs de permisos asignados</param>
        [HttpPost]
        public ActionResult Guardar(int idUsuario, List<int> permisos)
        {
            // Solo Admin 2 puede gestionar permisos
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                return Json(new { success = false, message = "No tienes permisos para esta acción." });
            }

            try
            {
                var usuarioAsigno = UsuarioSesionHelper.UsuarioActual.Id;

                // Si permisos es null, crear lista vacía (significa que se quitaron todos)
                if (permisos == null)
                {
                    permisos = new List<int>();
                }

                _menuOpcionesService.GuardarOpcionesUsuario(idUsuario, permisos, usuarioAsigno);

                // Invalidar caché de permisos del admin que está guardando
                // (la caché del usuario target se refrescará cuando recargue su sesión)
                UsuarioSesionHelper.InvalidarCachePermisos();

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
