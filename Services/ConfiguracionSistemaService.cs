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
                    cn.Open();
                    bool conTipo = true;
                    SqlCommand cmd = new SqlCommand(
                        "SELECT Clave, Valor, ISNULL(Descripcion,'') AS Descripcion, IdTipo FROM ConfiguracionSistema ORDER BY Clave", cn);
                    SqlDataReader probe = null;
                    try { probe = cmd.ExecuteReader(); }
                    catch (SqlException ex) when (ex.Number == 207)
                    {
                        // Sin la columna IdTipo (script 028 sin ejecutar): consulta de siempre.
                        conTipo = false;
                        cmd = new SqlCommand(
                            "SELECT Clave, Valor, ISNULL(Descripcion,'') AS Descripcion FROM ConfiguracionSistema ORDER BY Clave", cn);
                    }
                    using (var r = probe ?? cmd.ExecuteReader())
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
                                IdTipo      = conTipo && r["IdTipo"] != DBNull.Value ? (int?)Convert.ToInt32(r["IdTipo"]) : null,
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
        // ───────────── Tipos (categorías) de configuración — Sql/028 ─────────────

        private static bool EsEsquemaFaltante(SqlException ex) { return ex.Number == 207 || ex.Number == 208; }

        /// <summary>Tipos con su cantidad de parámetros. disponible=false si el script 028 no se ha ejecutado.</summary>
        public List<ConfiguracionSistemaTipo> ObtenerTipos(out bool disponible)
        {
            var lista = new List<ConfiguracionSistemaTipo>();
            disponible = true;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT t.Id, t.Nombre, t.NombreEN, t.Icono, t.Orden, t.Activo, COUNT(c.Clave) AS Cantidad
                      FROM ConfiguracionSistemaTipos t
                      LEFT JOIN ConfiguracionSistema c ON c.IdTipo = t.Id
                     GROUP BY t.Id, t.Nombre, t.NombreEN, t.Icono, t.Orden, t.Activo
                     ORDER BY t.Orden, t.Nombre", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add(new ConfiguracionSistemaTipo
                            {
                                Id       = Convert.ToInt32(r["Id"]),
                                Nombre   = r["Nombre"].ToString(),
                                NombreEN = r["NombreEN"] == DBNull.Value ? null : r["NombreEN"].ToString(),
                                Icono    = r["Icono"].ToString(),
                                Orden    = Convert.ToInt32(r["Orden"]),
                                Activo   = Convert.ToBoolean(r["Activo"]),
                                Cantidad = Convert.ToInt32(r["Cantidad"])
                            });
                }
            }
            catch (SqlException ex)
            {
                if (EsEsquemaFaltante(ex)) disponible = false;
                else AppLogger.Error(ex, "ConfiguracionSistemaService.ObtenerTipos");
            }
            return lista;
        }

        /// <summary>Crea (id=0) o edita un tipo. Devuelve el Id, o 0 si falló.</summary>
        public int GuardarTipo(int id, string nombre, string nombreEN, string icono, int orden)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(id > 0
                    ? "UPDATE ConfiguracionSistemaTipos SET Nombre=@N, NombreEN=@E, Icono=@I, Orden=@O WHERE Id=@Id; SELECT @Id;"
                    : "INSERT INTO ConfiguracionSistemaTipos (Nombre, NombreEN, Icono, Orden) VALUES (@N, @E, @I, @O); SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@N", nombre.Trim());
                    cmd.Parameters.AddWithValue("@E", string.IsNullOrWhiteSpace(nombreEN) ? (object)DBNull.Value : nombreEN.Trim());
                    cmd.Parameters.AddWithValue("@I", string.IsNullOrWhiteSpace(icono) ? "fas fa-cog" : icono.Trim());
                    cmd.Parameters.AddWithValue("@O", orden);
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (SqlException ex)
            {
                AppLogger.Error(ex, "ConfiguracionSistemaService.GuardarTipo");
                return 0;
            }
        }

        /// <summary>Elimina un tipo; sus parámetros quedan sin tipo ("Otros"). En una sola transacción.</summary>
        public bool EliminarTipo(int id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    using (var tx = cn.BeginTransaction())
                    {
                        using (var a = new SqlCommand("UPDATE ConfiguracionSistema SET IdTipo = NULL WHERE IdTipo = @Id", cn, tx))
                        { a.Parameters.AddWithValue("@Id", id); a.ExecuteNonQuery(); }
                        int filas;
                        using (var b = new SqlCommand("DELETE FROM ConfiguracionSistemaTipos WHERE Id = @Id", cn, tx))
                        { b.Parameters.AddWithValue("@Id", id); filas = b.ExecuteNonQuery(); }
                        tx.Commit();
                        return filas > 0;
                    }
                }
            }
            catch (SqlException ex)
            {
                AppLogger.Error(ex, "ConfiguracionSistemaService.EliminarTipo");
                return false;
            }
        }

        /// <summary>Asigna el tipo a un parámetro (null = sin tipo). false si el script 028 no existe.</summary>
        public bool AsignarTipo(string clave, int? idTipo)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("UPDATE ConfiguracionSistema SET IdTipo = @T WHERE Clave = @C", cn))
                {
                    cmd.Parameters.AddWithValue("@T", (object)idTipo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@C", clave.Trim());
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "ConfiguracionSistemaService.AsignarTipo");
                return false;
            }
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
