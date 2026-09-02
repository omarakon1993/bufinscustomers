/*
 * TABLA REQUERIDA EN BD — ejecutar una vez:
 *
 *   CREATE TABLE dbo.IntentosLoginIP (
 *       Ip             NVARCHAR(45) NOT NULL CONSTRAINT PK_IntentosLoginIP PRIMARY KEY,
 *       Intentos       INT          NOT NULL CONSTRAINT DF_IntentosLoginIP_Intentos DEFAULT (0),
 *       PrimerIntento  DATETIME     NOT NULL CONSTRAINT DF_IntentosLoginIP_Primero  DEFAULT (GETDATE()),
 *       UltimoIntento  DATETIME     NOT NULL CONSTRAINT DF_IntentosLoginIP_Ultimo   DEFAULT (GETDATE()),
 *       BloqueadoHasta DATETIME     NULL
 *   );
 *   CREATE INDEX IX_IntentosLoginIP_Ultimo ON dbo.IntentosLoginIP (UltimoIntento);
 *
 * Mientras la tabla no exista, el servicio cae a un contador en memoria (HttpRuntime.Cache),
 * equivalente al comportamiento histórico: nunca deja el login sin protección.
 */

using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using System.Web.Caching;

namespace bufinscustomers.Services
{
    /// <summary>
    /// A14: rate limiting de inicios de sesión por IP, con estado PERSISTENTE en BD (sobrevive
    /// al reciclado del app pool y funciona con varios servidores web) más un contador en memoria
    /// de respaldo. Además detecta un "pico global" de intentos fallidos en todo el sistema y, en
    /// ese caso, endurece automáticamente el umbral por IP ("modo defensivo").
    ///
    /// Nunca lanza: ante cualquier error de BD cae al contador en memoria.
    /// </summary>
    public class RateLimitLoginService : BaseService
    {
        // ── Configuración (con override opcional en Web.config) ──────────────
        private static readonly int UMBRAL          = LeerInt("LoginRateLimit_Umbral", 8);
        private static readonly int UMBRAL_ESTRICTO = LeerInt("LoginRateLimit_UmbralEstricto", 3);
        private static readonly int UMBRAL_GLOBAL   = LeerInt("LoginRateLimit_UmbralGlobal", 100);
        private const int VENTANA_MIN        = 15;   // ventana de conteo por IP
        private const int BLOQUEO_MIN        = 15;   // duración del bloqueo por IP
        private const int VENTANA_GLOBAL_MIN = 10;   // ventana para medir el pico global
        private const int RETENCION_DIAS     = 1;    // se purgan las IPs inactivas más antiguas

        // ── Estado en proceso ───────────────────────────────────────────────
        private static readonly object _lock = new object();
        private static DateTime _defensivoChequeado = DateTime.MinValue;
        private static bool     _defensivoValor     = false;
        private static DateTime _ultimoAvisoDefensivo = DateTime.MinValue;
        private static DateTime _ultimaPurga        = DateTime.MinValue;

        public struct EstadoIp
        {
            public bool Bloqueada;
            public int  MinutosRestantes;
        }

        public struct ResultadoFallo
        {
            public bool Bloqueada;
            public int  MinutosRestantes;
            /// <summary>True SOLO en la transición a modo defensivo (para avisar una vez cada 30 min).</summary>
            public bool ModoDefensivoActivado;
        }

        /// <summary>¿La IP está bloqueada ahora mismo? Se llama antes de procesar el login.</summary>
        public EstadoIp Comprobar(string ip)
        {
            var res = new EstadoIp();
            if (string.IsNullOrEmpty(ip)) return res;

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT BloqueadoHasta FROM dbo.IntentosLoginIP WHERE Ip = @Ip", cn))
                {
                    cmd.Parameters.AddWithValue("@Ip", ip);
                    cn.Open();
                    object o = cmd.ExecuteScalar();
                    if (o != null && o != DBNull.Value)
                    {
                        DateTime hasta = Convert.ToDateTime(o);
                        if (hasta > DateTime.Now)
                        {
                            res.Bloqueada = true;
                            res.MinutosRestantes = (int)Math.Ceiling((hasta - DateTime.Now).TotalMinutes);
                        }
                    }
                    return res; // BD es la fuente autoritativa
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[RateLimitLoginService.Comprobar] {0}", ex.Message);
            }

            // Respaldo en memoria
            int actual = CacheContador(ip);
            if (actual >= UmbralEfectivo())
            {
                res.Bloqueada = true;
                res.MinutosRestantes = VENTANA_MIN;
            }
            return res;
        }

        /// <summary>Registra un intento fallido para la IP y devuelve el estado resultante.</summary>
        public ResultadoFallo RegistrarFallo(string ip)
        {
            var res = new ResultadoFallo();
            if (string.IsNullOrEmpty(ip)) return res;

            // Contador en memoria (best-effort, siempre)
            int enMemoria = CacheIncrementar(ip);

            int umbral = UmbralEfectivo();

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(SqlRegistrarFallo, cn))
                {
                    cmd.Parameters.AddWithValue("@Ip", ip);
                    cmd.Parameters.AddWithValue("@Ventana", VENTANA_MIN);
                    cmd.Parameters.AddWithValue("@Bloqueo", BLOQUEO_MIN);
                    cmd.Parameters.AddWithValue("@Umbral", umbral);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            DateTime? hasta = r["BloqueadoHasta"] == DBNull.Value
                                ? (DateTime?)null : Convert.ToDateTime(r["BloqueadoHasta"]);
                            if (hasta.HasValue && hasta.Value > DateTime.Now)
                            {
                                res.Bloqueada = true;
                                res.MinutosRestantes = (int)Math.Ceiling((hasta.Value - DateTime.Now).TotalMinutes);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[RateLimitLoginService.RegistrarFallo] {0}", ex.Message);
                if (enMemoria >= umbral)
                {
                    res.Bloqueada = true;
                    res.MinutosRestantes = VENTANA_MIN;
                }
            }

            res.ModoDefensivoActivado = DetectarTransicionDefensivo();
            PurgarSiToca();
            return res;
        }

