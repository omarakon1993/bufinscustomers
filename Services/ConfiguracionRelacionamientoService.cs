using bufinscustomers.Models;
using OfficeOpenXml;
using OfficeOpenXml.Table;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;

namespace bufinscustomers.Services
{
    public class ConfiguracionRelacionamientoService : BaseService
    {
        /// <summary>
        /// Auto-descubre tablas REL_ existentes en la base de datos
        /// </summary>
        public List<TablaRelInfo> ObtenerTablasRelDisponibles()
        {
            var tablas = new List<TablaRelInfo>();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    string query = @"
                        SELECT t.TABLE_NAME,
                               (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME) AS TotalColumnas
                        FROM INFORMATION_SCHEMA.TABLES t
                        WHERE t.TABLE_TYPE = 'BASE TABLE'
                          AND t.TABLE_NAME LIKE 'REL_%'
                        ORDER BY t.TABLE_NAME";

                    using (SqlCommand cmd = new SqlCommand(query, cn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string nombreTabla = reader.GetString(0);
                            int totalColumnas = reader.GetInt32(1);

                            tablas.Add(new TablaRelInfo
                            {
                                NombreTabla = nombreTabla,
                                TotalColumnas = totalColumnas
                            });
                        }
                    }

                    // Obtener conteo de registros para cada tabla
                    foreach (var tabla in tablas)
                    {
                        try
                        {
                            string countQuery = $"SELECT COUNT(*) FROM dbo.[{tabla.NombreTabla}]";
                            using (SqlCommand cmd = new SqlCommand(countQuery, cn))
                            {
                                tabla.TotalRegistros = (int)cmd.ExecuteScalar();
                            }
                        }
                        catch
                        {
                            tabla.TotalRegistros = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener tablas REL_: {ex.Message}");
            }

            return tablas;
        }

        /// <summary>
        /// Obtiene los nombres de columna de una tabla REL_
        /// </summary>
        private List<string> ObtenerColumnasTabla(SqlConnection cn, string nombreTabla)
        {
            var columnas = new List<string>();
            string query = $"SELECT TOP 0 * FROM dbo.[{nombreTabla}]";

            using (SqlCommand cmd = new SqlCommand(query, cn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columnas.Add(reader.GetName(i));
                }
            }

            return columnas;
        }

        /// <summary>
        /// Carga datos desde un Excel a las tablas REL_ correspondientes
        /// Usa transaccion global: si una hoja falla, se revierte TODO
        /// </summary>
        public ResultadoCargaRelacionamiento CargarDatosDesdeExcel(ExcelPackage package)
        {
            var resultado = new ResultadoCargaRelacionamiento();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    // Obtener tablas REL_ existentes en BD
                    var tablasRelBD = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string queryTablas = @"
                        SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
                        WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME LIKE 'REL_%'";

                    using (SqlCommand cmd = new SqlCommand(queryTablas, cn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tablasRelBD.Add(reader.GetString(0));
                        }
                    }

                    // Clasificar hojas del Excel
                    var hojasValidas = new List<(ExcelWorksheet hoja, string nombreTabla)>();

                    foreach (var hoja in package.Workbook.Worksheets)
                    {
                        string nombreHoja = hoja.Name.Trim();

                        // Buscar coincidencia case-insensitive con tablas REL_
                        string tablaMatch = tablasRelBD.FirstOrDefault(t =>
                            t.Equals(nombreHoja, StringComparison.OrdinalIgnoreCase));

                        if (tablaMatch != null)
                        {
                            // Verificar que la hoja tenga datos
                            if (hoja.Dimension == null || hoja.Dimension.End.Row < 2)
                            {
                                resultado.DetalleHojas.Add(new DetalleCargaHoja
                                {
                                    NombreHoja = nombreHoja,
                                    NombreTabla = tablaMatch,
                                    Estado = "Ignorada",
                                    MensajeError = "La hoja está vacía o solo tiene encabezados"
                                });
                                resultado.TotalHojasIgnoradas++;
                                continue;
                            }

                            hojasValidas.Add((hoja, tablaMatch));
                        }
                        else
                        {
                            resultado.DetalleHojas.Add(new DetalleCargaHoja
                            {
                                NombreHoja = nombreHoja,
                                NombreTabla = "-",
                                Estado = "Ignorada",
                                MensajeError = nombreHoja.StartsWith("REL_", StringComparison.OrdinalIgnoreCase)
                                    ? $"No existe la tabla '{nombreHoja}' en la base de datos"
                                    : "No es una hoja Rel_"
                            });
                            resultado.TotalHojasIgnoradas++;
                        }
                    }

                    if (hojasValidas.Count == 0)
                    {
                        resultado.Exito = false;
                        resultado.Mensaje = "No se encontraron hojas v\u00e1lidas que coincidan con tablas Rel_ en la base de datos";
                        return resultado;
                    }

                    // Ejecutar carga dentro de una transaccion
                    using (SqlTransaction transaction = cn.BeginTransaction())
                    {
                        string tablaConError = null;
                        DetalleCargaHoja detalleConError = null;

                        try
                        {
                            foreach (var (hoja, nombreTabla) in hojasValidas)
                            {
                                tablaConError = nombreTabla;
                                var detalle = new DetalleCargaHoja
                                {
                                    NombreHoja = hoja.Name,
                                    NombreTabla = nombreTabla
                                };
                                detalleConError = detalle;

                                // Obtener columnas de la tabla SQL
                                var columnasSQL = ObtenerColumnasTablaTx(cn, transaction, nombreTabla);

                                // Leer datos del Excel
                                int totalCols = hoja.Dimension.End.Column;
                                int totalRows = hoja.Dimension.End.Row;

                                // Leer encabezados del Excel
                                var columnasExcel = new List<string>();
                                for (int col = 1; col <= totalCols; col++)
                                {
                                    string header = hoja.Cells[1, col].Text?.Trim();
                                    if (!string.IsNullOrWhiteSpace(header))
                                        columnasExcel.Add(header);
                                    else
                                        break;
                                }

                                int columnasValidas = columnasExcel.Count;
                                detalle.TotalColumnas = columnasValidas;

                                // Crear DataTable con las columnas del Excel usando el tipo correcto de SQL
                                var dt = new DataTable(nombreTabla);
                                foreach (var colName in columnasExcel)
                                {
                                    var matchSQL = columnasSQL.FirstOrDefault(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                                    Type colType = matchSQL.Name != null ? matchSQL.ClrType : typeof(string);
                                    var dc = dt.Columns.Add(colName, colType);
                                    dc.AllowDBNull = true;
                                }

                                // Leer filas convirtiendo al tipo correcto
                                for (int row = 2; row <= totalRows; row++)
                                {
                                    bool filaVacia = true;
                                    var dr = dt.NewRow();

                                    for (int col = 1; col <= columnasValidas; col++)
                                    {
                                        var valor = hoja.Cells[row, col].Text?.Trim();
                                        if (!string.IsNullOrWhiteSpace(valor)) filaVacia = false;
                                        dr[col - 1] = ConvertirValorExcel(valor, dt.Columns[col - 1].DataType);
                                    }

                                    if (!filaVacia)
                                        dt.Rows.Add(dr);
                                }

                                if (dt.Rows.Count == 0)
                                {
                                    detalle.Estado = "Ignorada";
                                    detalle.MensajeError = "La hoja no contiene filas de datos";
                                    resultado.DetalleHojas.Add(detalle);
                                    resultado.TotalHojasIgnoradas++;
                                    continue;
                                }

                                // DELETE todos los registros existentes
                                string deleteQuery = $"DELETE FROM dbo.[{nombreTabla}]";
                                using (SqlCommand deleteCmd = new SqlCommand(deleteQuery, cn, transaction))
                                {
                                    deleteCmd.ExecuteNonQuery();
                                }

                                // SqlBulkCopy para insertar datos nuevos
                                using (SqlBulkCopy bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, transaction))
                                {
                                    bulk.DestinationTableName = $"dbo.[{nombreTabla}]";
                                    bulk.BulkCopyTimeout = 120;

                                    // Mapear columnas del DataTable a columnas SQL
                                    foreach (var colExcel in columnasExcel)
                                    {
                                        // Solo mapear si la columna existe en SQL
                                        var matchSQL = columnasSQL.FirstOrDefault(c => c.Name.Equals(colExcel, StringComparison.OrdinalIgnoreCase));
                                        if (matchSQL.Name != null)
                                            bulk.ColumnMappings.Add(colExcel, matchSQL.Name);
                                    }

                                    bulk.WriteToServer(dt);
                                }

                                detalle.FilasInsertadas = dt.Rows.Count;
                                detalle.Estado = "Exitoso";
                                resultado.DetalleHojas.Add(detalle);
                                resultado.TotalHojasProcesadas++;
                                resultado.TotalFilasInsertadas += dt.Rows.Count;
                            }

                            // Todo salio bien: COMMIT
                            transaction.Commit();
                            resultado.Exito = true;
                            resultado.Mensaje = $"Carga exitosa: {resultado.TotalHojasProcesadas} tabla(s) actualizada(s) con {resultado.TotalFilasInsertadas} registros";
                        }
                        catch (Exception ex)
                        {
                            // Error: ROLLBACK de todo
                            try { transaction.Rollback(); } catch { }

                            resultado.Exito = false;
                            string prefijo = tablaConError != null ? $" [Tabla: {tablaConError}]" : "";
                            resultado.Mensaje = $"Error durante la carga. Se revirtieron todos los cambios:{prefijo} {ex.Message}";

                            // Registrar el detalle de la tabla que falló (si aún no estaba en la lista)
                            if (detalleConError != null && !resultado.DetalleHojas.Contains(detalleConError))
                            {
                                detalleConError.Estado = "Error";
                                detalleConError.MensajeError = ex.Message;
                                resultado.DetalleHojas.Add(detalleConError);
                            }

                            // Marcar hojas ya procesadas como revertidas
                            foreach (var detalle in resultado.DetalleHojas.Where(d => d.Estado == "Exitoso"))
                            {
                                detalle.Estado = "Revertido";
                                detalle.MensajeError = "Revertido por error en otra tabla";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                resultado.Exito = false;
                resultado.Mensaje = $"Error de conexión: {ex.Message}";
            }

            return resultado;
        }

        /// <summary>
        /// Obtiene columnas de una tabla dentro de una transaccion (nombre + tipo CLR)
        /// </summary>
        private List<(string Name, Type ClrType)> ObtenerColumnasTablaTx(SqlConnection cn, SqlTransaction tx, string nombreTabla)
        {
            var columnas = new List<(string Name, Type ClrType)>();
            string query = $"SELECT TOP 0 * FROM dbo.[{nombreTabla}]";

            using (SqlCommand cmd = new SqlCommand(query, cn, tx))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columnas.Add((reader.GetName(i), reader.GetFieldType(i)));
                }
            }

            return columnas;
        }

        private object ConvertirValorExcel(string valor, Type tipo)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return DBNull.Value;

            if (tipo == typeof(string))
                return valor;

            if (tipo == typeof(int) || tipo == typeof(short) || tipo == typeof(long))
            {
                // Quitar decimales si vienen del Excel (e.g. "5.0" → 5)
                string limpio = valor.Split('.')[0].Split(',')[0].Trim();
                return long.TryParse(limpio, out long v) ? (object)Convert.ChangeType(v, tipo) : DBNull.Value;
            }

            if (tipo == typeof(byte))
                return byte.TryParse(valor, out byte b) ? (object)b : DBNull.Value;

            if (tipo == typeof(decimal) || tipo == typeof(double) || tipo == typeof(float))
            {
                string normalizado = valor.Replace(',', '.');
                return decimal.TryParse(normalizado, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal d)
                    ? (object)Convert.ChangeType(d, tipo) : DBNull.Value;
            }

            if (tipo == typeof(bool))
                return valor == "1" || valor.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || valor.Equals("si", StringComparison.OrdinalIgnoreCase);

            if (tipo == typeof(DateTime))
                return DateTime.TryParse(valor, out DateTime dt) ? (object)dt : DBNull.Value;

            return valor;
        }

