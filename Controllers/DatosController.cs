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
    public class DatosController : BaseController
    {
        private readonly EmpresaService _empresaService = new EmpresaService();

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
                    SetErrorMessage("Sesión no válida. Por favor, inicie sesión nuevamente.");
                    return RedirectToAction("Login", "Acceso");
                }

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
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

                SetSuccessMessage("Modelo ejecutado correctamente.");
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
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel, string accion)
        {
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
            int idEmpresa = usuarioActual?.IdEmpresa ?? 0;
            int idUsuario = usuarioActual?.Id ?? 0;

            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                {
                    conn.Open();

                    // 1. BORRAR TABLAS ANTIGUAS SOLO SI SON DE OTRA EMPRESA
                    string dropSql = $@"
                        DECLARE @sql NVARCHAR(MAX) = '';
                        SELECT @sql += 'DROP TABLE [dbo].[' + name + '];'
                        FROM sys.tables
                        WHERE name LIKE 'Z_%_IdUsuario_{idUsuario}_%'
                          AND name NOT LIKE 'Z_%_IdEmpresa_{idEmpresa}_IdUsuario_{idUsuario}_%';
                        EXEC(@sql);
                    ";
                    using (SqlCommand cmdDrop = new SqlCommand(dropSql, conn))
                    {
                        cmdDrop.ExecuteNonQuery();
                    }

                    // 2. PROCESAR TODAS LAS HOJAS
                    using (var package = new ExcelPackage(archivoStream))
                    {
                        var fechaFormateada = DateTime.Now.ToString("yyyyMMdd");

                        foreach (var hoja in package.Workbook.Worksheets)
                        {
                            int totalCols = hoja.Dimension?.End.Column ?? 0;
                            int totalRows = hoja.Dimension?.End.Row ?? 0;
                            if (totalCols == 0 || totalRows == 0) continue;

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

                            // Crear DataTable
                            //var nombreTabla = $"Z_{hoja.Name}_IdEmpresa_{idEmpresa}_IdUsuario_{idUsuario}_FechaCargue_{fechaFormateada}";
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
                                    var valor = hoja.Cells[row, col].Text;
                                    if (!string.IsNullOrWhiteSpace(valor)) filaVacia = false;
                                    dr[col - 1] = valor;
                                }
                                if (filaVacia) break;
                                dt.Rows.Add(dr);
                            }

                            // Guardar tabla
                            if (accion == "Importar")
                                GuardarEnSQLServer(dt);
                            else if (accion == "RetornoTablaDeDatos")
                                tablasExcel.Add((dt.TableName, dt));
                        }

                        // 3. REGISTRAR AUDITORÍA UNA VEZ
                        if (accion == "Importar")
                        {
                            RegistrarAuditoria(conn, Session["ArchivoExcelNombre"]?.ToString() ?? "Archivo desconocido");
                        }
                    }
                }

                if (accion == "RetornoTablaDeDatos")
                {
                    Session["TablasExcel"] = tablasExcel;
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
                TempData.Remove("Mensaje");
                TempData.Remove("MensajeTipo");
                TempData.Remove("MostrarBotonImportar");
            }

            var modelo = Session["TablasExcel"] as List<(string nombre, DataTable tabla)> ?? new List<(string nombre, DataTable tabla)>();

            // Traer último usuario que cargó
            ViewBag.UltimoUsuarioCargue = ObtenerUltimoUsuarioCargue();

            Session.Remove("TablasExcel");

            return View(modelo);
        }

        private bool GuardarEnSQLServer(DataTable tabla)
        {
            try
            {
                int idEmpresa = UsuarioSesionHelper.UsuarioActual?.IdEmpresa ?? 0;
                int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;
                DateTime fechaCargue = DateTime.Now;

                using (SqlConnection conn = new SqlConnection(CadenaConexion))
                {
                    conn.Open();

                    // 🔹 Normalizar nombre de la tabla
                    tabla.TableName = NormalizarNombre(tabla.TableName);

                    // 🔹 Crear tabla si no existe
                    CrearTablaSiNoExiste(conn, tabla);

                    // 🔹 Agregar columnas extra si no existen
                    if (!tabla.Columns.Contains("IdEmpresa"))
                        tabla.Columns.Add("IdEmpresa", typeof(int));
                    if (!tabla.Columns.Contains("IdUsuario"))
                        tabla.Columns.Add("IdUsuario", typeof(int));
                    if (!tabla.Columns.Contains("FechaCargue"))
                        tabla.Columns.Add("FechaCargue", typeof(DateTime));

                    foreach (DataRow row in tabla.Rows)
                    {
                        row["IdEmpresa"] = idEmpresa;
                        row["IdUsuario"] = idUsuario;
                        row["FechaCargue"] = fechaCargue;
                    }

                    // 🔹 1. Eliminar TODO lo anterior de esa empresa
                    using (SqlCommand deleteCmd = new SqlCommand($@"
                DELETE FROM [dbo].[{tabla.TableName}]
                WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        deleteCmd.ExecuteNonQuery();
                    }

                    // 🔹 2. Insertar lo nuevo con SqlBulkCopy
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
                // Aquí puedes loggear el error
                return false;
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

        private void CrearTablaSiNoExiste(SqlConnection conn, DataTable tabla)
        {
            int idEmpresa = UsuarioSesionHelper.UsuarioActual?.IdEmpresa ?? 0;
            int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

            // Construir columnas dinámicas del Excel
            var columnasExcel = tabla.Columns.Cast<DataColumn>()
                                  .Select(c => $"[{c.ColumnName}] NVARCHAR(MAX)");

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

        private void RegistrarAuditoria(SqlConnection conn, string nombreArchivo)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return;

            string nombreEmpresa = _empresaService.ObtenerEmpresas()
                                                   .FirstOrDefault(e => e.Id == usuario.IdEmpresa)?.Nombre ?? "Desconocida";

            string sql = @"
                INSERT INTO dbo.AuditoriaCargues (FechaCargue, IdUsuario, Usuario, IdEmpresa, NombreEmpresa, NombreArchivo)
                VALUES (@Fecha, @IdUsuario, @Usuario, @IdEmpresa, @NombreEmpresa, @NombreArchivo)
            ";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Fecha", DateTime.Now);
                cmd.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                cmd.Parameters.AddWithValue("@Usuario", usuario.Nombre+" "+usuario.Apellidos ?? "");
                cmd.Parameters.AddWithValue("@IdEmpresa", usuario.IdEmpresa);
                cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);

                cmd.ExecuteNonQuery();
            }
        }
        private string ObtenerUltimoUsuarioCargue()
        {
            string ultimoUsuario = "";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                SELECT TOP 1 Usuario
                FROM AuditoriaCargues
                ORDER BY FechaCargue DESC
            ", conn))
            {
                conn.Open();
                var result = cmd.ExecuteScalar();
                if (result != null && !string.IsNullOrEmpty(result.ToString()))
                    ultimoUsuario = result.ToString();
            }

            return ultimoUsuario;
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
