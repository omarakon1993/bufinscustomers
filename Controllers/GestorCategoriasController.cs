using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    [SoloSuperAdmin]
    public class GestorCategoriasController(MenuEstructuraService svc) : BaseController
    {
        public ActionResult Index()
        {
            return View("~/Views/Configuracion/GestorCategorias.cshtml", svc.ObtenerCategorias());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(CategoriaMenu model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la categoría es obligatorio.");
                    return RedirectToAction("Index");
                }
                bool ok = svc.CrearCategoria(model);
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
            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la categoría es obligatorio.");
                    return RedirectToAction("Index");
                }
                bool ok = svc.EditarCategoria(model);
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
            try
            {
                bool ok = svc.EliminarCategoria(id);
                if (ok) SetSuccessMessage("Categoría eliminada correctamente.");
                else    SetErrorMessage("No se pudo eliminar la categoría.");
            }
            catch (Exception ex) { SetErrorMessage("Error: " + ex.Message); }

            return RedirectToAction("Index");
        }
    }
}
