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
            int idUsuario = usuarioActual?.Id ?? 0;

            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                {
                    conn.Open();

                    using (var package = new ExcelPackage(archivoStream))
                    {
                        var empresasArchivo = new List<int>();

                        // =========================
                        // 🔹 1. VALIDAR EMPRESAS EN EL ARCHIVO
                        // =========================
                        var hojaEmpresas = package.Workbook.Worksheets
                            .FirstOrDefault(h => h.Name.Equals("Z_Empresas", StringComparison.OrdinalIgnoreCase));

                        if (hojaEmpresas != null)
                        {
                            int totalCols = hojaEmpresas.Dimension?.End.Column ?? 0;
                            int totalRows = hojaEmpresas.Dimension?.End.Row ?? 0;

                            if (totalCols > 0 && totalRows > 1)
                            {
                                int colEmpresa = -1;
                                for (int c = 1; c <= totalCols; c++)
                                {
                                    if (hojaEmpresas.Cells[1, c].Text.Trim()
                                        .Equals("EMPRESA", StringComparison.OrdinalIgnoreCase))
                                    {
                                        colEmpresa = c;
                                        break;
                                    }
                                }

                                if (colEmpresa > 0)
                                {
                                    for (int r = 2; r <= totalRows; r++)
                                    {
                                        string nombreEmpresa = hojaEmpresas.Cells[r, colEmpresa].Text?.Trim();

                                        if (string.IsNullOrWhiteSpace(nombreEmpresa) ||
                                            nombreEmpresa.Equals("EMPRESA", StringComparison.OrdinalIgnoreCase))
                                            continue; // ✅ Evitar encabezado o filas vacías

                                        var empresaObj = _empresaService.ObtenerEmpresas()
                                            .FirstOrDefault(e => e.Nombre.Equals(nombreEmpresa, StringComparison.OrdinalIgnoreCase));

                                        if (empresaObj == null)
                                            continue; // Empresa no existe → la ignoramos

                                        // Validar que usuario no admin cargue solo su empresa
                                        if ((usuarioActual?.Admin ?? 0) != 1 && empresaObj.Id != usuarioActual?.IdEmpresa)
                                        {
                                            TempData["Mensaje"] = $"Estás intentando cargar información de la empresa '{nombreEmpresa}', " +
                                                                  $"pero solo puedes cargar datos de tu empresa asignada.";
                                            TempData["MensajeTipo"] = "error";
                                            return RedirectToAction("CargueExcel", "Datos", new { limpiar = false });
                                        }

                                        empresasArchivo.Add(empresaObj.Id);
                                    }
                                }
                            }
                        }

                        Session["EmpresasArchivo"] = empresasArchivo;

                        // =========================
                        // 🔹 2. PROCESAR TODAS LAS HOJAS
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
                                if (empresasArchivo != null && empresasArchivo.Any())
                                {
                                    foreach (var empId in empresasArchivo.Distinct())
                                    {
                                        GuardarEnSQLServer(dt.Copy(), empId, idUsuario);
                                    }
                                }
                                else
                                {
                                    GuardarEnSQLServer(dt.Copy(), usuarioActual?.IdEmpresa ?? 0, idUsuario);
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

                            if (empresasArchivo != null && empresasArchivo.Any())
                            {
                                foreach (var empId in empresasArchivo.Distinct())
                                {
                                    RegistrarAuditoria(conn, nombreArchivo, empId);
                                }
                            }
                            else
                            {
                                RegistrarAuditoria(conn, nombreArchivo, usuarioActual?.IdEmpresa ?? 0);
                            }
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

        private bool GuardarEnSQLServer(DataTable tabla, int idEmpresa, int idUsuario)
        {
            try
            {
                DateTime fechaCargue = DateTime.Now;

                using (SqlConnection conn = new SqlConnection(CadenaConexion))
                {
                    conn.Open();

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

                    // 🔹 Insertar lo nuevo
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
                // Aquí podrías loguear ex.Message
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
<<<<<<< HEAD
                cmd.Parameters.AddWithValue("@Usuario", usuario.Nombre ?? "");
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresaArchivo);
=======
                cmd.Parameters.AddWithValue("@Usuario", usuario.Nombre+" "+usuario.Apellidos ?? "");
                cmd.Parameters.AddWithValue("@IdEmpresa", usuario.IdEmpresa);
>>>>>>> master
                cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);

                cmd.ExecuteNonQuery();
            }
        }


        private string ObtenerUltimoUsuarioCargue()
        {
<<<<<<< HEAD
            string ultimoCargue = "N/A";
=======
            string ultimoUsuario = "";
>>>>>>> master

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                    SELECT TOP 1 Usuario, FechaCargue
                    FROM AuditoriaCargues
                    ORDER BY FechaCargue DESC
                ", conn))
            {
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string usuario = reader["Usuario"].ToString();
                        DateTime fecha = Convert.ToDateTime(reader["FechaCargue"]);

                        ultimoCargue = $"Última carga realizada: {fecha:dd/MM/yyyy HH:mm:ss} por {usuario}";
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
