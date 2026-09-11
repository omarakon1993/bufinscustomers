using bufinscustomers.Helpers;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Hosting;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Genera la plantilla de cargue BUFINS (las mismas 9 hojas Z_) pero rellena con los
    /// datos que hoy están en las tablas Ini_ para una empresa y uno o varios años.
    ///
    /// - Los encabezados se leen de la plantilla real
    ///   (Assets/Plantillas/PlantillaBUFINS.xlsx) para que nombre / orden / mayúsculas calcen exactos.
    /// - Los valores se emparejan POR NOMBRE de columna contra la tabla Ini_ destino
    ///   (mapa hoja↔tabla en <see cref="TablasCargueHelper.MapeoZaIni"/>).
    /// - Predicado de lectura idéntico al de HistorialVersionesCarguesService, sin distinguir
    ///   modo: se trae todo lo del año (WHERE IdEmpresa_Log = @e AND [Año] IN (...)).
    /// </summary>
    public class PlantillaConDatosService : BaseService
    {
        private const string RutaPlantilla = "~/Assets/Plantillas/PlantillaBUFINS.xlsx";

        /// <summary>Columnas de bookkeeping de las tablas Ini_ que nunca se exportan.</summary>
        private static readonly HashSet<string> _columnasExcluidas =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Llave", "IdEmpresa_Log", "IdUsuarioCargue_Log", "FechaCargue_Log",
                "Historico_Log", "IdUsuarioEjecucion_Log", "FechaEjecucion_Log", "Observacion_Log"
            };

        /// <summary>Columnas de valores que se exportan con formato numérico (#,##0.00).</summary>
        private static readonly HashSet<string> _columnasNumericas =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SaldoAnterior", "Debito", "Credito", "NuevoSaldo", "Valor", "Saldo"
            };

        /// <summary>Orden preferido de las filas exportadas; se aplican solo las columnas que existan en cada tabla Ini_.</summary>
        private static readonly string[] _ordenPreferido = { "Año", "Mes", "Cuenta", "Empresa", "Pais" };

        private const string FormatoNumero = "#,##0.00";

        public class AnioConDatos
        {
            public int Anio { get; set; }
            public int Registros { get; set; }
        }

        /// <summary>
        /// Años con datos cargados para la empresa, con el total de filas (suma de las 9 tablas Ini_),
        /// ordenados de más reciente a más antiguo.
        /// </summary>
        public List<AnioConDatos> ObtenerAniosConDatos(int idEmpresa)
        {
            var acumulado = new Dictionary<int, int>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                foreach (var tabla in TablasCargueHelper.TablasIni)
                {
                    using (var cmd = new SqlCommand(
                        $"SELECT [Año] AS Anio, COUNT(*) AS Total FROM dbo.[{tabla}] " +
                        "WHERE IdEmpresa_Log = @IdEmpresa AND [Año] IS NOT NULL GROUP BY [Año]", cn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                if (r.IsDBNull(0)) continue;
                                if (!int.TryParse(Convert.ToString(r["Anio"]), out int anio)) continue;
                                int total = Convert.ToInt32(r["Total"]);
                                acumulado[anio] = (acumulado.TryGetValue(anio, out int prev) ? prev : 0) + total;
                            }
                        }
                    }
                }
            }

            return acumulado
                .Select(kv => new AnioConDatos { Anio = kv.Key, Registros = kv.Value })
                .OrderByDescending(a => a.Anio)
                .ToList();
        }

        /// <summary>
        /// Construye el Excel con la estructura de la plantilla BUFINS y las filas de las tablas Ini_
        /// para la empresa y los años indicados. <paramref name="totalFilas"/> devuelve el total de
        /// filas escritas (0 = no había datos para esos años).
        /// </summary>
        public byte[] GenerarExcel(int idEmpresa, List<int> anios, out int totalFilas)
        {
            totalFilas = 0;

            if (anios == null || anios.Count == 0)
                throw new ArgumentException("Debe indicar al menos un año.", nameof(anios));

            string rutaFisica = HostingEnvironment.MapPath(RutaPlantilla);
            if (string.IsNullOrEmpty(rutaFisica) || !File.Exists(rutaFisica))
                throw new FileNotFoundException("No se encontró la plantilla BUFINS.", RutaPlantilla);

            string inAnios = string.Join(",", anios.Select((_, i) => "@a" + i));

            using (var plantilla = new XLWorkbook(rutaFisica))
            using (var wb = new XLWorkbook())
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                foreach (var par in TablasCargueHelper.MapeoZaIni)
                {
                    string hoja = par.Key;        // Z_...
                    string tablaIni = par.Value;  // Ini_...

                    // 1. Encabezados = fila 1 de la plantilla real (hasta la primera celda vacía).
                    var headers = LeerEncabezados(plantilla, hoja);

                    // 2. Columnas reales de la tabla Ini_.
                    var columnasIni = LeerColumnas(cn, tablaIni);

                    // 3. Columnas exportables: encabezado de plantilla que exista en Ini_ y no sea bookkeeping.
                    var colsExport = headers
                        .Where(h => columnasIni.Contains(h) && !_columnasExcluidas.Contains(h))
                        .ToList();

                    var ws = wb.Worksheets.Add(hoja);

                    // Encabezados: se escriben TODOS los de la plantilla (aunque alguno no tenga datos).
                    for (int i = 0; i < headers.Count; i++)
                    {
                        var cell = ws.Cell(1, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                        cell.Style.Font.FontColor = XLColor.White;
                    }

                    // 4. Datos.
                    if (colsExport.Count > 0)
                    {
                        var dt = new DataTable();
                        string listaCols = string.Join(", ", colsExport.Select(c => "[" + c + "]"));

                        var colsOrden = _ordenPreferido
                            .Where(columnasIni.Contains)
                            .Select(c => (c == "Año" || c == "Mes")
                                ? $"TRY_CONVERT(int, [{c}])"   // ordena numéricamente aunque la columna sea texto
                                : "[" + c + "]")
                            .ToList();
                        string orderBy = colsOrden.Count > 0
                            ? " ORDER BY " + string.Join(", ", colsOrden)
                            : "";

                        using (var cmd = new SqlCommand(
                            $"SELECT {listaCols} FROM dbo.[{tablaIni}] " +
                            $"WHERE IdEmpresa_Log = @IdEmpresa AND [Año] IN ({inAnios}){orderBy}", cn))
                        {
                            cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                            for (int i = 0; i < anios.Count; i++)
                                cmd.Parameters.AddWithValue("@a" + i, anios[i]);

                            using (var da = new SqlDataAdapter(cmd))
                                da.Fill(dt);
                        }

                        for (int r = 0; r < dt.Rows.Count; r++)
                        {
                            var fila = dt.Rows[r];
                            for (int c = 0; c < headers.Count; c++)
                            {
                                if (!dt.Columns.Contains(headers[c])) continue;
                                ExcelCellHelper.SetValue(ws.Cell(r + 2, c + 1), fila[headers[c]]);
                            }
                        }

                        // Formato numérico para las columnas de valores.
                        if (dt.Rows.Count > 0)
                        {
                            for (int c = 0; c < headers.Count; c++)
                            {
                                if (!_columnasNumericas.Contains(headers[c])) continue;
                                ws.Range(2, c + 1, dt.Rows.Count + 1, c + 1)
                                    .Style.NumberFormat.Format = FormatoNumero;
                            }
                        }

                        totalFilas += dt.Rows.Count;
                    }

                    for (int c = 1; c <= Math.Max(headers.Count, 1); c++)
                        ws.Column(c).Width = 22;
                    ws.SheetView.Freeze(1, 0);
                }

                // Hoja auxiliar "Datos" (listas de validación) — copiada tal cual si existe.
                try
                {
                    var wsDatos = plantilla.Worksheets.FirstOrDefault(w =>
                        string.Equals(w.Name, "Datos", StringComparison.OrdinalIgnoreCase));
                    if (wsDatos != null && !wb.Worksheets.Contains("Datos"))
                        wsDatos.CopyTo(wb, "Datos");
                }
                catch { /* la hoja auxiliar es opcional, no rompe la exportación */ }

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static List<string> LeerEncabezados(XLWorkbook plantilla, string hoja)
        {
            var headers = new List<string>();

            var ws = plantilla.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, hoja, StringComparison.OrdinalIgnoreCase));
            if (ws == null) return headers;

            int col = 1;
            while (true)
            {
                string val = ws.Cell(1, col).GetString();
                if (string.IsNullOrWhiteSpace(val)) break;
                headers.Add(val.Trim());
                col++;
            }
            return headers;
        }

        private static HashSet<string> LeerColumnas(SqlConnection cn, string tabla)
        {
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SqlCommand(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t", cn))
            {
                cmd.Parameters.AddWithValue("@t", tabla);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        cols.Add(r.GetString(0));
            }
            return cols;
        }
    }
}
