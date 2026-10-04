using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    public class IndicadoresFinancierosService : BaseService
    {
        private static readonly HttpClient _http;

        static IndicadoresFinancierosService()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.Add("Accept", "application/xml,text/xml,*/*");
            _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; BufinsApp/1.0)");
        }

        private const string CacheFeeds   = "IndicadoresFeeds";
        private const int    MaxPorFeed   = 6;
        private const int    TimeoutFeed  = 8; // segundos por feed

        // -------------------------------------------------------
        // Punto de entrada público
        // -------------------------------------------------------
        public async Task<IndicadoresFinancierosViewModel> ObtenerSoloNoticiasAsync(bool forzarRefresh)
        {
            string feed1 = ConfigurationManager.AppSettings["IndicadoresRSSFeed1"] ?? "";
            string feed2 = ConfigurationManager.AppSettings["IndicadoresRSSFeed2"] ?? "";
            string feed3 = ConfigurationManager.AppSettings["IndicadoresRSSFeed3"] ?? "";
            string feed4 = ConfigurationManager.AppSettings["IndicadoresRSSFeed4"] ?? "";

            bool rssConf = !string.IsNullOrWhiteSpace(feed1) || !string.IsNullOrWhiteSpace(feed2)
                        || !string.IsNullOrWhiteSpace(feed3) || !string.IsNullOrWhiteSpace(feed4);

            var feeds = await ObtenerFeedsAsync(
                forzarRefresh,
                feed1.Trim(), feed2.Trim(), feed3.Trim(), feed4.Trim()
            ).ConfigureAwait(false);

            return new IndicadoresFinancierosViewModel { Feeds = feeds, RSSConfigurado = rssConf };
        }

        // -------------------------------------------------------
        // Feeds agrupados — 4 por fuente, en paralelo con timeout individual
        // -------------------------------------------------------
        private async Task<List<FeedNoticiaViewModel>> ObtenerFeedsAsync(
            bool forzarRefresh, string feed1, string feed2, string feed3, string feed4)
        {
            var cache = MemoryCache.Default;
            if (!forzarRefresh && cache.Contains(CacheFeeds))
                return (List<FeedNoticiaViewModel>)cache[CacheFeeds];

            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(feed1)) urls.Add(feed1);
            if (!string.IsNullOrWhiteSpace(feed2)) urls.Add(feed2);
            if (!string.IsNullOrWhiteSpace(feed3)) urls.Add(feed3);
            if (!string.IsNullOrWhiteSpace(feed4)) urls.Add(feed4);

            if (urls.Count == 0)
                return Cache(CacheFeeds, new List<FeedNoticiaViewModel>(), 30);

            // Cada feed tiene su propio timeout de 8 s — uno lento no bloquea a los otros
            var tareas = urls.Select(u => ParsarRSSAsync(u)).ToArray();
            var listas = await Task.WhenAll(tareas).ConfigureAwait(false);

            var resultado = new List<FeedNoticiaViewModel>();
            for (int i = 0; i < urls.Count; i++)
            {
                var items = listas[i]
                    .OrderByDescending(r => r.Fecha)
                    .Take(MaxPorFeed)
                    .Select(r => r.VM)
                    .ToList();

                if (!items.Any()) continue;

                string fuente;
                try { fuente = new Uri(urls[i]).Host.Replace("www.", ""); }
                catch { fuente = urls[i]; }

                resultado.Add(new FeedNoticiaViewModel { Fuente = fuente, Noticias = items });
            }

            return Cache(CacheFeeds, resultado, 30);
        }

        // -------------------------------------------------------
        // Parser RSS con timeout por feed (CancellationToken)
        // -------------------------------------------------------
        private static readonly System.Text.RegularExpressions.Regex _xmlRootTag =
            new System.Text.RegularExpressions.Regex(@"^\s*(<\?xml|<rss|<feed|<RDF)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private async Task<List<(NoticiaViewModel VM, DateTime Fecha)>> ParsarRSSAsync(string url)
        {
            try
            {
                string xml;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutFeed)))
                {
                    var req  = new HttpRequestMessage(HttpMethod.Get, url);
                    var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);

                    // Rechazar respuestas que claramente no son XML (HTML de error, redirects, etc.)
                    var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                    bool esXml = ct.Contains("xml") || ct.Contains("rss") || ct.Contains("atom")
                                 || ct == "text/plain" || ct == "";
                    if (!esXml)
                        return new List<(NoticiaViewModel, DateTime)>();

                    xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                // Quitar BOM (U+FEFF) que rompe XDocument.Parse
                xml = xml.Replace("\uFEFF", "").TrimStart();

                // Verificar que el contenido parece XML (RSS/Atom) y no HTML de error
                if (!_xmlRootTag.IsMatch(xml))
                    return new List<(NoticiaViewModel, DateTime)>();

                XDocument doc;
                try { doc = XDocument.Parse(xml); }
                catch (System.Xml.XmlException) { return new List<(NoticiaViewModel, DateTime)>(); }
                var lista = new List<(NoticiaViewModel, DateTime)>();

                string fuente;
                try { fuente = new Uri(url).Host.Replace("www.", ""); }
                catch { fuente = url; }

                // --- RSS 2.0 ---
                var items = doc.Descendants("item").ToList();
                if (items.Any())
                {
                    foreach (var item in items.Take(12))
                    {
                        string titulo  = item.Element("title")?.Value?.Trim() ?? "";
                        string enlace  = item.Element("link")?.Value?.Trim()
                                       ?? item.Elements()
                                              .FirstOrDefault(e => e.Name.LocalName == "link")
                                              ?.Value?.Trim() ?? "#";
                        var dt = ParseFecha(item.Element("pubDate")?.Value ?? "");
                        if (string.IsNullOrWhiteSpace(titulo)) continue;

                        lista.Add((new NoticiaViewModel
                        {
                            Titulo     = titulo,
                            Enlace     = string.IsNullOrWhiteSpace(enlace) ? "#" : enlace,
                            Fuente     = fuente,
                            FechaTexto = FmtFecha(dt)
                        }, dt));
                    }
                    return lista;
                }

                // --- Atom ---
                XNamespace atom    = "http://www.w3.org/2005/Atom";
                var        entries = doc.Descendants(atom + "entry").ToList();
                foreach (var entry in entries.Take(12))
                {
                    string titulo = entry.Element(atom + "title")?.Value?.Trim() ?? "";
                    string enlace = entry.Elements(atom + "link")
                        .FirstOrDefault(e => (e.Attribute("rel")?.Value ?? "alternate") == "alternate")
                        ?.Attribute("href")?.Value ?? "#";
                    string pub = entry.Element(atom + "published")?.Value
                              ?? entry.Element(atom + "updated")?.Value ?? "";
                    var dt = ParseFecha(pub);
                    if (string.IsNullOrWhiteSpace(titulo)) continue;

                    lista.Add((new NoticiaViewModel
                    {
                        Titulo     = titulo,
                        Enlace     = string.IsNullOrWhiteSpace(enlace) ? "#" : enlace,
                        Fuente     = fuente,
                        FechaTexto = FmtFecha(dt)
                    }, dt));
                }
                return lista;
            }
            catch
            {
                return new List<(NoticiaViewModel, DateTime)>();
            }
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private static T Cache<T>(string key, T value, int minutos)
        {
            MemoryCache.Default.Set(key, value, new CacheItemPolicy
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(minutos)
            });
            return value;
        }

        private static readonly string[] _fmts =
        {
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, d MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:sszzz",
            "dd MMM yyyy HH:mm:ss zzz"
        };

        private static DateTime ParseFecha(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
            DateTime dt;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt)) return dt;
            foreach (var fmt in _fmts)
                if (DateTime.TryParseExact(s, fmt,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt)) return dt;
            return DateTime.MinValue;
        }

        private static string FmtFecha(DateTime dt)
        {
            if (dt == DateTime.MinValue) return "";
            var diff = DateTime.Now - dt;
            if (diff.TotalMinutes < 1)  return "ahora";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m";
            if (diff.TotalHours   < 24) return $"{(int)diff.TotalHours}h";
            if (diff.TotalDays    < 7)  return $"{(int)diff.TotalDays}d";
            return dt.ToString("dd/MM");
        }
    }
}
