using bufinscustomers.Models;
using System;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Servicio para operaciones relacionadas con usuarios
    /// </summary>
    public class UsuarioService : BaseService
    {
        /// <summary>
        /// Obtiene un usuario por su ID
        /// </summary>
        public Usuarios ObtenerUsuarioPorId(int id)
        {
            Usuarios usuario = null;

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                string query = @"
                    SELECT
                        u.Id,
                        u.Usuario,
                        u.Clave,
                        u.Nombre,
                        u.Apellidos,
                        u.Correo,
                        u.Telefono,
                        u.Admin,
                        u.IdEmpresa
                    FROM Usuarios u
                    WHERE u.Id = @Id
                ";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
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
                                Telefono = reader["Telefono"] != DBNull.Value ? Convert.ToString(reader["Telefono"]) : null,
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
