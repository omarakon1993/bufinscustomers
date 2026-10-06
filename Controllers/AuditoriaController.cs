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
    ///  - <b>Admin de Empresa</b>: la misma experiencia completa (todos los tipos: cambios,
    ///    ejecuciones de modelos, cargues, etc.) pero acotada a su empresa o su mismo grupo
    ///    empresarial. Excepción: <c>Limpiar</c> sigue siendo exclusivo de Super Admin.
    ///  - <b>Usuario Normal</b>: acceso disponible (quién ve la opción en el menú se controla
    ///    por permiso/menú, no aquí); solo puede ver su propia auditoría (<c>IdUsuario</c>).
    /// </summary>
    [ValidarSesion]
    public class AuditoriaController : BaseController
    {
        private readonly AuditoriaService _svc = new AuditoriaService();

        // ── Gate + alcance ──────────────────────────────────────────────────

        /// <summary>
        /// Cualquier usuario con sesión válida puede entrar (quién ve la opción se controla por
        /// menú/permiso, no por rol aquí). Devuelve el rol para que cada endpoint aplique su alcance.
        /// </summary>
        private bool PuedeAcceder(out bool esSuperAdmin, out bool esUsuarioNormal)
        {
            esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();
            esUsuarioNormal = UsuarioSesionHelper.EsUsuarioNormal();
            return UsuarioSesionHelper.UsuarioActual != null;
        }

        /// <summary>
        /// Aplica el recorte por rol al filtro: Admin de Empresa ve todos los tipos igual que Super
        /// Admin, solo acotado por empresa/grupo; Usuario Normal solo ve sus propias filas (<c>IdUsuario</c>).
        /// </summary>
        private void AplicarAlcance(AuditoriaFiltro f, bool esSuperAdmin, bool esUsuarioNormal)
        {
            if (esSuperAdmin) return;

            if (esUsuarioNormal)
            {
                f.IdUsuario = UsuarioSesionHelper.UsuarioActual.Id;   // ignora cualquier idUsuario pedido por el cliente
                f.IdsEmpresaPermitidas = null;                        // ya queda acotado por usuario
                return;
            }

            var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual);
            f.IdsEmpresaPermitidas = permitidas ?? new List<int>();
        }

        private JsonResult Prohibido() =>
            Json(new { success = false, forbidden = true, message = R("Err_403_Json") },
                 JsonRequestBehavior.AllowGet);

        // ── Vistas / endpoints ─────────────────────────────────────────────

        public ActionResult Index()
        {
            if (!PuedeAcceder(out bool esSuper, out bool esNormal))
                return new RedirectResult("~/Error/Forbidden");

            var usuario = UsuarioSesionHelper.UsuarioActual;

            ViewBag.Embed = string.Equals(Request.QueryString["embed"], "1");
            ViewBag.EsSuperAdmin = esSuper;
            ViewBag.EsUsuarioNormal = esNormal;
            ViewBag.Tipos = _svc.ObtenerTiposUsados();

            var empresas = new EmpresaService().ObtenerEmpresas();
            List<int> permitidasEmp = null;
            if (esNormal)
            {
                empresas = empresas.Where(e => usuario.IdEmpresa.HasValue && e.Id == usuario.IdEmpresa.Value).ToList();
            }
            else if (!esSuper)
            {
                permitidasEmp = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => permitidasEmp.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresas;
            ViewBag.Usuarios = esNormal
                ? new List<KeyValuePair<int, string>>()
                : _svc.ObtenerUsuariosParaFiltro(permitidasEmp);

            return View("~/Views/Informes/Auditoria.cshtml");
        }

        [HttpGet]
        public JsonResult Consultar(string tipo, string accion, int? idEmpresa, int? idUsuario, string entidad,
            string entidadId, string severidad, string operacionId,
            string texto, string desde, string hasta, string excluirTipo = null, int pagina = 1, int tam = 25)
        {
            if (!PuedeAcceder(out bool esSuper, out bool esNormal)) return Prohibido();

            DateTime? d = null, h = null;
            if (DateTime.TryParse(desde, out var dd)) d = dd;
            if (DateTime.TryParse(hasta, out var hh)) h = hh;
            Guid? op = Guid.TryParse(operacionId, out var gg) ? gg : (Guid?)null;

            var filtro = new AuditoriaFiltro
            {
                Tipo = tipo, ExcluirTipo = excluirTipo, Accion = accion, IdEmpresa = idEmpresa, IdUsuario = idUsuario,
                Entidad = entidad, EntidadId = entidadId, Severidad = severidad, OperacionId = op,
                Texto = texto, Desde = d, Hasta = h, Pagina = pagina, TamanoPagina = tam
            };
            AplicarAlcance(filtro, esSuper, esNormal);

            var res = _svc.Consultar(filtro);
            var resumen = _svc.Resumen(filtro);

            return Json(new
            {
                success      = true,
                total        = res.Total,
                pagina       = res.Pagina,
                totalPaginas = res.TotalPaginas,
                resumen = new
                {
                    usuarios       = resumen.Usuarios,
                    relevanciaAlta = resumen.RelevanciaAlta,
                    ultimo         = resumen.Ultimo?.ToString("dd/MM/yyyy HH:mm:ss")
                },
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
        public ActionResult ExportarExcel(string tipo, string accion, int? idEmpresa, int? idUsuario, string entidad,
            string entidadId, string severidad, string operacionId,
            string texto, string desde, string hasta, string excluirTipo = null)
        {
            if (!PuedeAcceder(out bool esSuper, out bool esNormal))
                return new RedirectResult("~/Error/Forbidden");

            DateTime? d = null, h = null;
            if (DateTime.TryParse(desde, out var dd)) d = dd;
            if (DateTime.TryParse(hasta, out var hh)) h = hh;
            Guid? op = Guid.TryParse(operacionId, out var gg) ? gg : (Guid?)null;

            var filtro = new AuditoriaFiltro
            {
                Tipo = tipo, ExcluirTipo = excluirTipo, Accion = accion, IdEmpresa = idEmpresa, IdUsuario = idUsuario,
                Entidad = entidad, EntidadId = entidadId, Severidad = severidad, OperacionId = op,
                Texto = texto, Desde = d, Hasta = h
            };
            AplicarAlcance(filtro, esSuper, esNormal);

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
            if (!PuedeAcceder(out bool esSuper, out bool esNormal)) return Prohibido();

            var a = _svc.ObtenerPorId(id);
            if (a == null) return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            if (!esSuper)
            {
                if (esNormal)
                {
                    // Usuario Normal solo puede ver el detalle de su propia auditoría.
                    if (a.IdUsuario != UsuarioSesionHelper.UsuarioActual.Id)
                        return Prohibido();
                }
                else
                {
                    // Admin de Empresa solo puede ver el detalle de eventos dentro de su empresa/grupo.
                    var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual)
                                     ?? new List<int>();
                    if (!a.IdEmpresa.HasValue || !permitidas.Contains(a.IdEmpresa.Value))
                        return Prohibido();
                }
            }

            return Json(new
            {
                success       = true,
                fecha         = a.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                fechaIso      = a.Fecha.ToString("yyyy-MM-ddTHH:mm:ss"),
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
                valorNuevo    = Embellecer(a.ValorNuevo),
                permisosDetalle = ResolverPermisos(a)
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Para las filas de <c>UsuarioMenuPermisos</c> traduce el JSON crudo
        /// (<c>{"idUsuario":N,"permisos":[ids]}</c>) a nombre de usuario + nombres de las
        /// opciones de menú, para el bloque "Qué cambió" del detalle. Devuelve null si no aplica.
        /// </summary>
        private object ResolverPermisos(RegistroAuditoria a)
        {
            if (!string.Equals(a.Entidad, "UsuarioMenuPermisos", StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.IsNullOrWhiteSpace(a.ValorNuevo)) return null;

            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(a.ValorNuevo);
                int? idUsuario = (int?)o["idUsuario"];
                var ids = o["permisos"] != null
                    ? o["permisos"].Select(t => (int)t).ToList()
                    : new List<int>();

                bool en = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string usuario = idUsuario.HasValue ? "#" + idUsuario.Value : null;
                var opciones = new List<string>();

                using (var cn = new System.Data.SqlClient.SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    if (idUsuario.HasValue)
                        using (var cmd = new System.Data.SqlClient.SqlCommand(
                            "SELECT LTRIM(RTRIM(ISNULL(Nombre,'') + ' ' + ISNULL(Apellidos,''))) FROM Usuarios WHERE Id = @id", cn))
                        {
                            cmd.Parameters.AddWithValue("@id", idUsuario.Value);
                            var r = cmd.ExecuteScalar();
                            if (r != null && r != DBNull.Value && !string.IsNullOrWhiteSpace(r.ToString()))
                                usuario = r.ToString();
                        }

                    if (ids.Count > 0)
                    {
                        var slots = ids.Select((_, i) => "@p" + i).ToList();
                        using (var cmd = new System.Data.SqlClient.SqlCommand(
                            "SELECT " + (en ? "ISNULL(NombreEN, Nombre)" : "Nombre") +
                            " FROM MenuOpciones WHERE Id IN (" + string.Join(",", slots) + ") ORDER BY Orden", cn))
                        {
                            for (int i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue("@p" + i, ids[i]);
                            using (var rd = cmd.ExecuteReader())
                                while (rd.Read())
                                    if (rd[0] != DBNull.Value) opciones.Add(rd[0].ToString());
                        }
                    }
                }

                return new { usuario, cantidad = ids.Count, opciones };
            }
            catch { return null; }
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
