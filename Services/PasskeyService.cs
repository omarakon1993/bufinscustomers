using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using bufinscustomers.Helpers;

namespace bufinscustomers.Services
{
    public class PasskeyInfo
    {
        public int Id { get; set; }
        public int IdUsuario { get; set; }
        public byte[] CredentialId { get; set; }
        public byte[] ClavePublica { get; set; }
        public int Algoritmo { get; set; }
        public long Contador { get; set; }
        public string Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? UltimoUso { get; set; }
    }

    /// <summary>
    /// Passkeys (WebAuthn) de cada usuario — tabla <c>UsuarioPasskeys</c> (Sql/029). Tolerante a que
    /// la tabla aún no exista (error SQL 208): sin ella no hay passkeys y todo sigue con la app autenticadora.
    /// </summary>
    public class PasskeyService : BaseService
    {
        /// <summary>Máximo por defecto; se cambia con la clave PasskeysMaximoPorUsuario (ver DobleFactorService.PasskeysMaximo).</summary>
        public const int MaxPorDefecto = 5;

        private static bool EsEsquemaFaltante(SqlException ex) { return ex.Number == 207 || ex.Number == 208; }

        public List<PasskeyInfo> Listar(int idUsuario)
        {
            var lista = new List<PasskeyInfo>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT Id, IdUsuario, CredentialId, ClavePublica, Algoritmo, ContadorFirmas, Nombre, FechaCreacion, UltimoUso " +
                    "FROM UsuarioPasskeys WHERE IdUsuario = @U ORDER BY FechaCreacion", cn))
                {
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add(new PasskeyInfo
                            {
                                Id = Convert.ToInt32(r["Id"]),
                                IdUsuario = Convert.ToInt32(r["IdUsuario"]),
                                CredentialId = (byte[])r["CredentialId"],
                                ClavePublica = (byte[])r["ClavePublica"],
                                Algoritmo = Convert.ToInt32(r["Algoritmo"]),
                                Contador = Convert.ToInt64(r["ContadorFirmas"]),
                                Nombre = r["Nombre"].ToString(),
                                FechaCreacion = Convert.ToDateTime(r["FechaCreacion"]),
                                UltimoUso = r["UltimoUso"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UltimoUso"])
                            });
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "PasskeyService.Listar");
            }
            return lista;
        }

        /// <summary>
        /// ¿Se puede mostrar al usuario la invitación a configurar una passkey? false si la decisión del
        /// usuario aún rige («Más tarde» o «No volver a mostrar») o si falta el script 031 (sin dónde
        /// recordar la decisión, la franja del Home no se muestra).
        /// </summary>
        public bool AvisoVigente(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT PasskeyAvisoHasta FROM Usuarios WHERE Id = @U", cn))
                {
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cn.Open();
                    object r = cmd.ExecuteScalar();
                    if (r == null) return false;
                    return r == DBNull.Value || Convert.ToDateTime(r) <= DateTime.Now;
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "PasskeyService.AvisoVigente");
                return false;
            }
        }

        /// <summary>Pospone la invitación <paramref name="dias"/> días; 0 o menos = no volver a mostrarla.</summary>
        public bool PosponerAviso(int idUsuario, int dias)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("UPDATE Usuarios SET PasskeyAvisoHasta = @H WHERE Id = @U", cn))
                {
                    cmd.Parameters.AddWithValue("@H", dias > 0 ? DateTime.Now.AddDays(dias) : new DateTime(9999, 12, 31));
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "PasskeyService.PosponerAviso");
                return false;
            }
        }

        /// <summary>true si el script 029 está ejecutado (la tabla existe).</summary>
        public bool TablaDisponible()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT TOP 1 1 FROM UsuarioPasskeys", cn))
                {
                    cn.Open();
                    cmd.ExecuteScalar();
                    return true;
                }
            }
            catch (SqlException) { return false; }
        }

        public bool Agregar(int idUsuario, byte[] credentialId, byte[] clavePublica, int algoritmo, long contador, string nombre)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "INSERT INTO UsuarioPasskeys (IdUsuario, CredentialId, ClavePublica, Algoritmo, ContadorFirmas, Nombre) " +
                    "VALUES (@U, @C, @K, @A, @N, @Nom)", cn))
                {
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cmd.Parameters.Add("@C", System.Data.SqlDbType.VarBinary, 512).Value = credentialId;
                    cmd.Parameters.Add("@K", System.Data.SqlDbType.VarBinary, -1).Value = clavePublica;
                    cmd.Parameters.AddWithValue("@A", algoritmo);
                    cmd.Parameters.AddWithValue("@N", contador);
                    cmd.Parameters.AddWithValue("@Nom", (nombre ?? "").Trim());
                    cn.Open();
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (SqlException ex)
            {
                // 2601/2627 = la credencial ya estaba registrada.
                if (ex.Number != 2601 && ex.Number != 2627 && !EsEsquemaFaltante(ex)) AppLogger.Error(ex, "PasskeyService.Agregar");
                return false;
            }
        }

        /// <summary>Registra el uso (contador y fecha) tras un inicio de sesión correcto.</summary>
        public void RegistrarUso(int idPasskey, long contador)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE UsuarioPasskeys SET ContadorFirmas = @N, UltimoUso = GETDATE() WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@N", contador);
                    cmd.Parameters.AddWithValue("@Id", idPasskey);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (SqlException ex) { AppLogger.Error(ex, "PasskeyService.RegistrarUso"); }
        }

        /// <summary>Elimina una passkey; el filtro por usuario impide borrar la de otro.</summary>
        public bool Eliminar(int idUsuario, int idPasskey)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM UsuarioPasskeys WHERE Id = @Id AND IdUsuario = @U", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idPasskey);
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "PasskeyService.Eliminar");
                return false;
            }
        }

        /// <summary>Borra todas las passkeys del usuario (reinicio por Super Admin). Devuelve cuántas.</summary>
        public int EliminarTodas(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM UsuarioPasskeys WHERE IdUsuario = @U", cn))
                {
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery();
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "PasskeyService.EliminarTodas");
                return 0;
            }
        }
    }
}
