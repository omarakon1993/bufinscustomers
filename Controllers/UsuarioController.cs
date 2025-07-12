using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Security.Cryptography;
using System.Text;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    public class UsuarioController : Controller
    {
        static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        private EmpresaService _empresaService = new EmpresaService();

        // GET: Usuario
        public ActionResult Usuarios()
        {
            var empresas = _empresaService.ObtenerEmpresas();
            ViewBag.Empresas = empresas;
            List<Usuarios> usuarios = GetUsuariosFromStoredProcedure();           
            return View("~/Views/Configuracion/Usuarios.cshtml", usuarios);
        }


        private List<Usuarios> GetUsuariosFromStoredProcedure()
        {
            List<Usuarios> usuarios = new List<Usuarios>();

            using (SqlConnection connection = new SqlConnection(cadena))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerUsuarios", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Usuarios usuario = new Usuarios();
                            usuario.Id = (int)reader["Id"];
                            usuario.Usuario = (string)reader["Usuario"];
                            usuario.Clave = (string)reader["Clave"];
                            usuario.Nombre = (string)reader["Nombre"];
                            usuario.Apellidos = (string)reader["Apellidos"];
                            usuario.Correo = (string)reader["Correo"];
                            usuario.Telefono = (string)reader["Telefono"];
                            usuario.Admin = reader["Admin"] != DBNull.Value ? (byte?)reader["Admin"] : null;
                            usuario.IdEmpresa = reader["IdEmpresa"] != DBNull.Value ? (int?)reader["IdEmpresa"] : null;
                            usuarios.Add(usuario);
                        }
                    }
                }
            }

            return usuarios;
        }

        [HttpPost]
        public ActionResult EliminarUsuario(int idUsuario)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(cadena))
                {
                    using (SqlCommand command = new SqlCommand("sp_EliminarUsuario", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Id", idUsuario);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "Usuario eliminado correctamente.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error al eliminar el usuario: " + ex.Message;
            }

            return RedirectToAction("Usuarios");
        }

        [HttpPost]
        public ActionResult EditarUsuario(Usuarios oUsuario)
        {
            oUsuario.Telefono = oUsuario.Telefono == null ? "" : oUsuario.Telefono;
            try
            {
                using (SqlConnection connection = new SqlConnection(cadena))
                {
                    using (SqlCommand command = new SqlCommand("sp_EditarUsuario", connection))
                    {
                        if (oUsuario.Admin == null)
                        {
                            oUsuario.Admin = 0;
                        }
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Id", oUsuario.Id);
                        command.Parameters.AddWithValue("@Nombre", oUsuario.Nombre);
                        command.Parameters.AddWithValue("@Apellidos", oUsuario.Apellidos);
                        command.Parameters.AddWithValue("@Correo", oUsuario.Correo);
                        command.Parameters.AddWithValue("@Telefono", oUsuario.Telefono);              
                        command.Parameters.AddWithValue("@Admin", oUsuario.Admin);
                        command.Parameters.AddWithValue("@IdEmpresa", oUsuario.IdEmpresa);
                        command.Parameters.AddWithValue("@Usuario", oUsuario.Usuario);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error al actualizar el usuario: " + ex.Message;
            }

                return RedirectToAction("Usuarios");
        }

        [HttpPost]
        public ActionResult Registrar(Usuarios oUsuario)
        {
            bool registrado;
            string mensaje;

            if (!EsUsuarioValido(oUsuario.Usuario))
            {
                TempData["ErrorMessage"] = "El nombre de usuario debe ser en minúsculas, sin espacios, puede contener números y puntos, y debe tener entre 4 y 20 caracteres.";
                return RedirectToAction("Usuarios");
            }


            // ⚡ Elimina espacios de correo y clave

            oUsuario.Nombre = oUsuario.Nombre == null ? "" : oUsuario.Nombre.Trim();
            oUsuario.Apellidos = oUsuario.Apellidos == null ? "" : oUsuario.Apellidos.Trim();
            oUsuario.Correo = oUsuario.Correo == null ? "" : oUsuario.Correo.Trim();
            oUsuario.Telefono = oUsuario.Telefono == null ? "" : oUsuario.Telefono;
            oUsuario.IdEmpresa = oUsuario.IdEmpresa == null ? 0 : oUsuario.IdEmpresa;


            oUsuario.Usuario = oUsuario.Usuario.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();
            oUsuario.ConfirmarClave = oUsuario.ConfirmarClave.Trim();


            if (oUsuario.Clave == oUsuario.ConfirmarClave)
            {
                oUsuario.Clave = ConvertirSha256(oUsuario.Clave);
            }
            else
            {
                TempData["ErrorMessage"] = "Las contraseñas no coinciden";
                return RedirectToAction("Usuarios");
            }

            if (oUsuario.Admin == null)
            {
                oUsuario.Admin = 0;
            }

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarUsuario", cn);
                cmd.Parameters.AddWithValue("Usuario", oUsuario.Usuario);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.Parameters.AddWithValue("Nombre", oUsuario.Nombre);
                cmd.Parameters.AddWithValue("Apellidos", oUsuario.Apellidos);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Telefono", oUsuario.Telefono);
                cmd.Parameters.AddWithValue("Admin", oUsuario.Admin);
                cmd.Parameters.AddWithValue("IdEmpresa", oUsuario.IdEmpresa);
                cmd.Parameters.Add("Registrado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("Mensaje", SqlDbType.VarChar, 100).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                registrado = Convert.ToBoolean(cmd.Parameters["Registrado"].Value);
                mensaje = cmd.Parameters["Mensaje"].Value.ToString();
            }

            if (registrado)
            {
                TempData["SuccessMessage"] = mensaje;
            }
            else
            {
                TempData["ErrorMessage"] = mensaje;
            }

            return RedirectToAction("Usuarios");         
        }



        [HttpPost]
        public ActionResult CambiarClave(int idUsuario, string nuevaClave, string confirmarNuevaClave)
        {
            // Validar que las claves coincidan
            if (nuevaClave.Trim() != confirmarNuevaClave.Trim())
            {
                TempData["ErrorMessage"] = "Las contraseñas no coinciden.";
                return RedirectToAction("Usuarios");
            }

            try
            {
                // Encriptar la nueva clave
                string claveEncriptada = ConvertirSha256(nuevaClave.Trim());

                using (SqlConnection connection = new SqlConnection(cadena))
                {
                    using (SqlCommand command = new SqlCommand("sp_CambiarClaveUsuario", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;

                        // Agregar parámetros al procedimiento almacenado
                        command.Parameters.AddWithValue("@IdUsuario", idUsuario);
                        command.Parameters.AddWithValue("@NuevaClave", claveEncriptada);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "Clave actualizada correctamente.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error al cambiar la clave: " + ex.Message;
            }

            return RedirectToAction("Usuarios");
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

        private bool EsUsuarioValido(string usuario)
        {
            // Solo letras minúsculas, números y puntos, sin espacios, empieza con letra, 4-20 caracteres
            return System.Text.RegularExpressions.Regex.IsMatch(usuario, @"^[a-z][a-z0-9.]{3,19}$");
        }
    }
}
