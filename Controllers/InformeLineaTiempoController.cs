using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe "Línea de Tiempo Financiera": evolución mes a mes de un indicador de las tablas
    /// de resultados de Ejecución de Modelos (dbo.Modelo*), con opción de comparar Escenario 1
    /// vs Escenario 2. Reutiliza InformeTablasDatosService para catálogo (tablas/variables) e
    /// InformeLineaTiempoService para la consulta agregada de la serie. Habilitado para todos
    /// los roles, igual criterio que InformeModelosController — el acceso real se controla por
    /// empresa/grupo vía EmpresaAccesoHelper, no por rol.
    /// </summary>
    [ValidarSesion]
    public class InformeLineaTiempoController : BaseController
    {
        private readonly InformeTablasDatosService _catalogo = new InformeTablasDatosService();
        private readonly InformeLineaTiempoService _service = new InformeLineaTiempoService();

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var empresas = _catalogo.ObtenerEmpresas();
            if (!esAdmin)
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            ViewBag.Tablas = _catalogo.ObtenerTablasModelos();
            ViewBag.Empresas = empresas;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            return View("~/Views/Informes/InformeLineaTiempo.cshtml");
        }

        // Mismos colores que LT_PALETTE y la serie de comparación (ámbar) en InformeLineaTiempo.cshtml,
        // para que el gráfico del Excel se vea como el de pantalla.
        private static readonly string[] ColoresSerie = { "6366F1", "D97706", "06B6D4", "16A34A", "DC2626", "8B5CF6", "0EA5E9", "F59E0B" };
        private const string ColorComparacion = "D97706";

        private static string Recortar(string texto, int max)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            texto = texto.Trim();
            return texto.Length <= max ? texto : texto.Substring(0, max);
        }

        private bool EsTablaDeModelo(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla)
                && _catalogo.ObtenerTablasModelos().Any(t => t.NombreTabla == nombreTabla);
        }

        /// <summary>Resuelve y valida la empresa a consultar para el usuario actual; null = sin acceso.</summary>
        private int? ResolverEmpresaConsulta(int? idEmpresaSolicitada, bool esAdmin, int? idEmpresaUsuario)
        {
            if (esAdmin)
                return idEmpresaSolicitada ?? idEmpresaUsuario;

            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (idEmpresaSolicitada.HasValue)
                return EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresaSolicitada.Value) ? idEmpresaSolicitada : (int?)null;

            return idEmpresaUsuario;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerIndicadores(string nombreTabla, int? idEmpresa, int? idEscenario)
        {
            if (!EsTablaDeModelo(nombreTabla))
                return Json(new { success = false, message = R("Common_TablaNoValida") });

            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresaConsulta = ResolverEmpresaConsulta(idEmpresa, esAdmin, usuario?.IdEmpresa);
            if (!idEmpresaConsulta.HasValue)
                return Json(new { success = false, message = R("Common_SinPermisos") });

            var indicadores = _service.ObtenerIndicadoresDisponibles(nombreTabla, idEmpresaConsulta, idEscenario);
            return Json(new { success = true, indicadores });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerCampos(string nombreTabla)
        {
            if (!EsTablaDeModelo(nombreTabla))
                return Json(new { success = false, message = R("Common_TablaNoValida") });

            var campos = _service.ObtenerCamposNumericos(nombreTabla)
                .Select(c => new { nombreTecnico = c.NombreTecnico, nombreAmigable = c.NombreAmigable });
            return Json(new { success = true, campos });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerRangoMeses(string nombreTabla, int? idEmpresa, int? idEscenario)
        {
            if (!EsTablaDeModelo(nombreTabla))
                return Json(new { success = false, message = R("Common_TablaNoValida") });

            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresaConsulta = ResolverEmpresaConsulta(idEmpresa, esAdmin, usuario?.IdEmpresa);
            if (!idEmpresaConsulta.HasValue)
                return Json(new { success = false, message = R("Common_SinPermisos") });

            // Se proyecta a camelCase explícito: ASP.NET MVC 5 serializa JSON respetando el
            // PascalCase de C# tal cual (no hay auto-camelCase como en ASP.NET Core), y el JS
            // de la vista lee minúscula inicial en todas sus propiedades.
            var meses = _service.ObtenerMesesDisponibles(nombreTabla, idEmpresaConsulta, idEscenario)
                .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
            return Json(new { success = true, meses });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerSerie(FiltrosLineaTiempo filtros)
        {
            try
            {
                if (filtros == null || !EsTablaDeModelo(filtros.NombreTabla))
                    return Json(new { success = false, message = R("Common_TablaNoValida") });

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaConsulta = ResolverEmpresaConsulta(filtros.IdEmpresa, esAdmin, usuario?.IdEmpresa);
                if (!idEmpresaConsulta.HasValue)
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var resultado = _service.ObtenerSerieTiempo(filtros, idEmpresaConsulta);

                return Json(new
                {
                    success = true,
                    series = resultado.Series.Select(s => new
                    {
                        indicador = s.Indicador,
                        puntos = s.Puntos.Select(p => new { anio = p.Anio, mes = p.Mes, etiqueta = p.Etiqueta, valor = p.Valor })
                    }),
                    comparacion = resultado.Comparacion?.Select(p => new { anio = p.Anio, mes = p.Mes, etiqueta = p.Etiqueta, valor = p.Valor }),
                    nombreEscenarioPrincipal = resultado.NombreEscenarioPrincipal,
                    nombreEscenarioComparacion = resultado.NombreEscenarioComparacion
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineaTiempoController.ObtenerSerie");
                return Json(new { success = false, message = $"Error al consultar la serie: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosLineaTiempo filtros, OpcionesExportLineaTiempo opciones)
        {
            try
            {
                if (filtros == null || !EsTablaDeModelo(filtros.NombreTabla))
                {
                    TempData["ErrorMessage"] = R("Common_TablaNoValida");
                    return RedirectToAction("Index");
                }

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaConsulta = ResolverEmpresaConsulta(filtros.IdEmpresa, esAdmin, usuario?.IdEmpresa);
                if (!idEmpresaConsulta.HasValue)
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var resultado = _service.ObtenerSerieTiempo(filtros, idEmpresaConsulta);
                if (resultado.Series.Count == 0 || resultado.Series.All(s => s.Puntos.Count == 0))
                {
                    TempData["InfoMessage"] = R("LineaTiempo_SinDatosExportar");
                    return RedirectToAction("Index");
                }

                opciones = opciones ?? new OpcionesExportLineaTiempo();
                bool barras = string.Equals(opciones.TipoGrafico, "bar", StringComparison.OrdinalIgnoreCase);
                string unidad = (opciones.Unidad ?? "pesos").ToLowerInvariant();

                // Series visibles en pantalla (la tira de series permite ocultarlas). Se conserva el
                // índice original de cada una para que el color coincida con el de pantalla. Si el
                // usuario ocultó todas, se exportan todas (un Excel vacío no le sirve a nadie).
                var ocultas = new HashSet<string>(opciones.SeriesOcultas ?? new List<string>());
                var visibles = resultado.Series
                    .Select((s, i) => new { Serie = s, Indice = i })
                    .Where(x => !ocultas.Contains(x.Serie.Indicador ?? ""))
                    .ToList();
                if (visibles.Count == 0)
                    visibles = resultado.Series.Select((s, i) => new { Serie = s, Indice = i }).ToList();

                bool unaVariable = visibles.Count == 1;
                bool tieneComparacion = resultado.Series.Count == 1 && resultado.Comparacion != null && resultado.Comparacion.Count > 0;

                // Los valores se guardan completos y solo el FORMATO los muestra en la unidad elegida
                // (la coma final de un formato de Excel divide entre 1.000): el dato real no se pierde.
                string formatoCelda, formatoEje, unidadTexto;
                switch (unidad)
                {
                    case "miles":
                        formatoCelda = "#,##0.0,";
                        formatoEje = "#,##0.0,\" " + R("LineaTiempo_UnidadSufijoMiles") + "\"";
                        unidadTexto = R("LineaTiempo_UnidadMiles");
                        break;
                    case "millones":
                        formatoCelda = "#,##0.0,,";
                        formatoEje = "#,##0.0,,\" " + R("LineaTiempo_UnidadSufijoMillones") + "\"";
                        unidadTexto = R("LineaTiempo_UnidadMillones");
                        break;
                    default:
                        formatoCelda = "#,##0.00";
                        formatoEje = "#,##0";
                        unidadTexto = R("LineaTiempo_UnidadPesos");
                        break;
                }

                const string nombreHoja = "LineaTiempo";
                const int filaEncabezado = 5;
                const int filaInicio = filaEncabezado + 1;
                var colorPrincipal = XLColor.FromArgb(99, 102, 241);

                using (var package = new XLWorkbook())
                {
                    var worksheet = package.Worksheets.Add(nombreHoja);

                    // Cabecera: mismo título/subtítulo que la pantalla (los arma pintarLetterhead en la vista).
                    string titulo = Recortar(opciones.Titulo, 300);
                    worksheet.Cell(1, 1).Value = titulo;
                    worksheet.Cell(1, 1).Style.Font.Bold = true;
                    worksheet.Cell(1, 1).Style.Font.FontSize = 14;
                    worksheet.Cell(1, 1).Style.Font.FontColor = colorPrincipal;
                    worksheet.Cell(2, 1).Value = Recortar(opciones.Subtitulo, 500);
                    worksheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(91, 85, 112);
                    worksheet.Cell(3, 1).Value = R("LineaTiempo_PdfUnidadPrefijo") + " " + unidadTexto.ToLower(CultureInfo.CurrentCulture)
                        + "  ·  " + string.Format(R("LineaTiempo_GeneradoElFmt"), DateTime.Now.ToString("D", CultureInfo.CurrentCulture));
                    worksheet.Cell(3, 1).Style.Font.Italic = true;
                    worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.FromArgb(139, 133, 160);

                    // Columnas de valor: (columna, serie, color) — las mismas que la tabla en pantalla.
                    var columnasValor = new List<Tuple<int, List<PuntoLineaTiempo>, string>>();
                    worksheet.Cell(filaEncabezado, 1).Value = R("LineaTiempo_ColPeriodo");
                    int totalColumnas;
                    if (unaVariable)
                    {
                        var serie = visibles[0];
                        string encabezado = tieneComparacion
                            ? (resultado.NombreEscenarioPrincipal ?? serie.Serie.Indicador)
                            : (!string.IsNullOrWhiteSpace(opciones.CampoTexto) ? opciones.CampoTexto : serie.Serie.Indicador);
                        worksheet.Cell(filaEncabezado, 2).Value = encabezado ?? R("LineaTiempo_ColValor");
                        columnasValor.Add(Tuple.Create(2, serie.Serie.Puntos, ColoresSerie[serie.Indice % ColoresSerie.Length]));
                        totalColumnas = 2;

                        if (tieneComparacion)
                        {
                            worksheet.Cell(filaEncabezado, 3).Value = resultado.NombreEscenarioComparacion ?? R("LineaTiempo_ColComparacion");
                            worksheet.Cell(filaEncabezado, 4).Value = R("LineaTiempo_ColVariacion");
                            columnasValor.Add(Tuple.Create(3, resultado.Comparacion, ColorComparacion));
                            totalColumnas = 4;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < visibles.Count; i++)
                        {
                            worksheet.Cell(filaEncabezado, i + 2).Value = visibles[i].Serie.Indicador;
                            columnasValor.Add(Tuple.Create(i + 2, visibles[i].Serie.Puntos, ColoresSerie[visibles[i].Indice % ColoresSerie.Length]));
                        }
                        totalColumnas = visibles.Count + 1;
                    }

                    var headerRange = worksheet.Range(filaEncabezado, 1, filaEncabezado, totalColumnas);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = colorPrincipal;
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.WrapText = true;
                    worksheet.Range(filaEncabezado, 2, filaEncabezado, totalColumnas).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Unión ordenada de todos los períodos presentes en cualquiera de las series
                    // (normalmente coinciden, pero cada serie se consulta por separado).
                    var periodos = columnasValor
                        .SelectMany(c => c.Item2)
                        .Select(p => new { p.Anio, p.Mes, p.Etiqueta })
                        .Distinct()
                        .OrderBy(p => p.Anio).ThenBy(p => p.Mes)
                        .ToList();

                    var valoresGrafico = columnasValor.Select(c => new List<double?>()).ToList();
                    int fila = filaInicio;
                    foreach (var periodo in periodos)
                    {
                        worksheet.Cell(fila, 1).Value = periodo.Etiqueta;

                        for (int c = 0; c < columnasValor.Count; c++)
                        {
                            var punto = columnasValor[c].Item2.FirstOrDefault(p => p.Anio == periodo.Anio && p.Mes == periodo.Mes);
                            if (punto != null)
                                ExcelCellHelper.SetValue(worksheet.Cell(fila, columnasValor[c].Item1), punto.Valor);
                            valoresGrafico[c].Add(punto != null ? (double?)punto.Valor : null);
                        }

                        // Variación como FÓRMULA (misma cuenta que la tabla en pantalla: (principal - comparación) / |comparación|),
                        // así sigue siendo correcta si el usuario edita los valores.
                        if (tieneComparacion)
                        {
                            var celdaVar = worksheet.Cell(fila, 4);
                            celdaVar.FormulaA1 = $"IF(C{fila}=0,\"\",(B{fila}-C{fila})/ABS(C{fila}))";
                            celdaVar.Style.NumberFormat.Format = "0.0%;[Red]-0.0%";
                        }

                        fila++;
                    }

                    int filaFin = Math.Max(filaInicio, fila - 1);
                    var cuerpo = worksheet.Range(filaEncabezado, 1, filaFin, totalColumnas);
                    cuerpo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cuerpo.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    cuerpo.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 223, 236);
                    cuerpo.Style.Border.InsideBorderColor = XLColor.FromArgb(226, 223, 236);
                    foreach (var col in columnasValor)
                        worksheet.Range(filaInicio, col.Item1, filaFin, col.Item1).Style.NumberFormat.Format = formatoCelda;
                    worksheet.Range(filaInicio, 1, filaFin, 1).Style.Font.Bold = true;

                    worksheet.Column(1).Width = 16;
                    for (int c = 2; c <= totalColumnas; c++)
                        worksheet.Column(c).Width = 20;
                    worksheet.SheetView.Freeze(filaEncabezado, 0);

                    string nombreArchivo = $"LineaTiempoFinanciera_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }

                    // Gráfico nativo de Excel conectado a la tabla. Si algo falla se entrega el Excel
                    // sin gráfico: la descarga de los datos nunca se pierde por culpa del gráfico.
                    if (periodos.Count > 0)
                    {
                        try
                        {
                            var seriesGrafico = columnasValor.Select((c, i) => new SerieGraficoExcel
                            {
                                Nombre = worksheet.Cell(filaEncabezado, c.Item1).GetString(),
                                Columna = c.Item1,
                                ColorHex = c.Item3,
                                Valores = valoresGrafico[i]
                            }).ToList();

                            fileBytes = ExcelChartHelper.AgregarGrafico(fileBytes, nombreHoja, titulo, barras,
                                filaEncabezado, filaInicio, filaFin, 1, periodos.Select(p => p.Etiqueta).ToList(),
                                seriesGrafico, formatoEje, totalColumnas + 1, filaEncabezado - 1);
                        }
                        catch (Exception exGrafico)
                        {
                            AppLogger.Error(exGrafico, "InformeLineaTiempoController.ExportarExcel (gráfico)");
                        }
                    }

                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineaTiempoController.ExportarExcel");
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}
