using bufinscustomers.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Servicio para carga y administración de la tabla OrdenVariables (Variables PBI)
    /// </summary>
    public class ConfiguracionVariablesPBIService : BaseService
    {
        // Mapeo: encabezado Excel → columna BD
        private static readonly Dictionary<string, string> MapeoColumnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Nombre tabla",               "NombreTabla" },
            { "Variable",                   "Variable" },
            { "Orden variable",             "OrdenVariable" },
            { "Subtotal variable",          "SubtotalVariable" },
            { "Variable padre",             "VariablePadre" },
            { "Variable indicador",         "VariableIndicador" },
            { "Clase variable",             "ClaseVariable" },
            { "Agrupacion KEY",             "AgrupacionKEY" },
            { "Agrupacion KEY ABR",         "AgrupacionKEYABR" },
            { "Agrupacion KEY Orden",       "AgrupacionKEYOrden" },
            { "Variable padre real",        "VariablePadreReal" },
            { "Variable padre abr",         "VariablePadreAbr" },
            { "Variable padre real Orden",  "VariablePadreRealOrden" },
        };

        // Columnas numéricas (para conversión)
        private static readonly HashSet<string> ColumnasNumericas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OrdenVariable", "SubtotalVariable", "AgrupacionKEYOrden", "VariablePadreRealOrden"
        };

        private readonly VariablesPBIService _lecturaService = new VariablesPBIService();

        /// <summary>
        /// Obtiene el estado actual de la tabla OrdenVariables
        /// </summary>
        public EstadoVariablesPBI ObtenerEstadoActual()
        {
            return _lecturaService.ObtenerEstadoActual();
        }

        /// <summary>
        /// Carga los datos del Excel a la tabla OrdenVariables.
        /// Primero compara con BD para generar el diff, luego reemplaza con transacción.
        /// </summary>
        public ResultadoCargaVariablesPBI CargarDesdeExcel(ExcelPackage package, string usuarioNombre)
        {
            var resultado = new ResultadoCargaVariablesPBI();

            // Leer datos del Excel (primera hoja con datos)
            var datosExcel = LeerExcel(package);

            if (datosExcel == null || datosExcel.Count == 0)
            {
                resultado.Exito = false;
                resultado.Mensaje = "El archivo Excel no contiene datos válidos. Verifique que la hoja tenga encabezados y filas de datos.";
                return resultado;
            }

            // Generar diff antes de reemplazar
            var datosActuales = _lecturaService.ObtenerTodos();
            resultado.Comparacion = GenerarComparacion(datosActuales, datosExcel);
            resultado.FilasEliminadasPrevias = datosActuales.Count;

            // Insertar en BD dentro de transacción
            try
            {
                DateTime fechaCarga = DateTime.Now;

                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    using (SqlTransaction tx = cn.BeginTransaction())
                    {
                        try
                        {
                            // Eliminar todos los registros actuales
                            using (SqlCommand del = new SqlCommand("DELETE FROM OrdenVariables", cn, tx))
                                del.ExecuteNonQuery();

                            // Construir DataTable para bulk insert
                            var dt = ConstruirDataTable(datosExcel, usuarioNombre, fechaCarga);

                            using (SqlBulkCopy bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, tx))
                            {
                                bulk.DestinationTableName = "dbo.OrdenVariables";
                                bulk.BulkCopyTimeout = 120;

                                // Mapear columnas del DataTable a columnas SQL
                                foreach (DataColumn col in dt.Columns)
                                    bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

                                bulk.WriteToServer(dt);
                            }

                            tx.Commit();
                            resultado.Exito = true;
                            resultado.FilasInsertadas = dt.Rows.Count;
                            resultado.Mensaje = $"Carga exitosa: {dt.Rows.Count} registros actualizados en OrdenVariables.";
                        }
                        catch (Exception ex)
                        {
                            try { tx.Rollback(); } catch { }
                            resultado.Exito = false;
                            resultado.Mensaje = $"Error durante la carga. Se revirtieron los cambios: {ex.Message}";
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
        /// Genera un Excel plantilla con los encabezados de OrdenVariables
        /// </summary>
        public byte[] GenerarPlantillaExcel()
        {
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("OrdenVariables");
                var columnas = new[]
                {
                    "Nombre tabla", "Variable", "Orden variable", "Subtotal variable",
                    "Variable padre", "Variable indicador", "Clase variable",
                    "Agrupacion KEY", "Agrupacion KEY ABR", "Agrupacion KEY Orden",
                    "Variable padre real", "Variable padre abr", "Variable padre real Orden"
                };

                for (int i = 0; i < columnas.Length; i++)
                {
                    var cell = ws.Cells[1, i + 1];
                    cell.Value = columnas[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(99, 102, 241));
                    cell.Style.Font.Color.SetColor(Color.White);
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                ws.Cells[1, 1, 1, columnas.Length].AutoFilter = true;
                ws.View.FreezePanes(2, 1);
                ws.Cells.AutoFitColumns();

                return package.GetAsByteArray();
            }
        }

        // ─── Métodos privados ────────────────────────────────────────────────────

        /// <summary>
        /// Lee la primera hoja de datos del Excel y retorna lista de diccionarios col→valor (con nombres BD)
        /// </summary>
        private List<Dictionary<string, string>> LeerExcel(ExcelPackage package)
        {
            var datos = new List<Dictionary<string, string>>();

            if (package.Workbook.Worksheets.Count == 0)
                return datos;

            // Usar primera hoja que tenga datos
            ExcelWorksheet hoja = null;
            foreach (var ws in package.Workbook.Worksheets)
            {
                if (ws.Dimension != null && ws.Dimension.End.Row > 1)
                {
                    hoja = ws;
                    break;
                }
            }

            if (hoja == null)
                return datos;

            int totalCols = hoja.Dimension.End.Column;
            int totalRows = hoja.Dimension.End.Row;

            // Leer encabezados y mapear a nombres BD
            var mapeoIndice = new Dictionary<int, string>(); // indice columna Excel → nombre columna BD
            for (int col = 1; col <= totalCols; col++)
            {
                string header = hoja.Cells[1, col].Text?.Trim();
                if (string.IsNullOrWhiteSpace(header)) continue;

                if (MapeoColumnas.TryGetValue(header, out string colBD))
                    mapeoIndice[col] = colBD;
            }

            if (mapeoIndice.Count == 0)
                return datos;

            // Verificar que existan las columnas obligatorias
            bool tieneNombreTabla = mapeoIndice.ContainsValue("NombreTabla");
            bool tieneVariable = mapeoIndice.ContainsValue("Variable");

            if (!tieneNombreTabla || !tieneVariable)
                return datos;

            // Leer filas
            int filasVaciasConsecutivas = 0;
            for (int row = 2; row <= totalRows; row++)
            {
                bool filaVacia = true;
                var registro = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in mapeoIndice)
                {
                    string valor = hoja.Cells[row, kv.Key].Text?.Trim() ?? "";
                    registro[kv.Value] = valor;
                    if (!string.IsNullOrWhiteSpace(valor)) filaVacia = false;
                }

                if (filaVacia)
                {
                    filasVaciasConsecutivas++;
                    if (filasVaciasConsecutivas >= 5) break;
                    continue;
                }

                filasVaciasConsecutivas = 0;

                // Solo agregar si tiene NombreTabla y Variable
                if (!string.IsNullOrWhiteSpace(GetVal(registro,"NombreTabla")) &&
                    !string.IsNullOrWhiteSpace(GetVal(registro,"Variable")))
                {
                    datos.Add(registro);
                }
            }

            return datos;
        }

        /// <summary>
        /// Construye el DataTable para SqlBulkCopy con todos los registros del Excel
        /// </summary>
        private DataTable ConstruirDataTable(List<Dictionary<string, string>> datos, string usuarioNombre, DateTime fechaCarga)
        {
            var dt = new DataTable("OrdenVariables");
            dt.Columns.Add("NombreTabla",         typeof(string));
            dt.Columns.Add("Variable",             typeof(string));
            dt.Columns.Add("OrdenVariable",        typeof(int));
            dt.Columns.Add("SubtotalVariable",     typeof(bool));
            dt.Columns.Add("VariablePadre",        typeof(string));
            dt.Columns.Add("VariableIndicador",    typeof(string));
            dt.Columns.Add("ClaseVariable",        typeof(string));
            dt.Columns.Add("AgrupacionKEY",        typeof(string));
            dt.Columns.Add("AgrupacionKEYABR",     typeof(string));
            dt.Columns.Add("AgrupacionKEYOrden",   typeof(int));
            dt.Columns.Add("VariablePadreReal",    typeof(string));
            dt.Columns.Add("VariablePadreAbr",     typeof(string));
            dt.Columns.Add("VariablePadreRealOrden", typeof(int));
            dt.Columns.Add("UsuarioCargo",         typeof(string));
            dt.Columns.Add("FechaCarga",           typeof(DateTime));

            foreach (var registro in datos)
            {
                var dr = dt.NewRow();
                dr["NombreTabla"]           = ValorString(registro, "NombreTabla");
                dr["Variable"]              = ValorString(registro, "Variable");
                dr["OrdenVariable"]         = (object)ValorInt(registro, "OrdenVariable") ?? DBNull.Value;
                dr["SubtotalVariable"]      = ValorBool(registro, "SubtotalVariable");
                dr["VariablePadre"]         = ValorStringONull(registro, "VariablePadre");
                dr["VariableIndicador"]     = ValorStringONull(registro, "VariableIndicador");
                dr["ClaseVariable"]         = ValorStringONull(registro, "ClaseVariable");
                dr["AgrupacionKEY"]         = ValorStringONull(registro, "AgrupacionKEY");
                dr["AgrupacionKEYABR"]      = ValorStringONull(registro, "AgrupacionKEYABR");
                dr["AgrupacionKEYOrden"]    = (object)ValorInt(registro, "AgrupacionKEYOrden") ?? DBNull.Value;
                dr["VariablePadreReal"]     = ValorStringONull(registro, "VariablePadreReal");
                dr["VariablePadreAbr"]      = ValorStringONull(registro, "VariablePadreAbr");
                dr["VariablePadreRealOrden"] = (object)ValorInt(registro, "VariablePadreRealOrden") ?? DBNull.Value;
                dr["UsuarioCargo"]          = usuarioNombre ?? "";
                dr["FechaCarga"]            = fechaCarga;
                dt.Rows.Add(dr);
            }

            return dt;
        }

        /// <summary>
        /// Genera comparación entre datos actuales de BD y nuevos datos del Excel
        /// </summary>
        private ComparacionVariablesPBI GenerarComparacion(List<OrdenVariable> actuales, List<Dictionary<string, string>> nuevos)
        {
            var comp = new ComparacionVariablesPBI();

            // Construir diccionario de actuales: key = "NombreTabla|Variable"
            var dictActuales = new Dictionary<string, OrdenVariable>(StringComparer.OrdinalIgnoreCase);
            foreach (var ov in actuales)
            {
                string key = $"{ov.NombreTabla}|{ov.Variable}";
                if (!dictActuales.ContainsKey(key))
                    dictActuales[key] = ov;
            }

            // Construir diccionario de nuevos: key = "NombreTabla|Variable"
            var dictNuevos = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var reg in nuevos)
            {
                string key = GetVal(reg, "NombreTabla") + "|" + GetVal(reg, "Variable");
                if (!dictNuevos.ContainsKey(key))
                    dictNuevos[key] = reg;
            }

            // Detectar Agregados (en nuevos pero no en actuales)
            foreach (var kv in dictNuevos)
            {
                if (!dictActuales.ContainsKey(kv.Key))
                {
                    comp.Agregados.Add(new RegistroOrdenVariableResumen
                    {
                        NombreTabla   = GetVal(kv.Value, "NombreTabla"),
                        Variable      = GetVal(kv.Value, "Variable"),
                        Orden         = ValorInt(kv.Value, "OrdenVariable") ?? 0,
                        EsSubtotal    = ValorBool(kv.Value, "SubtotalVariable"),
                        VariablePadre = GetVal(kv.Value, "VariablePadre")
                    });
                }
                else
                {
                    // Detectar Modificados
                    var anterior = dictActuales[kv.Key];
                    var nuevo = kv.Value;
                    string cambios = GenerarResumenCambios(anterior, nuevo);
                    if (!string.IsNullOrEmpty(cambios))
                    {
                        comp.Modificados.Add(new ModificacionOrdenVariable
                        {
                            NombreTabla   = anterior.NombreTabla,
                            Variable      = anterior.Variable,
                            CambioResumen = cambios
                        });
                    }
                }
            }

            // Detectar Eliminados (en actuales pero no en nuevos)
            foreach (var kv in dictActuales)
            {
                if (!dictNuevos.ContainsKey(kv.Key))
                {
                    comp.Eliminados.Add(new RegistroOrdenVariableResumen
                    {
                        NombreTabla   = kv.Value.NombreTabla,
                        Variable      = kv.Value.Variable,
                        Orden         = kv.Value.OrdenVariable_,
                        EsSubtotal    = kv.Value.SubtotalVariable,
                        VariablePadre = kv.Value.VariablePadre
                    });
                }
            }

            return comp;
        }

        /// <summary>
        /// Compara los campos de un registro anterior (BD) con uno nuevo (Excel) y retorna resumen de cambios
        /// </summary>
        private string GenerarResumenCambios(OrdenVariable anterior, Dictionary<string, string> nuevo)
        {
            var cambios = new List<string>();

            CompararCampo(cambios, "Orden",
                anterior.OrdenVariable_.ToString(),
                GetVal(nuevo,"OrdenVariable") ?? "");

            CompararCampo(cambios, "Subtotal",
                anterior.SubtotalVariable ? "1" : "0",
                GetVal(nuevo,"SubtotalVariable") ?? "0");

            CompararCampo(cambios, "Variable Padre",
                anterior.VariablePadre ?? "",
                GetVal(nuevo,"VariablePadre") ?? "");

            CompararCampo(cambios, "Variable Indicador",
                anterior.VariableIndicador ?? "",
                GetVal(nuevo,"VariableIndicador") ?? "");

            CompararCampo(cambios, "Clase Variable",
                anterior.ClaseVariable ?? "",
                GetVal(nuevo,"ClaseVariable") ?? "");

            CompararCampo(cambios, "Agrupación KEY",
                anterior.AgrupacionKEY ?? "",
                GetVal(nuevo,"AgrupacionKEY") ?? "");

            CompararCampo(cambios, "Agrupación KEY ABR",
                anterior.AgrupacionKEYABR ?? "",
                GetVal(nuevo,"AgrupacionKEYABR") ?? "");

            CompararCampo(cambios, "Agrupación KEY Orden",
                anterior.AgrupacionKEYOrden?.ToString() ?? "",
                GetVal(nuevo,"AgrupacionKEYOrden") ?? "");

            CompararCampo(cambios, "Var. Padre Real",
                anterior.VariablePadreReal ?? "",
                GetVal(nuevo,"VariablePadreReal") ?? "");

            CompararCampo(cambios, "Var. Padre ABR",
                anterior.VariablePadreAbr ?? "",
                GetVal(nuevo,"VariablePadreAbr") ?? "");

            CompararCampo(cambios, "Var. Padre Real Orden",
                anterior.VariablePadreRealOrden?.ToString() ?? "",
                GetVal(nuevo,"VariablePadreRealOrden") ?? "");

            return cambios.Count > 0 ? string.Join(" | ", cambios) : null;
        }

        private void CompararCampo(List<string> cambios, string nombre, string valAnterior, string valNuevo)
        {
            // Normalizar nulos/vacíos
            string a = (valAnterior ?? "").Trim();
            string n = (valNuevo ?? "").Trim();
            if (!string.Equals(a, n, StringComparison.Ordinal))
            {
                string aDisplay = string.IsNullOrEmpty(a) ? "(vacío)" : (a.Length > 25 ? a.Substring(0, 22) + "..." : a);
                string nDisplay = string.IsNullOrEmpty(n) ? "(vacío)" : (n.Length > 25 ? n.Substring(0, 22) + "..." : n);
                cambios.Add($"{nombre}: {aDisplay}→{nDisplay}");
            }
        }

        // ─── Helpers de conversión ────────────────────────────────────────────────

        private static string GetVal(Dictionary<string, string> d, string key)
        {
            string v;
            return d.TryGetValue(key, out v) ? (v ?? "") : "";
        }

        private string ValorString(Dictionary<string, string> d, string key)
            => GetVal(d, key);

        private object ValorStringONull(Dictionary<string, string> d, string key)
        {
            if (d.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v))
                return (object)v;
            return DBNull.Value;
        }

        private int? ValorInt(Dictionary<string, string> d, string key)
        {
            if (d.TryGetValue(key, out string v) && int.TryParse(v?.Trim(), out int n))
                return n;
            return null;
        }

        private bool ValorBool(Dictionary<string, string> d, string key)
        {
            if (d.TryGetValue(key, out string v))
            {
                if (v?.Trim() == "1" || string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
