using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using Microsoft.Ajax.Utilities;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Windows.Media.Media3D;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class DatosController : BaseController
    {
        private readonly EmpresaService _empresaService = new EmpresaService();
        private readonly ConfiguracionEmpresaService _configuracionService = new ConfiguracionEmpresaService();
        private StringBuilder _logBuilder = new StringBuilder();

        private void LogToFile(string mensaje)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logMessage = $"[{timestamp}] {mensaje}";
                _logBuilder.AppendLine(logMessage);
            }
            catch { /* Ignorar errores de logging */ }
        }

        private void GuardarLogEnSession()
        {
            if (_logBuilder.Length > 0)
            {
                Session["LogImportacion"] = _logBuilder.ToString();
            }
        }

        public ActionResult DescargarLog()
        {
            var log = Session["LogImportacion"] as string;
            if (string.IsNullOrEmpty(log))
            {
                return Content("No hay log disponible para descargar.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(log);
            string fileName = $"LogImportacion_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return File(bytes, "text/plain", fileName);
        }

        // Modelo actions
        public ActionResult Modelo()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = _empresaService.ObtenerEmpresas();
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();
            }
            return View("~/Views/Datos/Modelo.cshtml", empresas);
        }

        [HttpPost]
        public ActionResult EjecutarModeloBalance(int idEmpresa, string anio)
        {
            try
            {
                var usuarioSession = (Usuarios)Session["usuario"];
                if (usuarioSession == null)
                {
                    SetErrorMessage("Sesión no válida. Por favor, inicie sesión nuevamente.");
                    return RedirectToAction("Login", "Acceso");
                }

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuarioSession.IdEmpresa != idEmpresa)
                {
                    SetErrorMessage("No tiene permisos para ejecutar el modelo en esta empresa.");
                    return RedirectToAction("Modelo");
                }

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                using (SqlCommand command = new SqlCommand("sp_ModeloBalance", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    command.Parameters.AddWithValue("@IdUsuario", usuarioSession.Id);
                    command.Parameters.AddWithValue("@Año", anio);

                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int codMessage = Convert.ToInt32(reader["CodMessage"]);
                            string mensaje = reader["ErrorMessage"].ToString();

                            if (codMessage == 1)
                                SetSuccessMessage(mensaje);
                            else
                                SetErrorMessage(mensaje);
                        }
                        else
                        {
                            SetErrorMessage("No se recibió respuesta del procedimiento.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al ejecutar el modelo: " + ex.Message);
            }

            return RedirectToAction("Modelo");
        }

        [HttpPost]
        public ActionResult LimpiarDatosImportacion()
        {
            Session["TablasExcel"] = null;
            TempData["MostrarBotonImportar"] = null;
            return RedirectToAction("CargueExcel");
        }

        [HttpPost]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel, string accion, int idEmpresaSeleccionada)
        {
            // Si viene 0, intentar recuperar de Session (caso de Importar después de Validar)
            if (idEmpresaSeleccionada == 0 && Session["idEmpresaSeleccionada"] != null)
            {
                idEmpresaSeleccionada = (int)Session["idEmpresaSeleccionada"];
            }

            // Validar que se haya seleccionado una empresa
            if (idEmpresaSeleccionada == 0)
            {
                TempData["Mensaje"] = "Debe seleccionar una empresa antes de cargar el archivo.";
                TempData["MensajeTipo"] = "error";
                return RedirectToAction("CargueExcel", "Datos");
            }

            // Validar que la empresa tenga configuración creada
            if (!_configuracionService.ExisteConfiguracion(idEmpresaSeleccionada))
            {
                TempData["Mensaje"] = "La empresa seleccionada no tiene configuración creada.";
                TempData["MensajeTipo"] = "error";
                return RedirectToAction("CargueExcel", "Datos", new { limpiar = false });
            }

            if ((archivoExcel == null || archivoExcel.ContentLength == 0) && Session["ArchivoExcelBytes"] == null)
            {
                TempData["Mensaje"] = "No se seleccionó ningún archivo.";
                TempData["MensajeTipo"] = "error";
                return RedirectToAction("CargueExcel", "Datos");
            }

            if (archivoExcel != null && archivoExcel.ContentLength > 0)
            {
                using (var ms = new MemoryStream())
                {
                    archivoExcel.InputStream.CopyTo(ms);
                    Session["ArchivoExcelBytes"] = ms.ToArray();
                    Session["ArchivoExcelNombre"] = Path.GetFileName(archivoExcel.FileName);
                }
            }

            var tablasExcel = new List<(string nombre, DataTable tabla)>();
            Stream archivoStream = archivoExcel != null && archivoExcel.ContentLength > 0
                ? archivoExcel.InputStream
                : new MemoryStream((byte[])Session["ArchivoExcelBytes"]);

            var usuarioActual = UsuarioSesionHelper.UsuarioActual;
            int idUsuario = usuarioActual?.Id ?? 0;

            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                {
                    conn.Open();

                    using (var package = new ExcelPackage(archivoStream))
                    {
                        // =========================
                        // 🔹 PROCESAR TODAS LAS HOJAS
                        // =========================
                        foreach (var hoja in package.Workbook.Worksheets)
                        {
                            int totalCols = hoja.Dimension?.End.Column ?? 0;
                            int totalRows = hoja.Dimension?.End.Row ?? 0;
                            if (totalCols == 0 || totalRows == 0) continue;

                            bool filaCabeceraValida = false;
                            for (int col = 1; col <= totalCols; col++)
                            {
                                if (!string.IsNullOrWhiteSpace(hoja.Cells[1, col].Text))
                                {
                                    filaCabeceraValida = true;
                                    break;
                                }
                            }
                            if (!filaCabeceraValida) continue;

                            int columnasValidas = 0;
                            for (int col = 1; col <= totalCols; col++)
                            {
                                if (!string.IsNullOrWhiteSpace(hoja.Cells[1, col].Text.Trim()))
                                    columnasValidas++;
                                else
                                    break;
                            }
                            if (columnasValidas == 0) continue;

                            var nombreTabla = $"{NormalizarNombre(hoja.Name)}";
                            var dt = new DataTable(nombreTabla);
                            for (int col = 1; col <= columnasValidas; col++)
                                dt.Columns.Add(hoja.Cells[1, col].Text.Trim());

                            for (int row = 2; row <= totalRows; row++)
                            {
                                bool filaVacia = true;
                                var dr = dt.NewRow();
                                for (int col = 1; col <= columnasValidas; col++)
                                {
                                    var valor = hoja.Cells[row, col].Text?.Trim();
                                    if (!string.IsNullOrWhiteSpace(valor)) filaVacia = false;
                                    dr[col - 1] = valor;
                                }

                                if (filaVacia) continue;  // ✅ ya no se corta en seco
                                dt.Rows.Add(dr);
                            }

                            if (accion == "Importar")
                            {
                                if (!GuardarEnSQLServer(dt.Copy(), idEmpresaSeleccionada, idUsuario))
                                {
                                    // Ya se guardó el mensaje en TempData en el catch, solo redirige
                                    return RedirectToAction("CargueExcel", "Datos", new { limpiar = false });
                                }
                            }
                            else if (accion == "RetornoTablaDeDatos")
                            {
                                tablasExcel.Add((dt.TableName, dt));
                            }

                        }

                        // =========================
                        // 🔹 3. REGISTRAR AUDITORÍA
                        // =========================
                        if (accion == "Importar")
                        {
                            string nombreArchivo = Session["ArchivoExcelNombre"]?.ToString() ?? "Archivo desconocido";
                            RegistrarAuditoria(conn, nombreArchivo, idEmpresaSeleccionada);
                        }
                    }
                }

                if (accion == "RetornoTablaDeDatos")
                {
                    Session["TablasExcel"] = tablasExcel;
                    Session["idEmpresaSeleccionada"] = idEmpresaSeleccionada;
                    TempData["MostrarBotonImportar"] = true;
                }
                else
                {
                    resultadoValidaciondeDatos();
                }
            }
            catch (Exception ex)
            {
                TempData["Mensaje"] = $"Error al procesar el archivo: {ex.Message}";
                TempData["MensajeTipo"] = "error";
            }

            return RedirectToAction("CargueExcel", "Datos", new { limpiar = false });
        }

        public ActionResult CargueExcel(bool limpiar = true)
        {
            if (limpiar)
            {
                Session.Remove("TablasExcel");
                Session.Remove("idEmpresaSeleccionada");
                TempData.Remove("Mensaje");
                TempData.Remove("MensajeTipo");
                TempData.Remove("MostrarBotonImportar");
            }

            var modelo = Session["TablasExcel"] as List<(string nombre, DataTable tabla)>
                         ?? new List<(string nombre, DataTable tabla)>();

            // 🔹 Obtener usuario actual
            var usuario = UsuarioSesionHelper.UsuarioActual;
            int idEmpresa = usuario?.IdEmpresa ?? 0;

            // 🔹 Cargar empresas según rol del usuario
            List<Empresas> empresasDisponibles;
            if (UsuarioSesionHelper.EsSuperAdmin())
            {
                // Super Admin: mostrar todas las empresas
                empresasDisponibles = _empresaService.ObtenerEmpresas();
            }
            else
            {
                // Admin de empresa y usuario normal: mostrar solo su empresa
                var empresaUsuario = _empresaService.ObtenerEmpresas()
                    .FirstOrDefault(e => e.Id == idEmpresa);
                empresasDisponibles = empresaUsuario != null
                    ? new List<Empresas> { empresaUsuario }
                    : new List<Empresas>();
            }
            ViewBag.Empresas = empresasDisponibles;

            // 🔹 Traer último usuario que cargó SOLO para esa empresa
            ViewBag.UltimoUsuarioCargue = ObtenerUltimoUsuarioCargue(idEmpresa);

            Session.Remove("TablasExcel");

            return View(modelo);
        }


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

                // 🔹 Limpiar registros anteriores solo de esa empresa
                using (SqlCommand deleteCmd = new SqlCommand($@"
                    DELETE FROM [dbo].[{tabla.TableName}]
                    WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                {
                    deleteCmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    deleteCmd.ExecuteNonQuery();
                }

                // 🔹 LOGGING: Información de la tabla
                LogToFile($"Columnas del DataTable ({tabla.Columns.Count}): {string.Join(", ", tabla.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");

                // Mostrar primeras 3 filas como muestra
                for (int i = 0; i < Math.Min(3, tabla.Rows.Count); i++)
                {
                    var valores = tabla.Rows[i].ItemArray.Select(v => v?.ToString() ?? "NULL").ToList();
                    LogToFile($"Fila {i + 1} (muestra): {string.Join(" | ", valores)}");
                }

                // 🔹 Convertir valores vacíos a 0 en columnas money/decimal
                LogToFile("Convirtiendo valores vacíos a 0 en columnas de dinero...");
                ConvertirValoresVaciosAZero(conn, tabla);

                // 🔹 Validar datos antes de insertar
                LogToFile("Iniciando validación de datos...");
                string errorValidacion = ValidarDatosParaBulkCopy(conn, tabla);
                if (!string.IsNullOrEmpty(errorValidacion))
                {
                    LogToFile($"❌ ERROR EN VALIDACIÓN: {errorValidacion}");
                    GuardarLogEnSession();
                    TempData["Mensaje"] = $"❌ ERROR DE VALIDACIÓN en '{tabla.TableName}':\n\n{errorValidacion}";
                    TempData["MensajeTipo"] = "error";
                    TempData["MostrarDescargaLog"] = true;

                    // Limpiar tablas en caso de error
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                LogToFile("✅ Validación previa completada sin errores");

                // 🔹 Insertar lo nuevo
                try
                {
                    LogToFile($"Iniciando SqlBulkCopy hacia tabla: {tabla.TableName}");
                    using (SqlBulkCopy bulk = new SqlBulkCopy(conn))
                    {
                        bulk.DestinationTableName = $"[dbo].[{tabla.TableName}]";
                        bulk.WriteToServer(tabla);
                    }
                    LogToFile($"✅ SqlBulkCopy completado exitosamente para {tabla.TableName}");
                }
                catch (Exception bulkEx)
                {
                    // Error en BulkCopy - intentar dar más detalles
                    LogToFile($"❌ ERROR en SqlBulkCopy: {bulkEx.Message}");
                    LogToFile($"StackTrace: {bulkEx.StackTrace}");

                    string detalleError = AnalizarErrorBulkCopy(conn, tabla, bulkEx);
                    LogToFile($"Análisis de error: {detalleError}");
                    GuardarLogEnSession();

                    TempData["Mensaje"] = $"❌ ERROR AL INSERTAR en '{tabla.TableName}':\n\n{detalleError}";
                    TempData["MensajeTipo"] = "error";
                    TempData["MostrarDescargaLog"] = true;

                    // Limpiar tablas en caso de error
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"❌ ERROR GENERAL: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
                GuardarLogEnSession();

                TempData["Mensaje"] = $"❌ ERROR GENERAL al guardar en '{tabla.TableName}':\n{ex.Message}";
                TempData["MensajeTipo"] = "error";
                TempData["MostrarDescargaLog"] = true;

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


        public List<(string nombre, DataTable tabla)> resultadoValidaciondeDatos()
        {
            var tablasExcel = new List<(string nombre, DataTable tabla)>();
            int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("dbo.SP_ValidarPlantillaInicial", conn))
            using (var adapter = new SqlDataAdapter(cmd))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);

                var dt = new DataTable();
                adapter.Fill(dt);

                // Validar si se retornó al menos una fila y CodMessage == 1
                if (dt.Rows.Count == 1 &&
                    int.TryParse(dt.Rows[0]["CodMessage"]?.ToString(), out int codMessage) &&
                    codMessage == 1)
                {
                    // Éxito real
                    TempData["Mensaje"] = dt.Rows[0]["ErrorMessage"]?.ToString() ?? "Proceso exitoso.";
                    TempData["MensajeTipo"] = "success";
                    Session.Remove("TablasExcel");
                }
                else
                {
                    // Errores u observaciones
                    tablasExcel.Add(("Errores encontrados", dt));
                    TempData["TablasExcel"] = tablasExcel;
                    Session["TablasExcel"] = tablasExcel;

                    TempData["Mensaje"] = "Se detectaron errores al importar la información por favor validar.";
                    TempData["MensajeTipo"] = "error";
                }
            }

            return tablasExcel;
        }

        private void ConvertirValoresVaciosAZero(SqlConnection conn, DataTable tabla)
        {
            try
            {
                // Obtener esquema de columnas de la tabla en SQL Server
                string sql = $@"
                    SELECT COLUMN_NAME, DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = '{tabla.TableName}'";

                var columnasMoneyDecimal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName = reader["COLUMN_NAME"].ToString();
                        string dataType = reader["DATA_TYPE"].ToString().ToLower();

                        // Solo columnas de tipo money/decimal
                        if (dataType == "money" || dataType == "smallmoney" ||
                            dataType == "decimal" || dataType == "numeric")
                        {
                            // Excluir columnas de año y mes
                            string colLower = columnName.ToLower();
                            if (!colLower.Contains("año") && !colLower.Contains("anio") &&
                                !colLower.Contains("year") && !colLower.Contains("mes") &&
                                !colLower.Contains("month") && !colLower.Contains("periodo") &&
                                !colLower.Contains("id"))
                            {
                                columnasMoneyDecimal.Add(columnName);
                            }
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

                // Recorrer todas las filas y convertir valores vacíos a 0
                foreach (DataRow fila in tabla.Rows)
                {
                    for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                    {
                        string nombreColumna = tabla.Columns[colIndex].ColumnName;

                        // Si es una columna de dinero
                        if (columnasMoneyDecimal.Contains(nombreColumna))
                        {
                            object valor = fila[colIndex];
                            string valorStr = valor?.ToString()?.Trim() ?? "";

                            // Si está vacío o es nulo, convertir a "0"
                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                fila[colIndex] = "0";
                                valoresConvertidos++;
                            }
                        }
                    }
                }

                LogToFile($"✅ Se convirtieron {valoresConvertidos} valores vacíos a 0");
            }
            catch (Exception ex)
            {
                LogToFile($"⚠️ Error al convertir valores vacíos: {ex.Message}");
            }
        }

        private void LimpiarTablasEnError(SqlConnection conn, int idEmpresa)
        {
            try
            {
                if (conn != null && conn.State == ConnectionState.Open)
                {
                    using (SqlCommand deleteCmd = new SqlCommand($@"
                        DELETE Z_BalancePrueba WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_CteYnoCte WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_EjecPCH WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_PCH WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_PptoPYGDetallado WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_PptoPYGDetalladoConAjuste WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_PresupuestoBalance WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_PYGDetallado WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_PYGDetalladoConAjuste WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL
                        DELETE Z_TablaPUC WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        deleteCmd.ExecuteNonQuery();
                    }
                }
            }
            catch { /* Ignore errors in cleanup */ }
        }

        private string AnalizarErrorBulkCopy(SqlConnection conn, DataTable tabla, Exception bulkEx)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Error original: {bulkEx.Message}");
            sb.AppendLine();

            try
            {
                // Obtener información del esquema
                string sql = $@"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = '{tabla.TableName}'
                    ORDER BY ORDINAL_POSITION";

                var columnasSQL = new Dictionary<string, string>();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
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

                // Buscar problemas específicos con columnas money
                sb.AppendLine("🔍 ANÁLISIS DETALLADO:");
                sb.AppendLine();

                for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                {
                    string nombreColumna = tabla.Columns[colIndex].ColumnName;

                    if (!columnasSQL.ContainsKey(nombreColumna)) continue;

                    string tipoSQL = columnasSQL[nombreColumna];

                    // Si es una columna money, buscar el primer valor problemático
                    if (tipoSQL.ToLower().Contains("money") || tipoSQL.ToLower().Contains("decimal"))
                    {
                        sb.AppendLine($"📊 Columna '{nombreColumna}': Tipo SQL = {tipoSQL}");

                        for (int rowIndex = 0; rowIndex < Math.Min(tabla.Rows.Count, 100); rowIndex++)
                        {
                            object valor = tabla.Rows[rowIndex][colIndex];
                            string valorStr = valor?.ToString()?.Trim() ?? "";

                            // Verificar si el valor es problemático
                            if (!string.IsNullOrWhiteSpace(valorStr))
                            {
                                decimal test;
                                string valorLimpio = valorStr.Replace("$", "").Replace(",", "").Trim();

                                if (!decimal.TryParse(valorLimpio, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out test))
                                {
                                    sb.AppendLine($"   ❌ FILA {rowIndex + 2} (Excel): Valor = '{valorStr}' | Tipo = {valor?.GetType().Name ?? "null"}");
                                    sb.AppendLine($"      Este valor NO se puede convertir a decimal/money");

                                    // Solo mostrar el primer error
                                    return sb.ToString();
                                }
                            }
                        }

                        sb.AppendLine($"   ✅ Primeras 100 filas validadas OK");
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

        private string ValidarDatosParaBulkCopy(SqlConnection conn, DataTable tabla)
        {
            try
            {
                // Obtener esquema de columnas de la tabla en SQL Server
                string sql = $@"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = '{tabla.TableName}'
                    ORDER BY ORDINAL_POSITION";

                var columnasSQL = new Dictionary<string, (string tipo, bool nullable)>();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName = reader["COLUMN_NAME"].ToString();
                        string dataType = reader["DATA_TYPE"].ToString();
                        bool isNullable = reader["IS_NULLABLE"].ToString() == "YES";
                        columnasSQL[columnName] = (dataType, isNullable);
                    }
                }

                LogToFile($"📋 Esquema SQL encontrado para {tabla.TableName}: {columnasSQL.Count} columnas");
                foreach (var col in columnasSQL)
                {
                    LogToFile($"   - {col.Key}: {col.Value.tipo} (Nullable: {col.Value.nullable})");
                }

                if (columnasSQL.Count == 0)
                {
                    LogToFile($"⚠️ Tabla {tabla.TableName} no existe en SQL Server, se creará dinámicamente");
                    return null; // Tabla no existe aún
                }

                // Validar cada fila del DataTable
                for (int rowIndex = 0; rowIndex < tabla.Rows.Count; rowIndex++)
                {
                    DataRow fila = tabla.Rows[rowIndex];

                    for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                    {
                        string nombreColumna = tabla.Columns[colIndex].ColumnName;

                        // Si la columna no existe en SQL, skip
                        if (!columnasSQL.ContainsKey(nombreColumna)) continue;

                        var (tipoSQL, nullable) = columnasSQL[nombreColumna];
                        object valor = fila[colIndex];
                        string valorStr = valor?.ToString()?.Trim() ?? "";

                        // Validar columnas de tipo money/decimal
                        if (tipoSQL.ToLower() == "money" || tipoSQL.ToLower() == "decimal" ||
                            tipoSQL.ToLower() == "numeric" || tipoSQL.ToLower() == "smallmoney")
                        {
                            // Si está vacío y la columna no acepta NULL
                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                if (!nullable)
                                {
                                    return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': Valor vacío pero la columna no acepta NULL. Tipo SQL: {tipoSQL}";
                                }
                                continue; // Si acepta NULL, está OK
                            }

                            // Intentar parsear como decimal
                            decimal valorDecimal;
                            // Limpiar caracteres comunes
                            string valorLimpio = valorStr.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();

                            if (!decimal.TryParse(valorLimpio, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out valorDecimal))
                            {
                                string mensajeError = $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': El valor '{valorStr}' no se puede convertir a {tipoSQL}. " +
                                       $"Valor en DataTable: '{valor}' (Tipo: {valor?.GetType().Name ?? "null"})";
                                LogToFile($"❌ ERROR DETECTADO EN VALIDACIÓN: {mensajeError}");
                                return mensajeError;
                            }
                        }
                        // Validar columnas de tipo int
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

                return null; // Todo OK
            }
            catch (Exception ex)
            {
                return $"Error en validación: {ex.Message}";
            }
        }

        private void CrearTablaSiNoExiste(SqlConnection conn, DataTable tabla)
        {
            int idEmpresa = UsuarioSesionHelper.UsuarioActual?.IdEmpresa ?? 0;
            int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

            // Construir columnas dinámicas del Excel
            var columnasExcel = tabla.Columns.Cast<DataColumn>()
                                  .Select(c => $"[{c.ColumnName}] VARCHAR(MAX)");

            // Agregar columnas fijas de auditoría
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
        private void RegistrarAuditoria(SqlConnection conn, string nombreArchivo, int idEmpresaArchivo)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return;

            string nombreEmpresa = _empresaService.ObtenerEmpresas()
                                                   .FirstOrDefault(e => e.Id == idEmpresaArchivo)?.Nombre ?? "Desconocida";

            string sql = @"
        INSERT INTO dbo.AuditoriaCargues (FechaCargue, IdUsuario, Usuario, IdEmpresa, NombreEmpresa, NombreArchivo)
        VALUES (@Fecha, @IdUsuario, @Usuario, @IdEmpresa, @NombreEmpresa, @NombreArchivo)
    ";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Fecha", DateTime.Now);
                cmd.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                cmd.Parameters.AddWithValue("@Usuario", usuario.Nombre+" "+usuario.Apellidos ?? "");
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresaArchivo);
                cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);

                cmd.ExecuteNonQuery();
            }
        }


        private string ObtenerUltimoUsuarioCargue(int idEmpresa)
        {
            string ultimoCargue = "";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
            SELECT  TOP 1 AuditoriaCargues.Usuario, FechaCargue, NombreEmpresa AS EmpNombre
            FROM  AuditoriaCargues
            WHERE AuditoriaCargues.IdEmpresa = @IdEmpresa
            ORDER BY FechaCargue DESC
        ", conn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string usuario = reader["Usuario"].ToString();
                        DateTime fecha = Convert.ToDateTime(reader["FechaCargue"]);
                        string empresa = reader["EmpNombre"].ToString();

                        ultimoCargue = $"Última carga de {empresa}: {fecha:dd/MM/yyyy HH:mm:ss} por {usuario}";
                    }
                }
            }

            return ultimoCargue;
        }



        private string NormalizarNombre(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return texto;

            // Pasar a FormD para separar letras y diacríticos (tildes)
            var normalized = texto.Normalize(System.Text.NormalizationForm.FormD);

            // Quitar diacríticos (acentos, tildes, etc.)
            var chars = normalized
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                            System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray();

            var sinTildes = new string(chars);

            // Reemplazar ñ/Ñ por n/N
            sinTildes = sinTildes.Replace("ñ", "n").Replace("Ñ", "N");

            // Quitar espacios y caracteres raros
            return sinTildes
                .Replace(" ", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("/", "")
                .Trim();
        }


    }
}
