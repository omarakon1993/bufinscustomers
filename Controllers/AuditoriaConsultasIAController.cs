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
                SetErrorMessage(R("AudIA_SinPermisoSeccion"));
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Embed         = string.Equals(Request.QueryString["embed"], "1");
            ViewBag.EsAdmin        = esSuperAdmin;
            ViewBag.EsAdminEmpresa = esAdminEmpresa;

            if (esSuperAdmin)
            {
                ViewBag.EmpresasFiltro = new InformeTablasDatosService().ObtenerEmpresas();
            }
            else if (esAdminEmpresa)
            {
                // Admin de Empresa: filtro de empresa acotado a su grupo (solo si el grupo
                // tiene más de una empresa; con una sola no aporta nada).
                var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual)
                                 ?? new List<int>();
                if (permitidas.Count > 1)
                    ViewBag.EmpresasFiltro = new InformeTablasDatosService().ObtenerEmpresas()
                        .Where(e => permitidas.Contains(e.Id)).ToList();
            }

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
                // Si pide una empresa concreta y está dentro de su grupo, se acota a ella.
                if (idEmpresa.HasValue && filtroIdsEmpresa.Contains(idEmpresa.Value))
                    filtroIdsEmpresa = new List<int> { idEmpresa.Value };
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
                r.FilasAnalizadas,
                r.TokensTotal,
                r.Valoracion
            }), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult LimpiarAuditoria(bool completo, int? meses)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return Json(new { success = false, message = R("AudIA_LimpiarSinPermiso") });

            try
            {
                int? mesesConservar = completo ? (int?)null : meses;

                if (!completo && (!mesesConservar.HasValue || mesesConservar.Value < 1 || mesesConservar.Value > 120))
                    return Json(new { success = false, message = R("AudIA_LimpiarMesesInvalido") });

                int eliminados = new AuditoriaAnalisisIAService().LimpiarAuditoria(mesesConservar);

                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario != null)
                {
                    string mensaje = completo
                        ? string.Format(R("AudIA_NotifMsgTodo"), eliminados)
                        : string.Format(R("AudIA_NotifMsgConservar"), eliminados, mesesConservar.Value);
                    new NotificacionesService().Crear(usuario.Id, R("Notif_AuditoriaLimpiada"), mensaje, "warning", "/AuditoriaConsultasIA");
                }

                return Json(new { success = true, eliminados });
            }
            catch
            {
                return Json(new { success = false, message = R("AudIA_LimpiarError") });
            }
        }

        [HttpGet]
        public JsonResult ObtenerUsuariosAuditoria(int? idEmpresa)
        {
            var usuario      = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();

            List<int> filtroIdsEmpresa;
            if (esSuperAdmin)
            {
                filtroIdsEmpresa = idEmpresa.HasValue ? new List<int> { idEmpresa.Value } : null;
            }
            else
            {
                filtroIdsEmpresa = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                if (idEmpresa.HasValue && filtroIdsEmpresa.Contains(idEmpresa.Value))
                    filtroIdsEmpresa = new List<int> { idEmpresa.Value };
            }

            var usuarios = new AuditoriaAnalisisIAService()
                .ObtenerUsuariosDeEmpresa(filtroIdsEmpresa);

            return Json(usuarios, JsonRequestBehavior.AllowGet);
        }
    }
}
