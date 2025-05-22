using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    public class UsuarioController : Controller
    {
        static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        // GET: Usuario
        public ActionResult Usuarios()
        {
            List<Usuarios> usuarios = GetUsuariosFromStoredProcedure();
            return View(usuarios);
        }

        // ...

        private List<Usuarios> GetUsuariosFromStoredProcedure()
        {
            List<Usuarios> usuarios = new List<Usuarios>();

            using (SqlConnection connection = new SqlConnection(cadena))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerUsuarios", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    //// Agrega los parámetros necesarios si el procedimiento almacenado los requiere
                    //command.Parameters.AddWithValue("@parametro1", valor1);
                    //command.Parameters.AddWithValue("@parametro2", valor2);

                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Usuarios usuario = new Usuarios();
                            usuario.Id = (int)reader["Id"];
                            usuario.Nombre = (string)reader["Nombre"];
                            usuario.Apellidos = (string)reader["Apellidos"];
                            usuario.Correo = (string)reader["Correo"];
                            usuario.Telefono = reader["Telefono"] != DBNull.Value ? (int?)reader["Telefono"] : null;
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
        public ActionResult EditarUsuario(Usuarios usuario)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(cadena))
                {
                    using (SqlCommand command = new SqlCommand("sp_EditarUsuario", connection))
                    {
                        if (usuario.Admin == null)
                        {
                            usuario.Admin = 0;
                        }
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Id", usuario.Id);
                        command.Parameters.AddWithValue("@Nombre", usuario.Nombre);
                        command.Parameters.AddWithValue("@Apellidos", usuario.Apellidos);
                        command.Parameters.AddWithValue("@Correo", usuario.Correo);
                        command.Parameters.AddWithValue("@Telefono", usuario.Telefono);              
                        command.Parameters.AddWithValue("@Admin", usuario.Admin);
                        command.Parameters.AddWithValue("@IdEmpresa", usuario.IdEmpresa);

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


    }
}
