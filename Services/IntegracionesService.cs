using bufinscustomers.Helpers;
using bufinscustomers.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace bufinscustomers.Services
{
    /// <summary>Una credencial de la API de ingesta (dbo.Integraciones). Nunca contiene la clave.</summary>
    public class Integracion
    {
        public int Id { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Nombre { get; set; }
        public int IdUsuarioTecnico { get; set; }
        public string NombreUsuarioTecnico { get; set; }
        public string Prefijo { get; set; }
        public bool Activo { get; set; }
        public bool PermiteConfirmar { get; set; }
        /// <summary>Integración de PRUEBAS (clave bft_): hace todo el flujo, pero «confirmar» es una simulación que nunca toca datos reales.</summary>
        public bool Sandbox { get; set; }
        public string IpPermitidas { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaUltimoUso { get; set; }
        public string IpUltimoUso { get; set; }
        public DateTime? FechaRevocada { get; set; }
        public bool Vigente => Activo && FechaRevocada == null;
    }

    /// <summary>Resultado de autenticar una clave de API.</summary>
    public class ResultadoAutenticacionApi
    {
        public bool Ok { get; set; }
        /// <summary>Motivo interno (para el log); al cliente siempre se responde un 401/403 genérico.</summary>
        public string Motivo { get; set; }
        public bool IpNoPermitida { get; set; }
        public Integracion Integracion { get; set; }
        public Usuarios UsuarioTecnico { get; set; }
    }

    /// <summary>
    /// Credenciales de la API de ingesta. Formato de la clave: <c>bfk_{prefijo}_{secreto}</c> (secreto de 256 bits, aleatorio).
    /// Como el secreto es aleatorio de alta entropía, basta un SHA-256 (no hace falta BCrypt); se compara en tiempo constante.
    /// La clave en claro solo existe en la respuesta de <see cref="Crear"/> / <see cref="Rotar"/>: no se puede volver a ver.
    /// Tolera que el script Sql/046 no se haya ejecutado (<see cref="Disponible"/>).
    /// </summary>
    public class IntegracionesService : BaseService
    {
        private const string PrefijoClave = "bfk_";            // producción
        private const string PrefijoClaveSandbox = "bft_";      // pruebas
        private static readonly byte[] HashSenuelo = SHA256.HashData(Encoding.UTF8.GetBytes("bufins-api-senuelo"));

        /// <summary>True si existen las tablas del script 046 (si no, la API responde 503 «no configurada»).</summary>
        public static bool Disponible()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT CASE WHEN OBJECT_ID('dbo.Integraciones','U') IS NOT NULL AND OBJECT_ID('dbo.IntegracionesIdempotencia','U') IS NOT NULL " +
                    "AND OBJECT_ID('dbo.CarguesLotesHojas','U') IS NOT NULL AND COL_LENGTH('dbo.CarguesLotes','Origen') IS NOT NULL AND COL_LENGTH('dbo.CarguesLotes','IdIntegracion') IS NOT NULL THEN 1 ELSE 0 END", cn))
                {
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch { return false; }
        }

        private static bool _sandboxDisponible; private static DateTime _sandboxHasta = DateTime.MinValue;

        /// <summary>True si dbo.Integraciones ya tiene la columna Sandbox (script 047). Se revisa de nuevo cada minuto.</summary>
        public static bool SandboxDisponible()
        {
            if (DateTime.Now < _sandboxHasta) return _sandboxDisponible;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Integraciones','Sandbox') IS NOT NULL THEN 1 ELSE 0 END", cn))
                {
                    cn.Open();
                    _sandboxDisponible = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch { _sandboxDisponible = false; }
            _sandboxHasta = DateTime.Now.AddSeconds(60);
            return _sandboxDisponible;
        }

        // ── Alta / baja ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Crea una integración. <c>claveEnClaro</c> es la única vez que se conoce la clave. El usuario técnico debe pertenecer a la
        /// empresa y no ser Super Admin (la API nunca opera fuera de su empresa). Un usuario no puede servir a la vez a una integración de
        /// pruebas y a una de producción (las pruebas descartan los cargues pendientes de su usuario). En caso de error devuelve un CÓDIGO
        /// (<c>nombre</c>, <c>usuario</c>, <c>usuario_en_uso</c>, <c>sandbox_no_disponible</c>, <c>ip:{valor}</c>, <c>ips_largo</c>) que el controlador traduce.
        /// </summary>
        public (bool ok, string error, int idIntegracion, string claveEnClaro) Crear(int idEmpresa, string nombre, int idUsuarioTecnico,
            bool permiteConfirmar, string ipPermitidas, int? idUsuarioCreador, bool sandbox = false)
        {
            nombre = (nombre ?? "").Trim();
            if (nombre.Length == 0 || nombre.Length > 100) return (false, "nombre", 0, null);

            var usuario = new UsuarioService().ObtenerUsuarioPorId(idUsuarioTecnico);
            if (usuario == null || usuario.IdEmpresa != idEmpresa || usuario.Admin == 2)
                return (false, "usuario", 0, null);

            if (!TryNormalizarIps(ipPermitidas, out string ips, out string errorIp))
                return (false, errorIp, 0, null);

            bool conSandbox = SandboxDisponible();
            if (sandbox && !conSandbox) return (false, "sandbox_no_disponible", 0, null);

            var (prefijo, clave, hash) = GenerarClave(sandbox);
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                if (conSandbox)
                    using (var cmdU = new SqlCommand(
                        "SELECT COUNT(1) FROM dbo.Integraciones WHERE IdUsuarioTecnico = @U AND FechaRevocada IS NULL AND Sandbox <> @S", cn))
                    {
                        cmdU.Parameters.AddWithValue("@U", idUsuarioTecnico);
                        cmdU.Parameters.AddWithValue("@S", sandbox);
                        if (Convert.ToInt32(cmdU.ExecuteScalar()) > 0) return (false, "usuario_en_uso", 0, null);
                    }

            using (var cmd = new SqlCommand(conSandbox
                ? @"INSERT INTO dbo.Integraciones (IdEmpresa, Nombre, IdUsuarioTecnico, Prefijo, HashClave, PermiteConfirmar, IpPermitidas, IdUsuarioCreador, Sandbox)
                    VALUES (@IdEmpresa, @Nombre, @IdUsuarioTecnico, @Prefijo, @Hash, @PermiteConfirmar, @Ips, @Creador, @Sandbox);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);"
                : @"INSERT INTO dbo.Integraciones (IdEmpresa, Nombre, IdUsuarioTecnico, Prefijo, HashClave, PermiteConfirmar, IpPermitidas, IdUsuarioCreador)
                    VALUES (@IdEmpresa, @Nombre, @IdUsuarioTecnico, @Prefijo, @Hash, @PermiteConfirmar, @Ips, @Creador);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
            {
                if (conSandbox) cmd.Parameters.AddWithValue("@Sandbox", sandbox);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Nombre", nombre);
                cmd.Parameters.AddWithValue("@IdUsuarioTecnico", idUsuarioTecnico);
                cmd.Parameters.AddWithValue("@Prefijo", prefijo);
                cmd.Parameters.Add("@Hash", System.Data.SqlDbType.VarBinary, 32).Value = hash;
                cmd.Parameters.AddWithValue("@PermiteConfirmar", permiteConfirmar);
                cmd.Parameters.AddWithValue("@Ips", (object)ips ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Creador", (object)idUsuarioCreador ?? DBNull.Value);
                int id = (int)cmd.ExecuteScalar();
                return (true, null, id, clave);
            }
            }
        }

        /// <summary>Genera una clave nueva (la anterior deja de servir al instante). Devuelve la nueva en claro, una sola vez.</summary>
        public (bool ok, string clave) Rotar(int idIntegracion)
        {
            var actual = Obtener(idIntegracion);
            if (actual == null) return (false, null);
            var (prefijo, clave, hash) = GenerarClave(actual.Sandbox);
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE dbo.Integraciones SET Prefijo = @Prefijo, HashClave = @Hash WHERE Id = @Id AND FechaRevocada IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@Prefijo", prefijo);
                cmd.Parameters.Add("@Hash", System.Data.SqlDbType.VarBinary, 32).Value = hash;
                cmd.Parameters.AddWithValue("@Id", idIntegracion);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1 ? (true, clave) : (false, null);
            }
        }

        /// <summary>Revoca la credencial de forma definitiva (no se reactiva: se crea otra).</summary>
        public bool Revocar(int idIntegracion)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE dbo.Integraciones SET Activo = 0, FechaRevocada = GETDATE() WHERE Id = @Id AND FechaRevocada IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@Id", idIntegracion);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1;
            }
        }

        public bool Actualizar(int idIntegracion, string nombre, bool activo, bool permiteConfirmar, string ipPermitidas, out string error)
        {
            error = null;
            if (!TryNormalizarIps(ipPermitidas, out string ips, out error)) return false;
            nombre = (nombre ?? "").Trim();
            if (nombre.Length == 0 || nombre.Length > 100) { error = "nombre"; return false; }
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE dbo.Integraciones SET Nombre = @Nombre, Activo = @Activo, PermiteConfirmar = @Pc, IpPermitidas = @Ips WHERE Id = @Id AND FechaRevocada IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@Nombre", nombre);
                cmd.Parameters.AddWithValue("@Activo", activo);
                cmd.Parameters.AddWithValue("@Pc", permiteConfirmar);
                cmd.Parameters.AddWithValue("@Ips", (object)ips ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", idIntegracion);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1;
            }
        }

        // ── Lectura ────────────────────────────────────────────────────────────────────

        private static string SelectBase => @"
            SELECT i.Id, i.IdEmpresa, e.EmpNombre AS NombreEmpresa, i.Nombre, i.IdUsuarioTecnico,
                   LTRIM(RTRIM(ISNULL(u.Nombre,'') + ' ' + ISNULL(u.Apellidos,''))) AS NombreUsuarioTecnico,
                   i.Prefijo, i.Activo, i.PermiteConfirmar, " + (SandboxDisponible() ? "i.Sandbox" : "CAST(0 AS BIT)") + @" AS Sandbox,
                   i.IpPermitidas, i.FechaCreacion, i.FechaUltimoUso, i.IpUltimoUso, i.FechaRevocada
            FROM dbo.Integraciones i
            LEFT JOIN dbo.Empresas e ON e.EmpId = i.IdEmpresa
            LEFT JOIN dbo.Usuarios u ON u.Id = i.IdUsuarioTecnico";

        /// <summary>Integraciones (todas si <paramref name="idsEmpresa"/> es null; si no, solo las de esas empresas).</summary>
        public List<Integracion> Listar(IEnumerable<int> idsEmpresa = null)
        {
            var lista = new List<Integracion>();
            var filtro = idsEmpresa == null ? "" : " WHERE i.IdEmpresa IN (" + string.Join(",", System.Linq.Enumerable.Select(idsEmpresa, x => x.ToString())) + ")";
            if (idsEmpresa != null && filtro.EndsWith("()")) return lista;
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(SelectBase + filtro + " ORDER BY i.FechaCreacion DESC", cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) lista.Add(Mapear(r));
            }
            return lista;
        }

        /// <summary>Usuarios de la empresa que pueden ser «usuario técnico» (todos menos Super Admin).</summary>
        public List<(int Id, string Nombre, string Usuario)> ListarUsuariosElegibles(int idEmpresa)
        {
            var lista = new List<(int, string, string)>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "SELECT Id, LTRIM(RTRIM(ISNULL(Nombre,'') + ' ' + ISNULL(Apellidos,''))) AS Nombre, Usuario FROM dbo.Usuarios WHERE IdEmpresa = @E AND ISNULL(Admin,0) <> 2 ORDER BY Nombre", cn))
            {
                cmd.Parameters.AddWithValue("@E", idEmpresa);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) lista.Add((Convert.ToInt32(r["Id"]), r["Nombre"] as string, r["Usuario"] as string));
            }
            return lista;
        }

        /// <summary>Ids de los Super Admin (destinatarios de las solicitudes de ayuda del asistente de conexión).</summary>
        public List<int> ObtenerIdsSuperAdmin()
        {
            var ids = new List<int>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Id FROM dbo.Usuarios WHERE Admin = 2", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["Id"]));
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "IntegracionesService.ObtenerIdsSuperAdmin"); }
            return ids;
        }

        public Integracion Obtener(int id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(SelectBase + " WHERE i.Id = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? Mapear(r) : null;
            }
        }

        private static Integracion Mapear(SqlDataReader r) => new Integracion
        {
            Id = Convert.ToInt32(r["Id"]),
            IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
            NombreEmpresa = r["NombreEmpresa"] as string,
            Nombre = r["Nombre"] as string,
            IdUsuarioTecnico = Convert.ToInt32(r["IdUsuarioTecnico"]),
            NombreUsuarioTecnico = r["NombreUsuarioTecnico"] as string,
            Prefijo = r["Prefijo"] as string,
            Activo = Convert.ToBoolean(r["Activo"]),
            PermiteConfirmar = Convert.ToBoolean(r["PermiteConfirmar"]),
            Sandbox = Convert.ToBoolean(r["Sandbox"]),
            IpPermitidas = r["IpPermitidas"] as string,
            FechaCreacion = Convert.ToDateTime(r["FechaCreacion"]),
            FechaUltimoUso = r["FechaUltimoUso"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaUltimoUso"]),
            IpUltimoUso = r["IpUltimoUso"] as string,
            FechaRevocada = r["FechaRevocada"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaRevocada"])
        };

        // ── Autenticación de una petición ──────────────────────────────────────────────

        /// <summary>
        /// Valida la clave presentada y la IP de origen. Siempre hace una comparación de hash (contra un señuelo si el prefijo no
        /// existe) para no revelar por tiempo si una clave existe. Registra el último uso.
        /// </summary>
        public ResultadoAutenticacionApi Autenticar(string claveCompleta, string ip)
        {
            var res = new ResultadoAutenticacionApi();
            if (!TryParsear(claveCompleta, out string prefijo))
            {
                FixedTimeEquals(HashSenuelo, SHA256.HashData(Encoding.UTF8.GetBytes(claveCompleta ?? "")));
                res.Motivo = "formato_invalido";
                return res;
            }

            byte[] hashGuardado = null;
            Integracion integ = null;
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var cmd = new SqlCommand("SELECT Id, HashClave FROM dbo.Integraciones WHERE Prefijo = @P", cn))
                {
                    cmd.Parameters.AddWithValue("@P", prefijo);
                    using (var r = cmd.ExecuteReader())
                        if (r.Read()) { integ = new Integracion { Id = Convert.ToInt32(r["Id"]) }; hashGuardado = (byte[])r["HashClave"]; }
                }
            }

            var hashPresentado = SHA256.HashData(Encoding.UTF8.GetBytes(claveCompleta));
            bool coincide = FixedTimeEquals(hashGuardado ?? HashSenuelo, hashPresentado) && hashGuardado != null;
            if (!coincide) { res.Motivo = "clave_incorrecta"; return res; }

            integ = Obtener(integ.Id);
            if (integ == null || !integ.Vigente) { res.Motivo = "revocada_o_inactiva"; return res; }

            if (!IpPermitida(integ.IpPermitidas, ip)) { res.Motivo = "ip_no_permitida"; res.IpNoPermitida = true; res.Integracion = integ; return res; }

            var usuario = new UsuarioService().ObtenerUsuarioPorId(integ.IdUsuarioTecnico);
            if (usuario == null || usuario.IdEmpresa != integ.IdEmpresa) { res.Motivo = "usuario_tecnico_invalido"; return res; }

            RegistrarUso(integ.Id, ip);
            res.Ok = true; res.Integracion = integ; res.UsuarioTecnico = usuario;
            return res;
        }

        private void RegistrarUso(int idIntegracion, string ip)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("UPDATE dbo.Integraciones SET FechaUltimoUso = GETDATE(), IpUltimoUso = @Ip WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Ip", (object)Recorte(ip, 45) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", idIntegracion);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Helpers.AppLogger.Warn("IntegracionesService.RegistrarUso: " + ex.Message); }
        }

        // ── Origen del lote e idempotencia ─────────────────────────────────────────────

        /// <summary>
        /// Marca un lote como creado por la API: 'API' (vence a las 48 h en vez de a las 2 h) o 'Sandbox' (pruebas: vence como un Excel y
        /// nunca bloquea a otro cargue de la misma llave).
        /// </summary>
        public void MarcarLoteApi(long idLote, int idIntegracion, bool sandbox = false)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("UPDATE dbo.CarguesLotes SET Origen = @O, IdIntegracion = @I WHERE IdLote = @L", cn))
            {
                cmd.Parameters.AddWithValue("@O", sandbox ? "Sandbox" : "API");
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cmd.Parameters.AddWithValue("@L", idLote);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>True si el lote lo creó esta integración (la API solo ve y toca sus propios lotes).</summary>
        public bool LoteEsDeIntegracion(long idLote, int idIntegracion)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.CarguesLotes WHERE IdLote = @L AND IdIntegracion = @I", cn))
            {
                cmd.Parameters.AddWithValue("@L", idLote);
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public long? BuscarIdempotencia(int idIntegracion, string clave)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT IdLote FROM dbo.IntegracionesIdempotencia WHERE IdIntegracion = @I AND Clave = @C", cn))
            {
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cmd.Parameters.AddWithValue("@C", clave);
                cn.Open();
                var r = cmd.ExecuteScalar();
                return r == null ? (long?)null : Convert.ToInt64(r);
            }
        }

        public void GuardarIdempotencia(int idIntegracion, string clave, long idLote)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    DELETE FROM dbo.IntegracionesIdempotencia WHERE Fecha < DATEADD(DAY, -7, GETDATE());
                    IF NOT EXISTS (SELECT 1 FROM dbo.IntegracionesIdempotencia WHERE IdIntegracion = @I AND Clave = @C)
                        INSERT INTO dbo.IntegracionesIdempotencia (IdIntegracion, Clave, IdLote) VALUES (@I, @C, @L);", cn))
                {
                    cmd.Parameters.AddWithValue("@I", idIntegracion);
                    cmd.Parameters.AddWithValue("@C", clave);
                    cmd.Parameters.AddWithValue("@L", idLote);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Helpers.AppLogger.Warn("IntegracionesService.GuardarIdempotencia: " + ex.Message); }
        }

        // ── Utilidades ─────────────────────────────────────────────────────────────────

        private static (string prefijo, string clave, byte[] hash) GenerarClave(bool sandbox = false)
        {
            const string alfabeto = "abcdefghijkmnpqrstuvwxyz23456789";
            var sb = new StringBuilder(8);
            for (int i = 0; i < 8; i++) sb.Append(alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)]);
            string prefijo = sb.ToString();
            string secreto = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            string clave = (sandbox ? PrefijoClaveSandbox : PrefijoClave) + prefijo + "_" + secreto;
            return (prefijo, clave, SHA256.HashData(Encoding.UTF8.GetBytes(clave)));
        }

        private static bool TryParsear(string clave, out string prefijo)
        {
            prefijo = null;
            if (string.IsNullOrEmpty(clave) || clave.Length > 100
                || !(clave.StartsWith(PrefijoClave, StringComparison.Ordinal) || clave.StartsWith(PrefijoClaveSandbox, StringComparison.Ordinal))) return false;
            var partes = clave.Substring(PrefijoClave.Length).Split('_');
            if (partes.Length < 2 || partes[0].Length != 8) return false;
            prefijo = partes[0];
            return true;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

        /// <summary>Lista de IPs separadas por coma/espacio; se validan y normalizan. Vacía = sin restricción.</summary>
        public static bool TryNormalizarIps(string entrada, out string normalizada, out string error)
        {
            normalizada = null; error = null;
            if (string.IsNullOrWhiteSpace(entrada)) return true;
            var ips = new List<string>();
            foreach (var parte in entrada.Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!System.Net.IPAddress.TryParse(parte.Trim(), out var ip)) { error = "ip:" + parte.Trim(); return false; }
                ips.Add(ip.ToString());
            }
            normalizada = string.Join(",", ips);
            if (normalizada.Length > 500) { error = "ips_largo"; normalizada = null; return false; }
            return true;
        }

        private static bool IpPermitida(string lista, string ip)
        {
            if (string.IsNullOrWhiteSpace(lista)) return true;
            if (!System.Net.IPAddress.TryParse(ip ?? "", out var origen)) return false;
            foreach (var p in lista.Split(','))
                if (System.Net.IPAddress.TryParse(p, out var permitida) && permitida.Equals(origen)) return true;
            return false;
        }

        private static string Recorte(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
