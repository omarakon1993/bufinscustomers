using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class AnalisisIAController : BaseController
    {
        private readonly InformeTablasDatosService _service = new InformeTablasDatosService();

        public ActionResult Index()
        {
            var usuario        = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin   = UsuarioSesionHelper.EsSuperAdmin();
            var esAdminEmpresa = UsuarioSesionHelper.EsAdminEmpresa();
            var idEmpresa      = usuario?.IdEmpresa ?? 0;

            ViewBag.Tablas   = _service.ObtenerTablasDisponibles();
            ViewBag.EsAdmin  = esSuperAdmin;
            ViewBag.EsAdminEmpresa  = esAdminEmpresa;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            if (esSuperAdmin)
                ViewBag.Empresas = _service.ObtenerEmpresas();
            else
                ViewBag.Empresas = _service.ObtenerEmpresas().Where(e => e.Id == idEmpresa).ToList();

            return View("~/Views/Informes/AnalisisIA.cshtml");
        }

        [HttpGet]
        public JsonResult ObtenerAuditoria(int? idEmpresa, int? idUsuario, string desde, string hasta)
        {
            var usuario        = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin   = UsuarioSesionHelper.EsSuperAdmin();
            var esAdminEmpresa = UsuarioSesionHelper.EsAdminEmpresa();

            int? filtroIdEmpresa = null;
            int? filtroIdUsuario = null;

            if (!esSuperAdmin && !esAdminEmpresa)
            {
                // Usuario normal: solo sus propios registros
                filtroIdUsuario = usuario.Id;
                filtroIdEmpresa = usuario.IdEmpresa;
            }
            else if (!esSuperAdmin)
            {
                // Admin empresa: solo su empresa, puede filtrar por usuario de la empresa
                filtroIdEmpresa = usuario.IdEmpresa;
                if (idUsuario.HasValue) filtroIdUsuario = idUsuario;
            }
            else
            {
                // Super admin: acceso total, puede filtrar por empresa y usuario
                if (idEmpresa.HasValue) filtroIdEmpresa = idEmpresa;
                if (idUsuario.HasValue) filtroIdUsuario = idUsuario;
            }

            DateTime? fechaDesde = null, fechaHasta = null;
            if (!string.IsNullOrWhiteSpace(desde) && DateTime.TryParse(desde, out var d)) fechaDesde = d;
            if (!string.IsNullOrWhiteSpace(hasta) && DateTime.TryParse(hasta, out var h)) fechaHasta = h;

            var registros = new AuditoriaAnalisisIAService()
                .ObtenerRegistros(filtroIdUsuario, filtroIdEmpresa, fechaDesde, fechaHasta);

            return Json(registros.Select(r => new
            {
                r.Id,
                r.NombreUsuario,
                r.NombreEmpresa,
                r.NombreTabla,
                r.Filtros,
                Pregunta        = r.Pregunta ?? "(Resumen Gerencial)",
                r.Respuesta,
                FechaPregunta   = r.FechaPregunta.ToString("dd/MM/yyyy HH:mm:ss"),
                r.FilasAnalizadas
            }), JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerUsuariosAuditoria(int? idEmpresa)
        {
            var usuario      = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();

            // Si no es super admin, siempre se fuerza a su empresa
            int? filtroIdEmpresa = esSuperAdmin ? idEmpresa : usuario.IdEmpresa;

            var usuarios = new AuditoriaAnalisisIAService()
                .ObtenerUsuariosDeEmpresa(filtroIdEmpresa);

            return Json(usuarios, JsonRequestBehavior.AllowGet);
        }
    }
}
