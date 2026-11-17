using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using bufinscustomers.Models;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Hojas de Excel de las secciones «PYG en una hoja» y «¿Cuánta plata me queda?». Las usan el informe de PYG, el de Flujo de caja y la versión
    /// para comité, para que la misma sección exporte igual en los tres. Las cifras ya vienen calculadas (InformeEjecutivoService).
    /// </summary>
    public static class EjecutivoExcel
    {
        /// <summary>Lo que necesitan las hojas: textos traducidos, nombre de mes, y cómo abrir una hoja (con logo) y pintar un encabezado (con el tema).</summary>
        public sealed class Contexto
        {
            public Func<string, string> R;
            public Func<int, string> Mes;
            /// <summary>Escribe el título con logo (si hay) en la hoja y devuelve la primera fila libre.</summary>
            public Func<IXLWorksheet, string, int> Nueva;
            /// <summary>Pinta una fila de encabezado con el color principal del tema.</summary>
            public Action<IXLWorksheet, int, string[]> Cab;
        }

        internal static Contexto Crear(Func<string, string> r, Func<int, string> mes, string titulo, string nombreEmpresa, LogoExcel logo, TemaInforme tema)
        {
            int o = logo != null ? ExcelLogoHelper.FilasBloque : 0;
            return new Contexto
            {
                R = r, Mes = mes,
                Nueva = (ws, sub) =>
                {
                    if (logo != null) ExcelLogoHelper.EscribirTitulo(ws, titulo + " — " + nombreEmpresa, sub);
                    return 1 + o;
                },
                Cab = (ws, fila, textos) =>
                {
                    for (int c = 0; c < textos.Length; c++) ws.Cell(fila, c + 1).Value = textos[c];
                    var rg = ws.Range(fila, 1, fila, textos.Length);
                    rg.Style.Font.Bold = true; rg.Style.Font.FontColor = XLColor.White; rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                }
            };
        }

        public static string Millones(decimal v) { return (v / 1000000m).ToString("N1") + " M"; }

        public static string NombreTramo(Func<string, string> R, string codigo)
        {
            switch (codigo)
            {
                case "GeneracionOperativa": return R("FlujoCaja_FU_GeneracionOperativa");
                case "Otros": return R("Flujo_CajaCascada_Otros");
                default: return R("FlujoCaja_Casc_" + codigo);
            }
        }

        public static void HojaPyg(XLWorkbook wb, PygHojaReporte p, Contexto x)
        {
            var R = x.R;
            var ws = wb.Worksheets.Add(R("PH_ExcelHoja"));
            int fila = x.Nueva(ws, x.Mes(p.MesCorte) + " " + p.Anio);
            var enc = new List<string> { R("PH_ColConcepto") };
            for (int m = 1; m <= 12; m++) enc.Add(x.Mes(m).Substring(0, Math.Min(3, x.Mes(m).Length)));
            enc.AddRange(new[] { R("PH_ColAcum"), R("PH_ColAv"), R("PH_ColPpto"), R("PH_ColVarPpto"), R("PH_ColAnioAnt"), R("PH_ColVarAnio") });
            x.Cab(ws, fila, enc.ToArray());
            int filaEnc = fila; fila++;
            foreach (var f in p.Filas)
            {
                ws.Cell(fila, 1).Value = f.Descripcion;
                for (int m = 0; m < 12; m++) { ExcelCellHelper.SetValue(ws.Cell(fila, 2 + m), f.Meses[m]); ws.Cell(fila, 2 + m).Style.NumberFormat.Format = "#,##0"; }
                ExcelCellHelper.SetValue(ws.Cell(fila, 14), f.Acumulado); ws.Cell(fila, 14).Style.NumberFormat.Format = "#,##0";
                ExcelCellHelper.SetValue(ws.Cell(fila, 15), f.Av); ws.Cell(fila, 15).Style.NumberFormat.Format = "0.0%";
                ExcelCellHelper.SetValue(ws.Cell(fila, 16), p.HayPresupuesto ? (decimal?)f.AcumPresupuesto : null); ws.Cell(fila, 16).Style.NumberFormat.Format = "#,##0";
                ExcelCellHelper.SetValue(ws.Cell(fila, 17), f.VarPresupuesto); ws.Cell(fila, 17).Style.NumberFormat.Format = "0.0%";
                ExcelCellHelper.SetValue(ws.Cell(fila, 18), f.AcumAnioAnterior); ws.Cell(fila, 18).Style.NumberFormat.Format = "#,##0";
                ExcelCellHelper.SetValue(ws.Cell(fila, 19), f.VarAnioAnterior); ws.Cell(fila, 19).Style.NumberFormat.Format = "0.0%";
                if (f.EsSubtotal) ws.Range(fila, 1, fila, 19).Style.Font.Bold = true;
                fila++;
            }
            ws.Columns().AdjustToContents();
            ws.SheetView.Freeze(filaEnc, 1);
        }

        public static void HojaCaja(XLWorkbook wb, CajaDisponibleReporte c, Contexto x)
        {
            var R = x.R;
            var ws = wb.Worksheets.Add(R("CD_ExcelHoja"));
            int fila = x.Nueva(ws, x.Mes(c.MesCorte) + " " + c.Anio);
            x.Cab(ws, fila, new[] { R("CD_PageTitle"), R("EJ_Valor") }); fila++;
            var items = new List<KeyValuePair<string, decimal?>>
            {
                new KeyValuePair<string, decimal?>(R("CD_Inicio"), c.HayCajaInicial ? (decimal?)c.CajaInicial : null),
                new KeyValuePair<string, decimal?>(R("CD_Entradas"), c.TotalFuentes),
                new KeyValuePair<string, decimal?>(R("CD_Salidas"), c.TotalUsos),
                new KeyValuePair<string, decimal?>(string.Format(R("CD_HoyFmt"), x.Mes(c.MesCorte)), c.HayCajaCorte ? (decimal?)c.CajaCorte : null),
                new KeyValuePair<string, decimal?>(R("CD_Proy"), c.HayProyeccion ? (decimal?)c.CajaProyectada : null),
                new KeyValuePair<string, decimal?>(R("CD_Minimo"), c.HayMinimo ? (decimal?)c.CajaMinima : null)
            };
            foreach (var it in items) { ws.Cell(fila, 1).Value = it.Key; ExcelCellHelper.SetValue(ws.Cell(fila, 2), it.Value); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0"; fila++; }
            // Los mismos avisos que muestra la pantalla.
            if (!c.HayCajaInicial) { ws.Cell(fila, 1).Value = R("CD_SinBalance"); fila++; }
            else
            {
                if (c.HayProyeccion && c.MesesEnDeficit > 0)
                    ws.Cell(fila, 1).Value = string.Format(R("CD_EstadoDeficitFmt"), c.MesesEnDeficit, Millones(c.CajaMinima), x.Mes(c.MesMinimo));
                else if (c.HayProyeccion)
                    ws.Cell(fila, 1).Value = R("CD_EstadoOk") + " " + string.Format(R("CD_MinimoFmt"), Millones(c.CajaMinima), x.Mes(c.MesMinimo));
                if (c.HayProyeccion) fila++;
                if (!c.Cuadra) { ws.Cell(fila, 1).Value = R("CD_NoCuadra"); fila++; }
            }
            fila++;
            x.Cab(ws, fila, new[] { R("CD_TramosTitulo"), R("EJ_Valor"), R("EJ_Tipo") }); fila++;
            foreach (var t in c.Tramos)
            {
                ws.Cell(fila, 1).Value = NombreTramo(R, t.Codigo); ExcelCellHelper.SetValue(ws.Cell(fila, 2), t.Valor); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0";
                ws.Cell(fila, 3).Value = t.Tipo == "fuente" ? R("CD_Entrada") : R("CD_Salida"); fila++;
            }
            fila++;
            if (c.Meses.Count > 0)
            {
                x.Cab(ws, fila, new[] { R("CD_ColMes"), R("CD_ColFlujo"), R("CD_ColCaja"), R("CD_ColTipo") }); fila++;
                foreach (var m in c.Meses)
                {
                    ws.Cell(fila, 1).Value = x.Mes(m.Mes);
                    ExcelCellHelper.SetValue(ws.Cell(fila, 2), m.Flujo); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0";
                    ExcelCellHelper.SetValue(ws.Cell(fila, 3), m.CajaFin); ws.Cell(fila, 3).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(fila, 4).Value = m.Proyectado ? R("CD_Proyectado") : R("CD_Real"); fila++;
                }
            }
            ws.Columns().AdjustToContents();
        }
    }
}
