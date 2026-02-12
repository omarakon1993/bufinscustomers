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

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tienes permisos para acceder a esta función.");
                return RedirectToAction("Index", "Home");
            }

            var opciones = _menuOpcionesService.ObtenerTodas();
            return View("~/Views/Configuracion/MenuOpciones.cshtml", opciones);
        }

        [HttpPost]
        public ActionResult Crear(MenuOpciones opcion)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tienes permisos para esta acción.");
                return RedirectToAction("Index", "Home");
            }

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
        public ActionResult Editar(MenuOpciones opcion)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tienes permisos para esta acción.");
                return RedirectToAction("Index", "Home");
            }

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
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tienes permisos para esta acción.");
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