        /// <summary>Login exitoso: se limpia el contador de la IP.</summary>
        public void Limpiar(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            try { HttpRuntime.Cache.Remove("_rl_" + ip); } catch { }
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM dbo.IntentosLoginIP WHERE Ip = @Ip", cn))
                {
                    cmd.Parameters.AddWithValue("@Ip", ip);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[RateLimitLoginService.Limpiar] {0}", ex.Message);
            }
        }

        /// <summary>
        /// True si la suma de intentos fallidos recientes en TODO el sistema supera el umbral
        /// global. Resultado cacheado 30 s. Ante error de BD → false (no se activa el modo).
        /// </summary>
        public bool EnModoDefensivo()
        {
            if ((DateTime.Now - _defensivoChequeado).TotalSeconds < 30) return _defensivoValor;
            lock (_lock)
            {
                if ((DateTime.Now - _defensivoChequeado).TotalSeconds < 30) return _defensivoValor;

                bool nuevo = false;
                try
                {
                    using (var cn = new SqlConnection(CadenaConexion))
                    using (var cmd = new SqlCommand(
                        @"SELECT ISNULL(SUM(Intentos), 0) FROM dbo.IntentosLoginIP
                          WHERE UltimoIntento > DATEADD(MINUTE, -@V, GETDATE())", cn))
                    {
                        cmd.Parameters.AddWithValue("@V", VENTANA_GLOBAL_MIN);
                        cn.Open();
                        nuevo = Convert.ToInt64(cmd.ExecuteScalar()) >= UMBRAL_GLOBAL;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("[RateLimitLoginService.EnModoDefensivo] {0}", ex.Message);
                }

                _defensivoValor = nuevo;
                _defensivoChequeado = DateTime.Now;
                return nuevo;
            }
        }

        // ── Helpers privados ────────────────────────────────────────────────

        private int UmbralEfectivo() => EnModoDefensivo() ? UMBRAL_ESTRICTO : UMBRAL;

        private bool DetectarTransicionDefensivo()
        {
            if (!EnModoDefensivo()) return false;
            if ((DateTime.Now - _ultimoAvisoDefensivo).TotalMinutes < 30) return false;
            lock (_lock)
            {
                if ((DateTime.Now - _ultimoAvisoDefensivo).TotalMinutes < 30) return false;
                _ultimoAvisoDefensivo = DateTime.Now;
                return true;
            }
        }

        private void PurgarSiToca()
        {
            if ((DateTime.Now - _ultimaPurga).TotalHours < 24) return;
            lock (_lock)
            {
                if ((DateTime.Now - _ultimaPurga).TotalHours < 24) return;
                _ultimaPurga = DateTime.Now;
            }
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"DELETE FROM dbo.IntentosLoginIP
                      WHERE UltimoIntento < DATEADD(DAY, -@D, GETDATE())
                        AND (BloqueadoHasta IS NULL OR BloqueadoHasta < GETDATE())", cn))
                {
                    cmd.Parameters.AddWithValue("@D", RETENCION_DIAS);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[RateLimitLoginService.PurgarSiToca] {0}", ex.Message);
            }
        }

        private static int CacheContador(string ip)
        {
            try { return HttpRuntime.Cache["_rl_" + ip] as int? ?? 0; }
            catch { return 0; }
        }

        private static int CacheIncrementar(string ip)
        {
            try
            {
                string key = "_rl_" + ip;
                int actual = (HttpRuntime.Cache[key] as int? ?? 0) + 1;
                HttpRuntime.Cache.Insert(key, actual, null,
                    DateTime.Now.AddMinutes(VENTANA_MIN), Cache.NoSlidingExpiration);
                return actual;
            }
            catch { return 0; }
        }

        private static int LeerInt(string clave, int porDefecto)
        {
            return int.TryParse(ConfigurationManager.AppSettings[clave], out int v) && v > 0 ? v : porDefecto;
        }

        // MERGE atómico por IP: si la ventana venció reinicia el contador; si no, lo incrementa.
        // Después marca el bloqueo si se alcanzó el umbral y devuelve el estado final.
        private const string SqlRegistrarFallo = @"
SET NOCOUNT ON;
DECLARE @now DATETIME = GETDATE();

MERGE dbo.IntentosLoginIP WITH (HOLDLOCK) AS t
USING (SELECT @Ip AS Ip) AS s ON t.Ip = s.Ip
WHEN MATCHED AND t.PrimerIntento < DATEADD(MINUTE, -@Ventana, @now) THEN
    UPDATE SET Intentos = 1, PrimerIntento = @now, UltimoIntento = @now, BloqueadoHasta = NULL
WHEN MATCHED THEN
    UPDATE SET Intentos = t.Intentos + 1, UltimoIntento = @now
WHEN NOT MATCHED THEN
    INSERT (Ip, Intentos, PrimerIntento, UltimoIntento) VALUES (@Ip, 1, @now, @now);

UPDATE dbo.IntentosLoginIP
   SET BloqueadoHasta = DATEADD(MINUTE, @Bloqueo, @now)
 WHERE Ip = @Ip AND Intentos >= @Umbral
   AND (BloqueadoHasta IS NULL OR BloqueadoHasta < @now);

SELECT Intentos, BloqueadoHasta FROM dbo.IntentosLoginIP WHERE Ip = @Ip;";
    }
}
