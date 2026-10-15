using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Web.Security;
using bufinscustomers.Helpers;

namespace bufinscustomers.Services
{
    /// <summary>Estado del segundo factor de un usuario (<c>Usuarios.Totp*</c>, script Sql/026).</summary>
    public class EstadoDobleFactor
    {
        /// <summary>false = el script 026 no se ha ejecutado (columnas inexistentes): el login sigue como siempre.</summary>
        public bool Disponible { get; set; }
        public bool Activo { get; set; }
        public byte[] Secreto { get; set; }
        public long? UltimoPaso { get; set; }
    }

    /// <summary>
    /// Doble factor TOTP para todos los usuarios. Tolerante a que el script 026 aún no exista
    /// (error SQL 207 / 208): en ese caso <see cref="Obtener"/> devuelve Disponible=false y nadie
    /// queda obligado. El interruptor global es <c>appSettings["TwoFactorHabilitado"]</c>.
    /// </summary>
    public class DobleFactorService : BaseService
    {
        public const int CantidadCodigosRecuperacion = 10;
        private const string PropositoProteccion = "Bufins.Totp.Secreto.v1";
        private const string AlfabetoRecuperacion = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private static bool? _habilitadoCache;
        private static string _alcanceCache;
        private static DateTime _cacheHasta = DateTime.MinValue;

        private static string LeerConfig(string clave)
        {
            string v = null;
            try { v = new ConfiguracionSistemaService().ObtenerValor(clave); } catch { }
            if (string.IsNullOrWhiteSpace(v)) v = ConfigurationManager.AppSettings[clave];
            return (v ?? "").Trim();
        }

        private static void RefrescarCache()
        {
            if (_habilitadoCache.HasValue && DateTime.Now < _cacheHasta) return;
            _habilitadoCache = string.Equals(LeerConfig("TwoFactorHabilitado"), "true", StringComparison.OrdinalIgnoreCase);
            _alcanceCache = LeerConfig("TwoFactorAlcance").ToLowerInvariant();
            _cacheHasta = DateTime.Now.AddSeconds(30);
        }

        /// <summary>
        /// Interruptor global. Manda la clave <c>TwoFactorHabilitado</c> de <c>ConfiguracionSistema</c>
        /// (UPDATE sin redesplegar; caché de 30 s); si no existe, cae al appSettings (por defecto "false").
        /// </summary>
        public static bool Habilitado { get { RefrescarCache(); return _habilitadoCache.Value; } }

        /// <summary>
        /// Alcance (clave <c>TwoFactorAlcance</c>): "todos" = todos los usuarios; cualquier otro valor
        /// o vacío = solo Super Admin (por defecto, mientras duran las pruebas).
        /// </summary>
        public static bool AplicaA(bufinscustomers.Models.Usuarios u)
        {
            if (u == null) return false;
            RefrescarCache();
            return _alcanceCache == "todos" || u.Admin == 2;
        }

        private static bool EsEsquemaFaltante(SqlException ex) { return ex.Number == 207 || ex.Number == 208; }

        // ---- Protección del secreto en reposo (aislado aquí para cambiarlo al migrar de .NET) ----
        private static string Proteger(byte[] secreto)
        {
            return Convert.ToBase64String(MachineKey.Protect(secreto, PropositoProteccion));
        }

        private static byte[] Desproteger(string base64)
        {
            return MachineKey.Unprotect(Convert.FromBase64String(base64), PropositoProteccion);
        }

