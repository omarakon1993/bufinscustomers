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
        private const string PropositoProteccion = "Bufins.Totp.Secreto.v1";

        private static bool? _habilitadoCache;
        private static string _alcanceCache;
        private static bool _passkeysCache;
        private static int _passkeysMaxCache = 5;
        private static DateTime _cacheHasta = DateTime.MinValue;

        internal static string LeerConfig(string clave)
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
            _passkeysCache = string.Equals(LeerConfig("PasskeysHabilitado"), "true", StringComparison.OrdinalIgnoreCase);
            int max;
            _passkeysMaxCache = int.TryParse(LeerConfig("PasskeysMaximoPorUsuario"), out max) ? Math.Max(1, Math.Min(max, 20)) : PasskeyService.MaxPorDefecto;
            _cacheHasta = DateTime.Now.AddSeconds(30);
        }

        /// <summary>
        /// Interruptor global. Manda la clave <c>TwoFactorHabilitado</c> de <c>ConfiguracionSistema</c>
        /// (UPDATE sin redesplegar; caché de 30 s); si no existe, cae al appSettings (por defecto "false").
        /// </summary>
        public static bool Habilitado { get { RefrescarCache(); return _habilitadoCache.Value; } }

        /// <summary>
        /// Passkeys (WebAuthn) permitidas: requiere el 2FA habilitado y la clave <c>PasskeysHabilitado</c>=true
        /// (ConfiguracionSistema, Sql/029). Apagado por defecto.
        /// </summary>
        public static bool PasskeysHabilitadas { get { RefrescarCache(); return _habilitadoCache.Value && _passkeysCache; } }

        /// <summary>
        /// Máximo de passkeys por usuario: clave <c>PasskeysMaximoPorUsuario</c> de ConfiguracionSistema
        /// (entre 1 y 20). Si falta o no es un número, vale <see cref="PasskeyService.MaxPorDefecto"/> (5).
        /// </summary>
        public static int PasskeysMaximo { get { RefrescarCache(); return _passkeysMaxCache; } }

        /// <summary>Dominio (RP ID) para WebAuthn: clave <c>WebAuthnRpId</c> o, si está vacía, el host de la petición.</summary>
        public static string RpId(System.Web.HttpRequestBase request)
        {
            string v = LeerConfig("WebAuthnRpId");
            return string.IsNullOrWhiteSpace(v) ? request.Url.Host : v.Trim().ToLowerInvariant();
        }

        /// <summary>Origen esperado (esquema + host + puerto) tomado de la petición.</summary>
        public static string Origen(System.Web.HttpRequestBase request)
        {
            return request.Url.GetLeftPart(UriPartial.Authority);
        }

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

        /// <summary>Activa el segundo factor (guarda el secreto protegido y el primer paso aceptado).</summary>
        public bool Activar(int idUsuario, byte[] secreto, long paso)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE Usuarios SET TotpActivo = 1, TotpSecretoCifrado = @S, TotpActivadoEn = GETDATE(), TotpUltimoPaso = @P WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@S", Proteger(secreto));
                    cmd.Parameters.AddWithValue("@P", paso);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "DobleFactorService.Activar");
                return false;
            }
        }

        /// <summary>
        /// Reinicia TODOS los métodos del usuario (app autenticadora y passkeys). Solo Super Admin; el
        /// llamador audita. En su próximo inicio de sesión configurará un autenticador nuevo.
        /// </summary>
        public bool Reiniciar(int idUsuario)
        {
            try
            {
                int filas;
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE Usuarios SET TotpActivo = 0, TotpSecretoCifrado = NULL, TotpActivadoEn = NULL, TotpUltimoPaso = NULL WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    filas = cmd.ExecuteNonQuery();
                }
                new PasskeyService().EliminarTodas(idUsuario);   // tolerante si no existe el script 029
                return filas == 1;
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "DobleFactorService.Reiniciar");
                return false;
            }
        }

        /// <summary>
        /// Reconfirmación de acciones sensibles dentro de una sesión ya abierta. A diferencia del inicio de
        /// sesión, acepta el MISMO código que se usó al entrar (el usuario suele ir directo a Seguridad y la
        /// app aún muestra ese código), pero nunca uno anterior al login ni uno ya usado en la reconfirmación
        /// de esta sesión (<paramref name="pasoMinimoSesion"/>, exclusivo). Devuelve el paso aceptado o -1.
        /// </summary>
        public long ValidarCodigoReconfirmacion(int idUsuario, string codigo, long? pasoMinimoSesion)
        {
            var e = Obtener(idUsuario);
            if (!e.Disponible || !e.Activo || e.Secreto == null) return -1;
            long? minimo = e.UltimoPaso.HasValue ? e.UltimoPaso.Value - 1 : (long?)null;
            if (pasoMinimoSesion.HasValue) minimo = minimo.HasValue ? Math.Max(minimo.Value, pasoMinimoSesion.Value) : pasoMinimoSesion;
            return TotpHelper.Validar(e.Secreto, codigo, minimo);
        }

        /// <summary>Valida un código TOTP de 6 dígitos del usuario (con anti-reutilización). Para acciones sensibles.</summary>
        public bool ValidarCodigoTotp(int idUsuario, string codigo)
        {
            var e = Obtener(idUsuario);
            if (!e.Disponible || !e.Activo || e.Secreto == null) return false;
            long paso = TotpHelper.Validar(e.Secreto, codigo, e.UltimoPaso);
            return paso >= 0 && RegistrarPaso(idUsuario, paso);
        }
    }
}
