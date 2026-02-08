using bufinscustomers.Models;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

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
                        try
                        {
                            foreach (var (hoja, nombreTabla) in hojasValidas)
                            {
                                var detalle = new DetalleCargaHoja
                                {
                                    NombreHoja = hoja.Name,
                                    NombreTabla = nombreTabla
                                };

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

                                // Crear DataTable con las columnas del Excel
                                var dt = new DataTable(nombreTabla);
                                foreach (var colName in columnasExcel)
                                {
                                    dt.Columns.Add(colName, typeof(string));
                                }

                                // Leer filas
                                for (int row = 2; row <= totalRows; row++)
                                {
                                    bool filaVacia = true;
                                    var dr = dt.NewRow();

                                    for (int col = 1; col <= columnasValidas; col++)
                                    {
                                        var valor = hoja.Cells[row, col].Text?.Trim();
                                        if (!string.IsNullOrWhiteSpace(valor)) filaVacia = false;
                                        dr[col - 1] = valor ?? "";
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
                                        if (columnasSQL.Any(c => c.Equals(colExcel, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            string colSQL = columnasSQL.First(c => c.Equals(colExcel, StringComparison.OrdinalIgnoreCase));
                                            bulk.ColumnMappings.Add(colExcel, colSQL);
                                        }
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
                            resultado.Mensaje = $"Error durante la carga. Se revirtieron todos los cambios: {ex.Message}";

                            // Marcar hojas no procesadas aun
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
        /// Obtiene columnas de una tabla dentro de una transaccion
        /// </summary>
        private List<string> ObtenerColumnasTablaTx(SqlConnection cn, SqlTransaction tx, string nombreTabla)
        {
            var columnas = new List<string>();
            string query = $"SELECT TOP 0 * FROM dbo.[{nombreTabla}]";

            using (SqlCommand cmd = new SqlCommand(query, cn, tx))
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
