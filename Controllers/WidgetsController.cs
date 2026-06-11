using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class WidgetsController : BaseController
    {
        private readonly WidgetsService _svc = new WidgetsService();

        public async Task<ActionResult> Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            return View("~/Views/Configuracion/Widgets.cshtml", await _svc.ObtenerTodasAsync());
        }

        [HttpPost]
       [ValidateAntiForgeryToken]
        [ValidateInput(false)]
        public async Task<ActionResult> Crear(WidgetTarjeta model)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la tarjeta es obligatorio.");
                    return RedirectToAction("Index");
                }

                bool ok = await _svc.CrearAsync(model);
                if (ok) SetSuccessMessage("Tarjeta '" + model.Nombre + "' creada correctamente.");
                else    SetErrorMessage("No se pudo crear la tarjeta.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al crear: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
       [ValidateAntiForgeryToken]
        [ValidateInput(false)]
        public async Task<ActionResult> Editar(WidgetTarjeta model)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la tarjeta es obligatorio.");
                    return RedirectToAction("Index");
                }

                bool ok = await _svc.EditarAsync(model);
                if (ok) SetSuccessMessage("Tarjeta '" + model.Nombre + "' actualizada correctamente.");
                else    SetErrorMessage("No se pudo actualizar la tarjeta.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al actualizar: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                bool ok = await _svc.EliminarAsync(id);
                if (ok) SetSuccessMessage("Tarjeta eliminada.");
                else    SetErrorMessage("No se pudo eliminar la tarjeta.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar: " + ex.Message);
            }

            return RedirectToAction("Index");
        }
    }
}
