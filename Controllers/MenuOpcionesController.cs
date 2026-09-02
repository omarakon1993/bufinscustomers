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
    [SoloSuperAdmin]
    public class MenuOpcionesController : BaseController
    {
        private readonly MenuOpcionesService _menuOpcionesService = new MenuOpcionesService();
        private readonly MenuEstructuraService _menuEstructuraService = new MenuEstructuraService();

        public ActionResult Index()
        {
            var opciones = _menuOpcionesService.ObtenerTodas();
            ViewBag.Grupos = _menuEstructuraService.ObtenerGrupos();
            ViewBag.Categorias = _menuEstructuraService.ObtenerCategorias();
            return View("~/Views/Configuracion/MenuOpciones.cshtml", opciones);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(MenuOpciones opcion)
        {
            try
            {
                bool creado = _menuOpcionesService.CrearMenuOpcion(opcion);

                if (creado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    MenuRutaCacheHelper.Invalidar();
                    SetSuccessMessage("Opción de menú creada correctamente.");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Menu, AuditoriaAccion.Crear,
                        "MenuOpciones", opcion.Id.ToString(), $"Opción de menú creada: {opcion.Nombre} ({opcion.Codigo})", null, opcion);
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
            try
            {
                bool editado = _menuOpcionesService.EditarMenuOpcion(opcion);

                if (editado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    MenuRutaCacheHelper.Invalidar();
                    SetSuccessMessage("Opción de menú actualizada correctamente.");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Menu, AuditoriaAccion.Editar,
                        "MenuOpciones", opcion.Id.ToString(), $"Opción de menú editada: {opcion.Nombre} ({opcion.Codigo})", null, opcion);
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
            if (ids == null || ordenes == null || ids.Count == 0 || ids.Count != ordenes.Count)
                return Json(new { success = false, message = R("Menu_ErrorActualizarOrden") });

            try
            {
                var pares = ids.Zip(ordenes, (id, orden) => (Id: id, Orden: orden)).ToList();
                bool actualizado = _menuOpcionesService.ActualizarOrden(pares);

                if (actualizado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    MenuRutaCacheHelper.Invalidar();
                }

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
            try
            {
                bool eliminado = _menuOpcionesService.EliminarMenuOpcion(id);

                if (eliminado)
                {
                    UsuarioSesionHelper.InvalidarCachePermisos();
                    MenuRutaCacheHelper.Invalidar();
                    SetSuccessMessage("Opción de menú eliminada correctamente.");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Menu, AuditoriaAccion.Eliminar,
                        "MenuOpciones", id.ToString(), $"Opción de menú eliminada (Id {id})");
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
