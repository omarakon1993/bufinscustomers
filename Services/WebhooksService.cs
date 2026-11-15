using bufinscustomers.Helpers;
using bufinscustomers.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class WebhookConfig
    {
        public string Url { get; set; }
        public bool TieneSecreto { get; set; }
    }

    public class WebhookEntrega
    {
        public long Id { get; set; }
        public string Evento { get; set; }
        public string Estado { get; set; }
        public int Intentos { get; set; }
        public int? Codigo { get; set; }
        public string Error { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? FechaEntrega { get; set; }
    }

    /// <summary>
    /// Webhook saliente de las integraciones: avisa a la URL del cliente cuando un cargue de la integración se confirma o se descarta
    /// (también si lo hace una persona en «Revisar cargue») y cuando terminan los modelos. Cada aviso va firmado con HMAC-SHA256
    /// (<c>X-Bufins-Signature: t={unix},v1={hex(HMAC(secreto, t + "." + cuerpo))}</c>) y se reintenta hasta 3 veces.
    /// SEGURIDAD (SSRF): solo https, sin credenciales en la URL, sin seguir redirecciones, y la conexión se hace SOLO a direcciones
    /// públicas resueltas en el momento de enviar (<see cref="EsPublica"/>), así una URL no puede apuntar a la red interna ni usar
    /// un DNS que cambie de respuesta. El secreto se guarda cifrado y no se registra jamás. Nunca lanza hacia quien lo invoca.
    /// Tolera que el script Sql/050 no se haya ejecutado (<see cref="Disponible"/>).
    /// </summary>
    public class WebhooksService : BaseService
    {
        private const string SecretoPrefijo = "whsec_";
        private static readonly int[] EsperasSegundos = { 0, 30, 300 };   // antes del intento 1, 2 y 3
        private static readonly ConcurrentDictionary<long, byte> EnVuelo = new ConcurrentDictionary<long, byte>();
        private static bool _disp; private static DateTime _dispHasta = DateTime.MinValue;
        private static DateTime _ultimoBarrido = DateTime.MinValue;
        private static readonly object _lockBarrido = new object();

        // ── Cliente HTTP con el filtro anti-SSRF en la conexión ───────────────────────

        private static readonly HttpClient Http = CrearHttp();

        private static HttpClient CrearHttp()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = async (contexto, ct) =>
                {
                    string host = contexto.DnsEndPoint.Host;
                    IPAddress[] todas = IPAddress.TryParse(host, out var literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(host, ct);
                    var publicas = todas.Where(EsPublica).ToArray();
                    if (publicas.Length == 0) throw new HttpRequestException("destino_no_permitido");

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(publicas, contexto.DnsEndPoint.Port, ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>true solo para direcciones de internet pública: rechaza loopback, privadas, enlace local, CGNAT, multicast, reservadas e IPv4 mapeadas a esas.</summary>
        public static bool EsPublica(IPAddress ip)
        {
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return false;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                if (b[0] == 0 || b[0] == 10 || b[0] >= 224) return false;                         // 0/8, 10/8, multicast y reservadas (224/4, 240/4)
                if (b[0] == 127) return false;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;                      // 100.64/10 (CGNAT)
                if (b[0] == 169 && b[1] == 254) return false;                                      // enlace local (incluye metadatos de nube)
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;                       // 172.16/12
                if (b[0] == 192 && b[1] == 168) return false;                                      // 192.168/16
                if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;                          // 192.0.0/24
                if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;                     // 198.18/15 (pruebas)
                return true;
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
                var b = ip.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return false;                                           // fc00::/7 (ULA)
                return true;
            }
            return false;
        }

        // ── Disponibilidad ─────────────────────────────────────────────────────────────

        public static bool Disponible()
        {
            if (DateTime.Now < _dispHasta) return _disp;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT CASE WHEN COL_LENGTH('dbo.Integraciones','WebhookUrl') IS NOT NULL AND COL_LENGTH('dbo.Integraciones','WebhookSecreto') IS NOT NULL " +
                    "AND OBJECT_ID('dbo.IntegracionesWebhookEntregas','U') IS NOT NULL THEN 1 ELSE 0 END", cn))
                {
                    cn.Open();
                    _disp = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch { _disp = false; }
            _dispHasta = DateTime.Now.AddSeconds(_disp ? 60 : 15);
            return _disp;
        }

        // ── Configuración (la usa la pantalla Integraciones) ───────────────────────────

        public WebhookConfig ObtenerConfig(int idIntegracion)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT WebhookUrl, WebhookSecreto FROM dbo.Integraciones WHERE Id = @I", cn))
            {
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read()
                        ? new WebhookConfig { Url = r["WebhookUrl"] as string, TieneSecreto = !string.IsNullOrEmpty(r["WebhookSecreto"] as string) }
                        : new WebhookConfig();
            }
        }

        /// <summary>
        /// Guarda la URL (vacía = desactiva el webhook). Si no había secreto, genera uno y lo devuelve (única vez que se ve).
        /// Errores como CÓDIGO: <c>url_invalida</c>, <c>solo_https</c>, <c>destino_privado</c>, <c>host_no_resuelve</c>, <c>no_disponible</c>, <c>cifrado</c>.
        /// </summary>
        public (bool ok, string error, string secretoNuevo) Guardar(int idIntegracion, string url)
        {
            if (!Disponible()) return (false, "no_disponible", null);
            url = (url ?? "").Trim();

            string secretoNuevo = null;
            string cifrado = null;
            if (url.Length > 0)
            {
                var err = ValidarUrl(url);
                if (err != null) return (false, err, null);

                if (!ObtenerConfig(idIntegracion).TieneSecreto)
                {
                    secretoNuevo = GenerarSecreto();
                    cifrado = SecretosProtegidos.Cifrar(secretoNuevo);
                    if (!SecretosProtegidos.EstaCifrado(cifrado)) return (false, "cifrado", null);   // nunca se guarda un secreto en claro
                }
            }

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE dbo.Integraciones SET WebhookUrl = @U" + (cifrado != null ? ", WebhookSecreto = @S" : "") + " WHERE Id = @I AND FechaRevocada IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@U", url.Length == 0 ? (object)DBNull.Value : url);
                if (cifrado != null) cmd.Parameters.AddWithValue("@S", cifrado);
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1 ? (true, null, secretoNuevo) : (false, "no_disponible", null);
            }
        }

        public (bool ok, string secreto, string error) RotarSecreto(int idIntegracion)
        {
            if (!Disponible()) return (false, null, "no_disponible");
            string nuevo = GenerarSecreto();
            string cifrado = SecretosProtegidos.Cifrar(nuevo);
            if (!SecretosProtegidos.EstaCifrado(cifrado)) return (false, null, "cifrado");
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("UPDATE dbo.Integraciones SET WebhookSecreto = @S WHERE Id = @I AND FechaRevocada IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@S", cifrado);
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                return cmd.ExecuteNonQuery() == 1 ? (true, nuevo, null) : (false, null, "no_disponible");
            }
        }

        public List<WebhookEntrega> UltimasEntregas(int idIntegracion, int n = 10)
        {
            var lista = new List<WebhookEntrega>();
            if (!Disponible()) return lista;
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                SELECT TOP (@N) Id, Evento, Estado, Intentos, UltimoCodigo, UltimoError, Fecha, FechaEntrega
                FROM dbo.IntegracionesWebhookEntregas WHERE IdIntegracion = @I ORDER BY Id DESC", cn))
            {
                cmd.Parameters.AddWithValue("@N", n);
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(new WebhookEntrega
                        {
                            Id = Convert.ToInt64(r["Id"]), Evento = r["Evento"].ToString(), Estado = r["Estado"].ToString(), Intentos = Convert.ToInt32(r["Intentos"]),
                            Codigo = r["UltimoCodigo"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["UltimoCodigo"]), Error = r["UltimoError"] as string,
                            Fecha = Convert.ToDateTime(r["Fecha"]), FechaEntrega = r["FechaEntrega"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaEntrega"])
                        });
            }
            return lista;
        }

        /// <summary>Envía un aviso de prueba (un solo intento, sin reintentos) para que el cliente verifique su receptor y su firma.</summary>
        public (bool ok, int? codigo, string error) EnviarPrueba(int idIntegracion)
        {
            try
            {
                var integ = new IntegracionesService().Obtener(idIntegracion);
                var (url, secreto) = ObtenerUrlYSecreto(idIntegracion);
                if (integ == null || url == null || secreto == null) return (false, null, "sin_webhook");

                var idEvento = Guid.NewGuid();
                string cuerpo = ConstruirCuerpo(idEvento, "webhook.prueba", integ, new { mensaje = "Aviso de prueba de BUFINS." });
                long id = InsertarEntrega(idIntegracion, idEvento, "webhook.prueba", cuerpo, siguienteIntento: null);

                var r = Intentar(url, secreto, idEvento, "webhook.prueba", cuerpo).GetAwaiter().GetResult();
                Marcar(id, r.ok ? "Entregado" : "Fallido", 1, r.codigo, r.error, null);
                return (r.ok, r.codigo, r.error);
            }
            catch (Exception ex) { AppLogger.Error(ex, "WebhooksService.EnviarPrueba"); return (false, null, "error_interno"); }
        }

        // ── Eventos de negocio (los llaman el orquestador y la ejecución de modelos) ───

        /// <summary>Id de la integración que creó el lote (null si fue de Excel o si no existe la columna).</summary>
        public int? IdIntegracionDeLote(long idLote)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT IdIntegracion FROM dbo.CarguesLotes WHERE IdLote = @L", cn))
                {
                    cmd.Parameters.AddWithValue("@L", idLote);
                    cn.Open();
                    var r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? (int?)null : Convert.ToInt32(r);
                }
            }
            catch { return null; }
        }

        public void NotificarCargueConfirmado(int? idIntegracion, CargueLote lote, ResultadoCargaExcel resultado, int idUsuarioConfirma, bool simulado)
        {
            if (!idIntegracion.HasValue || lote == null) return;
            try
            {
                var integ = new IntegracionesService().Obtener(idIntegracion.Value);
                if (integ == null) return;
                Encolar(integ, "cargue.confirmado", new
                {
                    idLote = lote.IdLote, anio = lote.Anio, modo = lote.Modo == 0 ? "ejecucion" : "historico", idEscenario = lote.IdEscenario,
                    archivo = lote.NombreArchivo, filas = lote.TotalFilas, simulado,
                    confirmadoPor = idUsuarioConfirma == integ.IdUsuarioTecnico ? "integracion" : "persona",
                    hojas = resultado?.DetalleHojas?.Select(h => new { hoja = h.NombreHoja, tabla = h.NombreTabla, filas = h.FilasInsertadas }).ToList()
                });
            }
            catch (Exception ex) { AppLogger.Warn("WebhooksService.NotificarCargueConfirmado: " + ex.Message); }
        }

        public void NotificarCargueDescartado(int? idIntegracion, CargueLote lote, int idUsuarioDescarta)
        {
            if (!idIntegracion.HasValue || lote == null) return;
            try
            {
                var integ = new IntegracionesService().Obtener(idIntegracion.Value);
                if (integ == null) return;
                Encolar(integ, "cargue.descartado", new
                {
                    idLote = lote.IdLote, anio = lote.Anio, modo = lote.Modo == 0 ? "ejecucion" : "historico", idEscenario = lote.IdEscenario,
                    archivo = lote.NombreArchivo, descartadoPor = idUsuarioDescarta == integ.IdUsuarioTecnico ? "integracion" : "persona"
                });
            }
            catch (Exception ex) { AppLogger.Warn("WebhooksService.NotificarCargueDescartado: " + ex.Message); }
        }

        public void NotificarModelosCompletados(Integracion integ, long idEjecucion, string estado, int idEscenario, IEnumerable<EjecucionModeloItem> modelos)
        {
            try
            {
                Encolar(integ, "modelos.completados", new
                {
                    idEjecucion, estado, idEscenario,
                    modelos = modelos.Select(m => new { modelo = m.Modelo, estado = m.Estado, mensaje = m.Mensaje, segundos = m.Segundos }).ToList()
                });
            }
            catch (Exception ex) { AppLogger.Warn("WebhooksService.NotificarModelosCompletados: " + ex.Message); }
        }

        // ── Cola y entrega ─────────────────────────────────────────────────────────────

        private void Encolar(Integracion integ, string evento, object datos)
        {
            if (!Disponible()) return;
            var (url, secreto) = ObtenerUrlYSecreto(integ.Id);
            if (url == null || secreto == null) return;   // sin webhook configurado: no se encola nada

            var idEvento = Guid.NewGuid();
            string cuerpo = ConstruirCuerpo(idEvento, evento, integ, datos);
            long id = InsertarEntrega(integ.Id, idEvento, evento, cuerpo, siguienteIntento: DateTime.Now);
            Task.Run(() => Entregar(id));
            BarrerSiToca();
        }

        private static string ConstruirCuerpo(Guid idEvento, string evento, Integracion integ, object datos) =>
            JsonSerializer.Serialize(new
            {
                id = idEvento, evento, fecha = DateTime.UtcNow, sandbox = integ.Sandbox,
                integracion = new { id = integ.Id, nombre = integ.Nombre }, datos
            });

        private long InsertarEntrega(int idIntegracion, Guid idEvento, string evento, string cuerpo, DateTime? siguienteIntento)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                INSERT INTO dbo.IntegracionesWebhookEntregas (IdIntegracion, IdEvento, Evento, Payload, Estado, FechaProximoIntento)
                VALUES (@I, @G, @E, @P, 'Pendiente', @F);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cmd.Parameters.AddWithValue("@G", idEvento);
                cmd.Parameters.AddWithValue("@E", evento);
                cmd.Parameters.AddWithValue("@P", cuerpo);
                cmd.Parameters.AddWithValue("@F", (object)siguienteIntento ?? DBNull.Value);
                cn.Open();
                return (long)cmd.ExecuteScalar();
            }
        }

        private async Task Entregar(long idEntrega)
        {
            if (!EnVuelo.TryAdd(idEntrega, 0)) return;   // otro hilo ya la está entregando
            try
            {
                int idIntegracion, intentos; Guid idEvento; string evento, cuerpo;
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT IdIntegracion, IdEvento, Evento, Payload, Intentos FROM dbo.IntegracionesWebhookEntregas WHERE Id = @Id AND Estado = 'Pendiente'", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idEntrega);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return;
                        idIntegracion = Convert.ToInt32(r["IdIntegracion"]); idEvento = (Guid)r["IdEvento"];
                        evento = r["Evento"].ToString(); cuerpo = r["Payload"].ToString(); intentos = Convert.ToInt32(r["Intentos"]);
                    }
                }

                var (url, secreto) = ObtenerUrlYSecreto(idIntegracion);
                if (url == null || secreto == null) { Marcar(idEntrega, "Fallido", intentos, null, "Webhook desactivado o sin secreto.", null); return; }

                for (int n = intentos; n < EsperasSegundos.Length; n++)
                {
                    if (n > intentos) await Task.Delay(TimeSpan.FromSeconds(EsperasSegundos[n]));
                    var r = await Intentar(url, secreto, idEvento, evento, cuerpo);
                    if (r.ok) { Marcar(idEntrega, "Entregado", n + 1, r.codigo, null, null); return; }

                    bool ultimo = n == EsperasSegundos.Length - 1;
                    Marcar(idEntrega, ultimo ? "Fallido" : "Pendiente", n + 1, r.codigo, r.error,
                        ultimo ? (DateTime?)null : DateTime.Now.AddSeconds(EsperasSegundos[n + 1]));
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "WebhooksService.Entregar"); }
            finally { EnVuelo.TryRemove(idEntrega, out _); }
        }

        private static async Task<(bool ok, int? codigo, string error)> Intentar(string url, string secreto, Guid idEvento, string evento, string cuerpo)
        {
            long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string firma = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes(t + "." + cuerpo))).ToLowerInvariant();

            using (var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") })
            {
                req.Headers.TryAddWithoutValidation("X-Bufins-Signature", $"t={t},v1={firma}");
                req.Headers.TryAddWithoutValidation("X-Bufins-Event", evento);
                req.Headers.TryAddWithoutValidation("X-Bufins-Delivery", idEvento.ToString());
                req.Headers.UserAgent.ParseAdd("BUFINS-Webhook/1.0");
                try
                {
                    using (var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
                    {
                        int codigo = (int)resp.StatusCode;
                        bool ok = codigo >= 200 && codigo < 300;
                        return (ok, codigo, ok ? null : "HTTP " + codigo);
                    }
                }
                catch (TaskCanceledException) { return (false, null, "Tiempo de espera agotado (15 s)."); }
                catch (HttpRequestException ex) { return (false, null, Recorte(ex.InnerException?.Message ?? ex.Message, 280)); }
                catch (Exception ex) { return (false, null, Recorte(ex.Message, 280)); }
            }
        }

        private void Marcar(long id, string estado, int intentos, int? codigo, string error, DateTime? proximo)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.IntegracionesWebhookEntregas
                       SET Estado = @E, Intentos = @N, UltimoCodigo = @C, UltimoError = @Er, FechaProximoIntento = @P,
                           FechaEntrega = CASE WHEN @E = 'Entregado' THEN GETDATE() ELSE FechaEntrega END
                     WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@E", estado);
                    cmd.Parameters.AddWithValue("@N", intentos);
                    cmd.Parameters.AddWithValue("@C", (object)codigo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Er", (object)Recorte(error, 300) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@P", (object)proximo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "WebhooksService.Marcar"); }
        }

        /// <summary>
        /// Retoma los avisos pendientes que ya vencieron (p. ej. el sitio se reinició en pleno reintento) y borra los de más de 30 días.
        /// Como mucho una vez por minuto; lo dispara la actividad de la API (no hay tareas programadas en el hosting).
        /// </summary>
        public void BarrerSiToca()
        {
            if (!Disponible()) return;
            if ((DateTime.Now - _ultimoBarrido).TotalSeconds < 60) return;
            lock (_lockBarrido)
            {
                if ((DateTime.Now - _ultimoBarrido).TotalSeconds < 60) return;
                _ultimoBarrido = DateTime.Now;
            }
            Task.Run(() =>
            {
                try
                {
                    var ids = new List<long>();
                    using (var cn = new SqlConnection(CadenaConexion))
                    {
                        cn.Open();
                        using (var del = new SqlCommand("DELETE FROM dbo.IntegracionesWebhookEntregas WHERE Fecha < DATEADD(DAY, -30, GETDATE())", cn)) del.ExecuteNonQuery();
                        using (var cmd = new SqlCommand(
                            "SELECT TOP 20 Id FROM dbo.IntegracionesWebhookEntregas WHERE Estado = 'Pendiente' AND FechaProximoIntento <= GETDATE() ORDER BY Id", cn))
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) ids.Add(r.GetInt64(0));
                    }
                    foreach (var id in ids) _ = Entregar(id);
                }
                catch (Exception ex) { AppLogger.Warn("WebhooksService.BarrerSiToca: " + ex.Message); }
            });
        }

        // ── Utilidades ─────────────────────────────────────────────────────────────────

        private (string url, string secreto) ObtenerUrlYSecreto(int idIntegracion)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT WebhookUrl, WebhookSecreto FROM dbo.Integraciones WHERE Id = @I AND Activo = 1 AND FechaRevocada IS NULL", cn))
            {
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return (null, null);
                    string url = r["WebhookUrl"] as string;
                    string sec = SecretosProtegidos.Descifrar(r["WebhookSecreto"] as string);
                    return (string.IsNullOrWhiteSpace(url) ? null : url, string.IsNullOrEmpty(sec) ? null : sec);
                }
            }
        }

        /// <summary>Valida la URL del webhook. Devuelve un código de error o null si es válida.</summary>
        public static string ValidarUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "url_invalida";
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return "solo_https";
            if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host)) return "url_invalida";

            IPAddress[] ips;
            if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal)) ips = new[] { literal };
            else
            {
                try { ips = Dns.GetHostAddresses(uri.Host); }
                catch (Exception) { return "host_no_resuelve"; }
            }
            return ips.Any(EsPublica) ? null : "destino_privado";
        }

        private static string GenerarSecreto() =>
            SecretoPrefijo + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static string Recorte(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
