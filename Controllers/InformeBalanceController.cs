using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe gerencial de Balance General — saldo al mes de corte, comparativo (cierre del año
    /// anterior, mes anterior o mismo mes año anterior), chequeo de cuadre, KPIs de liquidez/solvencia,
    /// estructura y tendencia, con insights de IA. Lee de dbo.ModeloBalance (ver InformeBalanceService);
    /// habilitado para todos los roles, acceso real acotado por empresa/grupo.
    /// </summary>
    [ValidarSesion]
    public class InformeBalanceController : BaseController
    {
        private readonly InformeBalanceService _service = new InformeBalanceService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        /// <summary>Prompt por defecto de los insights del Balance (cuando no existe
        /// RESUMEN_BALANCE_GERENCIAL en GestorPrompts). La vista corta la respuesta por las etiquetas
        /// "Liquidez:", "Solvencia:" y "Capital de trabajo:" para pintar 3 tarjetas — cualquier prompt
        /// personalizado debe conservar esas 3 etiquetas.</summary>
        private const string PromptResumenBalancePorDefecto =
            "Prepara el resumen ejecutivo del Balance General para la gerencia.\n" +
            "Cómo leer los datos: \"SaldoCorte\" es el saldo de la cuenta al mes de corte y \"SaldoComparativo\" el " +
            "comparativo elegido en los filtros (cierre del año anterior, mes anterior o mismo mes del año anterior); " +
            "\"PctVertical\" es el peso de la cuenta sobre el activo total; \"VariacionPorcentual\" viene como fracción " +
            "(0,05 = 5%). \"CuadraBalance\"/\"DiferenciaCuadre\" indican si Activo = Pasivo + Patrimonio. Los KPIs " +
            "(\"Kpis\") son CapitalTrabajo (moneda), RazonCorriente y Endeudamiento (ambos pueden venir con " +
            "NoAplica=true cuando no tienen sentido, p. ej. sin pasivo corriente) y ActivoTotal (tamaño).\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Liquidez: <lectura de capital de trabajo y razón corriente, con cifras>\n" +
            "Solvencia: <lectura del endeudamiento y la estructura de pasivo/patrimonio, con cifras>\n" +
            "Capital de trabajo: <una observación concreta sobre la composición del activo/pasivo corriente, apoyada en los datos>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; menciona la cuenta o el indicador y su variación " +
            "en % y/o en millones de pesos con formato colombiano (por ejemplo 170,0 M); pon en **negrita** la cifra " +
            "principal de cada línea; usa solo cifras presentes en los datos y nunca inventes valores. Si CuadraBalance " +
            "es false, menciónalo como alerta dentro de la línea de Solvencia.";

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

            return View("~/Views/Informes/InformeBalance.cshtml");
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

                var meses = _service.ObtenerMesesDisponibles(idEmpresa, idEscenario)
                    .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                var escenariosConDatos = _service.ObtenerEscenariosConDatos(idEmpresa);
                return Json(new { success = true, meses, escenariosConDatos });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeBalanceController.ObtenerMeses");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosBalance filtros)
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
                AppLogger.Error(ex, "InformeBalanceController.ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + ex.Message });
            }
        }

        /// <summary>Genera un resumen gerencial (Liquidez/Solvencia/Capital de trabajo) con IA a partir del
        /// reporte ya calculado, replicando el patrón de cupo/prompts/auditoría de InformePYGController.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(FiltrosBalance filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsBalance);
                if (denegado != null) return denegado;

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("BAL_SinDatosDesc") });

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "—";
                string filtrosDescripcion = $"Empresa {nombreEmpresa}, corte {reporte.MesHasta:00}/{reporte.AnioHasta}, " +
                    $"escenario {reporte.IdEscenario}, comparado contra {reporte.Comparar}" +
                    (reporte.HayEscenarioComparar ? $", escenario de comparacion {reporte.IdEscenarioComparar}" : "");

                var request = new IAConsultaRequest
                {
                    Pregunta = null,
                    DatosJson = JsonConvert.SerializeObject(new
                    {
                        filas = reporte.Filas,
                        kpis = reporte.Kpis,
                        cuadraBalance = reporte.CuadraBalance,
                        diferenciaCuadre = reporte.DiferenciaCuadre
                    }),
                    NombreTabla = "Balance Gerencial",
                    FiltrosDescripcion = filtrosDescripcion
                };

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsBalance,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = request,
                    CodigoPrompt = "RESUMEN_BALANCE_GERENCIAL",
                    PromptPorDefecto = PromptResumenBalancePorDefecto,
                    // La vista separa los insights por etiqueta (acepta español e inglés): en inglés se fijan las etiquetas.
                    Traducir = R,
                    Idioma = IdiomaIA,
                    InstruccionEnIngles = "Respond in English. Start the three lines with exactly these labels, in this order: \"Liquidity:\", \"Solvency:\", \"Working capital:\". Do not translate or change the labels.",
                    NombreTablaAuditoria = "Balance Gerencial",
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Filas.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = "Error al generar el resumen: " + ex.Message });
            }
        }

        /// <summary>"Pregúntale a este informe": responde una pregunta libre sobre el reporte ya calculado (se
        /// reconstruye en el servidor con los mismos filtros, nunca con datos del cliente). Cada pregunta es una
        /// consulta independiente y se mide como la función InsightsBalance.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PreguntarInforme(FiltrosBalance filtros, string pregunta)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                pregunta = (pregunta ?? "").Trim();
                int maxPregunta = IAModuloHelper.MaxCharsPregunta();
                if (pregunta.Length == 0 || pregunta.Length > maxPregunta)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = string.Format(R("IAInforme_PreguntaInvalida"), maxPregunta) });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsBalance);
                if (denegado != null) return denegado;

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("BAL_SinDatosDesc") });

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "—";
                string filtrosDescripcion = $"Empresa {nombreEmpresa}, corte {reporte.MesHasta:00}/{reporte.AnioHasta}, " +
                    $"escenario {reporte.IdEscenario}, comparado contra {reporte.Comparar}" +
                    (reporte.HayEscenarioComparar ? $", escenario de comparacion {reporte.IdEscenarioComparar}" : "");

                var request = new IAConsultaRequest
                {
                    Pregunta = pregunta,
                    DatosJson = JsonConvert.SerializeObject(new
                    {
                        filas = reporte.Filas,
                        kpis = reporte.Kpis,
                        cuadraBalance = reporte.CuadraBalance,
                        diferenciaCuadre = reporte.DiferenciaCuadre
                    }),
                    NombreTabla = "Balance Gerencial",
                    FiltrosDescripcion = filtrosDescripcion
                };

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsBalance,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = request,
                    Traducir = R,
                    Idioma = IdiomaIA,
                    NombreTablaAuditoria = "Balance Gerencial",
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Filas.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InsightsBalance.PreguntarInforme");
                return Json(new IAConsultaResponse { Exitoso = false, Error = R("IAInforme_Error") });
            }
        }

        public ActionResult ExportarExcel(FiltrosBalance filtros, bool soloSubtotales = false)
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
                    TempData["InfoMessage"] = R("BAL_SinDatosDesc");
                    return RedirectToAction("Index");
                }

                var empresaInfo = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa);
                string nombreEmpresa = empresaInfo?.Nombre ?? "Empresa";
                string nombreCorte = NombreMes(reporte.MesHasta) + " " + reporte.AnioHasta;

                string nombreEscenarioComp = null;
                if (reporte.HayEscenarioComparar)
                {
                    nombreEscenarioComp = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                        .FirstOrDefault(e => e.Id == reporte.IdEscenarioComparar)?.Nombre ?? ("#" + reporte.IdEscenarioComparar);
                }

                const string fmtNum = "#,##0.0", fmtPct = "0.0%";
                var columnas = new List<Tuple<string, Func<BalanceFilaReporte, decimal?>, string>>
                {
                    Tuple.Create<string, Func<BalanceFilaReporte, decimal?>, string>(R("BAL_ColSaldoCorte"), f => f.SaldoCorte, fmtNum),
                    Tuple.Create<string, Func<BalanceFilaReporte, decimal?>, string>(R("BAL_ColPctVertical"), f => f.PctVertical, fmtPct),
                    Tuple.Create<string, Func<BalanceFilaReporte, decimal?>, string>(R("BAL_CompCorto_" + reporte.Comparar), f => f.SaldoComparativo, fmtNum),
                    Tuple.Create<string, Func<BalanceFilaReporte, decimal?>, string>(R("BAL_ColVarAbs"), f => f.VariacionAbsoluta, fmtNum),
                    Tuple.Create<string, Func<BalanceFilaReporte, decimal?>, string>(R("BAL_ColVarPct"), f => f.VariacionPorcentual, fmtPct)
                };
                if (reporte.HayEscenarioComparar)
                    columnas.Add(Tuple.Create<string, Func<BalanceFilaReporte, decimal?>, string>(string.Format(R("BAL_ColVsEscenarioFmt"), nombreEscenarioComp), f => f.VariacionEscenarioPorcentual, fmtPct));

                int totalCols = 1 + columnas.Count;
                var filas = reporte.Filas.Where((f, i) => !soloSubtotales || f.EsSubtotal || i == reporte.Filas.Count - 1).ToList();

                using (var package = new XLWorkbook())
                {
                    var ws = package.Worksheets.Add("Balance");

                    ws.Cell(1, 1).Value = R("BAL_ColCuenta");
                    ws.Cell(1, 2).Value = string.Format(R("BAL_ColCorteGrupoFmt"), nombreCorte);
                    ws.Range(1, 2, 1, totalCols).Merge();
                    for (int c = 0; c < columnas.Count; c++)
                        ws.Cell(2, 2 + c).Value = columnas[c].Item1;

                    var headerRange = ws.Range(1, 1, 2, totalCols);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    ws.Range(1, 1, 2, 1).Merge();

                    int fila = 3;
                    foreach (var f in filas)
                    {
                        ws.Cell(fila, 1).Value = f.Descripcion;
                        for (int c = 0; c < columnas.Count; c++)
                        {
                            var celda = ws.Cell(fila, 2 + c);
                            ExcelCellHelper.SetValue(celda, columnas[c].Item2(f));
                            celda.Style.NumberFormat.Format = columnas[c].Item3;
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

                    string nombreArchivo = $"Balance_{nombreEmpresa}_{reporte.AnioHasta}{reporte.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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
                AppLogger.Error(ex, "InformeBalanceController.ExportarExcel");
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
    }
}
