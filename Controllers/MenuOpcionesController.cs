using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
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
