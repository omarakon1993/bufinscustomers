using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe gerencial de tesorería del presupuesto: ¿alcanza la caja del año?, ¿en qué meses falta?, ¿cuánta deuda
    /// nueva se necesita? — caja neta mensual y acumulada, puente de caja, KPIs, tabla por mes y insights de IA.
    /// Lee de dbo.ModeloTesoreriaPpto (ver InformeTesoreriaPptoService); habilitado para todos los roles, acceso real
    /// acotado por empresa/grupo.
    /// </summary>
    [ValidarSesion]
    public class InformeTesoreriaPptoController : BaseController
    {
        private readonly InformeTesoreriaPptoService _service = new InformeTesoreriaPptoService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        /// <summary>Prompt por defecto de los insights (cuando no existe RESUMEN_TESORERIA_PPTO en GestorPrompts). La
        /// vista corta la respuesta por las etiquetas "Posición de caja:", "Financiación:" y "Acción recomendada:" para
        /// pintar 3 tarjetas — cualquier prompt personalizado debe conservar esas 3 etiquetas.</summary>
        private const string PromptResumenTesoreriaPorDefecto =
            "Prepara el resumen ejecutivo de la tesorería del presupuesto para la gerencia.\n" +
            "Cómo leer los datos: \"Kpis\" trae la caja neta del período (CajaNetaPeriodo), la caja neta acumulada al cierre " +
            "del rango (CajaAcumFinal), el saldo mínimo de caja acumulada y el mes en que ocurre (SaldoMinimo), los meses con " +
            "caja acumulada negativa (MesesDeficit, de \"Total\" meses) y la deuda nueva requerida (DeudaNueva = máximo " +
            "déficit acumulado). \"Valor\" es la medida principal elegida (Medida: ppto = presupuesto, proy = proyección: " +
            "lo ejecutado más lo presupuestado restante) y \"ValorComp\" la otra medida; \"VariacionPorcentual\" viene como " +
            "fracción (0,05 = 5%). \"Meses\" da la caja neta y la caja neta acumulada de cada mes (CajaNeta, CajaNetaAcum; " +
            "las \"Comp\" son la otra medida). \"Filas\" trae las líneas con \"Total\" del rango (EsSaldo=true: es un saldo " +
            "corrido, no una suma). Una caja acumulada negativa significa que el presupuesto no alcanza y hay que financiarlo.\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Posición de caja: <lectura de la caja neta del período y la acumulada al cierre, con cifras>\n" +
            "Financiación: <cuánta deuda nueva se necesita, en qué meses se concentra el déficit y el saldo mínimo, con cifras>\n" +
            "Acción recomendada: <una acción concreta y accionable para la gerencia, apoyada en los rubros que más pesan>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; usa millones de pesos con formato colombiano (por " +
            "ejemplo 170,0 M); pon en **negrita** la cifra principal de cada línea; usa solo cifras presentes en los datos y " +
            "nunca inventes valores. Si no hay déficit, dilo y enfócate en cómo usar el excedente.";

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

            return View("~/Views/Informes/InformeTesoreriaPpto.cshtml");
        }

        /// <summary>Meses con datos (año+mes, cronológicos) para el slider de rango continuo.</summary>
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
                AppLogger.Error(ex, "InformeTesoreriaPptoController.ObtenerMeses");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosTesoreria filtros)
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
                AppLogger.Error(ex, "InformeTesoreriaPptoController.ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + MensajeError(ex) });
            }
        }

        /// <summary>Datos que se envían a la IA: ya calculados, sin nulos y sin las series por mes de las líneas de detalle
        /// (solo KPIs, caja mensual y el total de cada línea).</summary>
        private static string DatosParaIA(TesoreriaReporteViewModel r)
        {
            return JsonConvert.SerializeObject(new
            {
                medida = r.Medida,
                kpis = r.Kpis,
                meses = r.Meses,
                filas = r.Filas.Select(f => new
                {
                    f.Descripcion, f.EsSubtotal, f.EsSaldo, f.Total, f.TotalComp, f.VariacionAbsoluta, f.VariacionPorcentual
                })
            }, JsonIASinNulos);
        }

        private string DescripcionFiltros(TesoreriaReporteViewModel r, string nombreEmpresa)
        {
            return $"Empresa {nombreEmpresa}, {r.MesDesde:00}/{r.AnioDesde} a {r.MesHasta:00}/{r.AnioHasta}, escenario {r.IdEscenario}, " +
                   $"medida {r.Medida}" + (r.HayComparativo ? ", comparada contra la otra medida" : "") +
                   (r.HayEscenarioComparar ? $", escenario de comparacion {r.IdEscenarioComparar}" : "");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(FiltrosTesoreria filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsTesoreria);
                if (denegado != null) return denegado;

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("TesPpto_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string filtrosDescripcion = DescripcionFiltros(reporte, nombreEmpresa);

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsTesoreria,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = null,
                        DatosJson = DatosParaIA(reporte),
                        NombreTabla = "Tesorería Presupuesto",
                        FiltrosDescripcion = filtrosDescripcion
                    },
                    CodigoPrompt = "RESUMEN_TESORERIA_PPTO",
                    PromptPorDefecto = PromptResumenTesoreriaPorDefecto,
                    // La vista separa los insights por etiqueta (acepta español e inglés): en inglés se fijan las etiquetas.
                    Traducir = R,
                    Idioma = IdiomaIA,
                    InstruccionEnIngles = "Respond in English. Start the three lines with exactly these labels, in this order: \"Cash position:\", \"Funding:\", \"Recommended action:\". Do not translate or change the labels.",
                    NombreTablaAuditoria = "Tesorería Presupuesto",
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Filas.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTesoreriaPptoController.GenerarInsightsIA");
                return Json(new IAConsultaResponse { Exitoso = false, Error = "Error al generar el resumen: " + MensajeError(ex) });
            }
        }

        /// <summary>"Pregúntale a este informe": pregunta libre sobre el reporte ya calculado (se reconstruye en el
        /// servidor con los mismos filtros, nunca con datos del cliente). Se mide como InsightsTesoreria.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PreguntarInforme(FiltrosTesoreria filtros, string pregunta)
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

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsTesoreria);
                if (denegado != null) return denegado;

                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("TesPpto_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string filtrosDescripcion = DescripcionFiltros(reporte, nombreEmpresa);

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsTesoreria,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = pregunta,
                        DatosJson = DatosParaIA(reporte),
                        NombreTabla = "Tesorería Presupuesto",
                        FiltrosDescripcion = filtrosDescripcion
                    },
                    Traducir = R,
                    Idioma = IdiomaIA,
                    NombreTablaAuditoria = "Tesorería Presupuesto",
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Filas.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InsightsTesoreria.PreguntarInforme");
                return Json(new IAConsultaResponse { Exitoso = false, Error = R("IAInforme_Error") });
            }
        }

        public ActionResult ExportarExcel(FiltrosTesoreria filtros, bool soloSubtotales = false)
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
                    TempData["InfoMessage"] = R("TesPpto_SinDatosDesc");
                    return RedirectToAction("Index");
                }

                _tema = ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA);
                var bytes = GenerarExcel(reporte, soloSubtotales, out string nombreArchivo);
                return File(bytes, InformeCorreoHelper.MimeExcel, nombreArchivo);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTesoreriaPptoController.ExportarExcel");
                TempData["ErrorMessage"] = "Error al exportar a Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// Envía el informe por correo desde el botón Exportar (componente estándar): Excel (el mismo de la descarga) y PDF
        /// (el que arma el navegador) como adjuntos y, en el cuerpo, los indicadores de caja. El reporte se reconstruye en
        /// el servidor con los filtros recibidos.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosTesoreria filtros, bool soloSubtotales = false, string correos = null,
            bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });

            try
            {
                var reporte = _service.ConstruirReporte(filtros);
                if (reporte.SinDatos || reporte.Filas.Count == 0)
                    return Json(new { exito = false, mensaje = R("TesPpto_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                    .FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);

                _tema = ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA);
                var bytes = GenerarExcel(reporte, soloSubtotales, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string periodo = NombrePeriodo(reporte);
                string medida = R("TesPpto_Medida_" + reporte.Medida);
                var contexto = new List<(string etiqueta, string valor)>
                {
                    (R("TesPpto_CorreoPeriodo"), periodo), (R("Common_LabelEscenario"), escenario), (R("TesPpto_LabelMedida"), medida)
                };

                // Indicadores: moneda en millones; el color depende de si el valor es sano (caja positiva, 0 meses en déficit…).
                var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>();
                foreach (var k in reporte.Kpis)
                {
                    string etiqueta = R("TesPpto_Kpi_" + k.Codigo);
                    string valor = k.Codigo == "MesesDeficit" ? k.Valor.ToString("0") + " / " + k.Total
                        : (k.Valor / 1000000m).ToString("N1") + " M";
                    string detalle = k.Codigo == "SaldoMinimo" && k.Mes > 0 ? NombreMes(k.Mes) + " " + k.Anio : "";
                    int signo = k.Codigo == "MesesDeficit" || k.Codigo == "DeudaNueva" ? (k.Valor == 0m ? 1 : -1) : (k.Valor >= 0m ? 1 : -1);
                    kpis.Add((etiqueta, valor, detalle, signo));
                }

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                new EmailService().EnviarInforme(string.Join(", ", lista), R("TesPpto_PageTitle"), empresa, false, contexto, kpis,
                    null, null, null, mensaje, quienEnvia, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa), _tema);

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeTesoreriaPpto", filtros.IdEmpresa.ToString(),
                    $"Informe de tesorería del presupuesto enviado por correo a {string.Join(", ", lista)}: '{empresa}' — {periodo}, escenario {filtros.IdEscenario}.",
                    null,
                    new { empresa, periodo, escenario = filtros.IdEscenario, medida = reporte.Medida, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTesoreriaPptoController.EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        // ===== Excel =====

                // Tema de colores de la petición actual (cada petición es una instancia nueva del controlador). Bufins = colores de siempre.
        private TemaInforme _tema = TemaInforme.Bufins;
        private string[] PaletaSeries { get { return new[] { ExcelTemaHelper.Serie(_tema, 0), ExcelTemaHelper.Serie(_tema, 1), TemaInforme.SinAlmohadilla(_tema.Tercero) }; } }

        /// <summary>Libro de Excel del informe (lo usan la descarga y el envío por correo): Resumen (KPIs), Caja mensual
        /// (con gráfico nativo), Detalle por mes y Puente de caja.</summary>
        private byte[] GenerarExcel(TesoreriaReporteViewModel r, bool soloSubtotales, out string nombreArchivo)
        {
            string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == r.IdEmpresa)?.Nombre ?? "Empresa";
            string periodo = NombrePeriodo(r);
            string medida = R("TesPpto_Medida_" + r.Medida), medidaOtra = R("TesPpto_Medida_" + TesoreriaMedida.Otra(r.Medida));
            string nombreEsc = r.HayEscenarioComparar
                ? (EscenarioCacheHelper.ObtenerEscenariosCacheados().FirstOrDefault(e => e.Id == r.IdEscenarioComparar)?.Nombre ?? ("#" + r.IdEscenarioComparar))
                : null;

            string hojaResumen = R("TesPpto_ExcelHojaResumen"), hojaCaja = R("TesPpto_ExcelHojaCajaMensual"),
                   hojaDetalle = R("TesPpto_ExcelHojaDetalle"), hojaPuente = R("TesPpto_ExcelHojaPuente");
            const string fmtNum = "#,##0;[Red]-#,##0", fmtPct = "0.0%";
            byte[] bytes;
            int filaEncCaja = 4, colCajaFin = 3;
            var etiquetasMes = r.Meses.Select(m => NombreMes(m.Mes).Substring(0, Math.Min(3, NombreMes(m.Mes).Length)) + " " + m.Anio).ToList();

            using (var wb = new XLWorkbook())
            {
                // --- Resumen: indicadores
                var wsR = wb.Worksheets.Add(hojaResumen);
                EncabezadoHoja(wsR, R("TesPpto_PageTitle") + " — " + empresa, periodo + " · " + medida);
                int col = 5 + (r.HayEscenarioComparar ? 1 : 0);
                wsR.Cell(4, 1).Value = R("TesPpto_ColIndicador");
                wsR.Cell(4, 2).Value = medida;
                wsR.Cell(4, 3).Value = r.HayComparativo ? medidaOtra : "";
                wsR.Cell(4, 4).Value = R("TesPpto_ColVarPct");
                if (r.HayEscenarioComparar) wsR.Cell(4, 5).Value = nombreEsc;
                int f = 5;
                foreach (var k in r.Kpis)
                {
                    wsR.Cell(f, 1).Value = R("TesPpto_Kpi_" + k.Codigo) + (k.Codigo == "SaldoMinimo" && k.Mes > 0 ? " (" + NombreMes(k.Mes) + " " + k.Anio + ")" : "");
                    EscribirValor(wsR.Cell(f, 2), k.Valor, k.Codigo == "MesesDeficit" ? "0" : fmtNum);
                    if (r.HayComparativo) EscribirValor(wsR.Cell(f, 3), k.ValorComp, k.Codigo == "MesesDeficit" ? "0" : fmtNum);
                    EscribirValor(wsR.Cell(f, 4), r.HayComparativo ? k.VariacionPorcentual : null, fmtPct);
                    if (r.HayEscenarioComparar) EscribirValor(wsR.Cell(f, 5), k.ValorEsc, k.Codigo == "MesesDeficit" ? "0" : fmtNum);
                    f++;
                }
                EstiloTabla(wsR, 4, f - 1, col);

                // --- Caja mensual (+ gráfico)
                var wsC = wb.Worksheets.Add(hojaCaja);
                EncabezadoHoja(wsC, R("TesPpto_ExcelHojaCajaMensual") + " — " + empresa, periodo + " · " + medida);
                wsC.Cell(filaEncCaja, 1).Value = R("TesPpto_ColMes");
                wsC.Cell(filaEncCaja, 2).Value = R("TesPpto_CajaNeta") + " (" + medida + ")";
                wsC.Cell(filaEncCaja, 3).Value = R("TesPpto_CajaNetaAcum") + " (" + medida + ")";
                if (r.HayComparativo)
                {
                    wsC.Cell(filaEncCaja, 4).Value = R("TesPpto_CajaNeta") + " (" + medidaOtra + ")";
                    wsC.Cell(filaEncCaja, 5).Value = R("TesPpto_CajaNetaAcum") + " (" + medidaOtra + ")";
                    colCajaFin = 5;
                }
                for (int i = 0; i < r.Meses.Count; i++)
                {
                    var m = r.Meses[i];
                    wsC.Cell(filaEncCaja + 1 + i, 1).Value = etiquetasMes[i];
                    EscribirValor(wsC.Cell(filaEncCaja + 1 + i, 2), m.CajaNeta, fmtNum);
                    EscribirValor(wsC.Cell(filaEncCaja + 1 + i, 3), m.CajaNetaAcum, fmtNum);
                    if (r.HayComparativo)
                    {
                        EscribirValor(wsC.Cell(filaEncCaja + 1 + i, 4), m.CajaNetaComp, fmtNum);
                        EscribirValor(wsC.Cell(filaEncCaja + 1 + i, 5), m.CajaNetaAcumComp, fmtNum);
                    }
                    if (!m.EnRango) wsC.Range(filaEncCaja + 1 + i, 1, filaEncCaja + 1 + i, colCajaFin).Style.Font.FontColor = XLColor.Gray;
                }
                int finCaja = filaEncCaja + r.Meses.Count;
                EstiloTabla(wsC, filaEncCaja, finCaja, colCajaFin);

                // --- Detalle por mes
                var wsD = wb.Worksheets.Add(hojaDetalle);
                EncabezadoHoja(wsD, R("TesPpto_ExcelHojaDetalle") + " — " + empresa, periodo + " · " + medida);
                int nMes = r.MesesRango.Count;
                wsD.Cell(4, 1).Value = R("TesPpto_ColLinea");
                for (int i = 0; i < nMes; i++)
                    wsD.Cell(4, 2 + i).Value = NombreMes(r.MesesRango[i].Mes).Substring(0, Math.Min(3, NombreMes(r.MesesRango[i].Mes).Length)) + " " + r.MesesRango[i].Anio;
                int cTotal = 2 + nMes, cComp = cTotal + 1, cVar = cTotal + 2, cVarPct = cTotal + 3, cEsc = cTotal + 4;
                wsD.Cell(4, cTotal).Value = R("TesPpto_ColTotal");
                int colFinD = cTotal;
                if (r.HayComparativo)
                {
                    wsD.Cell(4, cComp).Value = medidaOtra;
                    wsD.Cell(4, cVar).Value = R("TesPpto_ColVarAbs");
                    wsD.Cell(4, cVarPct).Value = R("TesPpto_ColVarPct");
                    colFinD = cVarPct;
                }
                if (r.HayEscenarioComparar)
                {
                    wsD.Cell(4, r.HayComparativo ? cEsc : cComp).Value = string.Format(R("TesPpto_ColVsEscenarioFmt"), nombreEsc);
                    colFinD = r.HayComparativo ? cEsc : cComp;
                }
                var filas = r.Filas.Where(x => !soloSubtotales || x.EsSubtotal).ToList();
                int fd = 5;
                foreach (var x in filas)
                {
                    wsD.Cell(fd, 1).Value = x.Descripcion;
                    for (int i = 0; i < nMes; i++) EscribirValor(wsD.Cell(fd, 2 + i), x.Valores[i], fmtNum);
                    EscribirValor(wsD.Cell(fd, cTotal), x.Total, fmtNum);
                    if (r.HayComparativo)
                    {
                        EscribirValor(wsD.Cell(fd, cComp), x.TotalComp, fmtNum);
                        EscribirValor(wsD.Cell(fd, cVar), x.VariacionAbsoluta, fmtNum);
                        EscribirValor(wsD.Cell(fd, cVarPct), x.VariacionPorcentual, fmtPct);
                    }
                    if (r.HayEscenarioComparar)
                        EscribirValor(wsD.Cell(fd, r.HayComparativo ? cEsc : cComp), x.VariacionEscenarioPorcentual, fmtPct);
                    if (x.EsSubtotal)
                    {
                        var rg = wsD.Range(fd, 1, fd, colFinD);
                        rg.Style.Font.Bold = true;
                        rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Suave(_tema);
                    }
                    fd++;
                }
                EstiloTabla(wsD, 4, fd - 1, colFinD);

                // --- Puente de caja
                var wsP = wb.Worksheets.Add(hojaPuente);
                EncabezadoHoja(wsP, R("TesPpto_ExcelHojaPuente") + " — " + empresa, periodo + " · " + medida);
                wsP.Cell(4, 1).Value = R("TesPpto_ColConcepto");
                wsP.Cell(4, 2).Value = R("TesPpto_ColMovimiento");
                wsP.Cell(4, 3).Value = R("TesPpto_ColNivel");
                int fp = 5;
                decimal nivel = 0m;
                foreach (var b in r.Cascada)
                {
                    wsP.Cell(fp, 1).Value = R("TesPpto_Casc_" + b.Etiqueta);
                    if (b.Tipo == "total") { nivel = b.Hasta; EscribirValor(wsP.Cell(fp, 2), b.Hasta, fmtNum); }
                    else { nivel += b.Delta; EscribirValor(wsP.Cell(fp, 2), b.Delta, fmtNum); }
                    EscribirValor(wsP.Cell(fp, 3), nivel, fmtNum);
                    if (b.Tipo == "total") wsP.Range(fp, 1, fp, 3).Style.Font.Bold = true;
                    fp++;
                }
                EstiloTabla(wsP, 4, fp - 1, 3);

                // Logo de la empresa (si tiene) junto al título de cada hoja; sin logo el libro queda igual que siempre.
                ExcelLogoHelper.ColocarEnTodas(wb, ExcelLogoHelper.Obtener(r.IdEmpresa));
                using (var ms = new MemoryStream()) { wb.SaveAs(ms); bytes = ms.ToArray(); }
            }

            // Gráfico nativo en "Caja mensual": barras de caja neta (principal y, si aplica, la otra medida).
            try
            {
                var series = new List<SerieGraficoExcel>
                {
                    new SerieGraficoExcel { Nombre = R("TesPpto_CajaNeta") + " (" + medida + ")", Columna = 2, ColorHex = PaletaSeries[0],
                        Valores = r.Meses.Select(m => (double?)m.CajaNeta).ToList() }
                };
                if (r.HayComparativo)
                    series.Add(new SerieGraficoExcel { Nombre = R("TesPpto_CajaNeta") + " (" + medidaOtra + ")", Columna = 4, ColorHex = PaletaSeries[1],
                        Valores = r.Meses.Select(m => (double?)m.CajaNetaComp).ToList() });
                bytes = ExcelChartHelper.AgregarGrafico(bytes, hojaCaja, R("TesPpto_GraficoTitulo"), true, filaEncCaja, filaEncCaja + 1,
                    filaEncCaja + r.Meses.Count, 1, etiquetasMes, series, "#,##0", colCajaFin + 2, filaEncCaja - 1);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeTesoreriaPptoController.GenerarExcel (gráfico)");
            }

            nombreArchivo = $"TesoreriaPpto_{empresa}_{r.AnioHasta}{r.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
            return bytes;
        }

        private static void EncabezadoHoja(IXLWorksheet ws, string titulo, string subtitulo)
        {
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = subtitulo + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        private static void EscribirValor(IXLCell celda, decimal? valor, string formato)
        {
            if (!valor.HasValue) return;
            celda.Value = (double)valor.Value;
            celda.Style.NumberFormat.Format = formato;
        }

        private void EstiloTabla(IXLWorksheet ws, int filaEnc, int filaFin, int colFin)
        {
            var enc = ws.Range(filaEnc, 1, filaEnc, colFin);
            enc.Style.Font.Bold = true;
            enc.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(_tema);
            enc.Style.Font.FontColor = XLColor.White;
            enc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var cuerpo = ws.Range(filaEnc, 1, filaFin, colFin);
            cuerpo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cuerpo.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            cuerpo.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
            ws.Column(1).Style.Font.Bold = true;
            ws.Columns().AdjustToContents();
            ws.SheetView.Freeze(filaEnc, 1);
        }

        private string NombrePeriodo(TesoreriaReporteViewModel r)
        {
            string a = NombreMes(r.MesDesde) + " " + r.AnioDesde, b = NombreMes(r.MesHasta) + " " + r.AnioHasta;
            return a == b ? a : a + " – " + b;
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
