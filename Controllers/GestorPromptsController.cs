using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    [SoloSuperAdmin]
    public class GestorPromptsController(GestorPromptsService service) : BaseController
    {
        public ActionResult Index()
        {
            var prompts = service.ObtenerTodos();
            return View("~/Views/Configuracion/GestorPrompts.cshtml", prompts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(PromptIA prompt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prompt.Codigo) || string.IsNullOrWhiteSpace(prompt.Nombre) || string.IsNullOrWhiteSpace(prompt.TextoPrompt))
                {
                    SetErrorMessage("El código, nombre y texto del prompt son obligatorios.");
                    return RedirectToAction("Index");
                }

                bool ok = service.Crear(prompt);
                if (ok)
                    SetSuccessMessage("Prompt '" + prompt.Nombre + "' creado correctamente.");
                else
                    SetErrorMessage("No se pudo crear el prompt. Intente nuevamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al crear el prompt: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(PromptIA prompt)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prompt.Codigo) || string.IsNullOrWhiteSpace(prompt.Nombre) || string.IsNullOrWhiteSpace(prompt.TextoPrompt))
                {
                    SetErrorMessage("El código, nombre y texto del prompt son obligatorios.");
                    return RedirectToAction("Index");
                }

                bool ok = service.Editar(prompt);
                if (ok)
                    SetSuccessMessage("Prompt '" + prompt.Nombre + "' actualizado correctamente.");
                else
                    SetErrorMessage("No se pudo actualizar el prompt. Intente nuevamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al actualizar el prompt: " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id)
        {
            try
            {
                bool ok = service.Eliminar(id);
                if (ok)
                    SetSuccessMessage("Prompt eliminado correctamente.");
                else
                    SetErrorMessage("No se pudo eliminar el prompt. Intente nuevamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar el prompt: " + ex.Message);
            }

            return RedirectToAction("Index");
        }
    }
}
