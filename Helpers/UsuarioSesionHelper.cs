using bufinscustomers.Models;
using System.Data.SqlClient;
using System.Windows.Media.Media3D;
using System;

namespace bufinscustomers.Helpers
{
    public static class UsuarioSesionHelper
    {
        static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        public static Usuarios UsuarioActual
        {
            get
            {
                var context = System.Web.HttpContext.Current;
                if (context == null || context.Session == null)
                    return null;

                var idUsuario = context.Session["IdUsuario"] as int?;
                if (idUsuario == null)
                    return null;

                // Aquí puedes cachear el usuario en sesión si lo deseas
                if (context.Items["UsuarioActual"] is Usuarios usuarioCacheado)
                    return usuarioCacheado;

                var usuario = ObtenerUsuarioPorId(idUsuario.Value);
                context.Items["UsuarioActual"] = usuario; // Cachear por request
                return usuario;
            }
        }


        private static Usuarios ObtenerUsuarioPorId(int idUsuario)
        {
            Usuarios usuario = null;

            using (SqlConnection connection = new SqlConnection(cadena))
            {
                string query = @"
            SELECT 
                Id,
                Usuario,
                Clave,
                Nombre,
                Apellidos,
                Correo,
                Telefono,
                Admin,
                IdEmpresa
            FROM Usuarios
            WHERE Id = @IdUsuario";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdUsuario", idUsuario);

                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            usuario = new Usuarios
                            {
                                Id = (int)reader["Id"],
                                Usuario = (string)reader["Usuario"],
                                Clave = (string)reader["Clave"],
                                Nombre = (string)reader["Nombre"],
                                Apellidos = (string)reader["Apellidos"],
                                Correo = (string)reader["Correo"],
                                Telefono = reader["Telefono"] != DBNull.Value ? (int?)reader["Telefono"] : null,
                                Admin = reader["Admin"] != DBNull.Value ? (byte?)reader["Admin"] : null,
                                IdEmpresa = reader["IdEmpresa"] != DBNull.Value ? (int?)reader["IdEmpresa"] : null
                            };
                        }
                    }
                }
            }

            return usuario;
        }
    }
}