        public EstadoDobleFactor Obtener(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT TotpActivo, TotpSecretoCifrado, TotpUltimoPaso FROM Usuarios WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        var e = new EstadoDobleFactor { Disponible = true };
                        if (!r.Read()) return e;
                        e.Activo = r["TotpActivo"] != DBNull.Value && Convert.ToBoolean(r["TotpActivo"]);
                        if (e.Activo && r["TotpSecretoCifrado"] != DBNull.Value)
                        {
                            try { e.Secreto = Desproteger(r["TotpSecretoCifrado"].ToString()); }
                            catch (Exception ex) { AppLogger.Error(ex, "DobleFactor.Desproteger"); e.Secreto = null; }
                        }
                        if (r["TotpUltimoPaso"] != DBNull.Value) e.UltimoPaso = Convert.ToInt64(r["TotpUltimoPaso"]);
                        return e;
                    }
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "DobleFactorService.Obtener");
                return new EstadoDobleFactor { Disponible = false };
            }
        }

        /// <summary>Atómico: solo acepta el paso si es mayor al último guardado (no se reutiliza un código).</summary>
        public bool RegistrarPaso(int idUsuario, long paso)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE Usuarios SET TotpUltimoPaso = @P WHERE Id = @Id AND (TotpUltimoPaso IS NULL OR TotpUltimoPaso < @P)", cn))
            {
                cmd.Parameters.AddWithValue("@P", paso);
                cmd.Parameters.AddWithValue("@Id", idUsuario);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1;
            }
        }

        /// <summary>Activa el segundo factor y guarda los códigos de recuperación (hash) en UNA transacción.</summary>
        public bool Activar(int idUsuario, byte[] secreto, long paso, IList<string> codigosRecuperacion)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = new SqlCommand(
                            "UPDATE Usuarios SET TotpActivo = 1, TotpSecretoCifrado = @S, TotpActivadoEn = GETDATE(), TotpUltimoPaso = @P WHERE Id = @Id", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@S", Proteger(secreto));
                            cmd.Parameters.AddWithValue("@P", paso);
                            cmd.Parameters.AddWithValue("@Id", idUsuario);
                            if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
                        }
                        using (var del = new SqlCommand("DELETE FROM UsuarioCodigosRecuperacion WHERE IdUsuario = @Id", cn, tx))
                        {
                            del.Parameters.AddWithValue("@Id", idUsuario);
                            del.ExecuteNonQuery();
                        }
                        foreach (var c in codigosRecuperacion)
                        {
                            using (var ins = new SqlCommand(
                                "INSERT INTO UsuarioCodigosRecuperacion (IdUsuario, HashCodigo) VALUES (@Id, @H)", cn, tx))
                            {
                                ins.Parameters.AddWithValue("@Id", idUsuario);
                                ins.Parameters.AddWithValue("@H", HashCodigo(c));
                                ins.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { }
                        AppLogger.Error(ex, "DobleFactorService.Activar");
                        return false;
                    }
                }
            }
        }

        /// <summary>Quita el segundo factor y los códigos de recuperación (solo Super Admin, auditado por el llamador).</summary>
        public bool Reiniciar(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    using (var tx = cn.BeginTransaction())
                    {
                        int filas;
                        using (var cmd = new SqlCommand(
                            "UPDATE Usuarios SET TotpActivo = 0, TotpSecretoCifrado = NULL, TotpActivadoEn = NULL, TotpUltimoPaso = NULL WHERE Id = @Id", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Id", idUsuario);
                            filas = cmd.ExecuteNonQuery();
                        }
                        using (var del = new SqlCommand("DELETE FROM UsuarioCodigosRecuperacion WHERE IdUsuario = @Id", cn, tx))
                        {
                            del.Parameters.AddWithValue("@Id", idUsuario);
                            del.ExecuteNonQuery();
                        }
                        tx.Commit();
                        return filas == 1;
                    }
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "DobleFactorService.Reiniciar");
                return false;
            }
        }

        /// <summary>Consume un código de recuperación (un solo uso). true = era válido y quedó gastado.</summary>
        public bool ConsumirCodigoRecuperacion(int idUsuario, string codigo)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE UsuarioCodigosRecuperacion SET UsadoEn = GETDATE() WHERE IdUsuario = @Id AND HashCodigo = @H AND UsadoEn IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@Id", idUsuario);
                cmd.Parameters.AddWithValue("@H", HashCodigo(codigo));
                cn.Open();
                return cmd.ExecuteNonQuery() == 1;
            }
        }

        public int CodigosRestantes(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT COUNT(*) FROM UsuarioCodigosRecuperacion WHERE IdUsuario = @Id AND UsadoEn IS NULL", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (SqlException) { return 0; }
        }

        public List<string> GenerarCodigosRecuperacion()
        {
            var lista = new List<string>();
            var bytes = new byte[CantidadCodigosRecuperacion * 10];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            for (int i = 0; i < CantidadCodigosRecuperacion; i++)
            {
                var sb = new StringBuilder(11);
                for (int j = 0; j < 10; j++)
                {
                    if (j == 5) sb.Append('-');
                    sb.Append(AlfabetoRecuperacion[bytes[i * 10 + j] % AlfabetoRecuperacion.Length]);
                }
                lista.Add(sb.ToString());
            }
            return lista;
        }

        /// <summary>true si el texto tiene forma de código de recuperación (10 letras/dígitos, con o sin guion).</summary>
        public static bool PareceCodigoRecuperacion(string texto)
        {
            return Normalizar(texto).Length == 10;
        }

        private static string Normalizar(string codigo)
        {
            return (codigo ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        }

        private static string HashCodigo(string codigo)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(Normalizar(codigo)));
                var sb = new StringBuilder(64);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
