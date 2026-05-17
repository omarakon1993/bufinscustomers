using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class EmpresaController : BaseController
    {
        private EmpresaService _empresaService = new EmpresaService();

        // Listar empresas
        public ActionResult Empresas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = _empresaService.ObtenerEmpresas();

            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();
            }

            return View("~/Views/Configuracion/Empresas.cshtml", empresas);
        }


        // POST: Crear empresa (solo Super Admin)
        [HttpPost]
        public ActionResult CrearEmpresa(Empresas empresa)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tiene permisos para crear empresas.");
                return RedirectToAction("Empresas");
            }

            string mensaje;
            bool registrado = _empresaService.CrearEmpresa(empresa, out mensaje);

            if (registrado)
            {
                EmpresasViewBagFilter.Invalidar();
                SetSuccessMessage("Empresa creada correctamente.");
            }
            else
                SetErrorMessage(mensaje);

            return RedirectToAction("Empresas");
        }


        // POST: Editar empresa
        [HttpPost]
        public ActionResult EditarEmpresa(Empresas empresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (!UsuarioSesionHelper.EsSuperAdmin() && empresa.Id != usuario.IdEmpresa)
            {
                SetErrorMessage("No tiene permisos para editar esta empresa.");
                return RedirectToAction("Empresas");
            }

            string mensaje;
            bool actualizado = _empresaService.EditarEmpresa(empresa, out mensaje);

            if (actualizado)
            {
                EmpresasViewBagFilter.Invalidar();
                SetSuccessMessage(mensaje);
            }
            else
                SetErrorMessage(mensaje);

            return RedirectToAction("Empresas");
        }

        // POST: Eliminar empresa (solo Super Admin)
        [HttpPost]
        public ActionResult EliminarEmpresa(int idEmpresa)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("No tiene permisos para eliminar empresas.");
                return RedirectToAction("Empresas");
            }

            bool eliminado = _empresaService.EliminarEmpresa(idEmpresa);

            if (eliminado)
            {
                EmpresasViewBagFilter.Invalidar();
                SetSuccessMessage("Empresa eliminada correctamente.");
            }
            else
                SetErrorMessage("Error al eliminar la empresa.");

            return RedirectToAction("Empresas");
        }
    }
}
