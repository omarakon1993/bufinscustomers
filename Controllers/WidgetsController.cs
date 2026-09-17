using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    [SoloSuperAdmin]
    public class WidgetsController : BaseController
    {
        private readonly WidgetsService _svc = new WidgetsService();

        public async Task<ActionResult> Index()
        {
            return View("~/Views/Configuracion/Widgets.cshtml", await _svc.ObtenerTodasAsync());
        }

        [HttpPost]
       [ValidateAntiForgeryToken]
        [ValidateInput(false)]
        public async Task<ActionResult> Crear(WidgetTarjeta model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la tarjeta es obligatorio.");
                    return RedirectToAction("Index");
                }
                if (!ValidarFuente(model)) return RedirectToAction("Index");
                if (model.Tipo != 3) { model.Severidad = null; model.Subtipo = null; }

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
            try
            {
                if (string.IsNullOrWhiteSpace(model.Nombre))
                {
                    SetErrorMessage("El nombre de la tarjeta es obligatorio.");
                    return RedirectToAction("Index");
                }
                if (!ValidarFuente(model)) return RedirectToAction("Index");
                if (model.Tipo != 3) { model.Severidad = null; model.Subtipo = null; }

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
        public async Task<ActionResult> Eliminar(int? id)
        {
            if (!id.HasValue)
            {
                SetErrorMessage("No se identificó la tarjeta a eliminar.");
                return RedirectToAction("Index");
            }

            try
            {
                bool ok = await _svc.EliminarAsync(id.Value);
                if (ok) SetSuccessMessage("Tarjeta eliminada.");
                else    SetErrorMessage("No se pudo eliminar la tarjeta.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ActualizarOrden(List<int> ids, List<int> ordenes)
        {
            if (ids == null || ordenes == null || ids.Count == 0 || ids.Count != ordenes.Count)
                return Json(new { success = false, message = R("Wid_ErrorActualizarOrden") });

            try
            {
                var pares = ids.Zip(ordenes, (id, orden) => (Id: id, Orden: orden)).ToList();
                bool ok = _svc.ActualizarOrden(pares);
                return Json(new { success = ok, message = ok ? R("Wid_OrdenActualizado") : R("Wid_ErrorActualizarOrden") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = R("Wid_ErrorActualizarOrden") + ": " + ex.Message });
            }
        }

        private bool ValidarFuente(WidgetTarjeta model)
        {
            if (model.TipoFuente == 2)
            {
                if (string.IsNullOrWhiteSpace(model.NombreSP))
                {
                    SetErrorMessage(R("Wid_Err_NombreSPRequerido"));
                    return false;
                }
            }
            else if (string.IsNullOrWhiteSpace(model.ConsultaSQL))
            {
                SetErrorMessage(R("Wid_Err_ConsultaRequerida"));
                return false;
            }
            return true;
        }
    }
}
