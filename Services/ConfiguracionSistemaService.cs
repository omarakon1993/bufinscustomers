using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class ConfiguracionSistemaService : BaseService
    {
        /// <summary>Claves cuyo valor es un secreto: se cifra en reposo y se enmascara en la lista.</summary>
        private static readonly HashSet<string> ClavesSecretas =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OpenAIApiKey" };

        public static bool EsClaveSecreta(string clave) =>
            !string.IsNullOrWhiteSpace(clave) && ClavesSecretas.Contains(clave.Trim());

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
                        {
                            var clave    = r["Clave"].ToString();
                            var valorRaw = r["Valor"].ToString();
                            bool secreta = EsClaveSecreta(clave);
                            lista.Add(new ConfiguracionSistemaItem
                            {
                                Clave       = clave,
                                // Un secreto NUNCA sale de aquí en claro: solo su versión enmascarada.
                                Valor       = secreta
                                                ? SecretosProtegidos.Enmascarar(SecretosProtegidos.Descifrar(valorRaw))
                                                : valorRaw,
                                Descripcion = r["Descripcion"].ToString(),
                                EsSecreta   = secreta
                            });
                        }
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
                clave = clave.Trim();
                bool secreta = EsClaveSecreta(clave);
                string valorLimpio = valor?.Trim() ?? "";

                // Secreto sin valor nuevo (o con la máscara de puntos): se conserva el valor
                // almacenado y solo se actualiza la descripción — nunca se sobrescribe con vacío.
                if (secreta && (valorLimpio.Length == 0 || valorLimpio.IndexOf('•') >= 0))
                {
                    using (var cn = new SqlConnection(CadenaConexion))
                    {
                        var cmdD = new SqlCommand(
                            "UPDATE ConfiguracionSistema SET Descripcion = @Descripcion WHERE Clave = @Clave", cn);
                        cmdD.Parameters.AddWithValue("@Clave", clave);
                        cmdD.Parameters.AddWithValue("@Descripcion",
                            string.IsNullOrWhiteSpace(descripcion) ? (object)DBNull.Value : descripcion.Trim());
                        cn.Open();
                        return cmdD.ExecuteNonQuery() > 0;
                    }
                }

                // Un secreto se guarda cifrado en reposo.
                string valorGuardar = secreta ? SecretosProtegidos.Cifrar(valorLimpio) : valorLimpio;

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
                    cmd.Parameters.AddWithValue("@Clave",       clave);
                    cmd.Parameters.AddWithValue("@Valor",        valorGuardar);
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
                    // Descifrar es no-op si el valor no lleva el prefijo enc:v1: (valores en claro heredados).
                    return result != null && result != DBNull.Value
                        ? SecretosProtegidos.Descifrar(result.ToString().Trim())
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
                    // Descifrar es no-op si el valor no lleva el prefijo enc:v1: (valores en claro heredados).
                    return result != null && result != DBNull.Value
                        ? SecretosProtegidos.Descifrar(result.ToString().Trim())
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
