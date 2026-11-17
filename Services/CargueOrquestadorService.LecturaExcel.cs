using bufinscustomers.Helpers;
using bufinscustomers.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace bufinscustomers.Services
{
    // LecturaExcel: parte de CargueOrquestadorService (misma clase; solo se separa el codigo por responsabilidad).
    public partial class CargueOrquestadorService
    {
        private void ConvertirValoresVaciosAZero(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            try
            {
                var columnasMoneyDecimal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in esquema)
                {
                    string dataType = kv.Value.tipo.ToLower();
                    if (dataType == "money" || dataType == "smallmoney" ||
                        dataType == "decimal" || dataType == "numeric")
                    {
                        string colLower = kv.Key.ToLower();
                        if (!colLower.Contains("año") && !colLower.Contains("anio") &&
                            !colLower.Contains("year") && !colLower.Contains("mes") &&
                            !colLower.Contains("month") && !colLower.Contains("periodo") &&
                            !colLower.Contains("id"))
                        {
                            columnasMoneyDecimal.Add(kv.Key);
                        }
                    }
                }

                if (columnasMoneyDecimal.Count == 0)
                {
                    LogToFile("No se encontraron columnas de dinero para convertir");
                    return;
                }

                LogToFile($"Columnas de dinero encontradas: {string.Join(", ", columnasMoneyDecimal)}");

                int valoresConvertidos = 0;

                foreach (DataRow fila in tabla.Rows)
                {
                    for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                    {
                        string nombreColumna = tabla.Columns[colIndex].ColumnName;

                        if (columnasMoneyDecimal.Contains(nombreColumna))
                        {
                            object valor = fila[colIndex];
                            string valorStr = valor?.ToString()?.Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                fila[colIndex] = "0";
                                valoresConvertidos++;
                            }
                        }
                    }
                }

                LogToFile($"Se convirtieron {valoresConvertidos} valores vacíos a 0");
            }
            catch (Exception ex)
            {
                LogToFile($"Error al convertir valores vacíos: {ex.Message}");
            }
        }

        // Tablas Z_ que se usan en GuardarEnSQLServer pero no tienen mapeo Z_ → Ini_
        private static readonly string[] _tablasZSinMapeo = { "Z_TablaPUC" };

        // Nombres (normalizados) de las hojas que el cargue reconoce y procesa para una empresa:
        // las de su mapeo Z_ → Ini_ (estándar + personalizadas) más las Z_ que van directo a SQL.
        // Cualquier otra hoja del libro (p. ej. la hoja auxiliar "Datos" con las listas de
        // validación de la plantilla) se ignora por completo: no se valida ni se carga.
        private static HashSet<string> HojasCargueReconocidas(IReadOnlyDictionary<string, string> mapeoEmpresa) =>
            new HashSet<string>(mapeoEmpresa.Keys.Concat(_tablasZSinMapeo), StringComparer.OrdinalIgnoreCase);

        // ─── New helper methods ───────────────────────────────────────────────────

        private DataTable LeerHojaEnDataTable(IXLWorksheet hoja, DetalleCargaHojaExcel detalle, string filtroAnio = null)
        {
            int totalCols = hoja.LastColumnUsed()?.ColumnNumber() ?? 0;
            int totalRows = hoja.LastRowUsed()?.RowNumber() ?? 0;

            if (totalCols == 0 || totalRows == 0)
            {
                if (string.IsNullOrEmpty(detalle.NombreTabla)) detalle.NombreTabla = "-";
                detalle.Estado = "Ignorada";
                detalle.MensajeError = "La hoja está vacía";
                return null;
            }

            bool filaCabeceraValida = false;
            for (int col = 1; col <= totalCols; col++)
            {
                if (!string.IsNullOrWhiteSpace(hoja.Cell(1, col).GetFormattedString()))
                {
                    filaCabeceraValida = true;
                    break;
                }
            }
            if (!filaCabeceraValida)
            {
                if (string.IsNullOrEmpty(detalle.NombreTabla)) detalle.NombreTabla = "-";
                detalle.Estado = "Ignorada";
                detalle.MensajeError = "La hoja no tiene encabezados válidos";
                return null;
            }

            int columnasValidas = 0;
            for (int col = 1; col <= totalCols; col++)
            {
                if (!string.IsNullOrWhiteSpace(hoja.Cell(1, col).GetFormattedString().Trim()))
                    columnasValidas++;
                else
                    break;
            }
            if (columnasValidas == 0)
            {
                if (string.IsNullOrEmpty(detalle.NombreTabla)) detalle.NombreTabla = "-";
                detalle.Estado = "Ignorada";
                detalle.MensajeError = "No se encontraron columnas con encabezado";
                return null;
            }

            // Localizar columna de año para filtrar (si aplica)
            int colAnioIdx = -1;
            if (filtroAnio != null)
            {
                for (int col = 1; col <= columnasValidas; col++)
                {
                    var header = hoja.Cell(1, col).GetFormattedString()?.Trim() ?? "";
                    if (string.Equals(header, "Año", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(header, "Anio", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(header, "Year", StringComparison.OrdinalIgnoreCase))
                    {
                        colAnioIdx = col;
                        break;
                    }
                }
            }

            string nombreTabla = NormalizarNombre(hoja.Name);
            if (string.IsNullOrEmpty(detalle.NombreTabla))
                detalle.NombreTabla = nombreTabla;

            var dt = new DataTable(nombreTabla);
            for (int col = 1; col <= columnasValidas; col++)
                dt.Columns.Add(hoja.Cell(1, col).GetFormattedString().Trim(), typeof(object));

            int filasVaciasConsecutivas = 0;
            int filasIgnoradasPorAnio = 0;
            for (int row = 2; row <= totalRows; row++)
            {
                bool filaVacia = true;
                var dr = dt.NewRow();
                for (int col = 1; col <= columnasValidas; col++)
                {
                    var cellValue = hoja.Cell(row, col).Value;
                    object drVal;
                    if (cellValue.IsNumber)
                    {
                        // Celda numérica: guardar como double nativo — sin conversión a texto
                        // para evitar toda ambigüedad de separadores decimales/miles
                        drVal = cellValue.GetNumber();
                        filaVacia = false; // cualquier número (incluso cero) es dato real
                    }
                    else
                    {
                        string valorStr = cellValue.IsBlank ? "" : cellValue.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(valorStr)) filaVacia = false;
                        drVal = (object)valorStr;
                    }
                    dr[col - 1] = drVal;
                }

                if (filaVacia)
                {
                    filasVaciasConsecutivas++;
                    if (filasVaciasConsecutivas >= 5) break;
                    continue;
                }

                // Filtrar por año seleccionado: filas con otro año se omiten silenciosamente
                if (filtroAnio != null && colAnioIdx != -1)
                {
                    var valorAnio = dr[colAnioIdx - 1]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(valorAnio) && valorAnio != filtroAnio)
                    {
                        filasIgnoradasPorAnio++;
                        continue;
                    }
                }

                filasVaciasConsecutivas = 0;
                dt.Rows.Add(dr);
            }

            if (filasIgnoradasPorAnio > 0)
                LogToFile($"Hoja '{hoja.Name}': {filasIgnoradasPorAnio} fila(s) omitidas por año distinto a {filtroAnio}");

            if (dt.Rows.Count == 0)
            {
                detalle.Estado = "Ignorada";
                detalle.MensajeError = filtroAnio != null
                    ? $"No hay filas con año {filtroAnio} en esta hoja"
                    : "La hoja no contiene filas de datos";
                return null;
            }

            return dt;
        }

        private string ValidarValoresNumericos(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            foreach (DataColumn col in tabla.Columns)
            {
                if (!esquema.TryGetValue(col.ColumnName, out var info)) continue;
                string tipo = info.tipo.ToLower();
                bool esNumerico = tipo == "money" || tipo == "smallmoney" || tipo == "decimal"
                               || tipo == "numeric" || tipo == "float"   || tipo == "real"
                               || tipo == "int"    || tipo == "bigint"   || tipo == "smallint"
                               || tipo == "tinyint";
                if (!esNumerico) continue;

                string colLower = col.ColumnName.ToLower();
                if (colLower.Contains("año") || colLower.Contains("anio") || colLower.Contains("year")
                    || colLower.Contains("mes") || colLower.Contains("month") || colLower.Contains("id"))
                    continue;

                for (int r = 0; r < tabla.Rows.Count; r++)
                {
                    object val = tabla.Rows[r][col];
                    if (val is string sVal && !string.IsNullOrWhiteSpace(sVal))
                    {
                        string razon = CargueConversion.DetectarErrorNumerico(sVal);
                        if (razon != null)
                            return $"Fila {r + 2}, Columna '{col.ColumnName}': {razon}. " +
                                   $"Revise la hoja completa por errores similares antes de reintentar.";
                    }
                }
            }
            return null;
        }

        private void SanitizarColumnasNumericas(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            foreach (DataRow fila in tabla.Rows)
            {
                for (int i = 0; i < tabla.Columns.Count; i++)
                {
                    string colName = tabla.Columns[i].ColumnName;
                    if (!esquema.ContainsKey(colName)) continue;
                    string tipo = esquema[colName].tipo.ToLower();
                    if (tipo != "money" && tipo != "smallmoney" && tipo != "decimal" &&
                        tipo != "numeric" && tipo != "float" && tipo != "real") continue;
                    string colLower = colName.ToLower();
                    if (colLower.Contains("año") || colLower.Contains("anio") || colLower.Contains("year") ||
                        colLower.Contains("mes") || colLower.Contains("month") || colLower.Contains("id"))
                        continue;
                    // Guardar como decimal nativo: SqlBulkCopy lo mapea directamente
                    fila[i] = (object)CargueConversion.ExtraerDecimal(fila[i]);
                }
            }
        }
    }
}
