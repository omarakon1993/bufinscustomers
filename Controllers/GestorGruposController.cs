using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class GestorGruposController : BaseController
    {
        private readonly MenuEstructuraService _svc = new MenuEstructuraService();

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            ViewBag.Categorias = _svc.ObtenerCategorias();
            return View("~/Views/Configuracion/GestorGrupos.cshtml", _svc.ObtenerGrupos());
        }

        [HttpPost]
        public ActionResult Crear(GrupoMenu model)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre del grupo es obligatorio.");
                    return RedirectToAction("Index");
                }
                bool ok = _svc.CrearGrupo(model);
                if (ok) SetSuccessMessage("Grupo '" + model.Nombre + "' creado correctamente.");
                else    SetErrorMessage("No se pudo crear el grupo.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public ActionResult Editar(GrupoMenu model)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre del grupo es obligatorio.");
                    return RedirectToAction("Index");
                }
                bool ok = _svc.EditarGrupo(model);
                if (ok) { UsuarioSesionHelper.InvalidarCachePermisos(); SetSuccessMessage("Grupo '" + model.Nombre + "' actualizado correctamente."); }
                else    SetErrorMessage("No se pudo actualizar el grupo.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                bool ok = _svc.EliminarGrupo(id);
                if (ok) SetSuccessMessage("Grupo eliminado correctamente.");
                else    SetErrorMessage("No se pudo eliminar el grupo.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }
    }
}
