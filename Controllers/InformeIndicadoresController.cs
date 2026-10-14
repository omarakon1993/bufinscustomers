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
    /// Informe de indicadores con semáforo: ~26 indicadores (rentabilidad, retornos, eficiencia, liquidez, solvencia y crecimiento)
    /// calculados sobre dbo.ModeloPYG, dbo.ModeloBalance y dbo.ModeloFlujoCaja (ver InformeIndicadoresService). Cada indicador tiene
    /// una ficha (pregunta, fórmula, rangos del semáforo, cómo mejorarlo) con textos en los .resx (Ind_{Codigo}_*). Habilitado para
    /// todos los roles; el acceso real se acota por empresa/grupo. La IA solo recibe el reporte ya calculado (tablas Modelo*).
    /// </summary>
    [ValidarSesion]
    public class InformeIndicadoresController : BaseController
    {
        private readonly InformeIndicadoresService _servicio = new InformeIndicadoresService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        private const string NombreControlador = "InformeIndicadores";
        private const string NombreModelo = "Indicadores";

        private const string PromptPorDefecto =
            "Eres un analista financiero corporativo colombiano. Recibirás los indicadores financieros YA CALCULADOS de una empresa " +
            "(valor, valor de comparación, variación y semáforo: verde = bien, amarillo = atención, naranja = riesgo, rojo = crítico; " +
            "'gris' = parcial o no aplica). Responde en español con EXACTAMENTE tres líneas, cada una comenzando con estas etiquetas, " +
            "en este orden: 'Fortaleza:' (el indicador o grupo más sólido y por qué importa), 'Riesgo:' (el principal riesgo que muestran " +
            "los indicadores en naranja o rojo, con la cifra clave) y 'Prioridad:' (una acción concreta y priorizada para mejorar). " +
            "Máximo dos frases por línea y la cifra clave en **negrita**. Usa solo los datos entregados, no inventes cifras y no " +
            "concluyas con indicadores parciales o que no aplican. Los semáforos son referencias generales que dependen de la industria.";

        // ===== Página y datos =====

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

            return View("~/Views/Informes/InformeIndicadores.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerMeses(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var meses = _servicio.ObtenerMesesDisponibles(idEmpresa, idEscenario)
                    .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                return Json(new { success = true, meses });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ObtenerMeses");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosIndicadores filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var reporte = _servicio.ConstruirReporte(filtros);
                var jsonResult = Json(new { success = true, reporte });
                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + ex.Message });
            }
        }

        // ===== IA =====

        /// <summary>Datos que se envían a la IA: ya calculados y sin nulos (valor, comparación, variación, semáforo y últimos meses).</summary>
        private string DatosParaIA(IndicadoresReporteViewModel r)
        {
            return JsonConvert.SerializeObject(new
            {
                corte = $"{r.MesHasta:00}/{r.AnioHasta}",
                comparadoCon = $"{r.MesComp:00}/{r.AnioComp}",
                hayComparativo = r.HayComparativo,
                mesesDeFlujoEnLaVentana = r.MesesLtm,
                resumen = new { verde = r.TotalVerde, amarillo = r.TotalAmarillo, naranja = r.TotalNaranja, rojo = r.TotalRojo, grisParcialONoAplica = r.TotalGris },
                indicadores = r.Indicadores.Select(i => new
                {
                    indicador = R("Ind_" + i.Codigo + "_Nombre"),
                    familia = i.Familia,
                    formato = i.Formato,
                    valor = i.Valor,
                    valorComparacion = i.ValorComp,
                    variacion = i.Variacion,
                    favorable = i.Favorable,
                    semaforo = i.Nivel,
                    motivoSinValor = i.NoAplica || i.Parcial ? i.Motivo : null,
                    ultimosMeses = i.Tendencia.Skip(Math.Max(0, i.Tendencia.Count - 6)).Select(p => p.Valor)
                })
            }, JsonIASinNulos);
        }

        private string DescripcionFiltros(IndicadoresReporteViewModel r, string nombreEmpresa)
        {
            return $"{NombreModelo}. Empresa {nombreEmpresa}, corte {r.MesHasta:00}/{r.AnioHasta} (flujos de los 12 meses que terminan ahi, saldos del mes), " +
                   $"escenario {r.IdEscenario}" + (r.HayComparativo ? $", comparado con {r.MesComp:00}/{r.AnioComp}" : "");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(FiltrosIndicadores filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsIndicadores);
                if (denegado != null) return denegado;

                var reporte = _servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Ind_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string filtrosDescripcion = DescripcionFiltros(reporte, nombreEmpresa);

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsIndicadores,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = null,
                        DatosJson = DatosParaIA(reporte),
                        NombreTabla = NombreModelo,
                        FiltrosDescripcion = filtrosDescripcion
                    },
                    CodigoPrompt = "RESUMEN_INDICADORES_GERENCIAL",
                    PromptPorDefecto = PromptPorDefecto,
                    // La vista separa los insights por etiqueta (acepta español e inglés): en inglés se fijan las etiquetas.
                    Traducir = R,
                    Idioma = IdiomaIA,
                    InstruccionEnIngles = "Respond in English. Start the three lines with exactly these labels, in this order: \"Strength:\", \"Risk:\", \"Priority:\". Do not translate or change the labels.",
                    NombreTablaAuditoria = NombreModelo,
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Indicadores.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".GenerarInsightsIA");
                return Json(new IAConsultaResponse { Exitoso = false, Error = "Error al generar el resumen: " + ex.Message });
            }
        }

        /// <summary>"Pregúntale a este informe": pregunta libre sobre el reporte ya calculado (se reconstruye en el servidor con los
        /// mismos filtros, nunca con datos del cliente). Se mide con la misma función de IA que los insights.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PreguntarInforme(FiltrosIndicadores filtros, string pregunta)
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

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.InsightsIndicadores);
                if (denegado != null) return denegado;

                var reporte = _servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Ind_SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string filtrosDescripcion = DescripcionFiltros(reporte, nombreEmpresa);

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.InsightsIndicadores,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = pregunta,
                        DatosJson = DatosParaIA(reporte),
                        NombreTabla = NombreModelo,
                        FiltrosDescripcion = filtrosDescripcion
                    },
                    Traducir = R,
                    Idioma = IdiomaIA,
                    NombreTablaAuditoria = NombreModelo,
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Indicadores.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".PreguntarInforme");
                return Json(new IAConsultaResponse { Exitoso = false, Error = R("IAInforme_Error") });
            }
        }

        // ===== Exportaciones =====

        public ActionResult ExportarExcel(FiltrosIndicadores filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var reporte = _servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos || reporte.Indicadores.Count == 0)
                {
                    TempData["InfoMessage"] = R("Ind_SinDatosDesc");
                    return RedirectToAction("Index");
                }

                var bytes = GenerarExcel(reporte, out string nombreArchivo);
                return File(bytes, InformeCorreoHelper.MimeExcel, nombreArchivo);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ExportarExcel");
                TempData["ErrorMessage"] = "Error al exportar a Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>Envía el informe por correo desde el botón Exportar (componente estándar): Excel (el mismo de la descarga) y PDF
        /// (el que arma el navegador) como adjuntos y, en el cuerpo, el resumen del semáforo y los indicadores que requieren
        /// atención. El reporte se reconstruye en el servidor con los filtros recibidos.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosIndicadores filtros, string correos = null,
            bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });

            try
            {
                var reporte = _servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos || reporte.Indicadores.Count == 0)
                    return Json(new { exito = false, mensaje = R("Ind_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                    .FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);

                var bytes = GenerarExcel(reporte, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string corte = NombreMes(reporte.MesHasta) + " " + reporte.AnioHasta;
                var contexto = new List<(string etiqueta, string valor)>
                {
                    (R("Ind_CorreoCorte"), corte), (R("Common_LabelEscenario"), escenario)
                };
                if (reporte.HayComparativo)
                    contexto.Add((R("Ind_CorreoComparado"), NombreMes(reporte.MesComp) + " " + reporte.AnioComp));

                // Tarjetas: cuántos indicadores hay en cada nivel del semáforo.
                var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>
                {
                    (R("Ind_Nivel_verde"), reporte.TotalVerde.ToString(), "", 1),
                    (R("Ind_Nivel_amarillo"), reporte.TotalAmarillo.ToString(), "", 0),
                    (R("Ind_Nivel_naranja"), reporte.TotalNaranja.ToString(), "", 0),
                    (R("Ind_Nivel_rojo"), reporte.TotalRojo.ToString(), "", -1)
                };

                // Tabla corta: los que requieren atención (rojos y naranjas, el más grave primero), máximo 8.
                var atencion = reporte.Indicadores
                    .Where(i => i.Nivel == IndicadorNivel.Rojo || i.Nivel == IndicadorNivel.Naranja)
                    .OrderByDescending(i => IndicadoresCatalogo.Severidad(i.Nivel)).Take(8).ToList();
                string tablaTitulo = atencion.Count > 0 ? R("Ind_CorreoAtencion") : null;
                var columnas = new[] { R("Ind_ColIndicador"), R("Ind_ColValor"), R("Ind_ColVariacion"), R("Ind_ColSemaforo") };
                var filas = atencion.Select(i => (new[]
                {
                    R("Ind_" + i.Codigo + "_Nombre"), TextoValor(i, i.Valor), TextoVariacion(i), R("Ind_Nivel_" + i.Nivel)
                }, i.Nivel == IndicadorNivel.Rojo ? -1 : 0)).ToList();

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                new EmailService().EnviarInforme(string.Join(", ", lista), R("Ind_PageTitle"), empresa, false, contexto, kpis,
                    tablaTitulo, atencion.Count > 0 ? columnas : null, atencion.Count > 0 ? filas : null, mensaje, quienEnvia, adjuntos, esIngles);

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeIndicadores", filtros.IdEmpresa.ToString(),
                    $"Informe de indicadores enviado por correo a {string.Join(", ", lista)}: '{empresa}' — corte {corte}, escenario {filtros.IdEscenario}.",
                    null,
                    new { empresa, corte, escenario = filtros.IdEscenario, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        // ===== Textos de valores (correo y Excel) =====

        private string TextoValor(IndicadorValor i, decimal? v)
        {
            if (!v.HasValue) return i.Motivo != null ? R("Ind_Motivo_" + i.Motivo) : "—";
            var c = CultureInfo.CurrentCulture;
            switch (i.Formato)
            {
                case "pct": return (v.Value * 100m).ToString("0.0", c) + " %";
                case "x": return v.Value.ToString("0.00", c) + "x";
                case "dias": return v.Value.ToString("0", c) + " " + R("Ind_UnidadDias");
                default: return (v.Value / 1000000m).ToString("N2", c) + " M";
            }
        }

        private string TextoVariacion(IndicadorValor i)
        {
            if (!i.Variacion.HasValue) return "—";
            var c = CultureInfo.CurrentCulture; decimal v = i.Variacion.Value;
            switch (i.Formato)
            {
                case "pct": return (v * 100m).ToString("+0.0;-0.0;0.0", c) + " pp";
                case "x": return v.ToString("+0.00;-0.00;0.00", c) + "x";
                case "dias": return v.ToString("+0;-0;0", c) + " " + R("Ind_UnidadDias");
                default: return (v / 1000000m).ToString("+0.00;-0.00;0.00", c) + " M";
            }
        }

        private string TextoRango(IndicadorValor i)
        {
            if (i.Formato == "moneda") return R("Ind_RangoPositivo");
            if (!i.MasEsMejor.HasValue || i.Cortes == null || i.Cortes.Length < 3) return "";
            Func<decimal, string> f = x => TextoValor(i, x);
            string signo = i.MasEsMejor.Value ? "≥ " : "≤ ";
            return R("Ind_Nivel_verde") + " " + signo + f(i.Cortes[0]) + " · " + R("Ind_Nivel_amarillo") + " " + signo + f(i.Cortes[1]) +
                   " · " + R("Ind_Nivel_naranja") + " " + signo + f(i.Cortes[2]) + " · " + R("Ind_Nivel_rojo");
        }

        // ===== Excel =====

        private static readonly string[] PaletaSeries = { "6366F1", "D97706", "16A34A", "0EA5E9" };

        /// <summary>Libro de Excel del informe (lo usan la descarga y el correo): Resumen (todos los indicadores), Tendencia mensual
        /// (con gráfico nativo de los márgenes) y Fichas (qué mide cada indicador y cómo se lee el semáforo).</summary>
        private byte[] GenerarExcel(IndicadoresReporteViewModel r, out string nombreArchivo)
        {
            string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == r.IdEmpresa)?.Nombre ?? "Empresa";
            string corte = NombreMes(r.MesHasta) + " " + r.AnioHasta;
            string titulo = R("Ind_PageTitle");
            string hojaResumen = R("Ind_ExcelHojaResumen"), hojaTend = R("Ind_ExcelHojaTendencia"), hojaFichas = R("Ind_ExcelHojaFichas");
            string subtitulo = R("Ind_CorreoCorte") + ": " + corte + (r.HayComparativo ? " · " + R("Ind_CorreoComparado") + ": " + NombreMes(r.MesComp) + " " + r.AnioComp : "");
            byte[] bytes;
            var etiquetasMes = new List<string>();
            const int filaEnc = 4;

            using (var wb = new XLWorkbook())
            {
                // --- Resumen
                var wsR = wb.Worksheets.Add(hojaResumen);
                EncabezadoHoja(wsR, titulo + " — " + empresa, subtitulo);
                var colsR = new[] { R("Ind_ColFamilia"), R("Ind_ColIndicador"), R("Ind_ColValor"), R("Ind_ColComparativo"), R("Ind_ColVariacion"), R("Ind_ColSemaforo"), R("Ind_ColReferencia") };
                for (int c = 0; c < colsR.Length; c++) wsR.Cell(filaEnc, 1 + c).Value = colsR[c];
                int f = filaEnc + 1;
                foreach (var i in r.Indicadores)
                {
                    wsR.Cell(f, 1).Value = R("Ind_Fam_" + i.Familia);
                    wsR.Cell(f, 2).Value = R("Ind_" + i.Codigo + "_Nombre");
                    wsR.Cell(f, 3).Value = TextoValor(i, i.Valor) + (i.Parcial && i.Valor.HasValue ? " (" + string.Format(R("Ind_ParcialFmt"), i.MesesLtm) + ")" : "");
                    wsR.Cell(f, 4).Value = r.HayComparativo ? TextoValor(i, i.ValorComp) : "—";
                    wsR.Cell(f, 5).Value = TextoVariacion(i);
                    wsR.Cell(f, 6).Value = R("Ind_Nivel_" + i.Nivel);
                    wsR.Cell(f, 6).Style.Font.FontColor = ColorNivel(i.Nivel);
                    wsR.Cell(f, 6).Style.Font.Bold = true;
                    wsR.Cell(f, 7).Value = TextoRango(i);
                    f++;
                }
                EstiloTabla(wsR, filaEnc, f - 1, colsR.Length);
                wsR.Cell(f + 1, 1).Value = R("Ind_NotaReferencia");
                wsR.Cell(f + 1, 1).Style.Font.Italic = true;
                wsR.Cell(f + 1, 1).Style.Font.FontColor = XLColor.Gray;

                // --- Tendencia mensual (valor de cada indicador evaluado en cada mes)
                var wsT = wb.Worksheets.Add(hojaTend);
                EncabezadoHoja(wsT, hojaTend + " — " + empresa, subtitulo);
                wsT.Cell(filaEnc, 1).Value = R("Ind_ColMes");
                var meses = r.Indicadores.SelectMany(x => x.Tendencia).Select(p => new { p.Anio, p.Mes }).Distinct().OrderBy(p => p.Anio).ThenBy(p => p.Mes).ToList();
                for (int k = 0; k < r.Indicadores.Count; k++) wsT.Cell(filaEnc, 2 + k).Value = R("Ind_" + r.Indicadores[k].Codigo + "_Nombre");
                for (int m = 0; m < meses.Count; m++)
                {
                    string et = NombreMes(meses[m].Mes).Substring(0, Math.Min(3, NombreMes(meses[m].Mes).Length)) + " " + meses[m].Anio;
                    etiquetasMes.Add(et);
                    wsT.Cell(filaEnc + 1 + m, 1).Value = et;
                    for (int k = 0; k < r.Indicadores.Count; k++)
                    {
                        var ind = r.Indicadores[k];
                        var p = ind.Tendencia.FirstOrDefault(x => x.Anio == meses[m].Anio && x.Mes == meses[m].Mes);
                        if (p == null || !p.Valor.HasValue) continue;
                        wsT.Cell(filaEnc + 1 + m, 2 + k).Value = (double)p.Valor.Value;
                        wsT.Cell(filaEnc + 1 + m, 2 + k).Style.NumberFormat.Format = ind.Formato == "pct" ? "0.0%" : (ind.Formato == "moneda" ? "#,##0" : (ind.Formato == "dias" ? "0" : "0.00"));
                    }
                }
                EstiloTabla(wsT, filaEnc, filaEnc + Math.Max(1, meses.Count), 1 + r.Indicadores.Count);

                // --- Fichas
                var wsF = wb.Worksheets.Add(hojaFichas);
                EncabezadoHoja(wsF, hojaFichas + " — " + titulo, R("Ind_NotaReferencia"));
                var colsF = new[] { R("Ind_ColFamilia"), R("Ind_ColIndicador"), R("Ind_Ficha_Pregunta"), R("Ind_Ficha_Que"), R("Ind_Ficha_Formula"), R("Ind_Ficha_Rangos"), R("Ind_Ficha_Mejorar") };
                for (int c = 0; c < colsF.Length; c++) wsF.Cell(filaEnc, 1 + c).Value = colsF[c];
                int ff = filaEnc + 1;
                foreach (var i in r.Indicadores)
                {
                    wsF.Cell(ff, 1).Value = R("Ind_Fam_" + i.Familia);
                    wsF.Cell(ff, 2).Value = R("Ind_" + i.Codigo + "_Nombre");
                    wsF.Cell(ff, 3).Value = R("Ind_" + i.Codigo + "_Pregunta");
                    wsF.Cell(ff, 4).Value = R("Ind_" + i.Codigo + "_Que");
                    wsF.Cell(ff, 5).Value = R("Ind_" + i.Codigo + "_Formula");
                    wsF.Cell(ff, 6).Value = TextoRango(i);
                    wsF.Cell(ff, 7).Value = R("Ind_" + i.Codigo + "_Mejorar");
                    ff++;
                }
                EstiloTabla(wsF, filaEnc, ff - 1, colsF.Length);
                foreach (int col in new[] { 3, 4, 5, 6, 7 }) { wsF.Column(col).Width = 48; wsF.Column(col).Style.Alignment.WrapText = true; }
                wsF.Range(filaEnc + 1, 1, ff - 1, colsF.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

                using (var ms = new MemoryStream()) { wb.SaveAs(ms); bytes = ms.ToArray(); }
            }

            // Gráfico nativo en "Tendencia mensual": líneas de los márgenes bruto, EBITDA y neto.
            try
            {
                int Col(string cod) { return r.Indicadores.FindIndex(x => x.Codigo == cod) + 2; }
                var series = new List<SerieGraficoExcel>();
                int pal = 0;
                foreach (var cod in new[] { "MargenBruto", "MargenEbitda", "MargenNeto" })
                {
                    var ind = r.Indicadores.FirstOrDefault(x => x.Codigo == cod);
                    if (ind == null) continue;
                    var valores = new List<double?>();
                    var meses = r.Indicadores.SelectMany(x => x.Tendencia).Select(p => new { p.Anio, p.Mes }).Distinct().OrderBy(p => p.Anio).ThenBy(p => p.Mes).ToList();
                    foreach (var m in meses)
                    {
                        var p = ind.Tendencia.FirstOrDefault(x => x.Anio == m.Anio && x.Mes == m.Mes);
                        valores.Add(p != null && p.Valor.HasValue ? (double?)p.Valor.Value : null);
                    }
                    series.Add(new SerieGraficoExcel { Nombre = R("Ind_" + cod + "_Nombre"), Columna = Col(cod), ColorHex = PaletaSeries[pal++ % PaletaSeries.Length], Valores = valores });
                }
                if (series.Count > 0 && etiquetasMes.Count > 0)
                    bytes = ExcelChartHelper.AgregarGrafico(bytes, hojaTend, R("Ind_GraficoMargenesTitulo"), false, filaEnc, filaEnc + 1,
                        filaEnc + etiquetasMes.Count, 1, etiquetasMes, series, "0.0%", r.Indicadores.Count + 3, filaEnc - 1);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".GenerarExcel (gráfico)");
            }

            nombreArchivo = $"Indicadores_{empresa}_{r.AnioHasta}{r.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
            return bytes;
        }

        private static XLColor ColorNivel(string nivel)
        {
            switch (nivel)
            {
                case IndicadorNivel.Verde: return XLColor.FromArgb(21, 128, 61);
                case IndicadorNivel.Amarillo: return XLColor.FromArgb(161, 98, 7);
                case IndicadorNivel.Naranja: return XLColor.FromArgb(194, 65, 12);
                case IndicadorNivel.Rojo: return XLColor.FromArgb(185, 28, 28);
                default: return XLColor.Gray;
            }
        }

        private static void EncabezadoHoja(IXLWorksheet ws, string titulo, string subtitulo)
        {
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = subtitulo + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        private static void EstiloTabla(IXLWorksheet ws, int filaEnc, int filaFin, int colFin)
        {
            var enc = ws.Range(filaEnc, 1, filaEnc, colFin);
            enc.Style.Font.Bold = true;
            enc.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
            enc.Style.Font.FontColor = XLColor.White;
            enc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var cuerpo = ws.Range(filaEnc, 1, filaFin, colFin);
            cuerpo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cuerpo.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            cuerpo.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
            ws.Columns().AdjustToContents();
            ws.SheetView.Freeze(filaEnc, 1);
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
