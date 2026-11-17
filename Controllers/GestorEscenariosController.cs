using bufinscustomers.Filters;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
namespace bufinscustomers.Controllers
{
    /// <summary>
    /// CRUD del catálogo de Escenarios de datos. Super Admin únicamente — la elección de
    /// escenario en Cargue/Historial/Modelos es independiente de este gestor, que solo
    /// administra qué escenarios existen (nombre, orden, activo/inactivo).
    /// </summary>
    [ValidarSesion]
    [SoloSuperAdmin]
    public class GestorEscenariosController(
        EscenarioService escenarioService,
        AuditoriaService auditoriaService) : BaseController
    {
        public ActionResult Index()
        {
            var escenarios = escenarioService.ObtenerTodos();
            return View("~/Views/Configuracion/GestorEscenarios.cshtml", escenarios);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(string nombre)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nombre))
                {
                    SetErrorMessage(R("GestorEscenarios_ErrorNombreRequerido"));
                    return RedirectToAction("Index");
                }

                bool ok = escenarioService.Crear(nombre);
                if (ok)
                {
                    EscenariosViewBagFilter.Invalidar();
                    SetSuccessMessage(string.Format(R("GestorEscenarios_Creado"), nombre.Trim()));
                    auditoriaService.RegistrarCambio(AuditoriaTipo.Escenarios, AuditoriaAccion.Crear,
                        "Escenarios", null, $"Escenario creado: {nombre.Trim()}", null, new { nombre = nombre.Trim() });
                }
                else
                    SetErrorMessage(R("GestorEscenarios_ErrorGuardar"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("GestorEscenarios_ErrorGuardar") + " " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(int id, string nombre, int orden, bool activo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nombre))
                {
                    SetErrorMessage(R("GestorEscenarios_ErrorNombreRequerido"));
                    return RedirectToAction("Index");
                }

                bool ok = escenarioService.Editar(id, nombre, orden, activo);
                if (ok)
                {
                    EscenariosViewBagFilter.Invalidar();
                    SetSuccessMessage(string.Format(R("GestorEscenarios_Editado"), nombre.Trim()));
                    auditoriaService.RegistrarCambio(AuditoriaTipo.Escenarios, AuditoriaAccion.Editar,
                        "Escenarios", id.ToString(), $"Escenario editado: {nombre.Trim()}", null,
                        new { nombre = nombre.Trim(), orden, activo });
                }
                else
                    SetErrorMessage(R("GestorEscenarios_ErrorGuardar"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("GestorEscenarios_ErrorGuardar") + " " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id)
        {
            try
            {
                bool ok = escenarioService.Desactivar(id);
                if (ok)
                {
                    EscenariosViewBagFilter.Invalidar();
                    SetSuccessMessage(R("GestorEscenarios_Desactivado"));
                    auditoriaService.RegistrarCambio(AuditoriaTipo.Escenarios, AuditoriaAccion.Eliminar,
                        "Escenarios", id.ToString(), $"Escenario desactivado (Id {id})");
                }
                else
                    SetErrorMessage(R("GestorEscenarios_ErrorGuardar"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("GestorEscenarios_ErrorGuardar") + " " + ex.Message);
            }

            return RedirectToAction("Index");
        }
    }
}
