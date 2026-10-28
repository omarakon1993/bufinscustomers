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
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe de rentabilidad por línea de negocio — estado de resultados por línea (ingresos → utilidad neta), mezcla de
    /// ingresos, márgenes y comparación contra el mismo rango del año anterior. Lee dbo.ModeloLineasNegocio (ver
    /// InformeLineasNegocioService); las líneas son dinámicas por empresa. Acceso por empresa/grupo.
    /// </summary>
    [ValidarSesion]
    [RequierePermiso("INFORMES_LINEAS_NEGOCIO")]
    public class InformeLineasNegocioController : BaseController
    {
        private readonly InformeLineasNegocioService _service = new InformeLineasNegocioService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        /// <summary>Prompt por defecto de los insights (si no existe RESUMEN_LINEAS_NEGOCIO en GestorPrompts). La vista corta
        /// la respuesta por "Rentabilidad:", "Concentración:" y "Acción recomendada:" — un prompt personalizado debe conservarlas.</summary>
        private const string PromptResumenPorDefecto =
            "Prepara el resumen ejecutivo de la rentabilidad por línea de negocio para la gerencia.\n" +
            "Cómo leer los datos: \"Resumen\" trae una entrada por línea con Ingresos, PctIngresos (participación en el ingreso total), " +
            "los márgenes (MargenBruto, MargenOperacional, MargenEbitda, MargenNeto, como fracción: 0,05 = 5%), UtilidadOperacional, " +
            "PctUtilidadOperacional (aporte a la utilidad operacional total) y, si HayAnioAnterior es true, VariacionIngresos y " +
            "VariacionUtilidadOperacional contra el mismo rango del año anterior. \"Total\" es la suma de las líneas. " +
            "Costos y gastos son magnitudes positivas.\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Rentabilidad: <qué línea es más y menos rentable, con márgenes>\n" +
            "Concentración: <cuánto depende el negocio de una sola línea, comparando su peso en ingresos con su aporte a la utilidad>\n" +
            "Acción recomendada: <una acción concreta sobre una línea, apoyada en los datos>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; usa nombres de línea tal como vienen en los datos y cifras en " +
            "% o millones de pesos con formato colombiano (por ejemplo 170,0 M); pon en **negrita** la cifra principal de cada línea; " +
            "usa solo cifras presentes en los datos y nunca inventes valores. Si solo hay una línea, dilo en Concentración.";

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
            return View("~/Views/Informes/InformeLineasNegocio.cshtml");
        }

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
                return Json(new { success = true, meses });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineasNegocioController.ObtenerMeses");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosLineasNegocio filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });
                var reporte = _service.ConstruirReporte(filtros);
                var json = Json(new { success = true, reporte });
                json.MaxJsonLength = int.MaxValue;
                return json;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineasNegocioController.ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + MensajeError(ex) });
            }
        }

        private object DatosParaIA(LineasNegocioReporteViewModel r)
        {
            return new { resumen = r.Resumen, total = r.Total, hayAnioAnterior = r.HayAnioAnterior,
                         cuadraIngresosPYG = r.HayComparacionPYG ? (bool?)r.CuadraIngresosPYG : null,
                         cuadraUtilidadPYG = r.HayComparacionPYG ? (bool?)r.CuadraUtilidadPYG : null };
        }

        private string DescripcionFiltros(string nombreEmpresa, LineasNegocioReporteViewModel r)
            => $"Empresa {nombreEmpresa}, rango {r.MesDesde:00}/{r.AnioDesde} a {r.MesHasta:00}/{r.AnioHasta}, escenario {r.IdEscenario}";

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(FiltrosLineasNegocio filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;
                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsLineasNegocio);
                if (denegado != null) return denegado;

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos) return Json(new IAConsultaResponse { Exitoso = false, Error = R("LN_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string desc = DescripcionFiltros(nombreEmpresa, reporte);
                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsLineasNegocio, Usuario = usuario, IdEmpresa = idEmpresa, NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = null, DatosJson = JsonConvert.SerializeObject(DatosParaIA(reporte), JsonIASinNulos),
                        NombreTabla = "Líneas de negocio", FiltrosDescripcion = desc
                    },
                    CodigoPrompt = "RESUMEN_LINEAS_NEGOCIO", PromptPorDefecto = PromptResumenPorDefecto,
                    Traducir = R, Idioma = IdiomaIA,
                    InstruccionEnIngles = "Respond in English. Start the three lines with exactly these labels, in this order: \"Profitability:\", \"Concentration:\", \"Recommended action:\". Do not translate or change the labels.",
                    NombreTablaAuditoria = "Líneas de negocio", FiltrosDescripcion = desc, FilasAnalizadas = reporte.Resumen.Count
                });
                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = "Error al generar el resumen: " + MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PreguntarInforme(FiltrosLineasNegocio filtros, string pregunta)
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

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsLineasNegocio);
                if (denegado != null) return denegado;

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos) return Json(new IAConsultaResponse { Exitoso = false, Error = R("LN_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string desc = DescripcionFiltros(nombreEmpresa, reporte);
                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsLineasNegocio, Usuario = usuario, IdEmpresa = idEmpresa, NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = pregunta, DatosJson = JsonConvert.SerializeObject(DatosParaIA(reporte), JsonIASinNulos),
                        NombreTabla = "Líneas de negocio", FiltrosDescripcion = desc
                    },
                    Traducir = R, Idioma = IdiomaIA,
                    NombreTablaAuditoria = "Líneas de negocio", FiltrosDescripcion = desc, FilasAnalizadas = reporte.Resumen.Count
                });
                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InsightsLineasNegocio.PreguntarInforme");
                return Json(new IAConsultaResponse { Exitoso = false, Error = R("IAInforme_Error") });
            }
        }

        public ActionResult ExportarExcel(FiltrosLineasNegocio filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }
                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                {
                    TempData["InfoMessage"] = R("LN_SinDatosDesc");
                    return RedirectToAction("Index");
                }
                var bytes = GenerarExcel(filtros, reporte, out string nombreArchivo);
                return File(bytes, InformeCorreoHelper.MimeExcel, nombreArchivo);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineasNegocioController.ExportarExcel");
                TempData["ErrorMessage"] = "Error al exportar a Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>Correo del botón Exportar: Excel + PDF como adjuntos y, en el cuerpo, los indicadores del rango. El reporte se
        /// reconstruye en el servidor con los filtros recibidos.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosLineasNegocio filtros, string correos = null, bool incluirmeAMi = false,
            string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });

            try
            {
                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos) return Json(new { exito = false, mensaje = R("LN_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados().FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);

                var bytes = GenerarExcel(filtros, reporte, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string periodo = NombrePeriodo(reporte);
                var contexto = new List<(string etiqueta, string valor)>
                {
                    (R("LN_CorreoPeriodo"), periodo), (R("Common_LabelEscenario"), escenario)
                };

                var t = reporte.Total;
                var lider = reporte.Resumen.First();
                var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>
                {
                    (R("LN_Kpi_Ingresos"), (t.Ingresos / 1000000m).ToString("N1") + " M", ConVar(t.VariacionIngresos), Signo(t.VariacionIngresos)),
                    (R("LN_Kpi_UtilOperacional"), (t.UtilidadOperacional / 1000000m).ToString("N1") + " M", ConVar(t.VariacionUtilidadOperacional), Signo(t.VariacionUtilidadOperacional)),
                    (R("LN_Kpi_MargenOperacional"), t.MargenOperacional.HasValue ? (t.MargenOperacional.Value * 100m).ToString("0.0") + " %" : "—", "", 0),
                    (R("LN_Kpi_LineaLider"), lider.Nombre, lider.PctIngresos.HasValue ? (lider.PctIngresos.Value * 100m).ToString("0.0") + " % " + R("LN_DeLosIngresos") : "", 0)
                };

                // Tabla corta: una fila por línea (ingresos, participación y margen operacional)
                var columnas = new[] { R("LN_Col_Linea"), R("LN_Col_Ingresos"), R("LN_Col_PctIngresos"), R("LN_Col_MargenOperacional") };
                var filas = reporte.Resumen.Select(l => (new[]
                {
                    l.Nombre, (l.Ingresos / 1000000m).ToString("N1") + " M",
                    l.PctIngresos.HasValue ? (l.PctIngresos.Value * 100m).ToString("0.0") + " %" : "—",
                    l.MargenOperacional.HasValue ? (l.MargenOperacional.Value * 100m).ToString("0.0") + " %" : "—"
                }, (l.UtilidadOperacional < 0m ? -1 : 0))).ToList();

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                new EmailService().EnviarInforme(string.Join(", ", lista), R("LN_PageTitle"), empresa, false, contexto, kpis,
                    R("LN_TablaCorreoTitulo"), columnas, filas, mensaje, quienEnvia, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa),
                    ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA));

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeLineasNegocio", filtros.IdEmpresa.ToString(),
                    $"Informe de líneas de negocio enviado por correo a {string.Join(", ", lista)}: '{empresa}' — {periodo}, escenario {filtros.IdEscenario}.",
                    null, new { empresa, periodo, escenario = filtros.IdEscenario, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineasNegocioController.EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        private static string ConVar(decimal? v) => v.HasValue ? (v.Value * 100m).ToString("+0.0;-0.0;0.0") + " %" : "";
        private static int Signo(decimal? v) => v.HasValue && v.Value != 0m ? (v.Value > 0m ? 1 : -1) : 0;

        private string NombrePeriodo(LineasNegocioReporteViewModel r)
        {
            string desde = NombreMes(r.MesDesde) + " " + r.AnioDesde, hasta = NombreMes(r.MesHasta) + " " + r.AnioHasta;
            return desde == hasta ? desde : desde + " – " + hasta;
        }

        private string NombreMes(int mes) => mes >= 1 && mes <= 12 ? R("Common_Mes" + mes) : mes.ToString();

        /// <summary>Libro de Excel (lo usan la descarga y el correo): Resumen por línea, Estado de resultados por línea y Mezcla mensual.</summary>
        private byte[] GenerarExcel(FiltrosLineasNegocio filtros, LineasNegocioReporteViewModel r, out string nombreArchivo)
        {
            string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
            string periodo = NombrePeriodo(r);
            var tema = ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA);
            var logo = ExcelLogoHelper.Obtener(filtros.IdEmpresa);
            int o = logo != null ? ExcelLogoHelper.FilasBloque : 0;
            const string fmtNum = "#,##0", fmtPct = "0.0%";

            using (var wb = new XLWorkbook())
            {
                // ---- Hoja 1: resumen por línea ----
                var ws = wb.Worksheets.Add(R("LN_ExcelHojaResumen"));
                if (logo != null) ExcelLogoHelper.EscribirTitulo(ws, R("LN_PageTitle") + " — " + nombreEmpresa, periodo + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                string[] enc = { R("LN_Col_Linea"), R("LN_Col_Ingresos"), R("LN_Col_PctIngresos"), R("LN_Col_MargenBruto"), R("LN_Col_MargenOperacional"),
                                 R("LN_Col_MargenEbitda"), R("LN_Col_MargenNeto"), R("LN_Col_UtilOperacional"), R("LN_Col_PctUtilOperacional"), R("LN_Col_UtilNeta") };
                if (r.HayAnioAnterior) enc = enc.Concat(new[] { R("LN_Col_VarIngresos"), R("LN_Col_VarUtilOperacional") }).ToArray();
                for (int c = 0; c < enc.Length; c++) ws.Cell(1 + o, c + 1).Value = enc[c];
                var cab = ws.Range(1 + o, 1, 1 + o, enc.Length);
                cab.Style.Font.Bold = true; cab.Style.Font.FontColor = XLColor.White;
                cab.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                cab.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int fila = 2 + o;
                foreach (var l in r.Resumen.Concat(new[] { r.Total }))
                {
                    bool esTotal = l == r.Total;
                    ws.Cell(fila, 1).Value = esTotal ? R("LN_Col_Total") : l.Nombre;
                    var valores = new object[] { l.Ingresos, l.PctIngresos, l.MargenBruto, l.MargenOperacional, l.MargenEbitda, l.MargenNeto,
                                                 l.UtilidadOperacional, l.PctUtilidadOperacional, l.UtilidadNeta };
                    string[] fmts = { fmtNum, fmtPct, fmtPct, fmtPct, fmtPct, fmtPct, fmtNum, fmtPct, fmtNum };
                    for (int c = 0; c < valores.Length; c++)
                    {
                        ExcelCellHelper.SetValue(ws.Cell(fila, c + 2), valores[c]);
                        ws.Cell(fila, c + 2).Style.NumberFormat.Format = fmts[c];
                    }
                    if (r.HayAnioAnterior)
                    {
                        ExcelCellHelper.SetValue(ws.Cell(fila, 11), l.VariacionIngresos); ws.Cell(fila, 11).Style.NumberFormat.Format = fmtPct;
                        ExcelCellHelper.SetValue(ws.Cell(fila, 12), l.VariacionUtilidadOperacional); ws.Cell(fila, 12).Style.NumberFormat.Format = fmtPct;
                    }
                    if (esTotal)
                    {
                        var rango = ws.Range(fila, 1, fila, enc.Length);
                        rango.Style.Font.Bold = true; rango.Style.Fill.BackgroundColor = ExcelTemaHelper.Suave(tema);
                    }
                    fila++;
                }
                ws.Columns().AdjustToContents();
                ws.SheetView.Freeze(1 + o, 1);

                // ---- Hoja 2: estado de resultados por línea ----
                var ws2 = wb.Worksheets.Add(R("LN_ExcelHojaEstado"));
                if (logo != null) ExcelLogoHelper.EscribirTitulo(ws2, R("LN_PageTitle") + " — " + nombreEmpresa, periodo);
                ws2.Cell(1 + o, 1).Value = R("LN_Col_Concepto");
                for (int i = 0; i < r.Lineas.Count; i++) ws2.Cell(1 + o, 2 + i).Value = r.Lineas[i];
                ws2.Cell(1 + o, 2 + r.Lineas.Count).Value = R("LN_Col_Total");
                int totCols = 2 + r.Lineas.Count;
                var cab2 = ws2.Range(1 + o, 1, 1 + o, totCols);
                cab2.Style.Font.Bold = true; cab2.Style.Font.FontColor = XLColor.White;
                cab2.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                int f2 = 2 + o;
                foreach (var fe in r.Filas)
                {
                    ws2.Cell(f2, 1).Value = R("LN_Fila_" + fe.Codigo);
                    for (int i = 0; i < fe.Valores.Count; i++) { ws2.Cell(f2, 2 + i).Value = fe.Valores[i]; ws2.Cell(f2, 2 + i).Style.NumberFormat.Format = fmtNum; }
                    ws2.Cell(f2, totCols).Value = fe.Total; ws2.Cell(f2, totCols).Style.NumberFormat.Format = fmtNum;
                    if (fe.EsSubtotal)
                    {
                        var rg = ws2.Range(f2, 1, f2, totCols);
                        rg.Style.Font.Bold = true; rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Suave(tema);
                    }
                    f2++;
                }
                ws2.Columns().AdjustToContents();
                ws2.SheetView.Freeze(1 + o, 1);

                // ---- Hoja 3: mezcla mensual de ingresos ----
                var ws3 = wb.Worksheets.Add(R("LN_ExcelHojaMezcla"));
                if (logo != null) ExcelLogoHelper.EscribirTitulo(ws3, R("LN_PageTitle") + " — " + nombreEmpresa, periodo);
                ws3.Cell(1 + o, 1).Value = R("LN_Col_Mes");
                for (int i = 0; i < r.Series.Count; i++) ws3.Cell(1 + o, 2 + i).Value = r.Series[i].Linea;
                var cab3 = ws3.Range(1 + o, 1, 1 + o, 1 + r.Series.Count);
                cab3.Style.Font.Bold = true; cab3.Style.Font.FontColor = XLColor.White;
                cab3.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                for (int m = 0; m < r.Etiquetas.Count; m++)
                {
                    ws3.Cell(2 + o + m, 1).Value = r.Etiquetas[m];
                    for (int i = 0; i < r.Series.Count; i++) { ws3.Cell(2 + o + m, 2 + i).Value = r.Series[i].Ingresos[m]; ws3.Cell(2 + o + m, 2 + i).Style.NumberFormat.Format = fmtNum; }
                }
                ws3.Columns().AdjustToContents();
                ws3.SheetView.Freeze(1 + o, 1);

                if (logo != null) ExcelLogoHelper.ColocarEnTodas(wb, logo);

                nombreArchivo = $"LineasNegocio_{nombreEmpresa}_{r.AnioHasta}{r.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private bool TieneAccesoEmpresa(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return UsuarioSesionHelper.EsSuperAdmin() || EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa);
        }
    }
}
