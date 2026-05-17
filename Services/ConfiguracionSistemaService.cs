using System;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class ConfiguracionSistemaService : BaseService
    {
        public string ObtenerValor(string clave)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
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

        public async Task<string> ObtenerValorAsync(string clave)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "SELECT Valor FROM ConfiguracionSistema WHERE Clave = @Clave", cn);
                    cmd.Parameters.AddWithValue("@Clave", clave);
                    await cn.OpenAsync().ConfigureAwait(false);
                    object result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
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
