using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe histórico de precios (Churido &amp; Ucrania) — informe PERSONALIZADO: solo existe para las empresas
    /// asignadas a la opción de menú <c>INFORMES_HIST_PRECIOS</c> (Gestor de menús → "Personalizada para empresas").
    /// Precio de venta por tipo de fruta, variación anual, promedio general y crecimiento compuesto, a partir de la
    /// hoja de cargue Z_HistPrecios_Churido (ver InformeHistPreciosService). No usa IA: la regla de datos de IA
    /// solo permite tablas dbo.Modelo*.
    /// </summary>
    [ValidarSesion]
    [RequierePermiso(InformeHistPreciosService.CodigoMenu)]
    public class InformeHistPreciosController : BaseController
    {
        private readonly InformeHistPreciosService _service = new InformeHistPreciosService();
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();
        private readonly MenuOpcionesService _menuService = new MenuOpcionesService();

        private static readonly string[] PaletaSeries =
            { "6366F1", "0EA5E9", "16A34A", "F59E0B", "EC4899", "8B5CF6", "14B8A6", "EF4444", "64748B" };

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = EmpresasDelInforme();

            ViewBag.Empresas = empresas;
            ViewBag.IdEmpresaUsuario = usuario?.IdEmpresa ?? 0;
            return View("~/Views/Informes/InformeHistPrecios.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerOpciones(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!PuedeVerEmpresa(idEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var o = _service.ObtenerOpciones(idEmpresa, idEscenario);
                return Json(new { success = true, semestres = o.Semestres.Select(m => new { anio = m.Anio, sem = m.Sem }), tipos = o.Tipos, hayDatos = o.HayDatos });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeHistPreciosController.ObtenerOpciones");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosHistPrecios filtros)
        {
            try
            {
                if (filtros == null || !PuedeVerEmpresa(filtros.IdEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var reporte = _service.Construir(filtros);
                var json = Json(new { success = true, reporte });
                json.MaxJsonLength = int.MaxValue;
                return json;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeHistPreciosController.ConsultarReporte");
                return Json(new { success = false, message = R("HP_ErrorServidor") });
            }
        }

        /// <summary>Excel con 3 hojas (precios por tipo, variación %, promedio general), cada una con su gráfico nativo.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosHistPrecios filtros)
        {
            try
            {
                if (filtros == null || !PuedeVerEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var reporte = _service.Construir(filtros);
                if (reporte.SinDatos)
                {
                    TempData["InfoMessage"] = R("HP_SinDatosDesc");
                    return RedirectToAction("Index");
                }

                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                byte[] bytes = GenerarExcel(reporte, empresa, filtros.IdEmpresa);

                return File(bytes, MimeExcel, NombreArchivo(empresa, "xlsx"));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeHistPreciosController.ExportarExcel");
                TempData["ErrorMessage"] = R("HP_ErrorExcel");
                return RedirectToAction("Index");
            }
        }

        private const string MimeExcel = InformeCorreoHelper.MimeExcel;

        /// <summary>
        /// Envía el informe por correo desde el botón Exportar: Excel (3 hojas con gráficos) + PDF (el que arma el
        /// navegador) como adjuntos y, en el cuerpo, los indicadores clave y los precios del último periodo. El reporte
        /// se reconstruye en el servidor con los filtros recibidos (nunca se confía en cifras del navegador).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosHistPrecios filtros, string correos, bool incluirmeAMi = false,
            string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !PuedeVerEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });

            try
            {
                var reporte = _service.Construir(filtros);
                if (reporte.SinDatos)
                    return Json(new { exito = false, mensaje = R("HP_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                    .FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);

                var adjuntos = InformeCorreoHelper.Adjuntos(NombreArchivo(empresa, "xlsx"), GenerarExcel(reporte, empresa, filtros.IdEmpresa),
                    InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                ResumenParaCorreo(reporte, out string periodoUltimo, out var kpis, out var filas);
                string rango = string.Format(R("HP_PeriodoSemestreFmt"), reporte.SemDesde, reporte.AnioDesde) + " – " +
                               string.Format(R("HP_PeriodoSemestreFmt"), reporte.SemHasta, reporte.AnioHasta);
                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                var contexto = new List<(string etiqueta, string valor)>
                {
                    (R("HP_Correo_Periodo"), rango), (R("HP_Correo_Vista"), R("HP_Agrupar_" + reporte.Agrupar)), (R("Common_LabelEscenario"), escenario)
                };

                new EmailService().EnviarInforme(
                    string.Join(", ", lista), R("HP_PageTitle"), empresa, true, contexto, kpis,
                    string.Format(R("HP_Correo_TablaTitulo"), periodoUltimo),
                    new[] { R("HP_ColTipo"), R("HP_Correo_ColPrecio"), R("HP_Correo_ColVsAnterior") }, filas,
                    mensaje, quienEnvia, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa));

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeHistPrecios", filtros.IdEmpresa.ToString(),
                    $"Informe histórico de precios enviado por correo a {string.Join(", ", lista)}: '{empresa}' — {rango}, escenario {filtros.IdEscenario}.",
                    null,
                    new { empresa, rango, vista = reporte.Agrupar, escenario = filtros.IdEscenario, tipos = filtros.Tipos, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeHistPreciosController.EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        /// <summary>Cifras del cuerpo del correo (las mismas 4 tarjetas de la pantalla + precios del último periodo).</summary>
        private void ResumenParaCorreo(HpReporte r, out string periodoUltimo,
            out List<(string etiqueta, string valor, string detalle, int signo)> kpis,
            out List<(string[] celdas, int signo)> filas)
        {
            kpis = new List<(string etiqueta, string valor, string detalle, int signo)>();
            filas = new List<(string[] celdas, int signo)>();
            periodoUltimo = "";
            int u = r.IdxUltimo;
            if (u < 0) return;

            string Precio(decimal v) => v.ToString("0.00#");
            string Pct(decimal v) => (v * 100m).ToString("+0.0;-0.0;0.0") + " %";
            int Signo(decimal? v) => !v.HasValue ? 0 : (v.Value > 0 ? 1 : (v.Value < 0 ? -1 : 0));

            var p = r.Periodos[u];
            periodoUltimo = EtiquetaPeriodo(p, r.Agrupar);
            string comparable = EtiquetaPeriodo(new HpPeriodo { Anio = p.Anio - 1, Sub = p.Sub }, r.Agrupar);

            decimal? varTotal = r.Total.Variaciones[u];
            kpis.Add((R("HP_KpiPromedio"), Precio(r.Total.Valores[u].Value),
                varTotal.HasValue ? Pct(varTotal.Value) + " " + string.Format(R("HP_VsFmt"), comparable) : string.Format(R("HP_EnFmt"), periodoUltimo),
                Signo(varTotal)));

            if (r.MasCaro != null)
                kpis.Add((R("HP_KpiMasCaro"), Precio(r.MasCaro.Valor), r.MasCaro.Tipo, 0));

            bool alza = r.MayorAlza != null && r.MayorAlza.Valor >= 0m;
            var mv = alza ? r.MayorAlza : (r.MenorVariacion ?? r.MayorAlza);
            if (mv != null)
                kpis.Add((alza ? R("HP_KpiMayorAlza") : R("HP_KpiMayorBaja"), Pct(mv.Valor), mv.Tipo, Signo(mv.Valor)));

            if (r.Cagr.HasValue)
                kpis.Add((R("HP_KpiCagr"), Pct(r.Cagr.Value), string.Format(R("HP_CagrSubFmt"), r.CagrAnioInicial, r.CagrAnioFinal), Signo(r.Cagr)));

            foreach (var s in r.Series.Where(x => x.Valores[u].HasValue))
            {
                decimal? v = s.Variaciones[u];
                filas.Add((new[] { s.Tipo, Precio(s.Valores[u].Value), v.HasValue ? Pct(v.Value) : "—" }, Signo(v)));
            }
        }

        // ---------- Acceso ----------

        /// <summary>Empresas para las que existe el informe Y a las que el usuario tiene acceso.</summary>
        private List<Empresas> EmpresasDelInforme()
        {
            var todas = _empresaService.ObtenerEmpresas();
            var delMenu = _menuService.ObtenerIdsEmpresasDeOpcion(InformeHistPreciosService.CodigoMenu);
            if (delMenu.Count > 0)
                todas = todas.Where(e => delMenu.Contains(e.Id)).ToList();

            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(UsuarioSesionHelper.UsuarioActual) ?? new List<int>();
                todas = todas.Where(e => permitidas.Contains(e.Id)).ToList();
            }
            return todas;
        }

        private bool PuedeVerEmpresa(int idEmpresa)
        {
            return EmpresasDelInforme().Any(e => e.Id == idEmpresa);
        }

        // ---------- Excel ----------

        /// <summary>Libro de Excel del informe (3 hojas, cada una con su gráfico nativo): lo usan la descarga y el correo.</summary>
        private byte[] GenerarExcel(HpReporte reporte, string empresa, int idEmpresa)
        {
            var etiquetas = reporte.Periodos.Select(p => EtiquetaPeriodo(p, reporte.Agrupar)).ToList();
            string hojaPrecios = R("HP_XlsHojaPrecios"), hojaVar = R("HP_XlsHojaVariacion"), hojaProm = R("HP_XlsHojaPromedio");
            string totalGeneral = R("HP_TotalGeneral");

            byte[] bytes;
            using (var wb = new XLWorkbook())
            {
                // --- Hoja 1: precio promedio por tipo (periodos en filas, tipos en columnas: lo que pide el gráfico nativo)
                var ws = wb.Worksheets.Add(hojaPrecios);
                EscribirEncabezadoHoja(ws, R("HP_PageTitle") + " — " + empresa, reporte, etiquetas.Count);
                int filaEnc = 4;
                ws.Cell(filaEnc, 1).Value = R("HP_XlsColPeriodo");
                for (int s = 0; s < reporte.Series.Count; s++) ws.Cell(filaEnc, 2 + s).Value = reporte.Series[s].Tipo;
                ws.Cell(filaEnc, 2 + reporte.Series.Count).Value = totalGeneral;
                int colTotal = 2 + reporte.Series.Count;
                for (int i = 0; i < etiquetas.Count; i++)
                {
                    ws.Cell(filaEnc + 1 + i, 1).Value = etiquetas[i];
                    for (int s = 0; s < reporte.Series.Count; s++)
                        EscribirValor(ws.Cell(filaEnc + 1 + i, 2 + s), reporte.Series[s].Valores[i], "#,##0.000");
                    EscribirValor(ws.Cell(filaEnc + 1 + i, colTotal), reporte.Total.Valores[i], "#,##0.000");
                }
                int filaPromedio = filaEnc + 1 + etiquetas.Count;
                ws.Cell(filaPromedio, 1).Value = R("HP_ColPromedio");
                for (int s = 0; s < reporte.Series.Count; s++) EscribirValor(ws.Cell(filaPromedio, 2 + s), reporte.Series[s].Promedio, "#,##0.000");
                EscribirValor(ws.Cell(filaPromedio, colTotal), reporte.Total.Promedio, "#,##0.000");
                EstiloTabla(ws, filaEnc, filaPromedio, colTotal);

                // --- Hoja 2: variación %
                var wv = wb.Worksheets.Add(hojaVar);
                EscribirEncabezadoHoja(wv, R("HP_TablaVariacionTitulo") + " — " + empresa, reporte, etiquetas.Count);
                wv.Cell(filaEnc, 1).Value = R("HP_XlsColPeriodo");
                for (int s = 0; s < reporte.Series.Count; s++) wv.Cell(filaEnc, 2 + s).Value = reporte.Series[s].Tipo;
                wv.Cell(filaEnc, colTotal).Value = totalGeneral;
                for (int i = 0; i < etiquetas.Count; i++)
                {
                    wv.Cell(filaEnc + 1 + i, 1).Value = etiquetas[i];
                    for (int s = 0; s < reporte.Series.Count; s++)
                        EscribirValor(wv.Cell(filaEnc + 1 + i, 2 + s), reporte.Series[s].Variaciones[i], "0.0%");
                    EscribirValor(wv.Cell(filaEnc + 1 + i, colTotal), reporte.Total.Variaciones[i], "0.0%");
                }
                EstiloTabla(wv, filaEnc, filaEnc + etiquetas.Count, colTotal);

                // --- Hoja 3: promedio general + variación
                var wp = wb.Worksheets.Add(hojaProm);
                EscribirEncabezadoHoja(wp, R("HP_PromedioTitulo") + " — " + empresa, reporte, etiquetas.Count);
                wp.Cell(filaEnc, 1).Value = R("HP_XlsColPeriodo");
                wp.Cell(filaEnc, 2).Value = R("HP_PromedioGeneral");
                wp.Cell(filaEnc, 3).Value = R("HP_ColVariacion");
                for (int i = 0; i < etiquetas.Count; i++)
                {
                    wp.Cell(filaEnc + 1 + i, 1).Value = etiquetas[i];
                    EscribirValor(wp.Cell(filaEnc + 1 + i, 2), reporte.Total.Valores[i], "#,##0.000");
                    EscribirValor(wp.Cell(filaEnc + 1 + i, 3), reporte.Total.Variaciones[i], "0.0%");
                }
                EstiloTabla(wp, filaEnc, filaEnc + etiquetas.Count, 3);

                // Logo de la empresa (si tiene) junto al título de cada hoja; sin logo el libro queda igual que siempre.
                ExcelLogoHelper.ColocarEnTodas(wb, ExcelLogoHelper.Obtener(idEmpresa));
                using (var ms = new MemoryStream()) { wb.SaveAs(ms); bytes = ms.ToArray(); }

                int fin = filaEnc + etiquetas.Count;
                bytes = AgregarGraficoSeguro(bytes, hojaPrecios, R("HP_GraficoEvolucionTitulo"), false, filaEnc, fin, etiquetas,
                    reporte.Series.Select((s, i) => new SerieGraficoExcel
                    {
                        Nombre = s.Tipo, Columna = 2 + i, ColorHex = PaletaSeries[i % PaletaSeries.Length],
                        Valores = s.Valores.Select(v => (double?)v).ToList()
                    }).ToList(), "0.00", colTotal + 1);
                bytes = AgregarGraficoSeguro(bytes, hojaVar, R("HP_TablaVariacionTitulo"), true, filaEnc, fin, etiquetas,
                    reporte.Series.Select((s, i) => new SerieGraficoExcel
                    {
                        Nombre = s.Tipo, Columna = 2 + i, ColorHex = PaletaSeries[i % PaletaSeries.Length],
                        Valores = s.Variaciones.Select(v => (double?)v).ToList()
                    }).ToList(), "0%", colTotal + 1);
                bytes = AgregarGraficoSeguro(bytes, hojaProm, R("HP_PromedioTitulo"), false, filaEnc, fin, etiquetas,
                    new List<SerieGraficoExcel>
                    {
                        new SerieGraficoExcel { Nombre = R("HP_PromedioGeneral"), Columna = 2, ColorHex = PaletaSeries[0],
                            Valores = reporte.Total.Valores.Select(v => (double?)v).ToList() }
                    }, "0.00", 5);
            }
            return bytes;
        }

        private static string NombreArchivo(string empresa, string extension)
        {
            string nombre = $"HistoricoPrecios_{empresa}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
            return string.Join("_", nombre.Split(Path.GetInvalidFileNameChars()));
        }


        private string EtiquetaPeriodo(HpPeriodo p, string agrupar)
        {
            switch (agrupar)
            {
                case "Semestre": return string.Format(R("HP_PeriodoSemestreFmt"), p.Sub, p.Anio);
                default: return p.Anio.ToString();
            }
        }

        private void EscribirEncabezadoHoja(IXLWorksheet ws, string titulo, HpReporte r, int nPeriodos)
        {
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = string.Format(R("HP_XlsSubtituloFmt"), string.Format(R("HP_PeriodoSemestreFmt"), r.SemDesde, r.AnioDesde), string.Format(R("HP_PeriodoSemestreFmt"), r.SemHasta, r.AnioHasta), R("HP_Agrupar_" + r.Agrupar), DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        private static void EscribirValor(IXLCell celda, decimal? valor, string formato)
        {
            if (!valor.HasValue) return;
            celda.Value = (double)valor.Value;
            celda.Style.NumberFormat.Format = formato;
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
            ws.Column(1).Style.Font.Bold = true;
            ws.Columns().AdjustToContents();
            ws.SheetView.Freeze(filaEnc, 1);
        }

        /// <summary>El gráfico nativo es un extra: si falla se entrega el Excel sin él (los datos nunca se pierden).</summary>
        private static byte[] AgregarGraficoSeguro(byte[] bytes, string hoja, string titulo, bool barras, int filaEnc, int filaFin,
            List<string> categorias, List<SerieGraficoExcel> series, string formatoEje, int columnaAncla)
        {
            try
            {
                return ExcelChartHelper.AgregarGrafico(bytes, hoja, titulo, barras, filaEnc, filaEnc + 1, filaFin, 1,
                    categorias, series, formatoEje, columnaAncla, filaEnc - 1);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeHistPreciosController.ExportarExcel (gráfico " + hoja + ")");
                return bytes;
            }
        }
    }
}
