using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe gerencial de Estado de Resultados (PYG) — Real vs Presupuesto, mes seleccionado y
    /// acumulado del año, con KPIs, tendencia e insights de IA. Lee de dbo.ModeloPYG (ver
    /// InformePYGService); habilitado para todos los roles, acceso real acotado por empresa/grupo.
    /// </summary>
    [ValidarSesion]
    public class InformePYGController : BaseController
    {
        private readonly InformePYGService _service = new InformePYGService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

            var empresas = _empresaService.ObtenerEmpresas();
            if (!esAdmin)
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            ViewBag.Empresas = empresas;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = usuario?.IdEmpresa ?? 0;

            return View("~/Views/Informes/InformePYG.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerAnios(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var años = _service.ObtenerAñosDisponibles(idEmpresa, idEscenario);
                return Json(new { success = true, años });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformePYGController.ObtenerAnios");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(int idEmpresa, byte idEscenario, int anio, int mesDesde, int mesHasta)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var reporte = _service.ConstruirReporte(idEmpresa, idEscenario, anio, mesDesde, mesHasta);
                var jsonResult = Json(new { success = true, reporte });
                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformePYGController.ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + ex.Message });
            }
        }

        /// <summary>Genera un resumen gerencial (Logro/Alerta/Eficiencia) con IA a partir del reporte ya
        /// calculado, replicando el patrón de cupo/prompts/auditoría de HomeController.ObtenerResumenIA.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(int idEmpresa, byte idEscenario, int anio, int mesDesde, int mesHasta)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                if (!TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                if (!esAdmin)
                {
                    int limiteDiario = ObtenerLimiteConsultasIA(usuario.Id);
                    if (limiteDiario <= 0)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });

                    int consultasHoy = new AuditoriaAnalisisIAService().ContarConsultasHoy(usuario.Id);
                    if (consultasHoy >= limiteDiario)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });
                }

                var reporte = _service.ConstruirReporte(idEmpresa, idEscenario, anio, mesDesde, mesHasta);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("PYG_SinDatosDesc") });

                var cfgSvc = new ConfiguracionSistemaService();
                var tApiKey = cfgSvc.ObtenerValorAsync("OpenAIApiKey");
                var tModelo = cfgSvc.ObtenerValorAsync("OpenAIModel");
                var tMaxTokens = cfgSvc.ObtenerValorAsync("OpenAIMaxTokens");
                var tTemp = cfgSvc.ObtenerValorAsync("OpenAITemperature");
                await Task.WhenAll(tApiKey, tModelo, tMaxTokens, tTemp);

                string apiKey = (tApiKey.Result ?? "").Trim();
                string modeloIA = (tModelo.Result ?? "gpt-4o").Trim();
                int maxTokensIA = (int.TryParse(tMaxTokens.Result, out int ptk) && ptk > 0) ? ptk : 8000;
                double? temperatureIA = double.TryParse(
                    tTemp.Result, NumberStyles.Float, CultureInfo.InvariantCulture, out double tVal)
                    ? (double?)tVal : null;

                var iaService = new IAService(apiKey);

                string instrucciones = new GestorPromptsService().ObtenerPorCodigo("RESUMEN_PYG_GERENCIAL")?.TextoPrompt;
                string guardrail = new GestorPromptsService().ObtenerPorCodigo("GUARDRAIL_SISTEMA")?.TextoPrompt;
                string contextoNegocio = new GestorPromptsService().ObtenerPorCodigo("CONTEXTO_NEGOCIO_BUFINS")?.TextoPrompt;

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "—";
                string filtrosDescripcion = $"Empresa {nombreEmpresa}, año {anio}, meses {mesDesde}-{mesHasta}, escenario {idEscenario}";

                var request = new IAConsultaRequest
                {
                    Pregunta = null,
                    DatosJson = JsonConvert.SerializeObject(new { filas = reporte.Filas, kpis = reporte.Kpis }),
                    NombreTabla = "PYG Gerencial",
                    FiltrosDescripcion = filtrosDescripcion
                };

                var response = await iaService.ConsultarAsync(request, instrucciones, guardrail, modeloIA, maxTokensIA, temperatureIA, contextoNegocio);

                if (response.Exitoso)
                {
                    try
                    {
                        new AuditoriaAnalisisIAService().Registrar(new AuditoriaAnalisisIA
                        {
                            IdUsuario = usuario.Id,
                            NombreUsuario = $"{usuario.Nombre} {usuario.Apellidos}".Trim(),
                            IdEmpresa = idEmpresa,
                            NombreEmpresa = nombreEmpresa,
                            NombreTabla = "PYG Gerencial",
                            Filtros = filtrosDescripcion,
                            Pregunta = null,
                            Respuesta = response.Respuesta,
                            FechaPregunta = DateTime.Now,
                            FilasAnalizadas = reporte.Filas.Count
                        });
                    }
                    catch { }
                }

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = "Error al generar el resumen: " + ex.Message });
            }
        }

        public ActionResult ExportarExcel(int idEmpresa, byte idEscenario, int anio, int mesDesde, int mesHasta)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var reporte = _service.ConstruirReporte(idEmpresa, idEscenario, anio, mesDesde, mesHasta);
                if (reporte.SinDatos || reporte.Filas.Count == 0)
                {
                    TempData["InfoMessage"] = R("PYG_SinDatosDesc");
                    return RedirectToAction("Index");
                }

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "Empresa";
                string nombreMesDesde = reporte.MesDesde >= 1 && reporte.MesDesde <= 12 ? R("Common_Mes" + reporte.MesDesde) : reporte.MesDesde.ToString();
                string nombreMesHasta = reporte.MesHasta >= 1 && reporte.MesHasta <= 12 ? R("Common_Mes" + reporte.MesHasta) : reporte.MesHasta.ToString();
                string nombrePeriodo = reporte.MesDesde == reporte.MesHasta
                    ? nombreMesDesde + " " + anio
                    : nombreMesDesde + "-" + nombreMesHasta + " " + anio;
                string nombreMesHastaConAnio = nombreMesHasta + " " + anio;

                using (var package = new XLWorkbook())
                {
                    var ws = package.Worksheets.Add("PYG");

                    string[] encabezados =
                    {
                        R("PYG_ColEstructura"),
                        R("PYG_ColReal"), R("PYG_ColPresupuesto"), R("PYG_ColVarAbs"), R("PYG_ColVarPct"), R("PYG_ColMargen"), R("PYG_ColVarYoY"),
                        R("PYG_ColReal"), R("PYG_ColPresupuesto"), R("PYG_ColVarAbs"), R("PYG_ColVarPct"), R("PYG_ColMargen"), R("PYG_ColVarYoY")
                    };

                    ws.Cell(1, 1).Value = R("PYG_ColEstructura");
                    ws.Range(1, 1, 2, 1).Merge();
                    ws.Cell(1, 2).Value = string.Format(R("PYG_ColMesGrupoFmt"), nombrePeriodo);
                    ws.Range(1, 2, 1, 7).Merge();
                    ws.Cell(1, 8).Value = string.Format(R("PYG_ColAcumGrupoFmt"), nombreMesHastaConAnio);
                    ws.Range(1, 8, 1, 13).Merge();

                    for (int col = 2; col <= 13; col++)
                        ws.Cell(2, col).Value = encabezados[col - 1];

                    var headerRange = ws.Range(1, 1, 2, 13);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    int fila = 3;
                    foreach (var f in reporte.Filas)
                    {
                        int col = 1;
                        ws.Cell(fila, col++).Value = f.Descripcion;
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.PeriodoReal);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.PeriodoPresupuesto);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.PeriodoVariacionAbsoluta);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.PeriodoVariacionPorcentual);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.PeriodoMargen);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.PeriodoVariacionYoYPorcentual);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.AcumReal);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.AcumPresupuesto);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.AcumVariacionAbsoluta);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.AcumVariacionPorcentual);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col++), f.AcumMargen);
                        ExcelCellHelper.SetValue(ws.Cell(fila, col), f.AcumVariacionYoYPorcentual);

                        var filaRange = ws.Range(fila, 1, fila, 13);
                        filaRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        filaRange.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
                        filaRange.Style.NumberFormat.Format = "#,##0.0";

                        if (f.EsSubtotal)
                        {
                            filaRange.Style.Font.Bold = true;
                            filaRange.Style.Fill.BackgroundColor = XLColor.FromArgb(244, 243, 251);
                        }
                        else if ((fila - 3) % 2 == 1)
                        {
                            filaRange.Style.Fill.BackgroundColor = XLColor.FromArgb(248, 250, 252);
                        }

                        fila++;
                    }

                    ws.Columns().AdjustToContents();
                    ws.Range(2, 1, fila - 1, 13).SetAutoFilter();
                    ws.SheetView.Freeze(2, 1);

                    string nombreArchivo = $"PYG_{nombreEmpresa}_{anio}_{reporte.MesDesde:00}-{reporte.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));

                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformePYGController.ExportarExcel");
                TempData["ErrorMessage"] = "Error al exportar a Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        private bool TieneAccesoEmpresa(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return UsuarioSesionHelper.EsSuperAdmin() || EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa);
        }

        private int ObtenerLimiteConsultasIA(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("SELECT LimiteConsultasIA FROM Usuarios WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    var result = cmd.ExecuteScalar();
                    return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
            catch { return 0; }
        }
    }
}
