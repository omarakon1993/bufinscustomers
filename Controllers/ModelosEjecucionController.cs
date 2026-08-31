using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    [SoloSuperAdmin]
    public class ModelosEjecucionController : BaseController
    {
        private readonly ModeloService _modeloService = new ModeloService();

        public ActionResult Index()
        {
            var modelos = _modeloService.ObtenerTodos();
            return View("~/Views/Configuracion/ModelosEjecucion.cshtml", modelos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(ModeloEjecucion modelo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelo.Nombre) || string.IsNullOrWhiteSpace(modelo.NombreSP))
                {
                    SetErrorMessage("El nombre y el nombre del SP son obligatorios.");
                    return RedirectToAction("Index");
                }

                bool ok = _modeloService.Crear(modelo);
                if (ok)
                {
                    SetSuccessMessage("Modelo '" + modelo.Nombre + "' creado correctamente.");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Modelos, AuditoriaAccion.Crear,
                        "ModelosEjecucion", modelo.Id.ToString(), $"Modelo de ejecución creado: {modelo.Nombre} (SP {modelo.NombreSP})", null, modelo);
                }
                else
                    SetErrorMessage("No se pudo crear el modelo. Intente nuevamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al crear el modelo: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(ModeloEjecucion modelo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelo.Nombre) || string.IsNullOrWhiteSpace(modelo.NombreSP))
                {
                    SetErrorMessage("El nombre y el nombre del SP son obligatorios.");
                    return RedirectToAction("Index");
                }

                bool ok = _modeloService.Editar(modelo);
                if (ok)
                {
                    SetSuccessMessage("Modelo '" + modelo.Nombre + "' actualizado correctamente.");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Modelos, AuditoriaAccion.Editar,
                        "ModelosEjecucion", modelo.Id.ToString(), $"Modelo de ejecución editado: {modelo.Nombre} (SP {modelo.NombreSP})", null, modelo);
                }
                else
                    SetErrorMessage("No se pudo actualizar el modelo. Intente nuevamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al actualizar el modelo: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id)
        {
            try
            {
                bool ok = _modeloService.Eliminar(id);
                if (ok)
                {
                    SetSuccessMessage("Modelo eliminado correctamente.");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Modelos, AuditoriaAccion.Eliminar,
                        "ModelosEjecucion", id.ToString(), $"Modelo de ejecución eliminado/inactivado (Id {id})");
                }
                else
                    SetErrorMessage("No se pudo eliminar el modelo. Intente nuevamente.");
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number == 547)
            {
                SetErrorMessage("No se puede eliminar el modelo porque tiene registros relacionados (por ejemplo, ejecuciones previas). Puedes inactivarlo en su lugar desde Editar.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar el modelo: " + ex.Message);
            }

            return RedirectToAction("Index");
        }
    }
}
