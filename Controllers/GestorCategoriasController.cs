using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class GestorCategoriasController : BaseController
    {
        private readonly MenuEstructuraService _svc = new MenuEstructuraService();

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            return View("~/Views/Configuracion/GestorCategorias.cshtml", _svc.ObtenerCategorias());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(CategoriaMenu model)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la categoría es obligatorio.");
                    return RedirectToAction("Index");
                }
                bool ok = _svc.CrearCategoria(model);
                if (ok) SetSuccessMessage("Categoría '" + model.Nombre + "' creada correctamente.");
                else    SetErrorMessage("No se pudo crear la categoría.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(CategoriaMenu model)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la categoría es obligatorio.");
                    return RedirectToAction("Index");
                }
                bool ok = _svc.EditarCategoria(model);
                if (ok) SetSuccessMessage("Categoría '" + model.Nombre + "' actualizada correctamente.");
                else    SetErrorMessage("No se pudo actualizar la categoría.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                bool ok = _svc.EliminarCategoria(id);
                if (ok) SetSuccessMessage("Categoría eliminada correctamente.");
                else    SetErrorMessage("No se pudo eliminar la categoría.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }
    }
}
