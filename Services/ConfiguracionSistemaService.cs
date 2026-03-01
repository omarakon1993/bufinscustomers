using System;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class ConfiguracionSistemaService : BaseService
    {
        public string ObtenerValor(string clave)
        {
            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    SqlCommand cmd = new SqlCommand(
                        "SELECT Valor FROM ConfiguracionSistema WHERE Clave = @Clave", cn);
                    cmd.Parameters.AddWithValue("@Clave", clave);
                    cn.Open();
                    object result = cmd.ExecuteScalar();
                    return result != null && result != DBNull.Value
                        ? result.ToString().Trim()
                        : null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener configuración '{clave}': {ex.Message}");
                return null;
            }
        }
    }
}