        /// <summary>
        /// Exporta todos los datos actuales de las tablas REL_ a un Excel profesional.
        /// Columnas money/smallmoney → formato moneda; resto → texto.
        /// </summary>
        public byte[] ExportarRelacionamientosExcel()
        {
            using (var package = new ExcelPackage())
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    var tablas = new List<TablaRelInfo>();
                    string queryTablas = @"
                        SELECT t.TABLE_NAME,
                               (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME) AS TotalColumnas
                        FROM INFORMATION_SCHEMA.TABLES t
                        WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_NAME LIKE 'REL_%'
                        ORDER BY t.TABLE_NAME";

                    using (SqlCommand cmd = new SqlCommand(queryTablas, cn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tablas.Add(new TablaRelInfo
                            {
                                NombreTabla = reader.GetString(0),
                                TotalColumnas = reader.GetInt32(1)
                            });
                        }
                    }

                    foreach (var tabla in tablas)
                    {
                        try
                        {
                            string countQuery = $"SELECT COUNT(*) FROM dbo.[{tabla.NombreTabla}]";
                            using (SqlCommand cmd = new SqlCommand(countQuery, cn))
                                tabla.TotalRegistros = (int)cmd.ExecuteScalar();
                        }
                        catch { tabla.TotalRegistros = 0; }
                    }

                    EscribirHojaIndice(package.Workbook.Worksheets.Add("Índice"), tablas);

                    foreach (var tabla in tablas)
                    {
                        var ws = package.Workbook.Worksheets.Add(tabla.NombreTabla);
                        EscribirHojaDatos(cn, ws, tabla.NombreTabla);
                    }
                }

