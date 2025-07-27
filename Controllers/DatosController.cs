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
using System.Web;
using System.Web.Mvc;
using System.Windows.Media.Media3D;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class DatosController : Controller
    {
        private readonly EmpresaService _empresaService = new EmpresaService();
        static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        // Modelo actions
        public ActionResult Modelo()
        {
            var empresas = _empresaService.ObtenerEmpresas();
            return View("~/Views/Datos/Modelo.cshtml", empresas);
        }

        [HttpPost]
        public ActionResult EjecutarModelo(int idEmpresa)
        {
            try
            {
                var usuarioSession = (Usuarios)Session["usuario"];
                if (usuarioSession == null)
                {
                    TempData["ErrorMessage"] = "Sesión no válida. Por favor, inicie sesión nuevamente.";
                    return RedirectToAction("Login", "Acceso");
                }

                using (SqlConnection connection = new SqlConnection(cadena))
                {
                    using (SqlCommand command = new SqlCommand("sp_EjecutarModelo_Balance", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        command.Parameters.AddWithValue("@IdUsuario", usuarioSession.Id);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "Modelo ejecutado correctamente.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error al ejecutar el modelo: " + ex.Message;
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
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel, string accion)
        {
            // Validar si hay archivo o si está en sesión
            if ((archivoExcel == null || archivoExcel.ContentLength == 0) && Session["ArchivoExcelBytes"] == null)
            {
                TempData["Mensaje"] = "No se seleccionó ningún archivo.";
                TempData["MensajeTipo"] = "error";
                return RedirectToAction("CargueExcel", "Datos");
            }

            // Guardar archivo en sesión si viene en la petición
            if (archivoExcel != null && archivoExcel.ContentLength > 0)
            {
                using (var ms = new MemoryStream())
                {
                    archivoExcel.InputStream.CopyTo(ms);
                    Session["ArchivoExcelBytes"] = ms.ToArray();
                    Session["ArchivoExcelNombre"] = Path.GetFileName(archivoExcel.FileName);
                }
            }

            try
            {
                var tablasExcel = new List<(string nombre, DataTable tabla)>();

                // Definir el stream para ExcelPackage
                Stream archivoStream;

                if (archivoExcel != null && archivoExcel.ContentLength > 0)
                {
                    // Si está el archivo en la petición
                    archivoStream = archivoExcel.InputStream;
                }
                else
                {
                    // Si no, cargar desde la sesión
                    var bytes = (byte[])Session["ArchivoExcelBytes"];
                    archivoStream = new MemoryStream(bytes);
                }

                using (var package = new ExcelPackage(archivoStream))
                {
                    var totalHojas = package.Workbook.Worksheets.Count;
                    var idEmpresa = UsuarioSesionHelper.UsuarioActual?.IdEmpresa ?? 0;
                    string prefijoTabla = $"_IdEmpresa_{idEmpresa}";
                    //string prefijoTabla = (totalHojas == 21) ? "Z_" : "X_";

                    foreach (var hoja in package.Workbook.Worksheets)
                    {
                        int totalCols = hoja.Dimension?.End.Column ?? 0;
                        int totalRows = hoja.Dimension?.End.Row ?? 0;

                        // Detectar la primera fila con datos (cabecera)
                        int filaCabecera = 1;

                        // Validar que hay al menos una celda con texto en la fila cabecera
                        bool filaCabeceraValida = false;
                        for (int col = 1; col <= totalCols; col++)
                        {
                            if (!string.IsNullOrWhiteSpace(hoja.Cells[filaCabecera, col].Text))
                            {
                                filaCabeceraValida = true;
                                break;
                            }
                        }

                        if (!filaCabeceraValida)
                            continue;

                        // Contar columnas válidas desde la fila cabecera
                        int columnasValidas = 0;
                        for (int col = 1; col <= totalCols; col++)
                        {
                            var nombreColumna = hoja.Cells[filaCabecera, col].Text.Trim();
                            if (!string.IsNullOrWhiteSpace(nombreColumna))
                                columnasValidas++;
                            else
                                break; // dejamos de contar cuando hay una vacía (lógica típica de tabla)
                        }

                        if (columnasValidas == 0)
                            continue;

                        // Crear DataTable con las columnas válidas
                        var dt = new DataTable(hoja.Name + $"_IdEmpresa_{idEmpresa}");
                        for (int col = 1; col <= columnasValidas; col++)
                        {
                            string colName = hoja.Cells[filaCabecera, col].Text.Trim();
                            dt.Columns.Add(colName);
                        }

                        // Cargar filas debajo de la cabecera (hasta que detecte fila vacía)
                        for (int row = filaCabecera + 1; row <= totalRows; row++)
                        {
                            bool filaVacia = true;
                            var dr = dt.NewRow();
                            for (int col = 1; col <= columnasValidas; col++)
                            {
                                var valor = hoja.Cells[row, col].Text;
                                if (!string.IsNullOrWhiteSpace(valor))
                                    filaVacia = false;

                                dr[col - 1] = valor;
                            }

                            if (filaVacia)
                                break; // detenemos al encontrar fila vacía (típico en tablas)

                            dt.Rows.Add(dr);
                        }

                        var modelo = new List<(string nombre, DataTable tabla)>();

                        if (accion == "Importar")
                        {
                            bool exito = GuardarEnSQLServer(dt);

                            if (exito)
                            {
                                var resultado = resultadoValidaciondeDatos();
                            }
                            else
                            {
                                Session["Mensaje"] = "  |Error al importar los datos. No se pudo crear la tabla.";
                                Session["MensajeTipo"] = "error";
                            }
                        }

                        else if (accion == "RetornoTablaDeDatos")
                        {
                            tablasExcel.Add((dt.TableName, dt));
                            Session["TablasExcel"] = tablasExcel;
                            TempData["MostrarBotonImportar"] = true;
                        }
                    }
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
                TempData.Remove("Mensaje");
                TempData.Remove("MensajeTipo");
                TempData.Remove("MostrarBotonImportar");
            }

            var modelo = Session["TablasExcel"] as List<(string nombre, DataTable tabla)> ?? new List<(string nombre, DataTable tabla)>();

            Session.Remove("TablasExcel");

            return View(modelo);
        }

        private bool GuardarEnSQLServer(DataTable tabla)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(cadena))
                {
                    conn.Open();

                    CrearTablaSiNoExiste(conn, tabla);

                    using (SqlBulkCopy bulk = new SqlBulkCopy(conn))
                    {
                        bulk.DestinationTableName = $"[dbo].[{tabla.TableName}]";
                        bulk.WriteToServer(tabla); 
                    }
                }

                return true; 
            }
            catch (Exception ex)
            {
                return false; 
            }
        }

        public List<(string nombre, DataTable tabla)> resultadoValidaciondeDatos()
        {
            var tablasExcel = new List<(string nombre, DataTable tabla)>();
            int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

            using (var conn = new SqlConnection(cadena))
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

        private void CrearTablaSiNoExiste(SqlConnection conn, DataTable tabla)
        {
            var columnas = tabla.Columns.Cast<DataColumn>()
                              .Select(c => $"[{c.ColumnName}] NVARCHAR(MAX)");

            string nombreTabla = $"[dbo].[{tabla.TableName}]"; // Forzar uso del esquema dbo
            string sql = $@"
                            IF OBJECT_ID('{nombreTabla}', 'U') IS NOT NULL 
                                DROP TABLE {nombreTabla};
                            CREATE TABLE {nombreTabla} ({string.Join(", ", columnas)});";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}
