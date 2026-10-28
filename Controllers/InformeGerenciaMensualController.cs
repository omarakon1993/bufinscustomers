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
    /// Informe mensual de gerencia — el cierre de UN mes en un solo documento: resultados vs presupuesto, balance, caja, indicadores en
    /// atención, líneas de negocio y comentarios de gestión. Reutiliza los servicios de los demás informes (ver
    /// InformeGerenciaMensualService). Acceso por empresa/grupo.
    /// </summary>
    [ValidarSesion]
    [RequierePermiso("INFORMES_GERENCIA_MENSUAL")]
    public class InformeGerenciaMensualController : BaseController
    {
        private readonly InformeGerenciaMensualService _service = new InformeGerenciaMensualService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        /// <summary>Prompt por defecto del resumen (si no existe RESUMEN_GERENCIA_MENSUAL en GestorPrompts). La vista corta por las
        /// etiquetas "Resultado:", "Caja y balance:" y "Foco del mes:".</summary>
        private const string PromptResumenPorDefecto =
            "Prepara el resumen ejecutivo del cierre del mes para la gerencia.\n" +
            "Cómo leer los datos: \"Resultados\" trae Ingresos, Ebitda y UtilidadNeta del mes (Real), contra el Presupuesto, con la variación " +
            "(VarPresupuesto y VarAnioAnterior como fracción: 0,05 = 5%) y el acumulado del año. \"Balance\" trae ActivoTotal, CapitalTrabajo, " +
            "RazonCorriente y Endeudamiento al cierre con su variación contra el cierre del año anterior. Caja: CajaInicial, FlujoNeto, " +
            "FlujoNetoPresupuesto, FclOperativo y CajaFinal. \"Atencion\" son los indicadores del semáforo que más requieren atención. " +
            "\"Lineas\" son las líneas de negocio del mes con su participación y margen. Costos y gastos son magnitudes positivas.\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Resultado: <cómo cerró el mes frente al presupuesto y al año anterior, con cifras>\n" +
            "Caja y balance: <posición de caja, capital de trabajo y endeudamiento al cierre, con cifras>\n" +
            "Foco del mes: <el tema que la gerencia debe atender primero, apoyado en los indicadores en atención y las líneas de negocio>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; usa cifras en % o millones de pesos con formato colombiano " +
            "(por ejemplo 170,0 M); pon en **negrita** la cifra principal de cada línea; usa solo cifras presentes en los datos y nunca " +
            "inventes valores. Si una sección no tiene datos (Hay... en false), no la menciones.";

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var empresas = _empresaService.ObtenerEmpresas();
            if (!esAdmin)
            {
                var ids = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => ids.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresas;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = usuario?.IdEmpresa ?? 0;
            return View("~/Views/Informes/InformeGerenciaMensual.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerMeses(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                var meses = _service.ObtenerMesesDisponibles(idEmpresa, idEscenario)
                    .OrderByDescending(m => m.Anio).ThenByDescending(m => m.Mes)
                    .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                return Json(new { success = true, meses });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeGerenciaMensualController.ObtenerMeses");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosGerenciaMensual filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa) || filtros.Mes < 1 || filtros.Mes > 12)
                    return Json(new { success = false, message = R("Common_SinPermisos") });
                var json = Json(new { success = true, reporte = _service.Construir(filtros) });
                json.MaxJsonLength = int.MaxValue;
                return json;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeGerenciaMensualController.ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + MensajeError(ex) });
            }
        }

        private string Descripcion(string empresa, GerenciaMensualReporteViewModel r) => $"Empresa {empresa}, cierre {r.Mes:00}/{r.Anio}, escenario {r.IdEscenario}";

        private object DatosParaIA(GerenciaMensualReporteViewModel r) => new
        {
            hayResultados = r.HayResultados, resultados = r.Resultados,
            hayBalance = r.HayBalance, cuadraBalance = r.CuadraBalance, balance = r.Balance,
            hayCaja = r.HayCaja, cajaInicial = r.HayCajaInicial ? (decimal?)r.CajaInicial : null, flujoNeto = r.FlujoNeto, flujoNetoPresupuesto = r.FlujoNetoPresupuesto,
            fclOperativo = r.FclOperativo, cajaFinal = r.HayCajaFinal ? (decimal?)r.CajaFinal : null,
            hayIndicadores = r.HayIndicadores, totalVerde = r.TotalVerde, totalAmarillo = r.TotalAmarillo, totalNaranja = r.TotalNaranja, totalRojo = r.TotalRojo,
            atencion = r.Atencion.Select(a => new { nombre = R("Ind_" + a.Codigo + "_Nombre"), a.Valor, a.Formato, a.Nivel }),
            hayLineas = r.HayLineas, lineas = r.Lineas.Select(l => new { l.Nombre, l.Ingresos, l.PctIngresos, l.MargenOperacional, l.UtilidadOperacional })
        };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(FiltrosGerenciaMensual filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;
                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });
                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsGerenciaMensual);
                if (denegado != null) return denegado;

                var reporte = _service.Construir(filtros);
                if (reporte.SinDatos) return Json(new IAConsultaResponse { Exitoso = false, Error = R("GM_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string desc = Descripcion(nombreEmpresa, reporte);
                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsGerenciaMensual, Usuario = usuario, IdEmpresa = idEmpresa, NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest { Pregunta = null, DatosJson = JsonConvert.SerializeObject(DatosParaIA(reporte), JsonIASinNulos), NombreTabla = "Informe mensual de gerencia", FiltrosDescripcion = desc },
                    CodigoPrompt = "RESUMEN_GERENCIA_MENSUAL", PromptPorDefecto = PromptResumenPorDefecto, Traducir = R, Idioma = IdiomaIA,
                    InstruccionEnIngles = "Respond in English. Start the three lines with exactly these labels, in this order: \"Result:\", \"Cash and balance:\", \"Focus of the month:\". Do not translate or change the labels.",
                    NombreTablaAuditoria = "Informe mensual de gerencia", FiltrosDescripcion = desc, FilasAnalizadas = 1
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
        public async Task<JsonResult> PreguntarInforme(FiltrosGerenciaMensual filtros, string pregunta)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;
                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });
                pregunta = (pregunta ?? "").Trim();
                int max = IAModuloHelper.MaxCharsPregunta();
                if (pregunta.Length == 0 || pregunta.Length > max)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = string.Format(R("IAInforme_PreguntaInvalida"), max) });
                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsGerenciaMensual);
                if (denegado != null) return denegado;

                var reporte = _service.Construir(filtros);
                if (reporte.SinDatos) return Json(new IAConsultaResponse { Exitoso = false, Error = R("GM_SinDatosDesc") });
                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string desc = Descripcion(nombreEmpresa, reporte);
                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsGerenciaMensual, Usuario = usuario, IdEmpresa = idEmpresa, NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest { Pregunta = pregunta, DatosJson = JsonConvert.SerializeObject(DatosParaIA(reporte), JsonIASinNulos), NombreTabla = "Informe mensual de gerencia", FiltrosDescripcion = desc },
                    Traducir = R, Idioma = IdiomaIA, NombreTablaAuditoria = "Informe mensual de gerencia", FiltrosDescripcion = desc, FilasAnalizadas = 1
                });
                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InsightsGerenciaMensual.PreguntarInforme");
                return Json(new IAConsultaResponse { Exitoso = false, Error = R("IAInforme_Error") });
            }
        }

        public ActionResult ExportarExcel(FiltrosGerenciaMensual filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }
                var reporte = _service.Construir(filtros);
                if (reporte.SinDatos)
                {
                    TempData["InfoMessage"] = R("GM_SinDatosDesc");
                    return RedirectToAction("Index");
                }
                return File(GenerarExcel(filtros, reporte, out string nombre), InformeCorreoHelper.MimeExcel, nombre);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeGerenciaMensualController.ExportarExcel");
                TempData["ErrorMessage"] = "Error al exportar a Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosGerenciaMensual filtros, string correos = null, bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });
            try
            {
                var r = _service.Construir(filtros);
                if (r.SinDatos) return Json(new { exito = false, mensaje = R("GM_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados().FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);
                var bytes = GenerarExcel(filtros, r, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string cierre = NombreMes(r.Mes) + " " + r.Anio;
                var contexto = new List<(string etiqueta, string valor)> { (R("GM_CorreoCierre"), cierre), (R("Common_LabelEscenario"), escenario) };
                var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>();
                foreach (var c in r.Resultados)
                    kpis.Add((R("GM_Res_" + c.Codigo), Millones(c.Real), c.VarPresupuesto.HasValue ? Pct(c.VarPresupuesto) + " " + R("GM_VsPptoCorto") : "", Signo(c.VarPresupuesto)));
                if (r.HayCajaFinal) kpis.Add((R("GM_CajaFinal"), Millones(r.CajaFinal), "", 0));

                string[] columnas = null; List<(string[] celdas, int signo)> filas = null; string tablaTitulo = null;
                if (r.Atencion.Count > 0)
                {
                    tablaTitulo = R("GM_AtencionTitulo");
                    columnas = new[] { R("GM_Col_Indicador"), R("GM_Col_Valor"), R("GM_Col_Semaforo") };
                    filas = r.Atencion.Select(a => (new[] { R("Ind_" + a.Codigo + "_Nombre"), FormatoIndicador(a), R("GM_Nivel_" + a.Nivel) }, -1)).ToList();
                }

                string quien = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                new EmailService().EnviarInforme(string.Join(", ", lista), R("GM_PageTitle"), empresa, false, contexto, kpis, tablaTitulo, columnas, filas,
                    mensaje, quien, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa),
                    ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA));

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeGerenciaMensual", filtros.IdEmpresa.ToString(),
                    $"Informe mensual de gerencia enviado por correo a {string.Join(", ", lista)}: '{empresa}' — cierre {cierre}, escenario {filtros.IdEscenario}.",
                    null, new { empresa, cierre, escenario = filtros.IdEscenario, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeGerenciaMensualController.EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        private static string Millones(decimal v) => (v / 1000000m).ToString("N1") + " M";
        private static string Pct(decimal? v) => v.HasValue ? (v.Value * 100m).ToString("+0.0;-0.0;0.0") + " %" : "—";
        private static int Signo(decimal? v) => v.HasValue && v.Value != 0m ? (v.Value > 0m ? 1 : -1) : 0;
        private string NombreMes(int mes) => mes >= 1 && mes <= 12 ? R("Common_Mes" + mes) : mes.ToString();

        private static string FormatoIndicador(GmIndicadorAtencion a)
        {
            if (!a.Valor.HasValue) return "—";
            switch (a.Formato)
            {
                case "pct": return (a.Valor.Value * 100m).ToString("0.0") + " %";
                case "x": return a.Valor.Value.ToString("0.00") + "x";
                case "dias": return a.Valor.Value.ToString("0") + " d";
                default: return Millones(a.Valor.Value);
            }
        }

        private byte[] GenerarExcel(FiltrosGerenciaMensual filtros, GerenciaMensualReporteViewModel r, out string nombreArchivo)
        {
            string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
            string cierre = NombreMes(r.Mes) + " " + r.Anio;
            var tema = ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA);
            var logo = ExcelLogoHelper.Obtener(filtros.IdEmpresa);
            int o = logo != null ? ExcelLogoHelper.FilasBloque : 0;
            const string fmtNum = "#,##0", fmtPct = "0.0%";

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(R("GM_ExcelHojaResumen"));
                if (logo != null) ExcelLogoHelper.EscribirTitulo(ws, R("GM_PageTitle") + " — " + nombreEmpresa, cierre + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                int fila = 1 + o;

                Action<string[]> cabecera = textos =>
                {
                    for (int c = 0; c < textos.Length; c++) ws.Cell(fila, c + 1).Value = textos[c];
                    var rg = ws.Range(fila, 1, fila, textos.Length);
                    rg.Style.Font.Bold = true; rg.Style.Font.FontColor = XLColor.White;
                    rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                    fila++;
                };

                if (r.HayResultados)
                {
                    cabecera(new[] { R("GM_SecResultados"), R("GM_Col_Real"), R("GM_Col_Presupuesto"), R("GM_Col_VarPpto"), R("GM_Col_VarAnio"), R("GM_Col_AcumReal"), R("GM_Col_AcumPpto"), R("GM_Col_AcumVarPpto") });
                    foreach (var c in r.Resultados)
                    {
                        ws.Cell(fila, 1).Value = R("GM_Res_" + c.Codigo);
                        ExcelCellHelper.SetValue(ws.Cell(fila, 2), c.Real); ExcelCellHelper.SetValue(ws.Cell(fila, 3), c.Presupuesto);
                        ExcelCellHelper.SetValue(ws.Cell(fila, 4), c.VarPresupuesto); ExcelCellHelper.SetValue(ws.Cell(fila, 5), c.VarAnioAnterior);
                        ExcelCellHelper.SetValue(ws.Cell(fila, 6), c.AcumReal); ExcelCellHelper.SetValue(ws.Cell(fila, 7), c.AcumPresupuesto);
                        ExcelCellHelper.SetValue(ws.Cell(fila, 8), c.AcumVarPresupuesto);
                        foreach (int col in new[] { 2, 3, 6, 7 }) ws.Cell(fila, col).Style.NumberFormat.Format = fmtNum;
                        foreach (int col in new[] { 4, 5, 8 }) ws.Cell(fila, col).Style.NumberFormat.Format = fmtPct;
                        fila++;
                    }
                    fila++;
                }
                if (r.HayBalance)
                {
                    cabecera(new[] { R("GM_SecBalance"), R("GM_Col_Valor"), R("GM_Col_VarCierre") });
                    foreach (var k in r.Balance)
                    {
                        ws.Cell(fila, 1).Value = R("BAL_Kpi" + k.Codigo);
                        if (k.NoAplica) ws.Cell(fila, 2).Value = R("BAL_KpiNoAplica");
                        else
                        {
                            ExcelCellHelper.SetValue(ws.Cell(fila, 2), k.Valor);
                            ws.Cell(fila, 2).Style.NumberFormat.Format = k.Codigo == "RazonCorriente" ? "0.00" : (k.Codigo == "Endeudamiento" ? fmtPct : fmtNum);
                        }
                        ExcelCellHelper.SetValue(ws.Cell(fila, 3), k.Variacion); ws.Cell(fila, 3).Style.NumberFormat.Format = fmtPct;
                        fila++;
                    }
                    fila++;
                }
                if (r.HayCaja)
                {
                    cabecera(new[] { R("GM_SecCaja"), R("GM_Col_Valor") });
                    var items = new List<KeyValuePair<string, decimal?>>
                    {
                        new KeyValuePair<string, decimal?>(R("GM_CajaInicial"), r.HayCajaInicial ? (decimal?)r.CajaInicial : null),
                        new KeyValuePair<string, decimal?>(R("GM_FlujoNeto"), r.FlujoNeto),
                        new KeyValuePair<string, decimal?>(R("GM_FlujoNetoPpto"), r.FlujoNetoPresupuesto),
                        new KeyValuePair<string, decimal?>(R("GM_Fcl"), r.FclOperativo),
                        new KeyValuePair<string, decimal?>(R("GM_CajaFinal"), r.HayCajaFinal ? (decimal?)r.CajaFinal : null)
                    };
                    foreach (var it in items) { ws.Cell(fila, 1).Value = it.Key; ExcelCellHelper.SetValue(ws.Cell(fila, 2), it.Value); ws.Cell(fila, 2).Style.NumberFormat.Format = fmtNum; fila++; }
                    fila++;
                }
                ws.Columns().AdjustToContents();

                if (r.HayIndicadores || r.HayLineas)
                {
                    var ws2 = wb.Worksheets.Add(R("GM_ExcelHojaFoco"));
                    int f2 = 1 + o;
                    if (logo != null) ExcelLogoHelper.EscribirTitulo(ws2, R("GM_PageTitle") + " — " + nombreEmpresa, cierre);
                    if (r.HayIndicadores)
                    {
                        ws2.Cell(f2, 1).Value = R("GM_AtencionTitulo"); ws2.Cell(f2, 2).Value = R("GM_Col_Valor"); ws2.Cell(f2, 3).Value = R("GM_Col_Semaforo");
                        var rg = ws2.Range(f2, 1, f2, 3); rg.Style.Font.Bold = true; rg.Style.Font.FontColor = XLColor.White; rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                        f2++;
                        foreach (var a in r.Atencion) { ws2.Cell(f2, 1).Value = R("Ind_" + a.Codigo + "_Nombre"); ws2.Cell(f2, 2).Value = FormatoIndicador(a); ws2.Cell(f2, 3).Value = R("GM_Nivel_" + a.Nivel); f2++; }
                        f2++;
                    }
                    if (r.HayLineas)
                    {
                        ws2.Cell(f2, 1).Value = R("LN_Col_Linea"); ws2.Cell(f2, 2).Value = R("LN_Col_Ingresos"); ws2.Cell(f2, 3).Value = R("LN_Col_PctIngresos"); ws2.Cell(f2, 4).Value = R("LN_Col_MargenOperacional");
                        var rg = ws2.Range(f2, 1, f2, 4); rg.Style.Font.Bold = true; rg.Style.Font.FontColor = XLColor.White; rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                        f2++;
                        foreach (var l in r.Lineas)
                        {
                            ws2.Cell(f2, 1).Value = l.Nombre;
                            ExcelCellHelper.SetValue(ws2.Cell(f2, 2), l.Ingresos); ws2.Cell(f2, 2).Style.NumberFormat.Format = fmtNum;
                            ExcelCellHelper.SetValue(ws2.Cell(f2, 3), l.PctIngresos); ws2.Cell(f2, 3).Style.NumberFormat.Format = fmtPct;
                            ExcelCellHelper.SetValue(ws2.Cell(f2, 4), l.MargenOperacional); ws2.Cell(f2, 4).Style.NumberFormat.Format = fmtPct;
                            f2++;
                        }
                    }
                    ws2.Columns().AdjustToContents();
                }

                // Comentarios de gestión del cierre (de todos los informes)
                var coms = new ComentariosGestionService().Listar(filtros.IdEmpresa, r.Anio, r.Mes, null, out bool disponible);
                if (disponible && coms.Count > 0)
                {
                    var ws3 = wb.Worksheets.Add(R("GM_ExcelHojaComentarios"));
                    int f3 = 1 + o;
                    if (logo != null) ExcelLogoHelper.EscribirTitulo(ws3, R("GM_PageTitle") + " — " + nombreEmpresa, cierre);
                    string[] enc = { R("Com_ColInforme"), R("Com_ColSobre"), R("Com_ColComentario"), R("Com_ColAutor"), R("Com_ColFecha") };
                    for (int c = 0; c < enc.Length; c++) ws3.Cell(f3, c + 1).Value = enc[c];
                    var rg = ws3.Range(f3, 1, f3, enc.Length); rg.Style.Font.Bold = true; rg.Style.Font.FontColor = XLColor.White; rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                    f3++;
                    foreach (var c in coms)
                    {
                        ws3.Cell(f3, 1).Value = R("Com_Inf_" + c.Informe); ws3.Cell(f3, 2).Value = c.Clave ?? ""; ws3.Cell(f3, 3).Value = c.Texto;
                        ws3.Cell(f3, 4).Value = c.NombreUsuario; ws3.Cell(f3, 5).Value = c.Fecha; f3++;
                    }
                    ws3.Column(3).Width = 70; ws3.Column(3).Style.Alignment.WrapText = true;
                }

                if (logo != null) ExcelLogoHelper.ColocarEnTodas(wb, logo);
                nombreArchivo = $"GerenciaMensual_{nombreEmpresa}_{r.Anio}{r.Mes:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
                using (var ms = new MemoryStream()) { wb.SaveAs(ms); return ms.ToArray(); }
            }
        }

        private bool TieneAccesoEmpresa(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return UsuarioSesionHelper.EsSuperAdmin() || EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa);
        }
    }
}
