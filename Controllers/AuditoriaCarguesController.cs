using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;
using bufinscustomers.Helpers;
using System.Collections.Generic;

namespace bufinscustomers.Controllers
{
    public class AuditoriaCarguesController : BaseController
    {
        private AuditoriaCarguesService _auditoriaCarguesService = new AuditoriaCarguesService();

        // Listar auditorias de cargues
        public ActionResult AuditoriaCargues()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = usuario?.Admin == 1;
            
            List<AuditoriaCargues> auditorias;
            
            if (esAdmin)
            {
                // Si es admin, obtener todos los registros
                auditorias = _auditoriaCarguesService.ObtenerAuditoriaCargues();
            }
            else
            {
                // Si no es admin, filtrar solo por su empresa
                var idEmpresa = usuario?.IdEmpresa;
                auditorias = _auditoriaCarguesService.ObtenerAuditoriaCargues(idEmpresa);
            }
            
            return View("~/Views/Informes/AuditoriaCargues.cshtml", auditorias);
        }
    }
}