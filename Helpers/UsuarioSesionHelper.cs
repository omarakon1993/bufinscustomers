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
                                Id = Convert.ToInt32(reader["Id"]),
                                Usuario = Convert.ToString(reader["Usuario"]),
                                Clave = Convert.ToString(reader["Clave"]),
                                Nombre = Convert.ToString(reader["Nombre"]),
                                Apellidos = Convert.ToString(reader["Apellidos"]),
                                Correo = Convert.ToString(reader["Correo"]),
                                Telefono = reader["Telefono"] != DBNull.Value ? Convert.ToInt32(reader["Telefono"]) : (int?)null,
                                Admin = reader["Admin"] != DBNull.Value ? Convert.ToByte(reader["Admin"]) : (byte?)null,
                                IdEmpresa = reader["IdEmpresa"] != DBNull.Value ? Convert.ToInt32(reader["IdEmpresa"]) : (int?)null
                            };
                        }
                    }
                }
            }

            return usuario;
        }

    }
}
