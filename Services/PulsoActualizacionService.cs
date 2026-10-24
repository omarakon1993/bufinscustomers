using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Hosting;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Actualización de los datos de Pulso Bufins público (bufins.com/pulso-bufins) desde el portal.
    /// El trabajo pesado NO corre dentro de IIS: se lanza PulsoEtl.exe como proceso aparte, que escribe los
    /// archivos de datos en la carpeta App_Data\pulso del sitio bufins.com y reporta su avance en etl_status.json.
    ///
    /// Rutas (sin valores "quemados" en el código ni en Web.config), en este orden:
    ///   1) clave de dbo.ConfiguracionSistema  (PulsoEtlExe / PulsoDatosDir; editable en la propia pantalla)
    ///   2) clave de appSettings con el mismo nombre
    ///   3) autodetección: el .exe en bin\ o App_Data\PulsoEtl\ del portal, o en la carpeta hermana bufinsweb\Tools\...;
    ///      los datos en la carpeta hermana bufinsweb\App_Data\pulso.
    /// Una ruta relativa (o que empiece por ~/) se resuelve desde la carpeta raíz del portal.
    /// </summary>
    public class PulsoActualizacionService
    {
        public const string ClaveExe = "PulsoEtlExe";
        public const string ClaveDatos = "PulsoDatosDir";

        private static readonly string[] Datasets = { "6hqw-m3dm", "pfdp-zks5", "prwj-nzxa", "ctcp-462n" };
        private const string CacheFuente = "obsdatos:fuente";

        public class Rutas
        {
            public string Exe, Datos;               // rutas absolutas resueltas (null si no se pudo determinar)
            public string OrigenExe, OrigenDatos;   // "config" | "auto" | null
            public string ValorExe, ValorDatos;     // valor configurado tal cual (para editarlo)
        }

        private Rutas _rutas;

        private static string Raiz()
        {
            var r = HostingEnvironment.MapPath("~/") ?? AppDomain.CurrentDomain.BaseDirectory;
            return Path.GetFullPath(r);
        }

        private static string Configurado(string clave)
        {
            string v = null;
            try { v = new ConfiguracionSistemaService().ObtenerValor(clave); } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(v)) v = ConfigurationManager.AppSettings[clave];
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        private static string Resolver(string valor)
        {
            if (valor.StartsWith("~/") || valor.StartsWith("~\\")) valor = valor.Substring(2);
            return Path.GetFullPath(Path.IsPathRooted(valor) ? valor : Path.Combine(Raiz(), valor));
        }

        public Rutas ObtenerRutas()
        {
            if (_rutas != null) return _rutas;
            var raiz = Raiz();
            var r = new Rutas { ValorExe = Configurado(ClaveExe), ValorDatos = Configurado(ClaveDatos) };

            if (r.ValorExe != null) { r.Exe = Resolver(r.ValorExe); r.OrigenExe = "config"; }
            else
            {
                var candidatos = new[]
                {
                    Path.Combine(raiz, "bin", "PulsoEtl.exe"),
                    Path.Combine(raiz, "App_Data", "PulsoEtl", "PulsoEtl.exe"),
                    Path.GetFullPath(Path.Combine(raiz, "..", "bufinsweb", "Tools", "PulsoEtl", "bin", "Release", "net48", "PulsoEtl.exe"))
                };
                r.Exe = candidatos.FirstOrDefault(File.Exists);
                if (r.Exe != null) r.OrigenExe = "auto";
            }

            if (r.ValorDatos != null) { r.Datos = Resolver(r.ValorDatos); r.OrigenDatos = "config"; }
            else
            {
                var hermana = Path.GetFullPath(Path.Combine(raiz, "..", "bufinsweb", "App_Data", "pulso"));
                if (Directory.Exists(hermana)) { r.Datos = hermana; r.OrigenDatos = "auto"; }
            }
            return _rutas = r;
        }

        public string ExePath { get { return ObtenerRutas().Exe ?? ""; } }
        public string DatosDir { get { return ObtenerRutas().Datos ?? ""; } }

        /// <summary>Devuelve null si todo está bien configurado; si no, el motivo.</summary>
        public string ProblemaConfiguracion()
        {
            if (ExePath.Length == 0 || DatosDir.Length == 0) return "SinConfigurar";
            if (!File.Exists(ExePath)) return "ExeNoExiste";
            if (!Directory.Exists(DatosDir)) return "CarpetaNoExiste";
            return null;
        }

        /// <summary>Guarda las rutas en dbo.ConfiguracionSistema (vacío = autodetectar). Devuelve null si ok o el código del error.</summary>
        public string GuardarRutas(string exe, string datos)
        {
            exe = (exe ?? "").Trim(); datos = (datos ?? "").Trim();
            if (exe.Length > 0 && !File.Exists(Resolver(exe))) return "ExeNoExiste";
            if (datos.Length > 0 && !Directory.Exists(Resolver(datos))) return "CarpetaNoExiste";
            var cfg = new ConfiguracionSistemaService();
            var ok = cfg.Guardar(ClaveExe, exe, "Pulso Bufins: ruta de PulsoEtl.exe (vacio = autodetectar)")
                  && cfg.Guardar(ClaveDatos, datos, "Pulso Bufins: carpeta App_Data\\pulso del sitio bufins.com (vacio = autodetectar)");
            _rutas = null;
            return ok ? null : "ErrorGuardar";
        }

        // ------------------------------------------------------------------ estado

        public object ObtenerEstado()
        {
            var problema = ProblemaConfiguracion();
            var rutas = ObtenerRutas();
            Dictionary<string, object> publicado = null, job = null;
            var enEjecucion = false;
            if (DatosDir.Length > 0 && Directory.Exists(DatosDir))
            {
                publicado = LeerJson(Path.Combine(DatosDir, "meta.json"));
                enEjecucion = EstaEnEjecucion();
                job = LeerJob(enEjecucion);
            }
            var fuente = ConsultarFuente();
            var fechaFuente = fuente.Item1;
            string fechaPublicada = null;
            if (publicado != null && publicado.ContainsKey("fuenteActualizado")) fechaPublicada = Convert.ToString(publicado["fuenteActualizado"]);

            return new
            {
                ok = true,
                configurado = problema == null,
                problema,
                rutas = new { exe = rutas.Exe, datos = rutas.Datos, origenExe = rutas.OrigenExe, origenDatos = rutas.OrigenDatos, valorExe = rutas.ValorExe, valorDatos = rutas.ValorDatos },
                publicado,
                fuente = new { fecha = fechaFuente, error = fuente.Item2 },
                hayNuevos = fechaFuente != null && fechaPublicada != null && string.CompareOrdinal(fechaFuente, fechaPublicada) > 0,
                enEjecucion,
                job
            };
        }

        private Dictionary<string, object> LeerJob(bool enEjecucion)
        {
            var j = LeerJson(Path.Combine(DatosDir, "etl_status.json"));
            if (j == null) return null;
            // "corriendo" pero sin bloqueo tomado => se interrumpió (reinicio del servidor, cierre manual…)
            if (Convert.ToString(j["estado"]) == "corriendo" && !enEjecucion) j["estado"] = "interrumpido";
            return j;
        }

        private static Dictionary<string, object> LeerJson(string ruta)
        {
            try
            {
                if (!File.Exists(ruta)) return null;
                string txt;
                using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) txt = sr.ReadToEnd();
                var o = JObject.Parse(txt);
                return o.ToObject<Dictionary<string, object>>().ToDictionary(kv => kv.Key, kv => Aplanar(kv.Value));
            }
            catch (Exception) { return null; }
        }

        private static object Aplanar(object v)
        {
            var t = v as JToken;
            if (t == null) return v;
            if (t is JArray) return ((JArray)t).Select(x => (object)x.ToString()).ToList();
            var jv = t as JValue;
            return jv != null ? jv.Value : t.ToString();
        }

        /// <summary>Hay una ejecución en curso si el archivo de bloqueo está tomado por otro proceso.</summary>
        public bool EstaEnEjecucion()
        {
            try
            {
                var lk = Path.Combine(DatosDir, "etl.lock");
                if (!File.Exists(lk)) return false;
                using (new FileStream(lk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { return false; }
            }
            catch (IOException) { return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Fecha de última actualización de la fuente (la más reciente de los 4 datasets). Caché de 10 min.</summary>
        private Tuple<string, string> ConsultarFuente()
        {
            var c = HttpRuntime.Cache[CacheFuente] as Tuple<string, string>;
            if (c != null) return c;
            Tuple<string, string> res;
            try
            {
                long max = 0;
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;   // datos.gov.co exige TLS 1.2+
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                {
                    foreach (var id in Datasets)
                    {
                        var json = http.GetStringAsync("https://www.datos.gov.co/api/views/" + id + ".json").GetAwaiter().GetResult();
                        var t = JObject.Parse(json)["rowsUpdatedAt"];
                        if (t != null) max = Math.Max(max, t.Value<long>());
                    }
                }
                res = Tuple.Create(max > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(max).ToString("yyyy-MM-dd") : null, (string)null);
                HttpRuntime.Cache.Insert(CacheFuente, res, null, DateTime.UtcNow.AddMinutes(10), System.Web.Caching.Cache.NoSlidingExpiration);
            }
            catch (Exception ex)
            {
                res = Tuple.Create((string)null, ex.GetBaseException().Message);
                HttpRuntime.Cache.Insert(CacheFuente, res, null, DateTime.UtcNow.AddMinutes(1), System.Web.Caching.Cache.NoSlidingExpiration);
            }
            return res;
        }

        // ------------------------------------------------------------------ iniciar

        public class ResultadoInicio { public bool Ok; public string Codigo; public string Detalle; }

        public ResultadoInicio Iniciar(bool forzar)
        {
            var problema = ProblemaConfiguracion();
            if (problema != null) return new ResultadoInicio { Ok = false, Codigo = problema };
            if (EstaEnEjecucion()) return new ResultadoInicio { Ok = false, Codigo = "YaEnCurso" };
            try
            {
                var args = "--out \"" + DatosDir.TrimEnd('\\') + "\"" + (forzar ? " --force" : "");
                var psi = new ProcessStartInfo(ExePath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(ExePath)
                };
                Process.Start(psi);
                return new ResultadoInicio { Ok = true, Codigo = "Iniciado" };
            }
            catch (Exception ex)
            {
                return new ResultadoInicio { Ok = false, Codigo = "ErrorInicio", Detalle = ex.GetBaseException().Message };
            }
        }

        /// <summary>
        /// Uso de Pulso Bufins de los últimos N días, leído de DatosDir\uso\yyyy-MM.json (contadores anónimos que escribe bufins.com:
        /// "día|tipo|clave" -> cantidad). No contiene IP, cookies ni datos personales.
        /// </summary>
        public object ObtenerUso(int dias)
        {
            dias = Math.Max(1, Math.Min(dias, 90));
            var datos = DatosDir;
            var carpeta = string.IsNullOrEmpty(datos) ? null : Path.Combine(datos, "uso");
            if (carpeta == null || !Directory.Exists(carpeta))
                return new { disponible = false, dias };

            var desde = DateTime.Today.AddDays(-(dias - 1));
            var totales = new Dictionary<string, long>();
            var claves = new Dictionary<string, Dictionary<string, long>>();
            var porDia = new SortedDictionary<string, long>();
            foreach (var archivo in Directory.GetFiles(carpeta, "*.json"))
            {
                JObject j;
                try { j = JObject.Parse(File.ReadAllText(archivo, Encoding.UTF8)); } catch (Exception) { continue; }
                foreach (var kv in j)
                {
                    var partes = kv.Key.Split('|');
                    DateTime dia;
                    if (partes.Length != 3 || !DateTime.TryParseExact(partes[0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dia) || dia < desde) continue;
                    var n = (long)kv.Value;
                    long t; totales.TryGetValue(partes[1], out t); totales[partes[1]] = t + n;
                    if (partes[1].StartsWith("vista_")) { long d0; porDia.TryGetValue(partes[0], out d0); porDia[partes[0]] = d0 + n; }
                    if (partes[2].Length == 0) continue;
                    Dictionary<string, long> m;
                    if (!claves.TryGetValue(partes[1], out m)) claves[partes[1]] = m = new Dictionary<string, long>();
                    long c; m.TryGetValue(partes[2], out c); m[partes[2]] = c + n;
                }
            }
            return new
            {
                disponible = true,
                dias,
                totales,
                porDia = porDia.Select(x => new { dia = x.Key, n = x.Value }).ToList(),
                top = claves.ToDictionary(k => k.Key, k => k.Value.OrderByDescending(x => x.Value).Take(8).Select(x => new { clave = x.Key, n = x.Value }).ToList())
            };
        }
    }
}
