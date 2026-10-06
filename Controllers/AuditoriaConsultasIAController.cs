using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using ClosedXML.Excel;
using System.IO;

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

        /// <summary>Registros visibles para el usuario según su rol (Usuario Normal: solo los propios; Admin de Empresa: su empresa/grupo; Super Admin: todo).</summary>
        private List<Models.AuditoriaAnalisisIA> ObtenerSegunAlcance(int? idEmpresa, int? idUsuario, string desde, string hasta)
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

            return new AuditoriaAnalisisIAService()
                .ObtenerRegistros(filtroIdUsuario, filtroIdsEmpresa, fechaDesde, fechaHasta);
        }

        private const string ResumenAuto = "(Resumen Gerencial)";

        [HttpGet]
        public JsonResult ObtenerAuditoria(int? idEmpresa, int? idUsuario, string desde, string hasta)
        {
            var registros = ObtenerSegunAlcance(idEmpresa, idUsuario, desde, hasta);

            return Json(registros.Select(r => new
            {
                r.Id,
                r.NombreUsuario,
                r.NombreEmpresa,
                r.NombreTabla,
                r.Filtros,
                Pregunta      = r.Pregunta ?? ResumenAuto,
                r.Respuesta,
                FechaPregunta = r.FechaPregunta.ToString("dd/MM/yyyy HH:mm:ss"),
                r.FilasAnalizadas,
                r.TokensTotal,
                r.Valoracion
            }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Exporta a Excel con los mismos filtros de la pantalla: los de servidor (empresa, usuario, período) y los
        /// que la vista aplica en el navegador (tipo, tabla analizada, valoración, texto).
        /// </summary>
        [HttpGet]
        public ActionResult ExportarExcel(int? idEmpresa, int? idUsuario, string desde, string hasta,
            string tipo = null, string tabla = null, string valoracion = null, string texto = null)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin() && !UsuarioSesionHelper.EsAdminEmpresa())
                return new RedirectResult("~/Error/Forbidden");

            var filas = ObtenerSegunAlcance(idEmpresa, idUsuario, desde, hasta)
                .Where(r =>
                {
                    bool esResumen = string.IsNullOrEmpty(r.Pregunta);
                    if (tipo == "resumen" && !esResumen) return false;
                    if (tipo == "pregunta" && esResumen) return false;
                    if (!string.IsNullOrEmpty(tabla) && !string.Equals(r.NombreTabla, tabla, StringComparison.Ordinal)) return false;
                    if (valoracion == "1" && r.Valoracion != 1) return false;
                    if (valoracion == "0" && r.Valoracion != 0) return false;
                    if (valoracion == "sin" && r.Valoracion.HasValue) return false;
                    if (!string.IsNullOrWhiteSpace(texto))
                    {
                        var t = texto.Trim();
                        bool ok = (r.Pregunta ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                               || (r.NombreTabla ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                               || (r.NombreUsuario ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                               || (r.NombreEmpresa ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                               || (r.Filtros ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!ok) return false;
                    }
                    return true;
                })
                .OrderByDescending(r => r.FechaPregunta)
                .Take(20000)
                .ToList();

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Consultas IA");
                string[] cab = { "Fecha", "Usuario", "Empresa", "Tabla", "Filtros", "Tipo", "Pregunta", "Respuesta", "Filas", "Tokens", "Valoracion" };
                for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                ws.Row(1).Style.Font.Bold = true;

                int fila = 2;
                foreach (var r in filas)
                {
                    ws.Cell(fila, 1).Value = r.FechaPregunta;
                    ws.Cell(fila, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    ws.Cell(fila, 2).Value = r.NombreUsuario ?? "";
                    ws.Cell(fila, 3).Value = r.NombreEmpresa ?? "";
                    ws.Cell(fila, 4).Value = r.NombreTabla ?? "";
                    ws.Cell(fila, 5).Value = r.Filtros ?? "";
                    ws.Cell(fila, 6).Value = string.IsNullOrEmpty(r.Pregunta) ? "Resumen" : "Pregunta";
                    ws.Cell(fila, 7).Value = r.Pregunta ?? "";
                    ws.Cell(fila, 8).Value = r.Respuesta ?? "";
                    ws.Cell(fila, 9).Value = r.FilasAnalizadas;
                    ws.Cell(fila, 10).Value = r.TokensTotal;
                    ws.Cell(fila, 11).Value = r.Valoracion == 1 ? "Util" : (r.Valoracion == 0 ? "No util" : "");
                    fila++;
                }

                ws.Columns().AdjustToContents();
                // La respuesta puede ser larga: se limita el ancho para que la hoja siga siendo legible.
                ws.Column(8).Width = 80;
                ws.Column(8).Style.Alignment.WrapText = true;
                ws.SheetView.Freeze(1, 0);

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(ms.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Auditoria_ConsultasIA_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
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
