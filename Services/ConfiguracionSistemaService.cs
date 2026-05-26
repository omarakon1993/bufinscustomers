using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class ConfiguracionSistemaService : BaseService
    {
        public List<ConfiguracionSistemaItem> ObtenerTodos()
        {
            var lista = new List<ConfiguracionSistemaItem>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "SELECT Clave, Valor, ISNULL(Descripcion,'') AS Descripcion FROM ConfiguracionSistema ORDER BY Clave",
                        cn);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add(new ConfiguracionSistemaItem
                            {
                                Clave       = r["Clave"].ToString(),
                                Valor       = r["Valor"].ToString(),
                                Descripcion = r["Descripcion"].ToString()
                            });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ConfiguracionSistema ObtenerTodos: " + ex.Message);
            }
            return lista;
        }

        public bool Guardar(string clave, string valor, string descripcion)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        IF EXISTS (SELECT 1 FROM ConfiguracionSistema WHERE Clave = @Clave)
                            UPDATE ConfiguracionSistema
                               SET Valor = @Valor, Descripcion = @Descripcion
                             WHERE Clave = @Clave
                        ELSE
                            INSERT INTO ConfiguracionSistema (Clave, Valor, Descripcion)
                            VALUES (@Clave, @Valor, @Descripcion)", cn);
                    cmd.Parameters.AddWithValue("@Clave",       clave.Trim());
                    cmd.Parameters.AddWithValue("@Valor",        valor?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@Descripcion",
                        string.IsNullOrWhiteSpace(descripcion) ? (object)DBNull.Value : descripcion.Trim());
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ConfiguracionSistema Guardar: " + ex.Message);
                return false;
            }
        }

        public bool Eliminar(string clave)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "DELETE FROM ConfiguracionSistema WHERE Clave = @Clave", cn);
                    cmd.Parameters.AddWithValue("@Clave", clave.Trim());
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch { return false; }
        }
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
