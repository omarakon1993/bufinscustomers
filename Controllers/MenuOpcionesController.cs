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

        // Otorga esta opción de menú a TODOS los usuarios que aún no la tengan (excepto Super Admin,
        // que ya tiene acceso total). Solo aplica a opciones que no sean SoloSuperAdmin — el botón ya
        // queda oculto para esas en la vista, pero se revalida aquí como defensa en profundidad.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult HabilitarParaTodos(int id)
        {
            try
            {
                var opcion = _menuOpcionesService.ObtenerTodas().FirstOrDefault(o => o.Id == id);
                if (opcion == null)
                    return Json(new { success = false, message = R("Menu_ErrorHabilitarTodos") });

                if (opcion.SoloSuperAdmin)
                    return Json(new { success = false, message = R("Menu_ErrorHabilitarTodosSuperAdmin") });

                int usuarioAsigno = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;
                int cantidad = _menuOpcionesService.HabilitarOpcionParaTodosLosUsuarios(opcion.Id, opcion.SoloAdminEmpresa, usuarioAsigno);

                UsuarioSesionHelper.InvalidarCachePermisos();
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Permisos, AuditoriaAccion.Asignar,
                    "UsuarioMenuPermisos", opcion.Id.ToString(),
                    $"Opción de menú '{opcion.Nombre}' ({opcion.Codigo}) habilitada para todos los usuarios ({cantidad} usuario(s) nuevo(s))",
                    null, new { IdMenuOpcion = opcion.Id, opcion.Codigo, CantidadAfectados = cantidad });

                return Json(new { success = true, message = string.Format(R("Menu_HabilitarTodosOkFmt"), cantidad) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = R("Menu_ErrorHabilitarTodos") + ": " + ex.Message });
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
