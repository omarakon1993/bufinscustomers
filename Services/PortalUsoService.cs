using bufinscustomers.Helpers;
using bufinscustomers.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Auditoría del portal público (hoy «Pulso Bufins»; el nombre visible es configurable, ver <see cref="PortalPublicoConfig"/>).
    /// Lee, de la carpeta de datos del sitio público, el registro de eventos ANÓNIMOS (<c>uso\eventos\yyyy-MM-dd.jsonl</c>) y los
    /// contadores mensuales (<c>uso\yyyy-MM.json</c>) que escribe bufins.com, y calcula visitas, recorridos, qué consultan,
    /// origen, audiencia y salud del sitio. No hay datos personales: el visitante es un identificador que se renueva cada día.
    /// </summary>
    public class PortalUsoService : BaseService
    {
        /// <summary>Máximo de días que se analizan de una vez (el sitio conserva 180).</summary>
        public const int MaxDias = 180;
        private const int GapSesionMin = 30;

        private class Ev
        {
            public DateTime Ts; public DateTime Dia; public string V, T, K, X, N, D, B, S, L, Z, R, Rh, U; public int Ms, Sc;
            public string VKey { get { return Dia.ToString("yyyyMMdd") + V; } }
        }

        private class CacheDia { public DateTime Escritura; public long Largo; public List<Ev> Eventos; }
        private static readonly ConcurrentDictionary<string, CacheDia> Cache = new ConcurrentDictionary<string, CacheDia>();

        private static string _datosDir; private static DateTime _datosDirHora = DateTime.MinValue;

        /// <summary>Carpeta de datos del sitio público (la misma de «Datos de Pulso Bufins»); se relee cada minuto.</summary>
        private static string DatosDir()
        {
            if (DateTime.UtcNow - _datosDirHora > TimeSpan.FromMinutes(1))
            {
                try { _datosDir = new PulsoActualizacionService().DatosDir; } catch (Exception) { _datosDir = null; }
                _datosDirHora = DateTime.UtcNow;
            }
            return _datosDir;
        }

        private static string Carpeta()
        {
            var datos = DatosDir();
            return string.IsNullOrEmpty(datos) ? null : Path.Combine(datos, "uso");
        }

        // ------------------------------------------------------------------ lectura

        private static List<Ev> LeerDia(string ruta, DateTime dia)
        {
            var fi = new FileInfo(ruta);
            CacheDia c;
            if (Cache.TryGetValue(ruta, out c) && c.Escritura == fi.LastWriteTimeUtc && c.Largo == fi.Length) return c.Eventos;

            var lista = new List<Ev>();
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string linea;
                while ((linea = sr.ReadLine()) != null)
                {
                    if (linea.Length < 5) continue;
                    try
                    {
                        var j = JObject.Parse(linea);
                        DateTime h;
                        if (!DateTime.TryParseExact((string)j["h"], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out h)) continue;
                        lista.Add(new Ev
                        {
                            Dia = dia, Ts = dia.Add(h.TimeOfDay),
                            V = (string)j["v"] ?? "", T = (string)j["t"] ?? "", K = (string)j["k"] ?? "", X = (string)j["x"] ?? "",
                            N = (string)j["n"] ?? "", D = (string)j["d"] ?? "", B = (string)j["b"] ?? "", S = (string)j["s"] ?? "",
                            L = (string)j["l"] ?? "", Z = (string)j["z"] ?? "", R = (string)j["r"] ?? "", Rh = (string)j["rh"] ?? "",
                            U = (string)j["u"] ?? "", Ms = j["ms"] != null ? (int)j["ms"] : 0, Sc = j["sc"] != null ? (int)j["sc"] : 0
                        });
                    }
                    catch (Exception) { /* línea corrupta (corte durante la escritura): se omite */ }
                }
            }
            Cache[ruta] = new CacheDia { Escritura = fi.LastWriteTimeUtc, Largo = fi.Length, Eventos = lista };
            return lista;
        }

        private static List<Ev> Leer(string carpeta, DateTime desde, DateTime hasta)
        {
            var todos = new List<Ev>();
            var dir = Path.Combine(carpeta, "eventos");
            if (!Directory.Exists(dir)) return todos;
            for (var d = desde.Date; d <= hasta.Date; d = d.AddDays(1))
            {
                var ruta = Path.Combine(dir, d.ToString("yyyy-MM-dd") + ".jsonl");
                if (File.Exists(ruta)) { try { todos.AddRange(LeerDia(ruta, d)); } catch (IOException) { } }
            }
            return todos;
        }

        /// <summary>Contadores diarios (día|tipo|clave → n) de los meses que cubre el rango. Los usan los bots y los límites (no tienen eventos).</summary>
        private static Dictionary<string, long> LeerContadores(string carpeta, DateTime desde, DateTime hasta)
        {
            var res = new Dictionary<string, long>();
            for (var m = new DateTime(desde.Year, desde.Month, 1); m <= hasta; m = m.AddMonths(1))
            {
                var ruta = Path.Combine(carpeta, m.ToString("yyyy-MM") + ".json");
                if (!File.Exists(ruta)) continue;
                try
                {
                    var j = JObject.Parse(File.ReadAllText(ruta, Encoding.UTF8));
                    foreach (var kv in j)
                    {
                        var p = kv.Key.Split('|');
                        DateTime dia;
                        if (p.Length != 3 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dia)) continue;
                        if (dia < desde.Date || dia > hasta.Date) continue;
                        res[kv.Key] = (long)kv.Value;
                    }
                }
                catch (Exception) { }
            }
            return res;
        }

        // ------------------------------------------------------------------ catálogo CIIU (nombres legibles)

        private static JObject _ciiu; private static DateTime _ciiuEscritura;
        private static string NombreSector(string clave)
        {
            if (string.IsNullOrEmpty(clave)) return "";
            try
            {
                var datos = DatosDir();
                var ruta = string.IsNullOrEmpty(datos) ? null : Path.Combine(datos, "ciiu.json");
                if (ruta != null && File.Exists(ruta))
                {
                    var w = File.GetLastWriteTimeUtc(ruta);
                    if (_ciiu == null || w != _ciiuEscritura) { _ciiu = JObject.Parse(File.ReadAllText(ruta, Encoding.UTF8)); _ciiuEscritura = w; }
                }
                if (_ciiu == null) return clave;
                var m = Regex.Match(clave, "^(seccion-|ciiu-)?([a-z0-9]+)$", RegexOptions.IgnoreCase);
                if (!m.Success) return clave;
                var cod = m.Groups[2].Value;
                if (m.Groups[1].Value.StartsWith("seccion", StringComparison.OrdinalIgnoreCase))
                    return (string)_ciiu["secciones"][cod.ToUpperInvariant()] ?? clave;
                if (cod.Length == 4) return cod + " · " + ((string)_ciiu["clases"][cod] ?? "");
                if (cod.Length == 3) return cod + " · " + ((string)_ciiu["grupos"][cod] ?? "");
                if (cod.Length == 2) return cod + " · " + ((string)_ciiu["divisiones"][cod]?["n"] ?? "");
                return clave;
            }
            catch (Exception) { return clave; }
        }

        // ------------------------------------------------------------------ análisis

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool EsVista(Ev e) { return e.T.StartsWith("vista_", StringComparison.Ordinal) || e.T == "informe_pdf"; }
        private static string TipoPagina(Ev e) { return e.T == "informe_pdf" ? "informe" : e.T.Substring(6); }

        private static List<PortalItem> Top(IEnumerable<Ev> fuente, Func<Ev, string> clave, int max = 12, Func<string, string> nombre = null)
        {
            return fuente.Where(e => !string.IsNullOrEmpty(clave(e)))
                .GroupBy(clave)
                .Select(g => new PortalItem { Clave = g.Key, Nombre = nombre != null ? nombre(g.Key) : g.Key, N = g.Count(), N2 = g.Select(x => x.VKey).Distinct().Count() })
                .OrderByDescending(x => x.N).ThenBy(x => x.Clave).Take(max).ToList();
        }

        /// <summary>Visitantes distintos (por día) con un valor: se toma el del primer evento de cada visitante-día.</summary>
        private static List<PortalItem> PorVisitante(IEnumerable<Ev> eventos, Func<Ev, string> valor, int max = 10)
        {
            return eventos.GroupBy(e => e.VKey)
                .Select(g => g.OrderBy(x => x.Ts).Select(valor).FirstOrDefault(v => !string.IsNullOrEmpty(v)))
                .Where(v => !string.IsNullOrEmpty(v))
                .GroupBy(v => v).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() })
                .OrderByDescending(x => x.N).ThenBy(x => x.Clave).Take(max).ToList();
        }

        public PortalResultado Consultar(PortalFiltro f)
        {
            var res = new PortalResultado();
            var carpeta = Carpeta();
            if (carpeta == null || !Directory.Exists(carpeta)) { res.Motivo = "SinCarpeta"; return res; }

            var hasta = f.Hasta.Date; var desde = f.Desde.Date;
            if (desde > hasta) { var t = desde; desde = hasta; hasta = t; }
            if ((hasta - desde).TotalDays + 1 > MaxDias) desde = hasta.AddDays(-(MaxDias - 1));
            res.Desde = desde.ToString("yyyy-MM-dd"); res.Hasta = hasta.ToString("yyyy-MM-dd"); res.Dias = (int)(hasta - desde).TotalDays + 1;

            var todos = Leer(carpeta, desde, hasta);
            res.Eventos = todos.Count;
            if (todos.Count == 0) { LlenarSalud(res, carpeta, desde, hasta); res.Motivo = "SinEventos"; return res; }
            res.Disponible = true;
            res.PrimerEvento = todos.Min(e => e.Dia).ToString("yyyy-MM-dd");

            // Origen de cada visitante-día = el de su primera página con origen
            var origenDe = todos.Where(e => EsVista(e) && !string.IsNullOrEmpty(e.R)).GroupBy(e => e.VKey)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Ts).First().R);

            IEnumerable<Ev> ev = todos;
            if (!string.IsNullOrEmpty(f.Dispositivo)) ev = ev.Where(e => e.D == f.Dispositivo);
            if (!string.IsNullOrEmpty(f.Idioma)) ev = ev.Where(e => e.L == f.Idioma);
            if (!string.IsNullOrEmpty(f.Origen)) ev = ev.Where(e => { string o; return origenDe.TryGetValue(e.VKey, out o) ? o == f.Origen : f.Origen == "directo"; });
            if (!string.IsNullOrWhiteSpace(f.Texto))
            {
                var q = Norm(f.Texto);   // sin tildes ni mayúsculas: «exito» encuentra «ÉXITO»
                ev = ev.Where(e => Norm(e.N).Contains(q) || e.K.Contains(q) || e.X.Contains(q) || Norm(NombreSector(e.K)).Contains(q));
            }
            var lista = ev.ToList();
            res.Eventos = lista.Count;
            if (lista.Count == 0) { LlenarSalud(res, carpeta, desde, hasta); res.Disponible = true; return res; }

            var vistas = lista.Where(EsVista).ToList();
            var accion = lista.Where(e => !EsVista(e) && e.T != "perm" && e.T != "error_404").ToList();

            // ---- visitas (sesiones de ≤30 min sin pausa)
            var sesiones = new List<List<Ev>>();
            foreach (var g in lista.Where(e => e.T != "error_404").GroupBy(e => e.VKey))
            {
                List<Ev> actual = null; DateTime ultimo = DateTime.MinValue;
                foreach (var e in g.OrderBy(x => x.Ts))
                {
                    if (actual == null || (e.Ts - ultimo).TotalMinutes > GapSesionMin) { actual = new List<Ev>(); sesiones.Add(actual); }
                    actual.Add(e); ultimo = e.Ts;
                }
            }
            var sesionesConVista = sesiones.Where(s => s.Any(EsVista)).ToList();

            var k = res.Kpis;
            k.Vistas = vistas.Count;
            k.Visitas = sesionesConVista.Count;
            var visitantesPorDia = vistas.GroupBy(e => e.Dia).ToDictionary(g => g.Key, g => g.Select(x => x.V).Distinct().Count());
            k.Visitantes = visitantesPorDia.Values.Sum();
            k.VisitantesPorDia = Math.Round(k.Visitantes / (double)Math.Max(1, res.Dias), 1);
            k.PaginasPorVisita = k.Visitas == 0 ? 0 : Math.Round(k.Vistas / (double)k.Visitas, 1);
            var duraciones = sesionesConVista.Select(s =>
            {
                var perm = s.Where(e => e.T == "perm").Sum(e => (long)e.Ms) / 1000;
                var rango = (long)(s.Max(e => e.Ts) - s.Min(e => e.Ts)).TotalSeconds;
                return Math.Max(perm, rango);
            }).ToList();
            k.DuracionSeg = duraciones.Count == 0 ? 0 : (int)Math.Round(duraciones.Average());
            var rebotes = sesionesConVista.Count(s => s.Count(EsVista) == 1 && !s.Any(e => !EsVista(e) && e.T != "perm"));
            k.RebotePct = k.Visitas == 0 ? 0 : Math.Round(rebotes * 100.0 / k.Visitas, 1);
            k.Descargas = lista.Count(e => e.T == "export_csv" || e.T == "export_xlsx" || e.T == "informe_pdf" || e.T == "imprimir");
            k.Contactos = lista.Count(e => e.T == "contacto_enviado");
            k.Busquedas = lista.Count(e => e.T == "busqueda_sel");

            // ---- serie diaria y mapa de calor
            var sesPorDia = sesionesConVista.GroupBy(s => s.Min(e => e.Dia)).ToDictionary(g => g.Key, g => g.Count());
            for (var d = desde; d <= hasta; d = d.AddDays(1))
            {
                int vs = 0, vi = 0, se = 0;
                vs = vistas.Count(e => e.Dia == d);
                visitantesPorDia.TryGetValue(d, out vi);
                sesPorDia.TryGetValue(d, out se);
                res.Serie.Add(new PortalDia { Dia = d.ToString("yyyy-MM-dd"), Vistas = vs, Visitantes = vi, Visitas = se });
            }
            res.Mapa = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
            foreach (var e in vistas)
            {
                int dow = ((int)e.Ts.DayOfWeek + 6) % 7;   // lunes = 0
                res.Mapa[dow][e.Ts.Hour]++;
            }

            // ---- en este momento (últimos 30 min, hora de Colombia)
            var ahora = DateTime.UtcNow.AddHours(-5);
            var recientes = lista.Where(e => e.Ts >= ahora.AddMinutes(-30) && e.Ts <= ahora.AddMinutes(1)).ToList();
            res.VisitantesAhora = recientes.Select(e => e.VKey).Distinct().Count();
            res.VistasAhora = recientes.Count(EsVista);

            // ---- qué consultan
            var nombrePorNit = lista.Where(e => e.T == "vista_empresa" && e.X.Length > 0 && e.N.Length > 0)
                .GroupBy(e => e.X).ToDictionary(g => g.Key, g => g.First().N);
            Func<string, string> nombreNit = nit => { string n; return nombrePorNit.TryGetValue(nit, out n) ? n : ""; };

            res.Empresas = lista.Where(e => e.T == "vista_empresa" && e.X.Length > 0).GroupBy(e => e.X)
                .Select(g => new PortalItem
                {
                    Clave = g.Key, Nombre = g.First().N, N = g.Count(), N2 = g.Select(x => x.VKey).Distinct().Count(),
                    Extra = NombreSector(g.First().K)
                })
                .OrderByDescending(x => x.N).ThenBy(x => x.Clave).Take(15).ToList();
            foreach (var it in res.Empresas)
                it.Extra = (it.Extra ?? "") + "|" + lista.Count(e => e.T == "informe_pdf" && e.X == it.Clave);

            var sectoresEmpresa = lista.Where(e => e.T == "vista_empresa" && e.K.Length > 0).GroupBy(e => e.K).Select(g => new { g.Key, N = g.Count(), V = g.Select(x => x.VKey).Distinct().Count() });
            var sectoresNodo = lista.Where(e => e.T == "vista_sector" && e.K.Length > 0).GroupBy(e => e.K).Select(g => new { g.Key, N = g.Count(), V = g.Select(x => x.VKey).Distinct().Count() });
            res.Sectores = sectoresEmpresa.Concat(sectoresNodo).GroupBy(x => x.Key)
                .Select(g => new PortalItem { Clave = g.Key, Nombre = NombreSector(g.Key), N = g.Sum(x => x.N), N2 = g.Sum(x => x.V) })
                .OrderByDescending(x => x.N).Take(12).ToList();

            res.Rankings = Top(lista.Where(e => e.T == "vista_ranking"), e => e.K, 10);
            res.RankingsSegmento = lista.Where(e => e.T == "vista_ranking" && e.X.Length > 1 && !e.X.EndsWith("/") && !e.X.StartsWith("nacional"))
                .GroupBy(e => e.X).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(10).ToList();
            res.Paginas = Top(vistas, TipoPagina, 10);
            res.Busquedas = lista.Where(e => e.T == "busqueda_sel" && e.K.Length > 0).GroupBy(e => e.K)
                .Select(g => new PortalItem { Clave = g.Key, Nombre = nombreNit(g.Key), N = g.Count() }).OrderByDescending(x => x.N).Take(10).ToList();
            res.Comparaciones = lista.Where(e => e.T == "comparar" && e.X.Contains(","))
                .GroupBy(e => string.Join(",", e.X.Split(',').OrderBy(x => x)))
                .Select(g => new PortalItem
                {
                    Clave = g.Key, N = g.Count(),
                    Nombre = string.Join("  vs  ", g.Key.Split(',').Select(n => { var nm = nombreNit(n); return string.IsNullOrEmpty(nm) ? n : nm; }))
                }).OrderByDescending(x => x.N).Take(10).ToList();
            res.FiltrosSector = Top(lista.Where(e => e.T == "filtro_sector"), e => e.K, 10, NombreSector);

            // ---- acciones
            res.Cta = Top(accion.Where(e => e.T == "cta_click"), e => e.K, 10);
            res.Exportaciones = accion.Where(e => e.T == "export_csv" || e.T == "export_xlsx" || e.T == "informe_pdf")
                .GroupBy(e => e.T + (e.K.Length > 0 && e.T != "informe_pdf" ? ":" + e.K : ""))
                .Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count(), N2 = g.Select(x => x.VKey).Distinct().Count() })
                .OrderByDescending(x => x.N).ToList();
            res.OtrasAcciones = accion.Where(e => e.T == "imprimir" || e.T == "copiar_enlace" || e.T == "unidad")
                .GroupBy(e => e.T + ":" + e.K).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() })
                .OrderByDescending(x => x.N).Take(12).ToList();
            res.Salidas = Top(accion.Where(e => e.T == "outbound"), e => e.K, 10);

            // ---- origen
            res.Origen = PorVisitante(vistas, e => e.R, 8);
            res.Sitios = Top(vistas.Where(e => e.Rh.Length > 0), e => e.Rh, 10);
            res.Campanas = Top(vistas.Where(e => e.U.Length > 0), e => e.U, 10);

            // ---- audiencia (visitantes por día)
            res.Dispositivos = PorVisitante(lista, e => e.D, 5);
            res.Navegadores = PorVisitante(lista, e => e.B, 8);
            res.Sistemas = PorVisitante(lista, e => e.S, 8);
            res.Idiomas = PorVisitante(lista, e => e.L, 6);
            res.Zonas = lista.Where(e => e.Z.Length > 0).GroupBy(e => e.VKey).Select(g => g.First().Z)
                .GroupBy(z => z).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(8).ToList();

            // ---- recorridos
            var paginasPorSesion = sesionesConVista.Select(s => s.Where(EsVista).OrderBy(e => e.Ts).Select(TipoPagina).ToList()).ToList();
            res.Entradas = paginasPorSesion.GroupBy(p => p.First()).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(8).ToList();
            res.SalidasPagina = paginasPorSesion.GroupBy(p => p.Last()).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(8).ToList();
            var pares = new Dictionary<string, int>();
            foreach (var p in paginasPorSesion)
                for (int i = 0; i + 1 < p.Count; i++) { var kk = p[i] + ">" + p[i + 1]; int c; pares.TryGetValue(kk, out c); pares[kk] = c + 1; }
            res.Transiciones = pares.OrderByDescending(x => x.Value).Take(10)
                .Select(x => new PortalTransicion { De = x.Key.Split('>')[0], A = x.Key.Split('>')[1], N = x.Value }).ToList();
            Func<int, string> bucket = n => n == 1 ? "1" : n == 2 ? "2" : n <= 4 ? "3-4" : n <= 9 ? "5-9" : "10+";
            res.Profundidad = paginasPorSesion.GroupBy(p => bucket(p.Count)).Select(g => new PortalItem { Clave = g.Key, Nombre = g.Key, N = g.Count() })
                .OrderBy(x => new[] { "1", "2", "3-4", "5-9", "10+" }.ToList().IndexOf(x.Clave)).ToList();

            // ---- embudo: visitantes → ven una empresa o ranking → clic en contacto → llegan a contacto desde el sitio → envían
            Func<Func<Ev, bool>, int> vis = pred => lista.Where(pred).Select(e => e.VKey).Distinct().Count();
            var etapas = new[]
            {
                new KeyValuePair<string,int>("visitantes", vis(e => EsVista(e))),
                new KeyValuePair<string,int>("consulta", vis(e => e.T == "vista_empresa" || e.T == "vista_ranking" || e.T == "vista_sectores" || e.T == "vista_comparar")),
                new KeyValuePair<string,int>("cta", vis(e => e.T == "cta_click")),
                new KeyValuePair<string,int>("contacto", vis(e => e.T == "vista_contacto" && e.K == "pulso")),
                new KeyValuePair<string,int>("enviado", vis(e => e.T == "contacto_enviado"))
            };
            res.Embudo = etapas.Select(x => new PortalItem { Clave = x.Key, Nombre = x.Key, N = x.Value,
                N2 = etapas[0].Value == 0 ? 0 : (int)Math.Round(x.Value * 100.0 / etapas[0].Value) }).ToList();

            // ---- lectura (permanencia y scroll por página)
            res.Lectura = lista.Where(e => e.T == "perm" && e.K.Length > 0).GroupBy(e => e.K)
                .Select(g => new PortalLectura
                {
                    Pagina = g.Key, N = g.Count(),
                    PermanenciaSeg = (int)Math.Round(g.Average(x => x.Ms) / 1000.0),
                    ScrollPct = (int)Math.Round(g.Where(x => x.Sc > 0).Select(x => (double)x.Sc).DefaultIfEmpty(0).Average())
                }).OrderByDescending(x => x.N).ToList();

            // ---- salud del sitio
            res.Errores404 = Top(lista.Where(e => e.T == "error_404"), e => e.K, 10);
            LlenarSalud(res, carpeta, desde, hasta);
            return res;
        }

        /// <summary>Bots y límites por IP: salen de los contadores mensuales (no tienen registro de eventos).</summary>
        private static void LlenarSalud(PortalResultado res, string carpeta, DateTime desde, DateTime hasta)
        {
            var c = LeerContadores(carpeta, desde, hasta);
            var bots = new Dictionary<string, PortalBot>();
            var limites = new Dictionary<string, int>();
            foreach (var kv in c)
            {
                var p = kv.Key.Split('|');
                if (p[1] == "bot")
                {
                    var partes = p[2].Split(new[] { '.' }, 2);
                    var fam = partes[0]; var pag = partes.Length > 1 ? partes[1] : "";
                    PortalBot b;
                    if (!bots.TryGetValue(fam, out b)) bots[fam] = b = new PortalBot { Familia = fam };
                    b.N += (int)kv.Value;
                    var it = b.Paginas.FirstOrDefault(x => x.Clave == pag);
                    if (it == null) b.Paginas.Add(it = new PortalItem { Clave = pag, Nombre = pag });
                    it.N += (int)kv.Value;
                }
                else if (p[1] == "throttle_429")
                {
                    int n; limites.TryGetValue(p[2], out n); limites[p[2]] = n + (int)kv.Value;
                }
            }
            res.Bots = bots.Values.OrderByDescending(b => b.N).ToList();
            foreach (var b in res.Bots) b.Paginas = b.Paginas.OrderByDescending(x => x.N).Take(5).ToList();
            res.Limites = limites.OrderByDescending(x => x.Value).Take(8).Select(x => new PortalItem { Clave = x.Key, Nombre = x.Key, N = x.Value }).ToList();
        }
    }
}
