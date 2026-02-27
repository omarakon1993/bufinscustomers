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
        private readonly ModeloService _modeloService = new ModeloService();
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
                empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();

            var vm = new Models.ModeloPageViewModel
            {
                Empresas = empresas,
                Modelos = _modeloService.ObtenerModelosActivos()
            };
            return View("~/Views/Datos/Modelo.cshtml", vm);
        }

        [HttpGet]
        public ActionResult ObtenerAnioEjecucion(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                int anio = config != null ? config.AnioEjecucion : DateTime.Now.Year;

                return Json(new { success = true, anioEjecucion = anio }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener el año: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public ActionResult EjecutarModelo(int idEmpresa, string anio, int idModelo)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                {
                    SetErrorMessage("Sesión no válida. Por favor, inicie sesión nuevamente.");
                    return RedirectToAction("Login", "Acceso");
                }

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                {
                    SetErrorMessage("No tiene permisos para ejecutar el modelo en esta empresa.");
                    return RedirectToAction("Modelo");
                }

                // El nombre del SP viene SIEMPRE de la BD, nunca del form input
                var modelo = _modeloService.ObtenerModeloPorId(idModelo);
                if (modelo == null)
                {
                    SetErrorMessage("El modelo seleccionado no existe o no está activo.");
                    return RedirectToAction("Modelo");
                }

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                using (SqlCommand command = new SqlCommand(modelo.NombreSP, connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    command.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                    command.Parameters.AddWithValue("@Año", anio);
                    command.CommandTimeout = 300;

                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        // El SP puede retornar múltiples result sets intermedios.
                        // Iteramos todos y nos quedamos con los valores del último.
                        int codMessage = 0;
                        string mensaje = "No se recibió respuesta del procedimiento.";
                        bool leido = false;

                        do
                        {
                            while (reader.Read())
                            {
                                try
                                {
                                    codMessage = Convert.ToInt32(reader["CodMessage"]);
                                    mensaje = reader["ErrorMessage"].ToString();
                                    leido = true;
                                }
                                catch { /* result set intermedio sin columnas de estado */ }
                            }
                        } while (reader.NextResult());

                        if (leido)
                        {
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

        public ActionResult CargueExcel()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            int idEmpresa = usuario?.IdEmpresa ?? 0;

            List<Empresas> empresasDisponibles;
            if (UsuarioSesionHelper.EsSuperAdmin())
            {
                empresasDisponibles = _empresaService.ObtenerEmpresas();
            }
            else
            {
                var empresaUsuario = _empresaService.ObtenerEmpresas()
                    .FirstOrDefault(e => e.Id == idEmpresa);
                empresasDisponibles = empresaUsuario != null
                    ? new List<Empresas> { empresaUsuario }
                    : new List<Empresas>();
            }
            ViewBag.Empresas = empresasDisponibles;
            ViewBag.UltimoUsuarioCargue = ObtenerUltimoUsuarioCargue(idEmpresa);

            return View("~/Views/Datos/CargueExcel.cshtml");
        }

        [HttpPost]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel, int idEmpresaSeleccionada)
        {
            var resultado = new ResultadoCargaExcel();
            string nombreArchivoOriginal = "";

            // Validar empresa seleccionada
            if (idEmpresaSeleccionada == 0)
            {
                SetErrorMessage("Debe seleccionar una empresa antes de cargar el archivo.");
                return RedirectToAction("CargueExcel");
            }

            // Validar configuración de empresa
            if (!_configuracionService.ExisteConfiguracion(idEmpresaSeleccionada))
            {
                SetErrorMessage("La empresa seleccionada no tiene configuración creada.");
                return RedirectToAction("CargueExcel");
            }

            // Validar archivo
            if (archivoExcel == null || archivoExcel.ContentLength == 0)
            {
                SetErrorMessage("No se seleccionó ningún archivo.");
                return RedirectToAction("CargueExcel");
            }

            string extension = Path.GetExtension(archivoExcel.FileName)?.ToLower();
            if (extension != ".xlsx" && extension != ".xlsm")
            {
                SetErrorMessage("Solo se permiten archivos Excel (.xlsx, .xlsm).");
                return RedirectToAction("CargueExcel");
            }

            nombreArchivoOriginal = Path.GetFileName(archivoExcel.FileName);
            var usuarioActual = UsuarioSesionHelper.UsuarioActual;
            int idUsuario = usuarioActual?.Id ?? 0;

            try
            {
                using (var memStream = new MemoryStream())
                {
                    archivoExcel.InputStream.CopyTo(memStream);
                    memStream.Position = 0;

                    using (var package = new ExcelPackage(memStream))
                    {
                        // Validar cantidad de hojas
                        if (package.Workbook.Worksheets.Count != 10)
                        {
                            resultado.Exito = false;
                            resultado.Mensaje = $"La plantilla debe contener exactamente 10 hojas. El archivo tiene {package.Workbook.Worksheets.Count} hojas.";
                            TempData["ResultadoCarga"] = resultado;
                            TempData["NombreArchivo"] = nombreArchivoOriginal;
                            SetErrorMessage(resultado.Mensaje);
                            return RedirectToAction("CargueExcel");
                        }

                        using (var conn = new SqlConnection(CadenaConexion))
                        {
                            conn.Open();

                            foreach (var hoja in package.Workbook.Worksheets)
                            {
                                var detalle = new DetalleCargaHojaExcel
                                {
                                    NombreHoja = hoja.Name
                                };

                                int totalCols = hoja.Dimension?.End.Column ?? 0;
                                int totalRows = hoja.Dimension?.End.Row ?? 0;

                                if (totalCols == 0 || totalRows == 0)
                                {
                                    detalle.NombreTabla = "-";
                                    detalle.Estado = "Ignorada";
                                    detalle.MensajeError = "La hoja está vacía";
                                    resultado.DetalleHojas.Add(detalle);
                                    resultado.TotalHojasIgnoradas++;
                                    continue;
                                }

                                // Validar cabecera
                                bool filaCabeceraValida = false;
                                for (int col = 1; col <= totalCols; col++)
                                {
                                    if (!string.IsNullOrWhiteSpace(hoja.Cells[1, col].Text))
                                    {
                                        filaCabeceraValida = true;
                                        break;
                                    }
                                }
                                if (!filaCabeceraValida)
                                {
                                    detalle.NombreTabla = "-";
                                    detalle.Estado = "Ignorada";
                                    detalle.MensajeError = "La hoja no tiene encabezados válidos";
                                    resultado.DetalleHojas.Add(detalle);
                                    resultado.TotalHojasIgnoradas++;
                                    continue;
                                }

                                // Contar columnas válidas (hasta la primera vacía)
                                int columnasValidas = 0;
                                for (int col = 1; col <= totalCols; col++)
                                {
                                    if (!string.IsNullOrWhiteSpace(hoja.Cells[1, col].Text.Trim()))
                                        columnasValidas++;
                                    else
                                        break;
                                }
                                if (columnasValidas == 0)
                                {
                                    detalle.NombreTabla = "-";
                                    detalle.Estado = "Ignorada";
                                    detalle.MensajeError = "No se encontraron columnas con encabezado";
                                    resultado.DetalleHojas.Add(detalle);
                                    resultado.TotalHojasIgnoradas++;
                                    continue;
                                }

                                var nombreTabla = NormalizarNombre(hoja.Name);
                                detalle.NombreTabla = nombreTabla;
                                detalle.TotalColumnas = columnasValidas;

                                var dt = new DataTable(nombreTabla);
                                for (int col = 1; col <= columnasValidas; col++)
                                    dt.Columns.Add(hoja.Cells[1, col].Text.Trim());

                                // Leer filas con detección de fin real de datos
                                // Si hay 5+ filas vacías consecutivas, se considera fin de datos
                                int filasVaciasConsecutivas = 0;
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

                                    if (filaVacia)
                                    {
                                        filasVaciasConsecutivas++;
                                        if (filasVaciasConsecutivas >= 5) break;
                                        continue;
                                    }

                                    filasVaciasConsecutivas = 0;
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

                                // Guardar en SQL Server
                                LogToFile($"========== INICIO GUARDADO TABLA: {nombreTabla} ==========");
                                LogToFile($"Filas en DataTable: {dt.Rows.Count} (Dimension.End.Row era: {totalRows})");

                                if (!GuardarEnSQLServer(dt.Copy(), idEmpresaSeleccionada, idUsuario))
                                {
                                    // Error al guardar - detener todo y limpiar
                                    detalle.Estado = "Error";
                                    detalle.MensajeError = TempData["Mensaje"]?.ToString() ?? "Error al guardar";
                                    resultado.DetalleHojas.Add(detalle);

                                    // Marcar hojas ya exitosas como revertidas
                                    foreach (var d in resultado.DetalleHojas.Where(x => x.Estado == "Exitoso"))
                                    {
                                        d.Estado = "Revertido";
                                        d.MensajeError = "Revertido por error en otra tabla";
                                    }

                                    resultado.Exito = false;
                                    resultado.Mensaje = $"Error al procesar '{hoja.Name}'. Se limpiaron todos los datos.";
                                    resultado.MostrarDescargaLog = TempData["MostrarDescargaLog"] != null && (bool)TempData["MostrarDescargaLog"];

                                    TempData["ResultadoCarga"] = resultado;
                                    TempData["NombreArchivo"] = nombreArchivoOriginal;
                                    SetErrorMessage(resultado.Mensaje);
                                    return RedirectToAction("CargueExcel");
                                }

                                detalle.FilasInsertadas = dt.Rows.Count;
                                detalle.Estado = "Exitoso";
                                resultado.DetalleHojas.Add(detalle);
                                resultado.TotalHojasProcesadas++;
                                resultado.TotalFilasInsertadas += dt.Rows.Count;
                            }

                            // Registrar auditoría
                            RegistrarAuditoria(conn, nombreArchivoOriginal, idEmpresaSeleccionada);
                        }
                    }
                }

                // Ejecutar validación SP
                var resultadoValidacion = EjecutarValidacionDatos(idUsuario);

                if (resultadoValidacion.esExitoso)
                {
                    resultado.Exito = true;
                    resultado.Mensaje = $"Carga exitosa: {resultado.TotalHojasProcesadas} tabla(s) con {resultado.TotalFilasInsertadas:N0} registros. {resultadoValidacion.mensaje}";
                }
                else
                {
                    resultado.Exito = false;
                    resultado.Mensaje = resultadoValidacion.mensaje;
                    resultado.MostrarDescargaLog = true;
                }

                GuardarLogEnSession();
                TempData["ResultadoCarga"] = resultado;
                TempData["NombreArchivo"] = nombreArchivoOriginal;

                if (resultado.Exito)
                    SetSuccessMessage(resultado.Mensaje);
                else
                    SetErrorMessage(resultado.Mensaje);
            }
            catch (Exception ex)
            {
                resultado.Exito = false;
                resultado.Mensaje = $"Error al procesar el archivo: {ex.Message}";
                GuardarLogEnSession();
                TempData["ResultadoCarga"] = resultado;
                TempData["NombreArchivo"] = nombreArchivoOriginal;
                SetErrorMessage(resultado.Mensaje);
            }

            return RedirectToAction("CargueExcel");
        }

        private (bool esExitoso, string mensaje) EjecutarValidacionDatos(int idUsuario)
        {
            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("dbo.SP_ValidarPlantillaInicial", conn))
                using (var adapter = new SqlDataAdapter(cmd))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);

                    var dt = new DataTable();
                    adapter.Fill(dt);

                    if (dt.Rows.Count == 1 &&
                        int.TryParse(dt.Rows[0]["CodMessage"]?.ToString(), out int codMessage) &&
                        codMessage == 1)
                    {
                        return (true, dt.Rows[0]["ErrorMessage"]?.ToString() ?? "Validación exitosa.");
                    }
                    else
                    {
                        // Construir detalle de errores
                        var errores = new StringBuilder();
                        errores.AppendLine("Se detectaron errores en la validación:");
                        foreach (DataRow row in dt.Rows)
                        {
                            var valores = row.ItemArray.Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v));
                            errores.AppendLine(string.Join(" | ", valores));
                        }
                        return (false, errores.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Error en SP_ValidarPlantillaInicial: {ex.Message}");
                return (true, "Datos cargados (no se pudo ejecutar validación adicional).");
            }
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

                // Mostrar primeras 3 filas como muestra
                for (int i = 0; i < Math.Min(3, tabla.Rows.Count); i++)
                {
                    var valores = tabla.Rows[i].ItemArray.Select(v => v?.ToString() ?? "NULL").ToList();
                    LogToFile($"Fila {i + 1} (muestra): {string.Join(" | ", valores)}");
                }

                // Convertir valores vacíos a 0 en columnas money/decimal
                LogToFile("Convirtiendo valores vacíos a 0 en columnas de dinero...");
                ConvertirValoresVaciosAZero(conn, tabla);

                // Validar datos antes de insertar
                LogToFile("Iniciando validación de datos...");
                string errorValidacion = ValidarDatosParaBulkCopy(conn, tabla);
                if (!string.IsNullOrEmpty(errorValidacion))
                {
                    LogToFile($"ERROR EN VALIDACIÓN: {errorValidacion}");
                    GuardarLogEnSession();
                    TempData["Mensaje"] = $"ERROR DE VALIDACIÓN en '{tabla.TableName}':\n\n{errorValidacion}";
                    TempData["MensajeTipo"] = "error";
                    TempData["MostrarDescargaLog"] = true;

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
                    GuardarLogEnSession();

                    TempData["Mensaje"] = $"ERROR AL INSERTAR en '{tabla.TableName}':\n\n{detalleError}";
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
                LogToFile($"ERROR GENERAL: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
                GuardarLogEnSession();

                TempData["Mensaje"] = $"ERROR GENERAL al guardar en '{tabla.TableName}':\n{ex.Message}";
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

        private void ConvertirValoresVaciosAZero(SqlConnection conn, DataTable tabla)
        {
            try
            {
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

                        if (dataType == "money" || dataType == "smallmoney" ||
                            dataType == "decimal" || dataType == "numeric")
                        {
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

        private string ValidarDatosParaBulkCopy(SqlConnection conn, DataTable tabla)
        {
            try
            {
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

                LogToFile($"Esquema SQL encontrado para {tabla.TableName}: {columnasSQL.Count} columnas");
                foreach (var col in columnasSQL)
                {
                    LogToFile($"   - {col.Key}: {col.Value.tipo} (Nullable: {col.Value.nullable})");
                }

                if (columnasSQL.Count == 0)
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

                        if (!columnasSQL.ContainsKey(nombreColumna)) continue;

                        var (tipoSQL, nullable) = columnasSQL[nombreColumna];
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
                            string valorLimpio = valorStr.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();

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
            int idEmpresa = UsuarioSesionHelper.UsuarioActual?.IdEmpresa ?? 0;
            int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

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

            var normalized = texto.Normalize(System.Text.NormalizationForm.FormD);

            var chars = normalized
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                            System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray();

            var sinTildes = new string(chars);

            sinTildes = sinTildes.Replace("ñ", "n").Replace("Ñ", "N");

            return sinTildes
                .Replace(" ", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("/", "")
                .Trim();
        }


    }
}
