using System;
using System.Data.SqlClient;
using bufinscustomers.Helpers;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Marca "debe cambiar la contraseña al iniciar sesión" (<c>Usuarios.DebeCambiarClave</c>,
    /// script Sql/025). Tolerante a que la columna aún no exista: sin ella nadie queda obligado
    /// y <see cref="Marcar"/> devuelve false para que el llamador avise.
    /// </summary>
    public class ClaveInicialService : BaseService
    {
        public bool DebeCambiar(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT DebeCambiarClave FROM Usuarios WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    var r = cmd.ExecuteScalar();
                    return r != null && r != DBNull.Value && Convert.ToBoolean(r);
                }
            }
            catch (SqlException ex)
            {
                // 207 = nombre de columna no válido (script 025 sin ejecutar).
                if (ex.Number != 207) AppLogger.Error(ex, "ClaveInicialService.DebeCambiar");
                return false;
            }
        }

        public bool Marcar(int idUsuario, bool debeCambiar)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("UPDATE Usuarios SET DebeCambiarClave = @V WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@V", debeCambiar);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number != 207) AppLogger.Error(ex, "ClaveInicialService.Marcar");
                return false;
            }
        }

        /// <summary>Guarda la nueva clave (ya hasheada) y apaga la marca en una sola sentencia.</summary>
        public bool GuardarClaveYQuitarMarca(int idUsuario, string hash)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE Usuarios SET Clave = @Clave, DebeCambiarClave = 0 WHERE Id = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Clave", hash);
                cmd.Parameters.AddWithValue("@Id", idUsuario);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1;
            }
        }
    }
}
