using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Visor de la tabla única de auditoría (<c>Auditoria</c>). Paginación server-side.
    ///
    /// Alcance:
    ///  - <b>Super Admin</b>: ve todo, todos los tipos, todas las empresas.
    ///  - <b>Admin de Empresa</b>: solo eventos de <c>Tipo = SEGURIDAD</c> (inicios de sesión)
    ///    de usuarios de su empresa o de su mismo grupo empresarial.
    ///  - Cualquier otro rol: sin acceso.
    /// </summary>
    [ValidarSesion]
    public class AuditoriaController : BaseController
    {
        private readonly AuditoriaService _svc = new AuditoriaService();

        // ── Gate + alcance ──────────────────────────────────────────────────

        private bool PuedeAcceder(out bool esSuperAdmin)
        {
            esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();
            return esSuperAdmin || UsuarioSesionHelper.EsAdminEmpresa();
        }

        /// <summary>Aplica el recorte por rol al filtro (fuerza Tipo/empresas para no-Super Admin).</summary>
        private void AplicarAlcance(AuditoriaFiltro f, bool esSuperAdmin)
        {
            if (esSuperAdmin) return;

            f.Tipo = AuditoriaTipo.Seguridad;   // ignora cualquier tipo pedido por el cliente
            f.Entidad = null;
            var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual);
            f.IdsEmpresaPermitidas = permitidas ?? new List<int>();
        }

        private JsonResult Prohibido() =>
            Json(new { success = false, forbidden = true, message = R("Err_403_Json") },
                 JsonRequestBehavior.AllowGet);

        // ── Vistas / endpoints ─────────────────────────────────────────────

        public ActionResult Index()
        {
            if (!PuedeAcceder(out bool esSuper))
                return new RedirectResult("~/Error/Forbidden");

            ViewBag.Embed = string.Equals(Request.QueryString["embed"], "1");
            ViewBag.EsSuperAdmin = esSuper;
            ViewBag.Tipos = esSuper ? _svc.ObtenerTiposUsados() : new List<string>();

            var empresas = new EmpresaService().ObtenerEmpresas();
            if (!esSuper)
            {
                var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual)
                                 ?? new List<int>();
                empresas = empresas.Where(e => permitidas.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresas;

            return View("~/Views/Informes/Auditoria.cshtml");
        }

        [HttpGet]
        public JsonResult Consultar(string tipo, string accion, int? idEmpresa, string entidad,
            string entidadId, string severidad, string operacionId,
            string texto, string desde, string hasta, int pagina = 1, int tam = 25)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            DateTime? d = null, h = null;
            if (DateTime.TryParse(desde, out var dd)) d = dd;
            if (DateTime.TryParse(hasta, out var hh)) h = hh;
            Guid? op = Guid.TryParse(operacionId, out var gg) ? gg : (Guid?)null;

            var filtro = new AuditoriaFiltro
            {
                Tipo = tipo, Accion = accion, IdEmpresa = idEmpresa, Entidad = entidad,
                EntidadId = entidadId, Severidad = severidad, OperacionId = op,
                Texto = texto, Desde = d, Hasta = h, Pagina = pagina, TamanoPagina = tam
            };
            AplicarAlcance(filtro, esSuper);

            var res = _svc.Consultar(filtro);

            return Json(new
            {
                success      = true,
                total        = res.Total,
                pagina       = res.Pagina,
                totalPaginas = res.TotalPaginas,
                items = res.Items.Select(a => new
                {
                    a.Id,
                    Fecha         = a.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                    a.Tipo,
                    a.Categoria,
                    a.Accion,
                    a.Entidad,
                    a.EntidadId,
                    a.EntidadNombre,
                    Severidad     = a.Severidad ?? AuditoriaSeveridad.Derivar(a.Tipo, a.Accion),
                    OperacionId   = a.OperacionId?.ToString(),
                    a.Descripcion,
                    a.NombreUsuario,
                    Empresa       = a.NombreEmpresa,
                    a.IpAddress,
                    TieneDetalle  = !string.IsNullOrEmpty(a.ValorAnterior) || !string.IsNullOrEmpty(a.ValorNuevo)
                })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult ExportarExcel(string tipo, string accion, int? idEmpresa, string entidad,
            string entidadId, string severidad, string operacionId,
            string texto, string desde, string hasta)
        {
            if (!PuedeAcceder(out bool esSuper))
                return new RedirectResult("~/Error/Forbidden");

            DateTime? d = null, h = null;
            if (DateTime.TryParse(desde, out var dd)) d = dd;
            if (DateTime.TryParse(hasta, out var hh)) h = hh;
            Guid? op = Guid.TryParse(operacionId, out var gg) ? gg : (Guid?)null;

            var filtro = new AuditoriaFiltro
            {
                Tipo = tipo, Accion = accion, IdEmpresa = idEmpresa, Entidad = entidad,
                EntidadId = entidadId, Severidad = severidad, OperacionId = op,
                Texto = texto, Desde = d, Hasta = h
            };
            AplicarAlcance(filtro, esSuper);

            var filas = _svc.ConsultarParaExport(filtro);

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Auditoria");
                string[] cab = { "Fecha", "Tipo", "Accion", "Entidad", "Nombre", "Severidad", "Descripcion",
                                 "Usuario", "Empresa", "IP", "ValorAnterior", "ValorNuevo" };
                for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                ws.Row(1).Style.Font.Bold = true;

                int fila = 2;
                foreach (var a in filas)
                {
                    ws.Cell(fila, 1).Value = a.Fecha;
                    ws.Cell(fila, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    ws.Cell(fila, 2).Value = a.Tipo ?? "";
                    ws.Cell(fila, 3).Value = a.Accion ?? "";
                    ws.Cell(fila, 4).Value = (a.Entidad ?? "") + (string.IsNullOrEmpty(a.EntidadId) ? "" : " #" + a.EntidadId);
                    ws.Cell(fila, 5).Value = a.EntidadNombre ?? "";
                    ws.Cell(fila, 6).Value = a.Severidad ?? AuditoriaSeveridad.Derivar(a.Tipo, a.Accion);
                    ws.Cell(fila, 7).Value = a.Descripcion ?? "";
                    ws.Cell(fila, 8).Value = a.NombreUsuario ?? "";
                    ws.Cell(fila, 9).Value = a.NombreEmpresa ?? "";
                    ws.Cell(fila, 10).Value = a.IpAddress ?? "";
                    ws.Cell(fila, 11).Value = a.ValorAnterior ?? "";
                    ws.Cell(fila, 12).Value = a.ValorNuevo ?? "";
                    fila++;
                }

                ws.Columns().AdjustToContents();
                ws.SheetView.Freeze(1, 0);

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(ms.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Auditoria_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
        }

        [HttpGet]
        public JsonResult Detalle(long id)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            var a = _svc.ObtenerPorId(id);
            if (a == null) return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            // Un Admin de Empresa solo puede ver el detalle de eventos de seguridad de su alcance.
            if (!esSuper)
            {
                if (!string.Equals(a.Tipo, AuditoriaTipo.Seguridad, StringComparison.OrdinalIgnoreCase))
                    return Prohibido();

                var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual)
                                 ?? new List<int>();
                if (!a.IdEmpresa.HasValue || !permitidas.Contains(a.IdEmpresa.Value))
                    return Prohibido();
            }

            return Json(new
            {
                success       = true,
                fecha         = a.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                a.Tipo,
                a.Accion,
                a.Entidad,
                a.EntidadId,
                a.EntidadNombre,
                severidad     = a.Severidad ?? AuditoriaSeveridad.Derivar(a.Tipo, a.Accion),
                operacionId   = a.OperacionId?.ToString(),
                a.Descripcion,
                a.NombreUsuario,
                empresa       = a.NombreEmpresa,
                a.IpAddress,
                a.UserAgent,
                valorAnterior = Embellecer(a.ValorAnterior),
                valorNuevo    = Embellecer(a.ValorNuevo)
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Limpiar(bool completo, int? meses, int? idEmpresa)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return Json(new { success = false, message = R("Audit_LimpiarSinPermiso") });

            try
            {
                int? mesesConservar = completo ? (int?)null : meses;

                if (!completo && (!mesesConservar.HasValue || mesesConservar.Value < 1 || mesesConservar.Value > 120))
                    return Json(new { success = false, message = R("Audit_LimpiarMesesInvalido") });

                int eliminados = _svc.Limpiar(mesesConservar, idEmpresa);

                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario != null)
                {
                    string mensaje = completo
                        ? string.Format(R("Audit_NotifMsgTodo"), eliminados)
                        : string.Format(R("Audit_NotifMsgConservar"), eliminados, mesesConservar.Value);
                    new NotificacionesService().Crear(usuario.Id, R("Notif_AuditoriaLimpiada"), mensaje, "warning", "/Auditoria");
                }

                return Json(new { success = true, eliminados });
            }
            catch
            {
                return Json(new { success = false, message = R("Audit_LimpiarError") });
            }
        }

        private static string Embellecer(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var obj = JsonConvert.DeserializeObject(json);
                return JsonConvert.SerializeObject(obj, Formatting.Indented);
            }
            catch { return json; }
        }
    }
}
