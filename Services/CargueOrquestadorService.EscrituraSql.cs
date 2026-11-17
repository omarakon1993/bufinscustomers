using bufinscustomers.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace bufinscustomers.Services
{
    // EscrituraSql: parte de CargueOrquestadorService (misma clase; solo se separa el codigo por responsabilidad).
    public partial class CargueOrquestadorService
    {
        private bool GuardarEnSQLServer(DataTable tabla, int idEmpresa, int idUsuario)
        {
            SqlConnection conn = null;
            try
            {
                DateTime fechaCargue = DateTime.Now;
                LogToFile($"========== INICIO GUARDADO TABLA: {tabla.TableName} ==========");
                LogToFile($"Filas en DataTable: {tabla.Rows.Count}");

                conn = new SqlConnection(CadenaConexion);
                conn.Open();
                LogToFile("Conexión a SQL Server abierta exitosamente");

                // Normalizar nombre
                tabla.TableName = NormalizarNombre(tabla.TableName);

                // Crear tabla si no existe
                CrearTablaSiNoExiste(conn, tabla);

                // Asegurar columnas extra
                if (!tabla.Columns.Contains("IdEmpresa"))
                    tabla.Columns.Add("IdEmpresa", typeof(int));
                if (!tabla.Columns.Contains("IdUsuario"))
                    tabla.Columns.Add("IdUsuario", typeof(int));
                if (!tabla.Columns.Contains("FechaCargue"))
                    tabla.Columns.Add("FechaCargue", typeof(DateTime));

                // Setear columnas para todas las filas
                foreach (DataRow row in tabla.Rows)
                {
                    row["IdEmpresa"] = idEmpresa;
                    row["IdUsuario"] = idUsuario;
                    row["FechaCargue"] = fechaCargue;
                }

                // Limpiar registros anteriores solo de esa empresa
                using (SqlCommand deleteCmd = new SqlCommand($@"
                    DELETE FROM [dbo].[{tabla.TableName}]
                    WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                {
                    deleteCmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    deleteCmd.ExecuteNonQuery();
                }

                // LOGGING: Información de la tabla
                LogToFile($"Columnas del DataTable ({tabla.Columns.Count}): {string.Join(", ", tabla.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");

#if DEBUG
                for (int i = 0; i < Math.Min(3, tabla.Rows.Count); i++)
                {
                    var valores = tabla.Rows[i].ItemArray.Select(v => v?.ToString() ?? "NULL").ToList();
                    LogToFile($"Fila {i + 1} (muestra): {string.Join(" | ", valores)}");
                }
#endif

                // Leer esquema una sola vez y reutilizar en conversión y validación
                var esquema = LeerEsquemaTabla(conn, tabla.TableName);

                // Convertir valores vacíos a 0 en columnas money/decimal
                LogToFile("Convirtiendo valores vacíos a 0 en columnas de dinero...");
                ConvertirValoresVaciosAZero(tabla, esquema);

                // Rechazar cargue si alguna celda numérica contiene texto (ej: "ggg")
                string errorNumerico = ValidarValoresNumericos(tabla, esquema);
                if (!string.IsNullOrEmpty(errorNumerico))
                {
                    LogToFile($"ERROR NUMÉRICO: {errorNumerico}");
                    UltimoMensajeHoja = $"ERROR NUMÉRICO en '{tabla.TableName}':\n\n{errorNumerico}";
                    MostrarDescargaLog = true;
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                // Normalizar separadores decimales/miles antes de insertar
                LogToFile("Sanitizando valores numéricos...");
                SanitizarColumnasNumericas(tabla, esquema);

                // Validar datos antes de insertar
                LogToFile("Iniciando validación de datos...");
                string errorValidacion = ValidarDatosParaBulkCopy(tabla, esquema);
                if (!string.IsNullOrEmpty(errorValidacion))
                {
                    LogToFile($"ERROR EN VALIDACIÓN: {errorValidacion}");
                    UltimoMensajeHoja = $"ERROR DE VALIDACIÓN en '{tabla.TableName}':\n\n{errorValidacion}";
                    MostrarDescargaLog = true;

                    // Limpiar tablas en caso de error
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                LogToFile("Validación previa completada sin errores");

                // Insertar lo nuevo
                try
                {
                    LogToFile($"Iniciando SqlBulkCopy hacia tabla: {tabla.TableName}");
                    using (SqlBulkCopy bulk = new SqlBulkCopy(conn))
                    {
                        bulk.DestinationTableName = $"[dbo].[{tabla.TableName}]";
                        bulk.BulkCopyTimeout = 120;
                        bulk.BatchSize = 5000;
                        bulk.WriteToServer(tabla);
                    }
                    LogToFile($"SqlBulkCopy completado exitosamente para {tabla.TableName}");
                }
                catch (Exception bulkEx)
                {
                    LogToFile($"ERROR en SqlBulkCopy: {bulkEx.Message}");
                    LogToFile($"StackTrace: {bulkEx.StackTrace}");

                    string detalleError = AnalizarErrorBulkCopy(conn, tabla, bulkEx);
                    LogToFile($"Análisis de error: {detalleError}");

                    UltimoMensajeHoja = $"ERROR AL INSERTAR en '{tabla.TableName}':\n\n{detalleError}";
                    MostrarDescargaLog = true;

                    // Limpiar tablas en caso de error
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"ERROR GENERAL: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");

                UltimoMensajeHoja = $"ERROR GENERAL al guardar en '{tabla.TableName}':\n{ex.Message}";
                MostrarDescargaLog = true;

                LimpiarTablasEnError(conn, idEmpresa);

                return false;
            }
            finally
            {
                if (conn != null)
                {
                    conn.Close();
                    conn.Dispose();
                }
            }
        }

        private Dictionary<string, (string tipo, bool nullable)> LeerEsquemaTabla(SqlConnection conn, string tableName)
        {
            var esquema = new Dictionary<string, (string tipo, bool nullable)>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(
                "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                conn))
            {
                cmd.Parameters.AddWithValue("@t", tableName);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        esquema[reader["COLUMN_NAME"].ToString()] = (
                            reader["DATA_TYPE"].ToString(),
                            reader["IS_NULLABLE"].ToString() == "YES"
                        );
                    }
                }
            }
            return esquema;
        }

        private void LimpiarTablasEnError(SqlConnection conn, int idEmpresa)
        {
            if (conn == null || conn.State != ConnectionState.Open) return;

            // Limpiar todas las tablas Z_: las del mapeo Ini_ + las que no tienen mapeo
            var tablasALimpiar = TablasCargueHelper.TablasZ.Concat(_tablasZSinMapeo);

            foreach (var tabla in tablasALimpiar)
            {
                try
                {
                    using (var cmd = new SqlCommand(
                        $"DELETE FROM [dbo].[{tabla}] WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { /* tabla puede no existir, continuar con las demás */ }
            }
        }

        private void EliminarAnosHistoricosDeIni(SqlConnection conn, int anio, int idEmpresa, int idEscenario, SqlTransaction tx = null)
        {
            foreach (var tablaIni in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                using (var cmd = new SqlCommand(
                    $"DELETE FROM [dbo].[{tablaIni}] WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Ano AND ISNULL(IdEscenario,1) = @IdEscenario", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Ano", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.ExecuteNonQuery();
                }
                LogToFile($"Eliminados datos históricos de {tablaIni} año {anio} escenario {idEscenario} para empresa {idEmpresa}");
            }
        }

        private void EliminarEjecucionDeIni(SqlConnection conn, int anio, int idEmpresa, int idEscenario, SqlTransaction tx = null)
        {
            foreach (var tablaIni in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                using (var cmd = new SqlCommand(
                    $"DELETE FROM [dbo].[{tablaIni}] WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Ano AND Historico_Log = 0 AND ISNULL(IdEscenario,1) = @IdEscenario", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Ano", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.ExecuteNonQuery();
                }
                LogToFile($"Eliminados datos de ejecución de {tablaIni} año {anio} escenario {idEscenario} para empresa {idEmpresa}");
            }
        }

        /// <summary>
        /// Copia un DataTable leído del Excel a la tabla destino indicada (esquema resuelto
        /// dinámicamente vía INFORMATION_SCHEMA.COLUMNS, así que sirve tanto para las tablas
        /// Ini_* reales como para su espejo Staging_Ini_* — ver validación en dos pasos en
        /// <see cref="bufinscustomers.Services.CargueStagingService"/>). Cuando <paramref name="idLote"/>
        /// tiene valor y la tabla destino tiene columna IdLote/NumeroFilaExcel (solo las de staging
        /// las tienen), se completan también esas dos columnas.
        /// </summary>
        private bool GuardarEnIni(SqlConnection conn, DataTable dtExcel, string nombreTablaIni, int idEmpresa, int idUsuario, int idEscenario, byte historicoLog = 1, SqlTransaction tx = null, long? idLote = null, int numeroFilaBase = 2)
        {
            try
            {
                LogToFile($"========== INICIO GUARDADO HISTÓRICO: {nombreTablaIni} ==========");
                LogToFile($"Filas en DataTable: {dtExcel.Rows.Count}");

                // Obtener esquema de la tabla Ini_ (nombre y tipo)
                var columnasIni = new List<string>();
                var columnasNumericas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Fecha / Sí-No: solo existen en hojas diseñadas en el Gestor de hojas de cargue (las
                // tablas anteriores no tienen columnas de negocio de estos tipos). Se convierten
                // explícitamente para no depender de la conversión implícita de texto de SqlBulkCopy.
                var columnasFecha = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var columnasBit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                    conn, tx))
                {
                    cmd.Parameters.AddWithValue("@t", nombreTablaIni);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string col = reader["COLUMN_NAME"].ToString();
                            string tipo = reader["DATA_TYPE"].ToString().ToLower();
                            columnasIni.Add(col);
                            if (tipo == "money" || tipo == "smallmoney" || tipo == "decimal" ||
                                tipo == "numeric" || tipo == "float" || tipo == "real" ||
                                tipo == "int" || tipo == "bigint" || tipo == "smallint" || tipo == "tinyint")
                                columnasNumericas.Add(col);
                            else if (tipo == "date" || tipo == "datetime" || tipo == "datetime2" || tipo == "smalldatetime")
                                columnasFecha.Add(col);
                            else if (tipo == "bit")
                                columnasBit.Add(col);
                        }
                    }
                }

                if (columnasIni.Count == 0)
                {
                    LogToFile($"Tabla {nombreTablaIni} no encontrada en BD");
                    UltimoMensajeHoja = $"No se encontró la tabla '{nombreTablaIni}' en la base de datos.";
                    MostrarDescargaLog = true;
                    return false;
                }

                // Mapa de columnas del Excel (case-insensitive)
                var colsExcel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dtExcel.Columns.Count; i++)
                    colsExcel[dtExcel.Columns[i].ColumnName] = i;

                // DataTable destino con esquema de Ini_
                var dtDest = new DataTable();
                foreach (var col in columnasIni)
                    dtDest.Columns.Add(col, typeof(object));

                DateTime fechaCargue = DateTime.Now;

                int indiceFila = 0;
                foreach (DataRow srcRow in dtExcel.Rows)
                {
                    var destRow = dtDest.NewRow();
                    int filaExcelActual = numeroFilaBase + indiceFila++;

                    // Valores de referencia para la Llave
                    string cuenta  = colsExcel.TryGetValue("Cuenta", out int value)  ? srcRow[value].ToString()  : "";
                    string empresa = colsExcel.TryGetValue("Empresa", out int value1) ? srcRow[value1].ToString() : "";
                    string ano     = colsExcel.TryGetValue("Año", out int value2)     ? srcRow[value2].ToString() :
                                     colsExcel.TryGetValue("Anio", out int value3)    ? srcRow[value3].ToString() : "";
                    string mes     = colsExcel.TryGetValue("Mes", out int value4)     ? srcRow[value4].ToString()     : "";

                    foreach (var colIni in columnasIni)
                    {
                        switch (colIni)
                        {
                            case "Llave":
                                destRow[colIni] = cuenta + empresa + ano + mes;
                                break;
                            case "IdEmpresa_Log":
                                destRow[colIni] = idEmpresa;
                                break;
                            case "IdEscenario":
                                destRow[colIni] = idEscenario;
                                break;
                            case "IdUsuarioCargue_Log":
                                destRow[colIni] = idUsuario;
                                break;
                            case "FechaCargue_Log":
                                destRow[colIni] = fechaCargue;
                                break;
                            case "Historico_Log":
                                destRow[colIni] = historicoLog;
                                break;
                            case "IdLote":
                                destRow[colIni] = idLote.HasValue ? (object)idLote.Value : DBNull.Value;
                                break;
                            case "NumeroFilaExcel":
                                destRow[colIni] = filaExcelActual;
                                break;
                            case "IdUsuarioEjecucion_Log":
                            case "FechaEjecucion_Log":
                            case "Observacion_Log":
                                destRow[colIni] = DBNull.Value;
                                break;
                            default:
                                if (colsExcel.TryGetValue(colIni, out int value5))
                                {
                                    var cellValue = srcRow[value5];
                                    if (columnasNumericas.Contains(colIni))
                                    {
                                        if (cellValue is string sValIni && !string.IsNullOrWhiteSpace(sValIni))
                                        {
                                            string razonIni = CargueConversion.DetectarErrorNumerico(sValIni);
                                            if (razonIni != null)
                                            {
                                                int filaExcel = filaExcelActual;
                                                string errorFmt =
                                                    $"Fila {filaExcel}, Columna '{colIni}': {razonIni}. " +
                                                    $"Revise la hoja completa por errores similares antes de reintentar.";
                                                LogToFile($"ERROR NUMÉRICO en {nombreTablaIni}: Fila {filaExcel}, Columna '{colIni}', valor '{sValIni}'");
                                                UltimoMensajeHoja = $"ERROR NUMÉRICO en '{nombreTablaIni}':\n\n{errorFmt}";
                                                MostrarDescargaLog = true;
                                                return false;
                                            }
                                            LogToFile($"Advertencia: Columna '{colIni}' fila {filaExcelActual} " +
                                                      $"tiene valor texto '{sValIni}' — se parseará como número. " +
                                                      $"Considere usar formato Número en Excel.");
                                        }
                                        destRow[colIni] = (object)CargueConversion.ExtraerDecimal(cellValue);
                                    }
                                    else if (columnasFecha.Contains(colIni) || columnasBit.Contains(colIni))
                                    {
                                        bool esFecha = columnasFecha.Contains(colIni);
                                        object convertido = esFecha ? CargueConversion.ConvertirCeldaFecha(cellValue) : CargueConversion.ConvertirCeldaBit(cellValue);
                                        if (convertido == null)
                                        {
                                            int filaExcel = filaExcelActual;
                                            string razon = esFecha
                                                ? "no es una fecha válida (use el formato de fecha de Excel o AAAA-MM-DD)"
                                                : "no es un valor Sí/No válido (use 1/0, Sí/No o Verdadero/Falso)";
                                            LogToFile($"ERROR DE FORMATO en {nombreTablaIni}: Fila {filaExcel}, Columna '{colIni}', valor '{cellValue}'");
                                            UltimoMensajeHoja = $"ERROR DE FORMATO en '{nombreTablaIni}':\n\nFila {filaExcel}, Columna '{colIni}': el valor '{cellValue}' {razon}.";
                                            MostrarDescargaLog = true;
                                            return false;
                                        }
                                        destRow[colIni] = convertido;
                                    }
                                    else
                                    {
                                        var rawVal = cellValue?.ToString()?.Trim() ?? "";
                                        destRow[colIni] = rawVal.Length > 0 ? (object)rawVal : DBNull.Value;
                                    }
                                }
                                else
                                {
                                    destRow[colIni] = DBNull.Value;
                                }
                                break;
                        }
                    }

                    dtDest.Rows.Add(destRow);
                }

                // En staging (idLote) se verifican las FK: ninguna fila puede apuntar a un lote inexistente.
                var opcionesBulk = idLote.HasValue ? SqlBulkCopyOptions.CheckConstraints : SqlBulkCopyOptions.Default;
                using (var bulk = new SqlBulkCopy(conn, opcionesBulk, tx))
                {
                    bulk.DestinationTableName = $"[dbo].[{nombreTablaIni}]";
                    bulk.BulkCopyTimeout = 120;
                    bulk.BatchSize = 5000;
                    foreach (DataColumn col in dtDest.Columns)
                        bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                    bulk.WriteToServer(dtDest);
                }

                LogToFile($"Guardado histórico exitoso: {dtDest.Rows.Count} filas en {nombreTablaIni}");
                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"ERROR en GuardarEnIni ({nombreTablaIni}): {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
                UltimoMensajeHoja = $"ERROR al guardar en '{nombreTablaIni}': {ex.Message}";
                MostrarDescargaLog = true;
                return false;
            }
        }

        private string AnalizarErrorBulkCopy(SqlConnection conn, DataTable tabla, Exception bulkEx)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Error original: {bulkEx.Message}");
            sb.AppendLine();

            try
            {
                var columnasSQL = new Dictionary<string, string>();
                using (var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                    conn))
                {
                    cmd.Parameters.AddWithValue("@t", tabla.TableName);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string colName = reader["COLUMN_NAME"].ToString();
                            string dataType = reader["DATA_TYPE"].ToString();
                            string nullable = reader["IS_NULLABLE"].ToString();
                            columnasSQL[colName] = $"{dataType} (NULL: {nullable})";
                        }
                    }
                }

                sb.AppendLine("ANÁLISIS DETALLADO:");
                sb.AppendLine();

                for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                {
                    string nombreColumna = tabla.Columns[colIndex].ColumnName;

                    if (!columnasSQL.ContainsKey(nombreColumna)) continue;

                    string tipoSQL = columnasSQL[nombreColumna];

                    if (tipoSQL.ToLower().Contains("money") || tipoSQL.ToLower().Contains("decimal"))
                    {
                        sb.AppendLine($"Columna '{nombreColumna}': Tipo SQL = {tipoSQL}");

                        for (int rowIndex = 0; rowIndex < Math.Min(tabla.Rows.Count, 100); rowIndex++)
                        {
                            object valor = tabla.Rows[rowIndex][colIndex];
                            string valorStr = valor?.ToString()?.Trim() ?? "";

                            if (!string.IsNullOrWhiteSpace(valorStr))
                            {
                                decimal test;
                                string valorLimpio = valorStr.Replace("$", "").Replace(",", "").Trim();

                                if (!decimal.TryParse(valorLimpio, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out test))
                                {
                                    sb.AppendLine($"   FILA {rowIndex + 2} (Excel): Valor = '{valorStr}' | Tipo = {valor?.GetType().Name ?? "null"}");
                                    sb.AppendLine($"      Este valor NO se puede convertir a decimal/money");
                                    return sb.ToString();
                                }
                            }
                        }

                        sb.AppendLine($"   Primeras 100 filas validadas OK");
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error al analizar: {ex.Message}");
            }

            return sb.ToString();
        }

        private string ValidarDatosParaBulkCopy(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            try
            {
                LogToFile($"Esquema SQL encontrado para {tabla.TableName}: {esquema.Count} columnas");
                foreach (var col in esquema)
                {
                    LogToFile($"   - {col.Key}: {col.Value.tipo} (Nullable: {col.Value.nullable})");
                }

                if (esquema.Count == 0)
                {
                    LogToFile($"Tabla {tabla.TableName} no existe en SQL Server, se creará dinámicamente");
                    return null;
                }

                for (int rowIndex = 0; rowIndex < tabla.Rows.Count; rowIndex++)
                {
                    DataRow fila = tabla.Rows[rowIndex];

                    for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                    {
                        string nombreColumna = tabla.Columns[colIndex].ColumnName;

                        if (!esquema.ContainsKey(nombreColumna)) continue;

                        var (tipoSQL, nullable) = esquema[nombreColumna];
                        object valor = fila[colIndex];
                        string valorStr = valor?.ToString()?.Trim() ?? "";

                        if (tipoSQL.ToLower() == "money" || tipoSQL.ToLower() == "decimal" ||
                            tipoSQL.ToLower() == "numeric" || tipoSQL.ToLower() == "smallmoney")
                        {
                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                if (!nullable)
                                {
                                    return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': Valor vacío pero la columna no acepta NULL. Tipo SQL: {tipoSQL}";
                                }
                                continue;
                            }

                            decimal valorDecimal;
                            // Usar SanitizarValorNumerico para aplicar el mismo criterio que en el insert
                            string valorLimpio = CargueConversion.SanitizarValorNumerico(valorStr);

                            if (!decimal.TryParse(valorLimpio, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out valorDecimal))
                            {
                                string mensajeError = $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': El valor '{valorStr}' no se puede convertir a {tipoSQL}. " +
                                       $"Valor en DataTable: '{valor}' (Tipo: {valor?.GetType().Name ?? "null"})";
                                LogToFile($"ERROR DETECTADO EN VALIDACIÓN: {mensajeError}");
                                return mensajeError;
                            }
                        }
                        else if (tipoSQL.ToLower() == "int" || tipoSQL.ToLower() == "bigint" ||
                                 tipoSQL.ToLower() == "smallint" || tipoSQL.ToLower() == "tinyint")
                        {
                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                if (!nullable)
                                {
                                    return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': Valor vacío pero la columna no acepta NULL. Tipo SQL: {tipoSQL}";
                                }
                                continue;
                            }

                            string valorLimpio = valorStr.Replace(",", "").Replace(" ", "").Trim();
                            int valorInt;
                            if (!int.TryParse(valorLimpio, out valorInt))
                            {
                                return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': El valor '{valorStr}' no se puede convertir a {tipoSQL}. " +
                                       $"Valor en DataTable: '{valor}' (Tipo: {valor?.GetType().Name ?? "null"})";
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return $"Error en validación: {ex.Message}";
            }
        }

        private void CrearTablaSiNoExiste(SqlConnection conn, DataTable tabla)
        {
            var columnasExcel = tabla.Columns.Cast<DataColumn>()
                                  .Select(c => $"[{c.ColumnName}] VARCHAR(MAX)");

            var columnasExtras = new List<string>
            {
                "[IdEmpresa] INT",
                "[IdUsuario] INT",
                "[FechaCargue] DATETIME"
            };

            string nombreTabla = $"[dbo].[{tabla.TableName}]";
            string sql = $@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{tabla.TableName}')
                BEGIN
                    CREATE TABLE {nombreTabla} (
                        {string.Join(", ", columnasExcel.Concat(columnasExtras))}
                    );
                END
            ";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}
