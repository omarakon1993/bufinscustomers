using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
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

            // Módulo apagado para la empresa (o usuario bloqueado): no se muestra; el servidor igual rechaza las consultas.
            if (!IAModuloHelper.Visible())
            {
                SetErrorMessage(R("IA_EmpresaSinIAMensaje"));
                return RedirectToAction("Index", "Home");
            }

            // Solo las tablas de resultados de Ejecución de Modelos — con el mismo nombre que
            // tienen configurado en Gestor de Modelos (ver InformeTablasDatosService.ObtenerTablasModelos()).
            ViewBag.Tablas           = _service.ObtenerTablasModelos();
            ViewBag.EsAdmin          = esSuperAdmin;
            ViewBag.EsAdminEmpresa   = esAdminEmpresa;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            if (esSuperAdmin)
                ViewBag.Empresas = _service.ObtenerEmpresas();
            else
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new System.Collections.Generic.List<int>();
                ViewBag.Empresas = _service.ObtenerEmpresas().Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            // Modelo IA activo desde BD (mostrado en el badge del header)
            var cfgSvc = new ConfiguracionSistemaService();
            string modeloBD = cfgSvc.ObtenerValor("OpenAIModel");
            ViewBag.ModeloIA = !string.IsNullOrWhiteSpace(modeloBD) ? modeloBD : "gpt-4o";

            // Límite de caracteres de la pregunta (configurable; única fuente para maxlength + validación).
            ViewBag.MaxCharsPregunta = IAModuloHelper.MaxCharsPregunta();

            return View("~/Views/Informes/AnalisisIA.cshtml");
        }


        // ── Mi consumo de IA (Super Admin / Admin de Empresa) ──────────────────────────────

        /// <summary>Empresa cuyo consumo puede ver el usuario: la pedida si tiene acceso (grupo-aware), si no la suya.
        /// Solo Super Admin y Admin de Empresa; devuelve false si no tiene permiso.</summary>
        private bool PuedeVerConsumo(int? idEmpresaPedida, out int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            idEmpresa = idEmpresaPedida ?? usuario?.IdEmpresa ?? 0;
            if (usuario == null || idEmpresa <= 0) return false;
            if (UsuarioSesionHelper.EsSuperAdmin()) return true;
            return UsuarioSesionHelper.EsAdminEmpresa() && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa);
        }

        [HttpGet]
        public JsonResult MiConsumo(int? idEmpresa)
        {
            if (!PuedeVerConsumo(idEmpresa, out int empresaId))
                return Json(new { ok = false, mensaje = R("Common_SinPermisos") }, JsonRequestBehavior.AllowGet);

            try
            {
                var iaUso = new IAUsoService();
                var cfg = new ConfiguracionIAEmpresaService().ObtenerConfig(empresaId);
                var estado = iaUso.ObtenerEstadoConsumo(empresaId, cfg);

                var hoy = DateTime.Now.Date;
                var inicio = estado.InicioPeriodo.Date;
                var fin = inicio.AddMonths(1).AddDays(-1);
                int diasPeriodo = (fin - inicio).Days + 1;
                int diasTranscurridos = Math.Max(1, (hoy - inicio).Days + 1);
                long proyeccion = (long)Math.Round(estado.Consumido / (double)diasTranscurridos * diasPeriodo);

                var reporte = new IAUsoReporteService();
                string nombreEmpresa = _service.ObtenerEmpresas().FirstOrDefault(e => e.Id == empresaId)?.Nombre ?? ("#" + empresaId);

                return Json(new
                {
                    ok = true,
                    empresa = nombreEmpresa,
                    habilitada = cfg.IaHabilitada,
                    consumido = estado.Consumido,
                    presupuesto = estado.Presupuesto,
                    ilimitado = estado.Presupuesto <= 0,
                    porcentaje = estado.Presupuesto > 0 ? (int)Math.Min(100, estado.Consumido * 100 / estado.Presupuesto) : 0,
                    inicio = inicio.ToString("dd/MM/yyyy"),
                    fin = fin.ToString("dd/MM/yyyy"),
                    inicioIso = inicio.ToString("yyyy-MM-dd"),
                    hoyIso = hoy.ToString("yyyy-MM-dd"),
                    proyeccion,
                    enPool = estado.EnPool,
                    empresasEnPool = estado.EmpresasEnPool,
                    porUsuario = reporte.PorUsuario(empresaId, inicio, hoy),
                    porFuncion = reporte.PorFuncion(empresaId, inicio, hoy)
                        .Select(f => new { funcion = NombreFuncion(f.Funcion), f.Consultas, f.Tokens })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "AnalisisIAController.MiConsumo");
                return Json(new { ok = false, mensaje = R("IAConsumo_Error") }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Etiqueta traducida de una función de IA (las mismas de Configuración IA → Por Empresa).</summary>
        private string NombreFuncion(string funcion)
        {
            string clave = "CfgIA_Emp_Func_" + funcion;
            string texto = R(clave);
            return texto == clave ? funcion : texto;
        }

        /// <summary>Reporte de uso de IA de la empresa en un rango de fechas (resumen por función, por usuario y detalle por consulta).</summary>
        [HttpGet]
        public ActionResult ExportarUso(int? idEmpresa, string desde, string hasta)
        {
            if (!PuedeVerConsumo(idEmpresa, out int empresaId))
            {
                SetErrorMessage(R("Common_SinPermisos"));
                return RedirectToAction("Index");
            }

            DateTime fHasta = DateTime.TryParse(hasta, out var h) ? h.Date : DateTime.Now.Date;
            DateTime fDesde = DateTime.TryParse(desde, out var d) ? d.Date : fHasta.AddDays(-30);
            if (fDesde > fHasta) { var tmp = fDesde; fDesde = fHasta; fHasta = tmp; }

            var reporte = new IAUsoReporteService();
            var porFuncion = reporte.PorFuncion(empresaId, fDesde, fHasta);
            var porUsuario = reporte.PorUsuario(empresaId, fDesde, fHasta);
            var detalle = reporte.Detalle(empresaId, fDesde, fHasta);
            string nombreEmpresa = _service.ObtenerEmpresas().FirstOrDefault(e => e.Id == empresaId)?.Nombre ?? ("#" + empresaId);

            using (var wb = new XLWorkbook())
            {
                // Hoja 1: resumen por función
                var ws1 = wb.Worksheets.Add(R("IAUso_XlsResumen"));
                ws1.Cell(1, 1).Value = nombreEmpresa;
                ws1.Cell(1, 1).Style.Font.Bold = true;
                ws1.Cell(2, 1).Value = fDesde.ToString("dd/MM/yyyy") + " - " + fHasta.ToString("dd/MM/yyyy");
                Encabezado(ws1, 4, R("IAUso_ColFuncion"), R("IAUso_ColConsultas"), R("IAUso_ColTokens"), R("IAUso_ColCosto"));
                int f = 5;
                foreach (var x in porFuncion)
                {
                    ws1.Cell(f, 1).Value = NombreFuncion(x.Funcion);
                    ws1.Cell(f, 2).Value = x.Consultas;
                    ws1.Cell(f, 3).Value = x.Tokens;
                    ws1.Cell(f, 4).Value = (double)x.Costo;
                    f++;
                }
                ws1.Columns().AdjustToContents();

                // Hoja 2: por usuario
                var ws2 = wb.Worksheets.Add(R("IAUso_XlsPorUsuario"));
                Encabezado(ws2, 1, R("IAUso_ColUsuario"), R("IAUso_ColConsultas"), R("IAUso_ColTokens"));
                f = 2;
                foreach (var x in porUsuario)
                {
                    ws2.Cell(f, 1).Value = x.Usuario;
                    ws2.Cell(f, 2).Value = x.Consultas;
                    ws2.Cell(f, 3).Value = x.Tokens;
                    f++;
                }
                ws2.Columns().AdjustToContents();

                // Hoja 3: detalle por consulta
                var ws3 = wb.Worksheets.Add(R("IAUso_XlsDetalle"));
                Encabezado(ws3, 1, R("IAUso_ColFecha"), R("IAUso_ColUsuario"), R("IAUso_ColFuncion"), R("IAUso_ColModelo"),
                    R("IAUso_ColTokPrompt"), R("IAUso_ColTokResp"), R("IAUso_ColCosto"), R("IAUso_ColCache"),
                    R("IAUso_ColResultado"), R("IAUso_ColSobre"), R("IAUso_ColDegr"));
                string si = R("IAUso_Si"), no = R("IAUso_No");
                f = 2;
                foreach (var x in detalle)
                {
                    ws3.Cell(f, 1).Value = x.Fecha;
                    ws3.Cell(f, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                    ws3.Cell(f, 2).Value = x.Usuario;
                    ws3.Cell(f, 3).Value = NombreFuncion(x.Funcion);
                    ws3.Cell(f, 4).Value = x.Modelo ?? "";
                    ws3.Cell(f, 5).Value = x.TokensPrompt;
                    ws3.Cell(f, 6).Value = x.TokensRespuesta;
                    if (x.Costo.HasValue) ws3.Cell(f, 7).Value = (double)x.Costo.Value;
                    ws3.Cell(f, 8).Value = x.DesdeCache ? si : no;
                    ws3.Cell(f, 9).Value = x.Exitoso ? R("IAUso_Ok") : (x.Error ?? R("IAUso_Error"));
                    ws3.Cell(f, 10).Value = x.Sobreconsumo ? si : no;
                    ws3.Cell(f, 11).Value = x.Degradado ? si : no;
                    f++;
                }
                ws3.Columns().AdjustToContents();

                using (var ms = new System.IO.MemoryStream())
                {
                    wb.SaveAs(ms);
                    string nombreArchivo = "UsoIA_" + System.Text.RegularExpressions.Regex.Replace(nombreEmpresa, @"[^\w\-]+", "_") +
                                           "_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx";
                    return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
        }

        private static void Encabezado(IXLWorksheet ws, int fila, params string[] titulos)
        {
            for (int i = 0; i < titulos.Length; i++)
            {
                var cell = ws.Cell(fila, i + 1);
                cell.Value = titulos[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#583AFF");
            }
        }

    }
}
