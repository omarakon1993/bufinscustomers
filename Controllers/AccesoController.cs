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
           
                using (var package = new ExcelPackage(archivoExcel.InputStream))
                {
                    // Proceso de lectura y carga de datos desde el archivo Excel
                    foreach (var hoja in package.Workbook.Worksheets)
                    {
                        var dt = new DataTable(hoja.Name);
                        int totalCols = hoja.Dimension.End.Column;
                        int totalRows = hoja.Dimension.End.Row;

                        // Crear las columnas de la DataTable
                        for (int col = 1; col <= totalCols; col++)
                        {
                            var colName = hoja.Cells[1, col].Text.Trim();
                            if (!dt.Columns.Contains(colName) && !string.IsNullOrWhiteSpace(colName))
                                dt.Columns.Add(colName);
                        }

                        // Llenar las filas con los datos del archivo Excel
                        for (int row = 2; row <= totalRows; row++)
                        {
                            var dr = dt.NewRow();
                            for (int col = 1; col <= totalCols; col++)
                            {
                                dr[col - 1] = hoja.Cells[row, col].Text;
                            }
                            dt.Rows.Add(dr);
                        }

                        // Guardar los datos procesados en SQL Server
                        GuardarEnSQLServer(dt);
                    }

                    TempData["Mensaje"] = "Archivo procesado correctamente.";  
                    TempData["MensajeTipo"] = "success";
                }
            }

            catch (Exception ex)
            {
                string mensaje = "Hubo un error al procesar el archivo.";

                if (ex.Message.Contains("not an valid Package file"))
                {
                    mensaje = "El archivo cargado debe ser de tipo .xlsx.";
                }
                else if (ex.Message.Contains("password"))
                {
                    mensaje = "El archivo está protegido con contraseña y no puede ser procesado.";
                }

                TempData["Mensaje"] = mensaje;
                TempData["MensajeTipo"] = "error";
            }

            return RedirectToAction("CargueExcel", "Home");
        }


        private void GuardarEnSQLServer(DataTable tabla)
        {
            using (SqlConnection conn = new SqlConnection(cadena))
            {
                conn.Open();
                CrearTablaSiNoExiste(conn, tabla);

                using (SqlBulkCopy bulk = new SqlBulkCopy(conn))
                {
                    bulk.DestinationTableName = $"[{tabla.TableName}]";
                    bulk.WriteToServer(tabla);
                }
            }
        }

        private void CrearTablaSiNoExiste(SqlConnection conn, DataTable tabla)
        {
            var columnas = tabla.Columns.Cast<DataColumn>()
                              .Select(c => $"[{c.ColumnName}] NVARCHAR(MAX)");
            string sql = $@"
            IF OBJECT_ID('{tabla.TableName}', 'U') IS NOT NULL DROP TABLE [{tabla.TableName}];
            CREATE TABLE [{tabla.TableName}] ({string.Join(", ", columnas)});
        ";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }
}