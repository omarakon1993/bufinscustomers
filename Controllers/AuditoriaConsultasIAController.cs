using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class AuditoriaConsultasIAController : BaseController
    {
        public ActionResult Index()
        {
            var esSuperAdmin   = UsuarioSesionHelper.EsSuperAdmin();
            var esAdminEmpresa = UsuarioSesionHelper.EsAdminEmpresa();

            if (!esSuperAdmin && !esAdminEmpresa)
            {
                SetErrorMessage("No tienes permisos para acceder a esta sección.");
                return RedirectToAction("Index", "Home");
            }

            ViewBag.EsAdmin        = esSuperAdmin;
            ViewBag.EsAdminEmpresa = esAdminEmpresa;

            if (esSuperAdmin)
                ViewBag.EmpresasFiltro = new InformeTablasDatosService().ObtenerEmpresas();

            return View("~/Views/Informes/AuditoriaConsultasIA.cshtml");
        }

        [HttpGet]
        public JsonResult ObtenerAuditoria(int? idEmpresa, int? idUsuario, string desde, string hasta)
        {
            var usuario        = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin   = UsuarioSesionHelper.EsSuperAdmin();
            var esAdminEmpresa = UsuarioSesionHelper.EsAdminEmpresa();

            List<int> filtroIdsEmpresa = null;
            int? filtroIdUsuario = null;

            if (!esSuperAdmin && !esAdminEmpresa)
            {
                filtroIdUsuario = usuario.Id;
                filtroIdsEmpresa = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
            }
            else if (!esSuperAdmin)
            {
                filtroIdsEmpresa = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                if (idUsuario.HasValue) filtroIdUsuario = idUsuario;
            }
            else
            {
                if (idEmpresa.HasValue) filtroIdsEmpresa = new List<int> { idEmpresa.Value };
                if (idUsuario.HasValue) filtroIdUsuario = idUsuario;
            }

            DateTime? fechaDesde = null, fechaHasta = null;
            if (!string.IsNullOrWhiteSpace(desde) && DateTime.TryParse(desde, out var d)) fechaDesde = d;
            if (!string.IsNullOrWhiteSpace(hasta) && DateTime.TryParse(hasta, out var h)) fechaHasta = h;

            var registros = new AuditoriaAnalisisIAService()
                .ObtenerRegistros(filtroIdUsuario, filtroIdsEmpresa, fechaDesde, fechaHasta);

            return Json(registros.Select(r => new
            {
                r.Id,
                r.NombreUsuario,
                r.NombreEmpresa,
                r.NombreTabla,
                r.Filtros,
                Pregunta      = r.Pregunta ?? "(Resumen Gerencial)",
                r.Respuesta,
                FechaPregunta = r.FechaPregunta.ToString("dd/MM/yyyy HH:mm:ss"),
                r.FilasAnalizadas
            }), JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerUsuariosAuditoria(int? idEmpresa)
        {
            var usuario      = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();

            List<int> filtroIdsEmpresa = esSuperAdmin
                ? (idEmpresa.HasValue ? new List<int> { idEmpresa.Value } : null)
                : (EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>());

            var usuarios = new AuditoriaAnalisisIAService()
                .ObtenerUsuariosDeEmpresa(filtroIdsEmpresa);

            return Json(usuarios, JsonRequestBehavior.AllowGet);
        }
    }
}
