using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class WidgetsController : BaseController
    {
        private readonly WidgetsService _svc = new WidgetsService();

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            return View("~/Views/Configuracion/Widgets.cshtml", _svc.ObtenerTodas());
        }

        [HttpPost]
        [ValidateInput(false)]
        public ActionResult Crear(WidgetTarjeta model)
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

                bool ok = _svc.Crear(model);
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
        [ValidateInput(false)]
        public ActionResult Editar(WidgetTarjeta model)
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

                bool ok = _svc.Editar(model);
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
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                bool ok = _svc.Eliminar(id);
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
