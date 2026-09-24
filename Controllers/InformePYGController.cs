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

        /// <summary>Prompt por defecto de los insights del PYG (cuando no existe RESUMEN_PYG_GERENCIAL en
        /// GestorPrompts). La vista corta la respuesta por las etiquetas "Logro:", "Alerta:" y "Eficiencia:"
        /// para pintar 3 tarjetas — cualquier prompt personalizado debe conservar esas 3 etiquetas.</summary>
        private const string PromptResumenPygPorDefecto =
            "Prepara el resumen ejecutivo del Estado de Resultados (P&G) para la gerencia.\n" +
            "Cómo leer los datos: los campos \"Periodo*\" son el período consultado y \"Acum*\" el acumulado del año; " +
            "los campos \"*Presupuesto*\" contienen el valor comparativo indicado en los filtros (presupuesto, presupuesto " +
            "con ajuste o forecast); \"*YoY*\" es la variación contra el mismo período del año anterior y \"*Escenario*\" " +
            "la variación contra el escenario de comparación, si existe. Los porcentajes vienen como fracción (0,05 = 5%). " +
            "En líneas con EsGastoOCosto = true, un valor real MAYOR al comparativo es DESFAVORABLE.\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Logro: <el resultado más positivo del período, con cifras>\n" +
            "Alerta: <el desvío desfavorable más importante: costos o gastos por encima de lo esperado, caída de ingresos o de márgenes, con cifras>\n" +
            "Eficiencia: <una oportunidad concreta para mejorar costos, gastos o márgenes, apoyada en los datos>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; menciona la cuenta o el indicador y su variación " +
            "en % y/o en millones de pesos con formato colombiano (por ejemplo 170,0 M); pon en **negrita** la cifra " +
            "principal de cada línea; usa solo cifras presentes en los datos y nunca inventes valores.";
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

        /// <summary>Meses con datos (año+mes, en orden cronológico) para el slider de rango continuo.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerMeses(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                // camelCase explícito: MVC 5 serializa el PascalCase de C# tal cual (mismo criterio que
                // InformeLineaTiempoController.ObtenerRangoMeses, cuyo slider se reutiliza aquí).
                var meses = _service.ObtenerMesesDisponibles(idEmpresa, idEscenario)
                    .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                var escenariosConDatos = _service.ObtenerEscenariosConDatos(idEmpresa);
                return Json(new { success = true, meses, escenariosConDatos });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformePYGController.ObtenerMeses");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosPYG filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var reporte = _service.ConstruirReporte(filtros);
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
        public async Task<JsonResult> GenerarInsightsIA(FiltrosPYG filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
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

                var reporte = _service.ConstruirReporte(filtros);
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

                // Si un Super Admin crea el prompt RESUMEN_PYG_GERENCIAL en el Gestor de Prompts, ese manda;
                // si no existe (o está inactivo) se usa el prompt por defecto del informe, no el genérico de IAService.
                string instrucciones = new GestorPromptsService().ObtenerPorCodigo("RESUMEN_PYG_GERENCIAL")?.TextoPrompt;
                if (string.IsNullOrWhiteSpace(instrucciones))
                    instrucciones = PromptResumenPygPorDefecto;
                string guardrail = new GestorPromptsService().ObtenerPorCodigo("GUARDRAIL_SISTEMA")?.TextoPrompt;
                string contextoNegocio = new GestorPromptsService().ObtenerPorCodigo("CONTEXTO_NEGOCIO_BUFINS")?.TextoPrompt;

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "—";
                // Descripción en texto fijo (va al prompt y a AuditoriaAnalisisIA, no a la UI).
                string filtrosDescripcion = $"Empresa {nombreEmpresa}, periodo {reporte.MesDesde:00}/{reporte.AnioDesde} a {reporte.MesHasta:00}/{reporte.AnioHasta}, " +
                    $"escenario {reporte.IdEscenario}, comparado contra {reporte.Comparar}" +
                    (reporte.HayEscenarioComparar ? $", escenario de comparacion {reporte.IdEscenarioComparar}" : "");

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

        public ActionResult ExportarExcel(FiltrosPYG filtros, bool soloSubtotales = false)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos || reporte.Filas.Count == 0)
                {
                    TempData["InfoMessage"] = R("PYG_SinDatosDesc");
                    return RedirectToAction("Index");
                }

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "Empresa";
                string nombrePeriodo = (reporte.AnioDesde == reporte.AnioHasta && reporte.MesDesde == reporte.MesHasta)
                    ? NombreMes(reporte.MesDesde) + " " + reporte.AnioDesde
                    : (reporte.AnioDesde == reporte.AnioHasta
                        ? NombreMes(reporte.MesDesde) + " - " + NombreMes(reporte.MesHasta) + " " + reporte.AnioHasta
                        : NombreMes(reporte.MesDesde) + " " + reporte.AnioDesde + " - " + NombreMes(reporte.MesHasta) + " " + reporte.AnioHasta);
                string nombreMesHastaConAnio = NombreMes(reporte.MesHasta) + " " + reporte.AnioHasta;

                string nombreEscenarioComp = null;
                if (reporte.HayEscenarioComparar)
                {
                    nombreEscenarioComp = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                        .FirstOrDefault(e => e.Id == reporte.IdEscenarioComparar)?.Nombre ?? ("#" + reporte.IdEscenarioComparar);
                }

                // Columnas de cada bloque (Periodo / Acumulado): encabezado, valor y formato — los % van
                // como fracción con formato de porcentaje (antes salían con formato numérico y se veían 0,0).
                const string fmtNum = "#,##0.0", fmtPct = "0.0%";
                var columnas = new List<Tuple<string, Func<PygFilaReporte, bool, decimal?>, string>>
                {
                    Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(R("PYG_ColReal"), (f, p) => p ? f.PeriodoReal : f.AcumReal, fmtNum),
                    Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(R("PYG_CompCorto_" + reporte.Comparar), (f, p) => p ? f.PeriodoPresupuesto : f.AcumPresupuesto, fmtNum),
                    Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(R("PYG_ColVarAbs"), (f, p) => p ? f.PeriodoVariacionAbsoluta : f.AcumVariacionAbsoluta, fmtNum),
                    Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(R("PYG_ColVarPct"), (f, p) => p ? f.PeriodoVariacionPorcentual : f.AcumVariacionPorcentual, fmtPct),
                    Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(R("PYG_ColMargen"), (f, p) => p ? f.PeriodoMargen : f.AcumMargen, fmtPct)
                };
                if (reporte.HayAnioAnterior)
                    columnas.Add(Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(R("PYG_ColVarYoY"), (f, p) => p ? f.PeriodoVariacionYoYPorcentual : f.AcumVariacionYoYPorcentual, fmtPct));
                if (reporte.HayEscenarioComparar)
                    columnas.Add(Tuple.Create<string, Func<PygFilaReporte, bool, decimal?>, string>(string.Format(R("PYG_ColVsEscenarioFmt"), nombreEscenarioComp), (f, p) => p ? f.PeriodoVariacionEscenarioPorcentual : f.AcumVariacionEscenarioPorcentual, fmtPct));

                int n = columnas.Count, totalCols = 1 + n * 2;
                var filas = reporte.Filas.Where((f, i) => !soloSubtotales || f.EsSubtotal || i == reporte.Filas.Count - 1).ToList();

                using (var package = new XLWorkbook())
                {
                    var ws = package.Worksheets.Add("PYG");

                    ws.Cell(1, 1).Value = R("PYG_ColEstructura");
                    ws.Range(1, 1, 2, 1).Merge();
                    ws.Cell(1, 2).Value = string.Format(R("PYG_ColMesGrupoFmt"), nombrePeriodo);
                    ws.Range(1, 2, 1, 1 + n).Merge();
                    ws.Cell(1, 2 + n).Value = string.Format(R("PYG_ColAcumGrupoFmt"), nombreMesHastaConAnio);
                    ws.Range(1, 2 + n, 1, totalCols).Merge();

                    for (int b = 0; b < 2; b++)
                        for (int c = 0; c < n; c++)
                            ws.Cell(2, 2 + b * n + c).Value = columnas[c].Item1;

                    var headerRange = ws.Range(1, 1, 2, totalCols);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    int fila = 3;
                    foreach (var f in filas)
                    {
                        ws.Cell(fila, 1).Value = f.Descripcion;
                        for (int b = 0; b < 2; b++)
                        {
                            for (int c = 0; c < n; c++)
                            {
                                var celda = ws.Cell(fila, 2 + b * n + c);
                                ExcelCellHelper.SetValue(celda, columnas[c].Item2(f, b == 0));
                                celda.Style.NumberFormat.Format = columnas[c].Item3;
                            }
                        }

                        var filaRange = ws.Range(fila, 1, fila, totalCols);
                        filaRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        filaRange.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);

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
                    ws.Range(2, 1, fila - 1, totalCols).SetAutoFilter();
                    ws.SheetView.Freeze(2, 1);

                    string nombreArchivo = $"PYG_{nombreEmpresa}_{reporte.AnioDesde}{reporte.MesDesde:00}-{reporte.AnioHasta}{reporte.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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

        private string NombreMes(int mes)
        {
            return mes >= 1 && mes <= 12 ? R("Common_Mes" + mes) : mes.ToString();
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
