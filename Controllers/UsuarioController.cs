using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Services;
using bufinscustomers.Helpers;
using System.IO;

namespace bufinscustomers.Controllers
{
    public class UsuarioController : BaseController
    {
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

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
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
                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                {
                    using (SqlCommand command = new SqlCommand("sp_EliminarUsuario", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Id", idUsuario);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                SetSuccessMessage("Usuario eliminado correctamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar el usuario: " + ex.Message);
            }

            return RedirectToAction("Usuarios");
        }

        [HttpPost]
        public ActionResult EditarUsuario(Usuarios oUsuario)
        {
            oUsuario.Telefono = oUsuario.Telefono == null ? "" : oUsuario.Telefono;
            try
            {
                using (SqlConnection connection = new SqlConnection(CadenaConexion))
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

                SetSuccessMessage("Usuario actualizado correctamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al actualizar el usuario: " + ex.Message);
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
                SetErrorMessage("El nombre de usuario debe ser en minúsculas, sin espacios, puede contener números y puntos, y debe tener entre 4 y 20 caracteres.");
                return RedirectToAction("Usuarios");
            }


            // Elimina espacios de campos
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
                SetErrorMessage("Las contraseñas no coinciden");
                return RedirectToAction("Usuarios");
            }

            if (oUsuario.Admin == null)
            {
                oUsuario.Admin = 0;
            }

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
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
                SetSuccessMessage(mensaje);
            }
            else
            {
                SetErrorMessage(mensaje);
            }

            return RedirectToAction("Usuarios");         
        }

        [HttpPost]
        public ActionResult CambiarClave(int idUsuario, string nuevaClave, string confirmarNuevaClave)
        {
            // Validar que las claves coincidan
            if (nuevaClave.Trim() != confirmarNuevaClave.Trim())
            {
                SetErrorMessage("Las contraseñas no coinciden.");
                return RedirectToAction("Usuarios");
            }

            try
            {
                // Encriptar la nueva clave
                string claveEncriptada = ConvertirSha256(nuevaClave.Trim());

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                {
                    using (SqlCommand command = new SqlCommand("sp_CambiarClaveUsuario", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@IdUsuario", idUsuario);
                        command.Parameters.AddWithValue("@NuevaClave", claveEncriptada);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                SetSuccessMessage("Clave actualizada correctamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al cambiar la clave: " + ex.Message);
            }

            return RedirectToAction("Usuarios");
        }

        private bool EsUsuarioValido(string usuario)
        {
            // Solo letras minúsculas, números y puntos, sin espacios, empieza con letra, 4-20 caracteres
            return System.Text.RegularExpressions.Regex.IsMatch(usuario, @"^[a-z][a-z0-9.]{3,19}$");
        }

        [HttpPost]
        public ActionResult CargarImagenUsuario(HttpPostedFileBase ImagenUsuario)
        {
            var base64Copia = "";
            var tipoImagenCopia = "";

            if (ImagenUsuario != null && ImagenUsuario.ContentLength > 0)
            {
                // Convertir la imagen a base64
                using (var ms = new MemoryStream())
                {
                    ImagenUsuario.InputStream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    var base64 = Convert.ToBase64String(bytes);
                    var tipoImagen = ImagenUsuario.ContentType;
                    var nombreImagen = Path.GetFileName(ImagenUsuario.FileName);
                    var usuarioId = UsuarioSesionHelper.UsuarioActual.Id;

                    base64Copia = base64;
                    tipoImagenCopia = tipoImagen;

                    using (SqlConnection connection = new SqlConnection(CadenaConexion))
                    {
                        using (SqlCommand command = new SqlCommand("sp_GuardarImagenUsuario", connection))
                        {
                            command.CommandType = CommandType.StoredProcedure;
                            command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                            command.Parameters.AddWithValue("@NombreImagen", nombreImagen);
                            command.Parameters.AddWithValue("@TipoImagen", tipoImagen);
                            command.Parameters.AddWithValue("@ImagenBase64", base64);

                            connection.Open();
                            command.ExecuteNonQuery();
                        }
                    }
                }

                return Json(new { success = true, message = "Imagen de usuario actualizada correctamente.", tipoImagen = tipoImagenCopia, imagenBase64 = base64Copia });
            }
            else
            {
                return Json(new { success = false, message = "Por favor, selecciona una imagen válida." });
            }
        }
    }
}
