using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace bufinscustomers.Helpers
{
    /// <summary>Una serie del gráfico: columna (1-based) de la hoja con sus valores + color de línea/barra.</summary>
    internal class SerieGraficoExcel
    {
        public string Nombre { get; set; }
        public int Columna { get; set; }
        public string ColorHex { get; set; }
        public List<double?> Valores { get; set; } = new List<double?>();
    }

    /// <summary>
    /// Agrega un gráfico NATIVO de Excel (línea o barras) a un .xlsx ya generado con ClosedXML.
    /// ClosedXML no sabe crear gráficos, así que el libro se reabre con DocumentFormat.OpenXml
    /// (ya es dependencia de ClosedXML) y se le inyecta un DrawingsPart + ChartPart. El gráfico
    /// referencia las celdas de la hoja: si el usuario edita un valor, el gráfico se actualiza.
    /// Los cachés (StringCache/NumberingCache) se llenan para que visores que no recalculan
    /// (vista previa, Excel online, LibreOffice) lo dibujen igual.
    /// </summary>
    internal static class ExcelChartHelper
    {
        private const string NsChart = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        private const string NsDrawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // Elementos del <worksheet> que por esquema van DESPUÉS de <drawing>.
        private static readonly HashSet<string> ElementosPosterioresADrawing = new HashSet<string>
        {
            "legacyDrawing", "legacyDrawingHF", "drawingHF", "picture", "oleObjects",
            "controls", "webPublishItems", "tableParts", "extLst"
        };

        /// <param name="filaInicio">Primera fila de datos (1-based), justo debajo de los encabezados.</param>
        /// <param name="filaFin">Última fila de datos (1-based).</param>
        /// <param name="columnaCategorias">Columna (1-based) con las etiquetas del eje X.</param>
        /// <param name="filaEncabezado">Fila (1-based) con el nombre de cada serie.</param>
        /// <param name="formatoEjeValores">Formato de número del eje Y (p. ej. <c>#,##0.0,,</c> para millones).</param>
        /// <param name="columnaAncla">Columna (0-based) donde empieza el gráfico.</param>
        /// <param name="filaAncla">Fila (0-based) donde empieza el gráfico.</param>
        public static byte[] AgregarGrafico(byte[] xlsx, string nombreHoja, string titulo, bool barras,
            int filaEncabezado, int filaInicio, int filaFin, int columnaCategorias, IList<string> categorias,
            IList<SerieGraficoExcel> series, string formatoEjeValores, int columnaAncla, int filaAncla)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(xlsx, 0, xlsx.Length);
                using (var doc = SpreadsheetDocument.Open(ms, true))
                {
                    var wbPart = doc.WorkbookPart;
                    var sheet = wbPart.Workbook.Descendants<S.Sheet>().FirstOrDefault(s => s.Name == nombreHoja);
                    if (sheet == null)
                        throw new InvalidOperationException("No existe la hoja '" + nombreHoja + "' en el libro.");

                    var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id);
                    var drawingsPart = wsPart.DrawingsPart;
                    if (drawingsPart == null)
                    {
                        drawingsPart = wsPart.AddNewPart<DrawingsPart>();
                        drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
                        drawingsPart.WorksheetDrawing.AddNamespaceDeclaration("xdr", "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
                        drawingsPart.WorksheetDrawing.AddNamespaceDeclaration("a", NsDrawing);
                        InsertarReferenciaDrawing(wsPart.Worksheet, new S.Drawing { Id = wsPart.GetIdOfPart(drawingsPart) });
                        wsPart.Worksheet.Save();
                    }

                    var chartPart = drawingsPart.AddNewPart<ChartPart>();
                    chartPart.ChartSpace = ConstruirChartSpace(nombreHoja, titulo, barras, filaEncabezado, filaInicio,
                        filaFin, columnaCategorias, categorias, series, formatoEjeValores);
                    chartPart.ChartSpace.Save();

                    int idForma = drawingsPart.WorksheetDrawing.Elements<Xdr.TwoCellAnchor>().Count() + 2;
                    drawingsPart.WorksheetDrawing.Append(ConstruirAncla(drawingsPart.GetIdOfPart(chartPart), idForma,
                        columnaAncla, filaAncla, columnaAncla + 10, filaAncla + 20));
                    drawingsPart.WorksheetDrawing.Save();
                }
                return ms.ToArray();
            }
        }

        private static void InsertarReferenciaDrawing(S.Worksheet worksheet, S.Drawing drawing)
        {
            var siguiente = worksheet.ChildElements.FirstOrDefault(e => ElementosPosterioresADrawing.Contains(e.LocalName));
            if (siguiente != null) worksheet.InsertBefore(drawing, siguiente);
            else worksheet.Append(drawing);
        }

        private static string Ref(string hoja, int col, int filaDesde, int filaHasta)
        {
            string letra = LetraColumna(col);
            string h = "'" + hoja.Replace("'", "''") + "'";
            return filaDesde == filaHasta
                ? h + "!$" + letra + "$" + filaDesde
                : h + "!$" + letra + "$" + filaDesde + ":$" + letra + "$" + filaHasta;
        }

        private static string LetraColumna(int col)
        {
            string s = "";
            while (col > 0) { int m = (col - 1) % 26; s = (char)('A' + m) + s; col = (col - 1) / 26; }
            return s;
        }

        private static C.ChartSpace ConstruirChartSpace(string hoja, string titulo, bool barras, int filaEncabezado,
            int filaInicio, int filaFin, int columnaCategorias, IList<string> categorias,
            IList<SerieGraficoExcel> series, string formatoEjeValores)
        {
            const uint idEjeCategorias = 48650112u;
            const uint idEjeValores = 48672768u;

            var chartSpace = new C.ChartSpace();
            chartSpace.AddNamespaceDeclaration("c", NsChart);
            chartSpace.AddNamespaceDeclaration("a", NsDrawing);
            chartSpace.AddNamespaceDeclaration("r", NsRel);
            chartSpace.Append(new C.RoundedCorners { Val = false });

            var chart = new C.Chart();
            if (!string.IsNullOrWhiteSpace(titulo))
            {
                chart.Append(ConstruirTitulo(titulo));
                chart.Append(new C.AutoTitleDeleted { Val = false });
            }
            else
            {
                chart.Append(new C.AutoTitleDeleted { Val = true });
            }

            string refCategorias = Ref(hoja, columnaCategorias, filaInicio, filaFin);
            var plotArea = new C.PlotArea(new C.Layout());

            DocumentFormat.OpenXml.OpenXmlCompositeElement grafico;
            if (barras)
            {
                grafico = new C.BarChart(
                    new C.BarDirection { Val = C.BarDirectionValues.Column },
                    new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
                    new C.VaryColors { Val = false });
            }
            else
            {
                grafico = new C.LineChart(
                    new C.Grouping { Val = C.GroupingValues.Standard },
                    new C.VaryColors { Val = false });
            }

            for (int i = 0; i < series.Count; i++)
            {
                var s = series[i];
                var tx = new C.SeriesText(new C.StringReference(
                    new C.Formula(Ref(hoja, s.Columna, filaEncabezado, filaEncabezado)),
                    CacheTexto(new[] { s.Nombre ?? "" })));
                var cat = new C.CategoryAxisData(new C.StringReference(new C.Formula(refCategorias), CacheTexto(categorias)));
                var val = new C.Values(new C.NumberReference(
                    new C.Formula(Ref(hoja, s.Columna, filaInicio, filaFin)), CacheNumeros(s.Valores)));

                if (barras)
                {
                    grafico.Append(new C.BarChartSeries(
                        new C.Index { Val = (uint)i },
                        new C.Order { Val = (uint)i },
                        tx,
                        new C.ChartShapeProperties(Relleno(s.ColorHex)),
                        new C.InvertIfNegative { Val = false },
                        cat,
                        val));
                }
                else
                {
                    grafico.Append(new C.LineChartSeries(
                        new C.Index { Val = (uint)i },
                        new C.Order { Val = (uint)i },
                        tx,
                        new C.ChartShapeProperties(new A.Outline(Relleno(s.ColorHex), new A.Round()) { Width = 28575 }),
                        new C.Marker(
                            new C.Symbol { Val = C.MarkerStyleValues.Circle },
                            new C.Size { Val = 5 },
                            new C.ChartShapeProperties(Relleno(s.ColorHex), new A.Outline(Relleno(s.ColorHex)))),
                        cat,
                        val,
                        new C.Smooth { Val = true }));
                }
            }

            if (barras)
            {
                grafico.Append(new C.GapWidth { Val = 80 });
            }
            else
            {
                grafico.Append(new C.ShowMarker { Val = true });
            }
            grafico.Append(new C.AxisId { Val = idEjeCategorias });
            grafico.Append(new C.AxisId { Val = idEjeValores });
            plotArea.Append(grafico);

            plotArea.Append(new C.CategoryAxis(
                new C.AxisId { Val = idEjeCategorias },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.Delete { Val = false },
                new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                new C.NumberingFormat { FormatCode = "General", SourceLinked = true },
                new C.MajorTickMark { Val = C.TickMarkValues.None },
                new C.MinorTickMark { Val = C.TickMarkValues.None },
                new C.TickLabelPosition { Val = C.TickLabelPositionValues.Low },
                new C.CrossingAxis { Val = idEjeValores },
                new C.Crosses { Val = C.CrossesValues.AutoZero },
                new C.AutoLabeled { Val = true },
                new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
                new C.LabelOffset { Val = 100 },
                new C.NoMultiLevelLabels { Val = false }));

            plotArea.Append(new C.ValueAxis(
                new C.AxisId { Val = idEjeValores },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.Delete { Val = false },
                new C.AxisPosition { Val = C.AxisPositionValues.Left },
                new C.MajorGridlines(new C.ChartShapeProperties(new A.Outline(Relleno("EEF0F6")))),
                new C.NumberingFormat { FormatCode = formatoEjeValores ?? "General", SourceLinked = false },
                new C.MajorTickMark { Val = C.TickMarkValues.None },
                new C.MinorTickMark { Val = C.TickMarkValues.None },
                new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
                new C.CrossingAxis { Val = idEjeCategorias },
                new C.Crosses { Val = C.CrossesValues.AutoZero },
                new C.CrossBetween { Val = C.CrossBetweenValues.Between }));

            chart.Append(plotArea);
            chart.Append(new C.Legend(
                new C.LegendPosition { Val = C.LegendPositionValues.Bottom },
                new C.Overlay { Val = false }));
            chart.Append(new C.PlotVisibleOnly { Val = true });
            chart.Append(new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Gap });

            chartSpace.Append(chart);
            return chartSpace;
        }

        private static A.SolidFill Relleno(string hex)
        {
            return new A.SolidFill(new A.RgbColorModelHex { Val = (hex ?? "6366F1").TrimStart('#').ToUpperInvariant() });
        }

        private static C.Title ConstruirTitulo(string titulo)
        {
            return new C.Title(
                new C.ChartText(new C.RichText(
                    new A.BodyProperties(),
                    new A.ListStyle(),
                    new A.Paragraph(
                        new A.ParagraphProperties(new A.DefaultRunProperties { FontSize = 1400, Bold = true }),
                        new A.Run(new A.RunProperties { FontSize = 1400, Bold = true }, new A.Text(titulo))))),
                new C.Overlay { Val = false });
        }

        private static C.StringCache CacheTexto(IList<string> textos)
        {
            var cache = new C.StringCache(new C.PointCount { Val = (uint)textos.Count });
            for (int i = 0; i < textos.Count; i++)
                cache.Append(new C.StringPoint(new C.NumericValue(textos[i] ?? "")) { Index = (uint)i });
            return cache;
        }

        private static C.NumberingCache CacheNumeros(IList<double?> valores)
        {
            var cache = new C.NumberingCache(new C.FormatCode("General"), new C.PointCount { Val = (uint)valores.Count });
            for (int i = 0; i < valores.Count; i++)
            {
                if (!valores[i].HasValue) continue;
                cache.Append(new C.NumericPoint(new C.NumericValue(valores[i].Value.ToString("R", CultureInfo.InvariantCulture))) { Index = (uint)i });
            }
            return cache;
        }

        private static Xdr.TwoCellAnchor ConstruirAncla(string idRelacionGrafico, int idForma, int colDesde, int filaDesde, int colHasta, int filaHasta)
        {
            var referencia = new C.ChartReference { Id = idRelacionGrafico };
            referencia.AddNamespaceDeclaration("c", NsChart);
            referencia.AddNamespaceDeclaration("r", NsRel);

            return new Xdr.TwoCellAnchor(
                new Xdr.FromMarker(
                    new Xdr.ColumnId(colDesde.ToString(CultureInfo.InvariantCulture)), new Xdr.ColumnOffset("0"),
                    new Xdr.RowId(filaDesde.ToString(CultureInfo.InvariantCulture)), new Xdr.RowOffset("0")),
                new Xdr.ToMarker(
                    new Xdr.ColumnId(colHasta.ToString(CultureInfo.InvariantCulture)), new Xdr.ColumnOffset("0"),
                    new Xdr.RowId(filaHasta.ToString(CultureInfo.InvariantCulture)), new Xdr.RowOffset("0")),
                new Xdr.GraphicFrame(
                    new Xdr.NonVisualGraphicFrameProperties(
                        new Xdr.NonVisualDrawingProperties { Id = (uint)idForma, Name = "Chart " + (idForma - 1) },
                        new Xdr.NonVisualGraphicFrameDrawingProperties()),
                    new Xdr.Transform(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = 0L, Cy = 0L }),
                    new A.Graphic(new A.GraphicData(referencia) { Uri = NsChart }))
                { Macro = "" },
                new Xdr.ClientData());
        }
    }
}
