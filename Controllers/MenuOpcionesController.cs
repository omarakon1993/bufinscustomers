using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class MenuOpcionesController : BaseController
    {
        private readonly MenuOpcionesService _menuOpcionesService = new MenuOpcionesService();
        private readonly MenuEstructuraService _menuEstructuraService = new MenuEstructuraService();

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                return RedirectToAction("Index", "Home");
            }

            var opciones = _menuOpcionesService.ObtenerTodas();
            ViewBag.Grupos = _menuEstructuraService.ObtenerGrupos();
            ViewBag.Categorias = _menuEstructuraService.ObtenerCategorias();
            return View("~/Views/Configuracion/MenuOpciones.cshtml", opciones);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(MenuOpciones opcion)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                return RedirectToAction("Index", "Home");
            }

            // Solo Super Admin puede crear menús destacados
            if (!UsuarioSesionHelper.EsSuperAdmin())
                opcion.EsDestacado = false;

            try
            {
                bool creado = _menuOpcionesService.CrearMenuOpcion(opcion);

                if (creado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    SetSuccessMessage("Opción de menú creada correctamente.");
                }
                else
                {
                    SetErrorMessage("Error al crear la opción de menú.");
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al crear la opción: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(MenuOpciones opcion)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                return RedirectToAction("Index", "Home");
            }

            // Solo Super Admin puede marcar menús como destacados
            if (!UsuarioSesionHelper.EsSuperAdmin())
                opcion.EsDestacado = false;

            try
            {
                bool editado = _menuOpcionesService.EditarMenuOpcion(opcion);

                if (editado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    SetSuccessMessage("Opción de menú actualizada correctamente.");
                }
                else
                {
                    SetErrorMessage("Error al actualizar la opción de menú.");
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al actualizar la opción: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        // Actualiza el Orden de varias opciones a la vez (drag & drop en la pantalla principal del gestor)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ActualizarOrden(List<int> ids, List<int> ordenes)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return Json(new { success = false, message = R("Menu_ErrorSinPermiso") });

            if (ids == null || ordenes == null || ids.Count == 0 || ids.Count != ordenes.Count)
                return Json(new { success = false, message = R("Menu_ErrorActualizarOrden") });

            try
            {
                var pares = ids.Zip(ordenes, (id, orden) => (Id: id, Orden: orden)).ToList();
                bool actualizado = _menuOpcionesService.ActualizarOrden(pares);

                if (actualizado)
                    UsuarioSesionHelper.InvalidarCachePermisos();

                return Json(new
                {
                    success = actualizado,
                    message = actualizado ? R("Menu_OrdenActualizado") : R("Menu_ErrorActualizarOrden")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = R("Menu_ErrorActualizarOrden") + ": " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                return RedirectToAction("Index", "Home");
            }

            try
            {
                bool eliminado = _menuOpcionesService.EliminarMenuOpcion(id);

                if (eliminado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    SetSuccessMessage("Opción de menú eliminada correctamente.");
                }
                else
                {
                    SetErrorMessage("Error al eliminar la opción de menú.");
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar la opción: " + ex.Message);
            }

            return RedirectToAction("Index");
        }
    }
}