                return package.GetAsByteArray();
            }
        }

        private void EscribirHojaIndice(ExcelWorksheet ws, List<TablaRelInfo> tablas)
        {
            var colorPrimario = System.Drawing.Color.FromArgb(99, 102, 241);
            var colorSecundario = System.Drawing.Color.FromArgb(59, 130, 246);
            var colorFilaPar = System.Drawing.Color.FromArgb(239, 246, 255);

            // Título
            ws.Cells[1, 1, 1, 4].Merge = true;
            ws.Cells[1, 1].Value = "Relacionamientos BUFINS";
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Size = 14;
            ws.Cells[1, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            ws.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(colorPrimario);
            ws.Cells[1, 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            ws.Cells[1, 1].Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
            ws.Row(1).Height = 28;

            // Subtítulo con fecha
            ws.Cells[2, 1, 2, 4].Merge = true;
            ws.Cells[2, 1].Value = $"Exportado el {DateTime.Now:dd/MM/yyyy} a las {DateTime.Now:HH:mm}  |  {tablas.Count} tabla(s) disponible(s)";
            ws.Cells[2, 1].Style.Font.Italic = true;
            ws.Cells[2, 1].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(107, 114, 128));
            ws.Cells[2, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

            // Encabezados de tabla
            string[] headers = { "Tabla en Base de Datos", "Registros", "Columnas", "Nombre en este archivo" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cells[4, c + 1];
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(colorSecundario);
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Medium;
                cell.Style.Border.Bottom.Color.SetColor(colorPrimario);
            }

            // Filas de datos
            for (int i = 0; i < tablas.Count; i++)
            {
                int fila = 5 + i;
                ws.Cells[fila, 1].Value = tablas[i].NombreTabla;
                ws.Cells[fila, 2].Value = tablas[i].TotalRegistros;
                ws.Cells[fila, 3].Value = tablas[i].TotalColumnas;
                ws.Cells[fila, 4].Value = tablas[i].NombreTabla;

                ws.Cells[fila, 2].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                ws.Cells[fila, 3].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

                if (i % 2 == 0)
                {
                    ws.Cells[fila, 1, fila, 4].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    ws.Cells[fila, 1, fila, 4].Style.Fill.BackgroundColor.SetColor(colorFilaPar);
                }

                // Borde inferior sutil
                ws.Cells[fila, 1, fila, 4].Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Hair;
                ws.Cells[fila, 1, fila, 4].Style.Border.Bottom.Color.SetColor(System.Drawing.Color.FromArgb(209, 213, 219));
            }

            ws.Column(1).Width = 38;
            ws.Column(2).Width = 14;
            ws.Column(3).Width = 12;
            ws.Column(4).Width = 38;
            ws.View.FreezePanes(5, 1);
        }

        private void EscribirHojaDatos(SqlConnection cn, ExcelWorksheet ws, string nombreTabla)
        {
            // 1. Obtener tipos SQL (para money) — reader cerrado antes de abrir el siguiente
            var tiposColumnas = ObtenerColumnasConTiposSql(cn, nombreTabla);
            var tipoPorNombre = tiposColumnas.ToDictionary(t => t.Name, t => t.SqlType, StringComparer.OrdinalIgnoreCase);

            // 2. Cargar todos los datos en memoria con DataAdapter (evita conflictos de reader abierto)
            var dt = new DataTable();
            using (SqlCommand cmd = new SqlCommand($"SELECT * FROM dbo.[{nombreTabla}]", cn))
            using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                adapter.Fill(dt);

            int totalCols = dt.Columns.Count;
            int totalRows = dt.Rows.Count;

            if (totalCols == 0) return;

            // 3. Identificar columnas numéricas upfront (una sola vez)
            var moneyColIndices = new HashSet<int>();
            for (int col = 0; col < totalCols; col++)
            {
                string colName = dt.Columns[col].ColumnName;
                if (tipoPorNombre.TryGetValue(colName, out string sqlType) &&
                    (sqlType == "money" || sqlType == "smallmoney" ||
                     sqlType == "decimal" || sqlType == "numeric" ||
                     sqlType == "float" || sqlType == "real"))
                    moneyColIndices.Add(col);
            }

            // 4. Escribir encabezados (solo valores, el estilo lo pone el ExcelTable)
            for (int col = 0; col < totalCols; col++)
                ws.Cells[1, col + 1].Value = dt.Columns[col].ColumnName;

            // 5. Escribir valores SIN operaciones de estilo por celda
            for (int row = 0; row < totalRows; row++)
            {
                for (int col = 0; col < totalCols; col++)
                {
                    var value = dt.Rows[row][col];
                    var cell = ws.Cells[row + 2, col + 1];

                    if (value == DBNull.Value || value == null)
                        cell.Value = "";
                    else if (moneyColIndices.Contains(col))
                        cell.Value = Convert.ToDouble(value);
                    else
                        cell.Value = value.ToString();
                }
            }

            // 6. Aplicar formato numérico por columna como operación de rango (una op por columna, no por celda)
            if (totalRows > 0)
            {
                for (int col = 0; col < totalCols; col++)
                {
                    ws.Cells[2, col + 1, totalRows + 1, col + 1].Style.Numberformat.Format =
                        moneyColIndices.Contains(col) ? "#,##0.00" : "@";
                }
            }

            // 7. ExcelTable con estilo nativo — alternado de filas, filtro y encabezado sin overhead de C#
            int lastRow = Math.Max(2, totalRows + 1);
            string safeName = "tbl_" + Regex.Replace(nombreTabla, "[^A-Za-z0-9]", "_");
            var tbl = ws.Tables.Add(ws.Cells[1, 1, lastRow, totalCols], safeName);
            tbl.TableStyle = TableStyles.Medium2;

            // 8. Anchos fijos (AutoFitColumns escanea todas las celdas y es extremadamente lento)
            for (int col = 1; col <= totalCols; col++)
                ws.Column(col).Width = 24;

            ws.View.FreezePanes(2, 1);
        }

        private List<(string Name, string SqlType)> ObtenerColumnasConTiposSql(SqlConnection cn, string nombreTabla)
        {
            var columnas = new List<(string Name, string SqlType)>();
            string query = @"
                SELECT COLUMN_NAME, DATA_TYPE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @TableName
                ORDER BY ORDINAL_POSITION";

            using (SqlCommand cmd = new SqlCommand(query, cn))
            {
                cmd.Parameters.AddWithValue("@TableName", nombreTabla);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        columnas.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            return columnas;
        }

        /// <summary>
        /// Genera un Excel plantilla con las hojas y columnas de las tablas REL_
        /// </summary>
        public byte[] GenerarPlantillaExcel()
        {
            using (var package = new ExcelPackage())
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    var tablas = ObtenerTablasRelDisponibles();

                    foreach (var tabla in tablas)
                    {
                        var ws = package.Workbook.Worksheets.Add(tabla.NombreTabla);

                        // Escribir encabezados
                        var columnas = ObtenerColumnasTabla(cn, tabla.NombreTabla);
                        for (int i = 0; i < columnas.Count; i++)
                        {
                            var cell = ws.Cells[1, i + 1];
                            cell.Value = columnas[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
                            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        }

                        ws.Cells[1, 1, 1, Math.Max(columnas.Count, 1)].AutoFilter = true;
                        ws.View.FreezePanes(2, 1);
                        ws.Cells.AutoFitColumns();
                    }
                }

                return package.GetAsByteArray();
            }
        }
    }
}
