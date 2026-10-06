using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe de páginas visitadas por usuario (tabla <c>AuditoriaNavegacion</c>).
    /// Modos: detalle paginado, resumen por usuario y resumen por página.
    ///
    /// Alcance:
    ///  - <b>Super Admin</b>: todos los usuarios y empresas.
    ///  - <b>Admin de Empresa</b>: solo usuarios de su empresa o de su grupo empresarial.
    ///  - Cualquier otro rol: sin acceso.
    /// No autocarga: la grilla solo se llena al pulsar «Consultar».
    /// </summary>
    [ValidarSesion]
    public class InformeNavegacionController : BaseController
    {
        private readonly AuditoriaNavegacionService _svc = new AuditoriaNavegacionService();

        // ── Gate + alcance ──────────────────────────────────────────────────

        private bool PuedeAcceder(out bool esSuperAdmin)
        {
            esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();
            return esSuperAdmin || UsuarioSesionHelper.EsAdminEmpresa();
        }

        private void AplicarAlcance(NavegacionFiltro f, bool esSuperAdmin)
        {
            if (esSuperAdmin) return;
            var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual);
            f.IdsEmpresaPermitidas = permitidas ?? new List<int>();
        }

        private JsonResult Prohibido() =>
            Json(new { success = false, forbidden = true, message = R("Err_403_Json") },
                 JsonRequestBehavior.AllowGet);

        private List<int> EmpresasPermitidasOTodas()
        {
            return EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual);
        }

        // ── Vista ──────────────────────────────────────────────────────────

        public ActionResult Index()
        {
            if (!PuedeAcceder(out bool esSuper))
                return new RedirectResult("~/Error/Forbidden");

            ViewBag.Embed = string.Equals(Request.QueryString["embed"], "1");
            ViewBag.EsSuperAdmin = esSuper;

            var empresas = new EmpresaService().ObtenerEmpresas();
            var permitidas = EmpresasPermitidasOTodas();
            if (!esSuper && permitidas != null)
                empresas = empresas.Where(e => permitidas.Contains(e.Id)).ToList();
            ViewBag.Empresas = empresas;

            ViewBag.Usuarios = ObtenerUsuariosParaFiltro(esSuper, permitidas);
            ViewBag.OpcionesMenu = ObtenerOpcionesMenu();

            return View("~/Views/Informes/InformeNavegacion.cshtml");
        }

        // ── Endpoints de datos ─────────────────────────────────────────────

        [HttpGet]
        public JsonResult Detalle(int? idUsuario, int? idEmpresa, string codigoMenu, int? rol,
            string desde, string hasta, string texto = null, int pagina = 1, int tam = 25)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            var f = ConstruirFiltro(idUsuario, idEmpresa, codigoMenu, rol, desde, hasta, texto);
            f.Pagina = pagina; f.TamanoPagina = tam;
            AplicarAlcance(f, esSuper);

            var res = _svc.Consultar(f);

            return Json(new
            {
                success      = true,
                total        = res.Total,
                pagina       = res.Pagina,
                totalPaginas = res.TotalPaginas,
                items = res.Items.Select(n => new
                {
                    Fecha    = n.Fecha.ToString("dd/MM/yyyy HH:mm:ss"),
                    n.NombreUsuario,
                    Empresa  = n.NombreEmpresa,
                    Rol      = RolTexto(n.RolUsuario),
                    Pagina   = n.TituloPagina ?? (n.Controller + "/" + n.Action),
                    Ruta     = n.Controller + "/" + n.Action,
                    n.IpAddress,
                    Navegador = ResumirUserAgent(n.UserAgent)
                })
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Totales del conjunto filtrado para las tarjetas de resumen (independiente del modo de la grilla).</summary>
        [HttpGet]
        public JsonResult ResumenGeneral(int? idUsuario, int? idEmpresa, string codigoMenu, int? rol,
            string desde, string hasta, string texto = null)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            var f = ConstruirFiltro(idUsuario, idEmpresa, codigoMenu, rol, desde, hasta, texto);
            AplicarAlcance(f, esSuper);

            var r = _svc.ResumenGeneral(f);
            return Json(new
            {
                success = true,
                visitas          = r.Visitas,
                usuarios         = r.Usuarios,
                paginasDistintas = r.PaginasDistintas,
                paginaTop        = r.PaginaTop,
                ultima           = r.Ultima?.ToString("dd/MM/yyyy HH:mm")
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ResumenUsuarios(int? idUsuario, int? idEmpresa, string codigoMenu, int? rol,
            string desde, string hasta, string texto = null)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            var f = ConstruirFiltro(idUsuario, idEmpresa, codigoMenu, rol, desde, hasta, texto);
            AplicarAlcance(f, esSuper);

            var filas = _svc.ResumenPorUsuario(f);
            return Json(new
            {
                success = true,
                items = filas.Select(u => new
                {
                    u.NombreUsuario,
                    Empresa           = u.NombreEmpresa,
                    u.Visitas,
                    u.PaginasDistintas,
                    Primera = u.Primera.ToString("dd/MM/yyyy HH:mm"),
                    Ultima  = u.Ultima.ToString("dd/MM/yyyy HH:mm")
                })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ResumenPaginas(int? idUsuario, int? idEmpresa, string codigoMenu, int? rol,
            string desde, string hasta, string texto = null)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            var f = ConstruirFiltro(idUsuario, idEmpresa, codigoMenu, rol, desde, hasta, texto);
            AplicarAlcance(f, esSuper);

            var filas = _svc.ResumenPorPagina(f);
            return Json(new
            {
                success = true,
                items = filas.Select(p => new
                {
                    Pagina = p.Titulo ?? p.Clave,
                    Ruta   = (p.Controller ?? "") + "/" + (p.Action ?? ""),
                    p.Visitas,
                    p.UsuariosDistintos,
                    Ultima = p.Ultima.ToString("dd/MM/yyyy HH:mm")
                })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ResumenUsuarioPagina(int? idUsuario, int? idEmpresa, string codigoMenu, int? rol,
            string desde, string hasta, string texto = null)
        {
            if (!PuedeAcceder(out bool esSuper)) return Prohibido();

            var f = ConstruirFiltro(idUsuario, idEmpresa, codigoMenu, rol, desde, hasta, texto);
            AplicarAlcance(f, esSuper);

            var filas = _svc.ResumenPorUsuarioPagina(f);
            return Json(new
            {
                success = true,
                items = filas.Select(x => new
                {
                    x.NombreUsuario,
                    Empresa = x.NombreEmpresa,
                    Pagina  = x.Titulo ?? x.Clave,
                    Ruta    = (x.Controller ?? "") + "/" + (x.Action ?? ""),
                    x.Visitas,
                    Primera = x.Primera.ToString("dd/MM/yyyy HH:mm"),
                    Ultima  = x.Ultima.ToString("dd/MM/yyyy HH:mm")
                })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult ExportarExcel(string modo, int? idUsuario, int? idEmpresa, string codigoMenu,
            int? rol, string desde, string hasta, string texto = null)
        {
            if (!PuedeAcceder(out bool esSuper))
                return new RedirectResult("~/Error/Forbidden");

            var f = ConstruirFiltro(idUsuario, idEmpresa, codigoMenu, rol, desde, hasta, texto);
            AplicarAlcance(f, esSuper);

            using (var wb = new XLWorkbook())
            {
                if (string.Equals(modo, "usuarios", StringComparison.OrdinalIgnoreCase))
                {
                    var ws = wb.Worksheets.Add("Resumen usuarios");
                    string[] cab = { "Usuario", "Empresa", "Visitas", "Páginas distintas", "Primera actividad", "Última actividad" };
                    for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                    ws.Row(1).Style.Font.Bold = true;
                    int fila = 2;
                    foreach (var u in _svc.ResumenPorUsuario(f))
                    {
                        ws.Cell(fila, 1).Value = u.NombreUsuario ?? "";
                        ws.Cell(fila, 2).Value = u.NombreEmpresa ?? "";
                        ws.Cell(fila, 3).Value = u.Visitas;
                        ws.Cell(fila, 4).Value = u.PaginasDistintas;
                        ws.Cell(fila, 5).Value = u.Primera; ws.Cell(fila, 5).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        ws.Cell(fila, 6).Value = u.Ultima;  ws.Cell(fila, 6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        fila++;
                    }
                    ws.Columns().AdjustToContents();
                    ws.SheetView.Freeze(1, 0);
                }
                else if (string.Equals(modo, "usuariopagina", StringComparison.OrdinalIgnoreCase))
                {
                    var ws = wb.Worksheets.Add("Resumen usuario y página");
                    string[] cab = { "Usuario", "Empresa", "Página", "Ruta", "Visitas", "Primera actividad", "Última actividad" };
                    for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                    ws.Row(1).Style.Font.Bold = true;
                    int fila = 2;
                    foreach (var x in _svc.ResumenPorUsuarioPagina(f))
                    {
                        ws.Cell(fila, 1).Value = x.NombreUsuario ?? "";
                        ws.Cell(fila, 2).Value = x.NombreEmpresa ?? "";
                        ws.Cell(fila, 3).Value = x.Titulo ?? x.Clave ?? "";
                        ws.Cell(fila, 4).Value = (x.Controller ?? "") + "/" + (x.Action ?? "");
                        ws.Cell(fila, 5).Value = x.Visitas;
                        ws.Cell(fila, 6).Value = x.Primera; ws.Cell(fila, 6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        ws.Cell(fila, 7).Value = x.Ultima;  ws.Cell(fila, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        fila++;
                    }
                    ws.Columns().AdjustToContents();
                    ws.SheetView.Freeze(1, 0);
                }
                else if (string.Equals(modo, "paginas", StringComparison.OrdinalIgnoreCase))
                {
                    var ws = wb.Worksheets.Add("Resumen páginas");
                    string[] cab = { "Página", "Ruta", "Visitas", "Usuarios distintos", "Última visita" };
                    for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                    ws.Row(1).Style.Font.Bold = true;
                    int fila = 2;
                    foreach (var p in _svc.ResumenPorPagina(f))
                    {
                        ws.Cell(fila, 1).Value = p.Titulo ?? p.Clave ?? "";
                        ws.Cell(fila, 2).Value = (p.Controller ?? "") + "/" + (p.Action ?? "");
                        ws.Cell(fila, 3).Value = p.Visitas;
                        ws.Cell(fila, 4).Value = p.UsuariosDistintos;
                        ws.Cell(fila, 5).Value = p.Ultima; ws.Cell(fila, 5).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        fila++;
                    }
                    ws.Columns().AdjustToContents();
                    ws.SheetView.Freeze(1, 0);
                }
                else
                {
                    var ws = wb.Worksheets.Add("Navegación");
                    string[] cab = { "Fecha", "Usuario", "Empresa", "Rol", "Página", "Ruta", "IP", "Navegador" };
                    for (int i = 0; i < cab.Length; i++) ws.Cell(1, i + 1).Value = cab[i];
                    ws.Row(1).Style.Font.Bold = true;
                    int fila = 2;
                    foreach (var n in _svc.ConsultarParaExport(f))
                    {
                        ws.Cell(fila, 1).Value = n.Fecha; ws.Cell(fila, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                        ws.Cell(fila, 2).Value = n.NombreUsuario ?? "";
                        ws.Cell(fila, 3).Value = n.NombreEmpresa ?? "";
                        ws.Cell(fila, 4).Value = RolTexto(n.RolUsuario);
                        ws.Cell(fila, 5).Value = n.TituloPagina ?? (n.Controller + "/" + n.Action);
                        ws.Cell(fila, 6).Value = (n.Controller ?? "") + "/" + (n.Action ?? "");
                        ws.Cell(fila, 7).Value = n.IpAddress ?? "";
                        ws.Cell(fila, 8).Value = ResumirUserAgent(n.UserAgent);
                        fila++;
                    }
                    ws.Columns().AdjustToContents();
                    ws.SheetView.Freeze(1, 0);
                }

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(ms.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Navegacion_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static NavegacionFiltro ConstruirFiltro(int? idUsuario, int? idEmpresa, string codigoMenu,
            int? rol, string desde, string hasta, string texto = null)
        {
            DateTime? d = null, h = null;
            if (DateTime.TryParse(desde, out var dd)) d = dd;
            if (DateTime.TryParse(hasta, out var hh)) h = hh;
            return new NavegacionFiltro
            {
                IdUsuario  = idUsuario.HasValue && idUsuario.Value > 0 ? idUsuario : null,
                IdEmpresa  = idEmpresa.HasValue && idEmpresa.Value > 0 ? idEmpresa : null,
                CodigoMenu = string.IsNullOrWhiteSpace(codigoMenu) ? null : codigoMenu.Trim(),
                Rol        = rol.HasValue && rol.Value >= 0 && rol.Value <= 2 ? (byte?)rol.Value : null,
                Texto      = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim(),
                Desde      = d,
                Hasta      = h
            };
        }

        private string RolTexto(byte? rol)
        {
            switch (rol)
            {
                case 2: return R("Nav_Rol2");
                case 1: return R("Nav_Rol1");
                case 0: return R("Nav_Rol0");
                default: return "—";
            }
        }

        /// <summary>Extrae una etiqueta corta del user-agent (navegador + plataforma aproximada).</summary>
        private static string ResumirUserAgent(string ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return "";
            string s = ua;
            string nav =
                s.IndexOf("Edg/", StringComparison.OrdinalIgnoreCase) >= 0 ? "Edge" :
                s.IndexOf("OPR/", StringComparison.OrdinalIgnoreCase) >= 0 ? "Opera" :
                s.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0 ? "Firefox" :
                s.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0 ? "Chrome" :
                s.IndexOf("Safari", StringComparison.OrdinalIgnoreCase) >= 0 ? "Safari" : "Otro";
            string so =
                s.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ? "Windows" :
                s.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0 ? "Android" :
                s.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0 ? "iOS" :
                s.IndexOf("Mac OS", StringComparison.OrdinalIgnoreCase) >= 0 ? "macOS" :
                s.IndexOf("Linux", StringComparison.OrdinalIgnoreCase) >= 0 ? "Linux" : "";
            return so.Length > 0 ? nav + " · " + so : nav;
        }

        private List<SelectListItem> ObtenerUsuariosParaFiltro(bool esSuper, List<int> permitidas)
        {
            var lista = new List<SelectListItem>();
            try
            {
                var sql = @"SELECT Id, LTRIM(RTRIM(ISNULL(Nombre,'') + ' ' + ISNULL(Apellidos,''))) AS NombreCompleto, Correo, IdEmpresa
                            FROM Usuarios";
                if (!esSuper && permitidas != null && permitidas.Count > 0)
                    sql += " WHERE IdEmpresa IN (" + string.Join(",", permitidas.Select((_, i) => "@e" + i)) + ")";
                else if (!esSuper)
                    sql += " WHERE 1 = 0";
                sql += " ORDER BY NombreCompleto";

                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    if (!esSuper && permitidas != null)
                        for (int i = 0; i < permitidas.Count; i++) cmd.Parameters.AddWithValue("@e" + i, permitidas[i]);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            string nombre = r["NombreCompleto"].ToString();
                            if (string.IsNullOrWhiteSpace(nombre)) nombre = r["Correo"].ToString();
                            lista.Add(new SelectListItem { Value = Convert.ToInt32(r["Id"]).ToString(), Text = nombre });
                        }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[InformeNavegacion.ObtenerUsuariosParaFiltro] {0}", ex.Message);
            }
            return lista;
        }

        private List<SelectListItem> ObtenerOpcionesMenu()
        {
            var lista = new List<SelectListItem>();
            try
            {
                bool en = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                foreach (var op in new MenuOpcionesService().ObtenerTodas()
                             .Where(o => !string.IsNullOrWhiteSpace(o.Codigo))
                             .GroupBy(o => o.Codigo).Select(g => g.First())
                             .OrderBy(o => en ? (o.NombreEN ?? o.Nombre) : o.Nombre))
                {
                    lista.Add(new SelectListItem { Value = op.Codigo, Text = en ? (op.NombreEN ?? op.Nombre) : op.Nombre });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[InformeNavegacion.ObtenerOpcionesMenu] {0}", ex.Message);
            }
            return lista;
        }
    }
}
