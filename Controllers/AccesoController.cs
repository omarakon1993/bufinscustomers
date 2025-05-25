using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using System.IO;
using System.ComponentModel;
using System.Web.Script.Serialization;

namespace bufinscustomers.Controllers
{
    public class AccesoController : Controller
    {
        //static string cadena = "Data Source=190.90.160.168\\MSSQLSERVER2016;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";
        static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        // GET: Acceso
        public ActionResult Login()
        {
            return View();
        }

        public ActionResult Registrar()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Registrar(Usuarios oUsuario)
        {
            bool registrado;
            string mensaje;

            // ⚡ Elimina espacios de correo y clave
            oUsuario.Correo = oUsuario.Correo.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();
            oUsuario.ConfirmarClave = oUsuario.ConfirmarClave.Trim();

            if (oUsuario.Clave == oUsuario.ConfirmarClave)
            {
                oUsuario.Clave = ConvertirSha256(oUsuario.Clave);
            }
            else
            {
                ViewData["Mensaje"] = "Las contraseñas no coinciden";
                return View();
            }

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarUsuario", cn);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.Parameters.Add("Registrado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("Mensaje", SqlDbType.VarChar, 100).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                registrado = Convert.ToBoolean(cmd.Parameters["Registrado"].Value);
                mensaje = cmd.Parameters["Mensaje"].Value.ToString();
            }

            ViewData["Mensaje"] = mensaje;
            if (registrado)
            {
                return RedirectToAction("Login", "Acceso");
            }
            else
            {
                return View();
            }
        }

        [HttpPost]
        public ActionResult Login(Usuarios oUsuario)
        {
            // ⚡ Elimina espacios
            oUsuario.Correo = oUsuario.Correo.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();

            if (!EsCorreoValido(oUsuario.Correo))
            {
                ViewData["Mensaje"] = "El correo ingresado no tiene un formato válido.";
                return View();
            }

            // Luego convierte
            oUsuario.Clave = ConvertirSha256(oUsuario.Clave);

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_ValidarUsuario", cn);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                oUsuario.Id = Convert.ToInt32(cmd.ExecuteScalar().ToString());
            }

            if (oUsuario.Id != 0)
            {
                Session["usuario"] = oUsuario;
                return RedirectToAction("index", "Home");
            }
            else
            {
                ViewData["Mensaje"] = "usuario no encontrado";
                return View();
            }
        }

        private bool EsCorreoValido(string correo)
        {
            // Expresión regular para validar correo
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            Regex regex = new Regex(pattern);
            return regex.IsMatch(correo);
        }

        public static string ConvertirSha256(string texto)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(texto));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2")); // Hexadecimal minúscula
                }
                return builder.ToString();
            }
        }

        [HttpPost]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel)
        {
            if (archivoExcel == null || archivoExcel.ContentLength == 0)
            {
                TempData["Mensaje"] = "No se seleccionó ningún archivo.";
                TempData["MensajeTipo"] = "error";
                return RedirectToAction("CargueExcel", "Home");
            }

            try
            {
                var tablasExcel = new List<(string nombre, DataTable tabla)>(); // lista de todas las tablas

                using (var package = new ExcelPackage(archivoExcel.InputStream))
                {
                    var totalHojas = package.Workbook.Worksheets.Count;
                    string prefijoTabla = (totalHojas == 21) ? "Z_" : "X_";

                    foreach (var hoja in package.Workbook.Worksheets)
                    {
                        var dt = new DataTable(prefijoTabla + hoja.Name);
                        int totalCols = hoja.Dimension?.End.Column ?? 0;
                        int totalRows = hoja.Dimension?.End.Row ?? 0;

                        if (totalCols == 0 || totalRows == 0)
                            continue;

                        // Columnas
                        for (int col = 1; col <= totalCols; col++)
                        {
                            var colName = hoja.Cells[1, col].Text.Trim();
                            if (!dt.Columns.Contains(colName) && !string.IsNullOrWhiteSpace(colName))
                                dt.Columns.Add(colName);
                        }

                        // Filas
                        for (int row = 2; row <= totalRows; row++)
                        {
                            var dr = dt.NewRow();
                            for (int col = 1; col <= totalCols; col++)
                            {
                                dr[col - 1] = hoja.Cells[row, col].Text;
                            }
                            dt.Rows.Add(dr);
                        }

                        GuardarEnSQLServer(dt); // guardar en SQL Server
                        tablasExcel.Add((dt.TableName, dt)); // guardar para mostrar
                    }

                    var resultadoValidacion = ValidarPlantilla();

                    if (resultadoValidacion.CodMessage == 0)
                    {
                        TempData["Mensaje"] = resultadoValidacion.Message;
                        TempData["MensajeTipo"] = "error";

                        // Guardamos las tablas para mostrarlas en la siguiente vista
                        TempData["TablasExcel"] = null;
                    }
                    else
                    {
                        TempData["Mensaje"] = resultadoValidacion.Message;
                        TempData["MensajeTipo"] = "success";

                        TempData["TablasExcel"] = tablasExcel;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["Mensaje"] = $"Error al procesar el archivo: {ex.Message}";
                TempData["MensajeTipo"] = "error";
            }

            return RedirectToAction("CargueExcel", "Home");
        }

        private (int CodMessage, string Message) ValidarPlantilla()
        {
            using (SqlConnection conn = new SqlConnection(cadena))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("dbo.SP_ValidarPlantillaInicial", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int cod = Convert.ToInt32(reader["CodMessage"]);
                            string msg = reader["Message"].ToString();
                            return (cod, msg);
                        }
                    }
                }
            }

            return (0, "Error desconocido en validación.");
        }

        private void GuardarEnSQLServer(DataTable tabla)
        {
            using (SqlConnection conn = new SqlConnection(cadena))
            {
                conn.Open();
                CrearTablaSiNoExiste(conn, tabla);

                using (SqlBulkCopy bulk = new SqlBulkCopy(conn))
                {
                    // Forzar esquema dbo
                    bulk.DestinationTableName = $"[dbo].[{tabla.TableName}]";
                    bulk.WriteToServer(tabla);
                }
            }
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