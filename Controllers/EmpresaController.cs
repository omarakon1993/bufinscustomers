using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    public class EmpresaController : BaseController
    {
        private EmpresaService _empresaService = new EmpresaService();

        // Listar empresas
        public ActionResult Empresas()
        {
            var empresas = _empresaService.ObtenerEmpresas();
            return View("~/Views/Configuracion/Empresas.cshtml", empresas);
        }


        // POST: Crear empresa
        [HttpPost]
        public ActionResult CrearEmpresa(Empresas empresa)
        {
            string mensaje;
            bool registrado = _empresaService.CrearEmpresa(empresa, out mensaje);

            if (registrado)
            {
                SetSuccessMessage("Empresa creada correctamente.");
                return RedirectToAction("Empresas");
            }
            else
            {
                ViewBag.ErrorMessage = mensaje;
                return View(empresa);
            }
        }


        // POST: Editar empresa
        [HttpPost]
        public ActionResult EditarEmpresa(Empresas empresa)
        {
            if (!ModelState.IsValid)
                return View(empresa);

            bool actualizado = _empresaService.EditarEmpresa(empresa);

            if (actualizado)
            {
                SetSuccessMessage("Empresa actualizada correctamente.");
                return RedirectToAction("Empresas");
            }
            else
            {
                ViewBag.ErrorMessage = "Error al actualizar la empresa.";
                return View(empresa);
            }
        }

        // POST: Eliminar empresa
        [HttpPost]
        public ActionResult EliminarEmpresa(int idEmpresa)
        {
            bool eliminado = _empresaService.EliminarEmpresa(idEmpresa);

            if (eliminado)
                SetSuccessMessage("Empresa eliminada correctamente.");
            else
                SetErrorMessage("Error al eliminar la empresa.");

            return RedirectToAction("Empresas");
        }
    }
}
