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
                // Empresa propia y las de su mismo grupo empresarial
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new System.Collections.Generic.List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            return View("~/Views/Configuracion/Empresas.cshtml", empresas);
        }


        // POST: Crear empresa (solo Super Admin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CrearEmpresa(Empresas empresa)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage(R("Emp_SinPermisoCrear"));
                return RedirectToAction("Empresas");
            }

            // Página web: opcional, pero si viene debe ser una URL válida (se normaliza: sin esquema → https://).
            if (!UrlWebHelper.TryNormalizar(empresa.PaginaWeb, out string webNormalizada))
            {
                SetErrorMessage(R("Emp_ErrorPaginaWeb"));
                return RedirectToAction("Empresas");
            }
            empresa.PaginaWeb = webNormalizada;

            string mensaje, advertencia;
            bool registrado = _empresaService.CrearEmpresa(empresa, out mensaje, out advertencia);

            if (registrado)
            {
                EmpresasViewBagFilter.Invalidar();
                if (advertencia == "WEB_SIN_COLUMNA") SetInfoMessage(R("Emp_PaginaWebSinColumna"));
                SetSuccessMessage(R("Emp_Creada"));
                new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_EmpresaCreada"), empresa.Nombre, "success", "/Empresa/Empresas");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Crear,
                    "Empresas", empresa.Id.ToString(), $"Empresa creada: {empresa.Nombre}", null, empresa, idEmpresa: empresa.Id);
            }
            else
                SetErrorMessage(mensaje);

            return RedirectToAction("Empresas");
        }


        // POST: Editar empresa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarEmpresa(Empresas empresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, empresa.Id))
            {
                SetErrorMessage(R("Emp_SinPermisoEditar"));
                return RedirectToAction("Empresas");
            }

            if (!UrlWebHelper.TryNormalizar(empresa.PaginaWeb, out string webNormalizada))
            {
                SetErrorMessage(R("Emp_ErrorPaginaWeb"));
                return RedirectToAction("Empresas");
            }
            empresa.PaginaWeb = webNormalizada;

            string mensaje, advertencia;
            bool actualizado = _empresaService.EditarEmpresa(empresa, out mensaje, out advertencia);

            if (actualizado)
            {
                EmpresasViewBagFilter.Invalidar();
                if (advertencia == "WEB_SIN_COLUMNA") SetInfoMessage(R("Emp_PaginaWebSinColumna"));
                SetSuccessMessage(mensaje);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "Empresas", empresa.Id.ToString(), $"Empresa editada: {empresa.Nombre}", null, empresa, idEmpresa: empresa.Id);
            }
            else
                SetErrorMessage(mensaje);

            return RedirectToAction("Empresas");
        }

        // POST: Eliminar empresa (solo Super Admin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EliminarEmpresa(int idEmpresa)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage(R("Emp_SinPermisoEliminar"));
                return RedirectToAction("Empresas");
            }

            bool eliminado = _empresaService.EliminarEmpresa(idEmpresa);

            if (eliminado)
            {
                EmpresasViewBagFilter.Invalidar();
                SetSuccessMessage(R("Emp_Eliminada"));
                new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_EmpresaEliminada"), null, "warning", "/Empresa/Empresas");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Eliminar,
                    "Empresas", idEmpresa.ToString(), $"Empresa eliminada (Id {idEmpresa})", idEmpresa: idEmpresa);
            }
            else
                SetErrorMessage(R("Emp_ErrorEliminar"));

            return RedirectToAction("Empresas");
        }
    }
}
