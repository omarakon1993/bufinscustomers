using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class GestorPromptsController : BaseController
    {
        private readonly GestorPromptsService _service = new GestorPromptsService();

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            var prompts = _service.ObtenerTodos();
            return View("~/Views/Configuracion/GestorPrompts.cshtml", prompts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(PromptIA prompt)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(prompt.Codigo) || string.IsNullOrWhiteSpace(prompt.Nombre) || string.IsNullOrWhiteSpace(prompt.TextoPrompt))
                {
                    SetErrorMessage("El código, nombre y texto del prompt son obligatorios.");
                    return RedirectToAction("Index");
                }

                bool ok = _service.Crear(prompt);
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
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(prompt.Codigo) || string.IsNullOrWhiteSpace(prompt.Nombre) || string.IsNullOrWhiteSpace(prompt.TextoPrompt))
                {
                    SetErrorMessage("El código, nombre y texto del prompt son obligatorios.");
                    return RedirectToAction("Index");
                }

                bool ok = _service.Editar(prompt);
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
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                bool ok = _service.Eliminar(id);
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
