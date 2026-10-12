using bufinscustomers.Helpers;
using bufinscustomers.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Hosting;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Arma la plantilla Excel de cargue para un conjunto de hojas (las de una empresa, o las que eligió
    /// un Super Admin), sin archivos por empresa:
    ///   1. Parte de la plantilla base (Assets/Plantillas/PlantillaBUFINS.xlsx): conserva formato,
    ///      validaciones y la hoja auxiliar "Datos".
    ///   2. Quita las hojas del catálogo que no van (p. ej. una estándar desactivada o vuelta personalizada).
    ///   3. Agrega las hojas que la base no trae (personalizadas o nuevas del gestor), con los encabezados
    ///      = columnas de negocio de su tabla Ini_ en orden físico.
    ///   4. Ordena las hojas según el catálogo (Orden); las auxiliares quedan al final.
    /// </summary>
    public class PlantillaCargueService : BaseService
    {
        /// <summary>Plantilla generada como bytes (.xlsx).</summary>
        public byte[] GenerarBytes(IReadOnlyList<HojaCargue> hojas)
        {
            using (var wb = GenerarLibro(hojas))
            using (var ms = new MemoryStream())
            {
                wb.SaveAs(ms);
                return ms.ToArray();
            }
        }

        /// <summary>Plantilla generada en memoria (quien la recibe la debe liberar).</summary>
        public XLWorkbook GenerarLibro(IReadOnlyList<HojaCargue> hojas)
        {
            hojas = (hojas ?? new List<HojaCargue>())
                .OrderBy(h => h.Orden).ThenBy(h => h.NombreHoja, StringComparer.OrdinalIgnoreCase).ToList();

            string rutaBase = HostingEnvironment.MapPath(TablasCargueHelper.PlantillaEstandar);
            if (string.IsNullOrEmpty(rutaBase) || !File.Exists(rutaBase))
                throw new FileNotFoundException("No se encontró la plantilla base de cargue.", TablasCargueHelper.PlantillaEstandar);

            var wb = new XLWorkbook(rutaBase);
            try
            {
                var deseadas = new HashSet<string>(hojas.Select(h => h.NombreHoja), StringComparer.OrdinalIgnoreCase);
                var delCatalogo = new HashSet<string>(CargueHojasCacheHelper.Todas().Select(h => h.NombreHoja), StringComparer.OrdinalIgnoreCase);

                // 2. Quitar las hojas del catálogo (o hojas Z_ sueltas) que no van en esta plantilla.
                foreach (var ws in wb.Worksheets.ToList())
                {
                    bool esHojaCargue = delCatalogo.Contains(ws.Name) || ws.Name.StartsWith("Z_", StringComparison.OrdinalIgnoreCase);
                    if (esHojaCargue && !deseadas.Contains(ws.Name) && wb.Worksheets.Count > 1)
                        ws.Delete();
                }

                // Estilo del encabezado: el de la primera hoja Z_ que quedó (mismo look en las agregadas).
                var hojaModelo = wb.Worksheets.FirstOrDefault(w => w.Name.StartsWith("Z_", StringComparison.OrdinalIgnoreCase));

                // 3. Agregar las que la base no trae.
                var servicio = new CargueHojasService();
                foreach (var h in hojas)
                {
                    if (wb.Worksheets.Any(w => w.Name.Equals(h.NombreHoja, StringComparison.OrdinalIgnoreCase))) continue;

                    var columnas = servicio.ObtenerColumnas(h.TablaIni).Where(c => !c.EsSistema).ToList();
                    var ws = wb.Worksheets.Add(h.NombreHoja);
                    for (int i = 0; i < columnas.Count; i++)
                    {
                        var cell = ws.Cell(1, i + 1);
                        cell.Value = columnas[i].Nombre;
                        if (hojaModelo != null)
                            cell.Style = hojaModelo.Cell(1, 1).Style;
                        else
                        {
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                            cell.Style.Font.FontColor = XLColor.White;
                        }

                        string tipo = columnas[i].Tipo;
                        var rango = ws.Range(2, i + 1, 5000, i + 1);
                        if (tipo == "decimal" || tipo == "money") rango.Style.NumberFormat.Format = "#,##0.00";
                        else if (tipo == "int" || tipo == "bigint") rango.Style.NumberFormat.Format = "0";
                        else if (tipo == "date") rango.Style.NumberFormat.Format = "yyyy-mm-dd";
                        else if (tipo == "datetime") rango.Style.NumberFormat.Format = "yyyy-mm-dd hh:mm";

                        ws.Column(i + 1).Width = Math.Max(14, Math.Min(40, columnas[i].Nombre.Length + 6));
                    }
                    ws.SheetView.Freeze(1, 0);
                }

                // 4. Orden: hojas del catálogo por Orden, luego las auxiliares (p. ej. "Datos") en su orden actual.
                int pos = 1;
                foreach (var h in hojas)
                {
                    var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(h.NombreHoja, StringComparison.OrdinalIgnoreCase));
                    if (ws != null) ws.Position = pos++;
                }
                // Abrir en la primera hoja (la activa de la base pudo haberse quitado) sin hojas agrupadas.
                foreach (var w in wb.Worksheets) w.TabSelected = false;
                if (wb.Worksheets.Count > 0)
                {
                    wb.Worksheet(1).TabActive = true;
                    wb.Worksheet(1).TabSelected = true;
                }

                return wb;
            }
            catch
            {
                wb.Dispose();
                throw;
            }
        }
    }
}
