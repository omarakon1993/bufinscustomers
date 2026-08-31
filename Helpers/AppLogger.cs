using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Logger ligero de la aplicación, sin dependencias externas. Escribe una línea por
    /// evento en <c>App_Data/logs/app-yyyyMMdd.log</c> (rotación diaria, retención 30 días).
    /// Nunca lanza excepciones: si el archivo no se puede escribir cae a <see cref="System.Diagnostics.Trace"/>.
    ///
    /// Puntos de uso previstos: <c>Global.asax.Application_Error</c>, <c>LoggingHandleErrorAttribute</c>,
    /// y cualquier <c>catch</c> relevante en controladores/servicios donde hoy solo se hace
    /// <c>SetErrorMessage(ex.Message)</c> o se traga la excepción.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private const int RetencionDias = 30;
        private static DateTime _ultimaPurga = DateTime.MinValue;

        public static void Info(string mensaje, string contexto = null) => Escribir("INFO", mensaje, null, contexto);
        public static void Warn(string mensaje, string contexto = null) => Escribir("WARN", mensaje, null, contexto);
        public static void Error(string mensaje, Exception ex = null, string contexto = null) => Escribir("ERROR", mensaje, ex, contexto);
        public static void Error(Exception ex, string contexto = null) => Escribir("ERROR", ex?.Message ?? "(sin mensaje)", ex, contexto);

        private static void Escribir(string nivel, string mensaje, Exception ex, string contexto)
        {
            try
            {
                string dir = RutaDirectorio();
                Directory.CreateDirectory(dir);
                string archivo = Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log");

                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                sb.Append(nivel.PadRight(5)).Append(' ');
                sb.Append(DatosPeticion());
                if (!string.IsNullOrEmpty(contexto)) sb.Append(" ctx=").Append(contexto);
                sb.Append(" :: ").Append((mensaje ?? "").Replace("\r", " ").Replace("\n", " "));

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append("    ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                        sb.AppendLine().Append(Indentar(ex.StackTrace));
                    var inner = ex.InnerException;
                    int guarda = 0;
                    while (inner != null && guarda++ < 8)
                    {
                        sb.AppendLine().Append("  --> ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
                        inner = inner.InnerException;
                    }
                }
                sb.AppendLine();

                lock (_lock)
                {
                    File.AppendAllText(archivo, sb.ToString(), Encoding.UTF8);
                    PurgarAntiguos(dir);
                }
            }
            catch
            {
                try { System.Diagnostics.Trace.TraceError("[AppLogger] {0}: {1}", nivel, mensaje); } catch { }
            }
        }

        private static string RutaDirectorio()
        {
            string baseDir = HttpRuntime.AppDomainAppPath ?? AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "App_Data", "logs");
        }

        private static string DatosPeticion()
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx == null) return "req=-";
                string ip  = ctx.Request?.UserHostAddress ?? "-";
                string url = ctx.Request?.RawUrl ?? "-";
                string user = "-";
                try { user = (ctx.Session?["UsuarioCompleto"] as bufinscustomers.Models.Usuarios)?.Correo ?? "-"; }
                catch { /* Session no disponible en esta etapa del pipeline */ }
                return $"ip={ip} user={user} url={url}";
            }
            catch { return "req=?"; }
        }

        private static string Indentar(string texto)
        {
            return string.Join(Environment.NewLine,
                texto.Split('\n').Select(l => "    " + l.TrimEnd('\r')));
        }

        private static void PurgarAntiguos(string dir)
        {
            if ((DateTime.Now - _ultimaPurga).TotalHours < 12) return;
            _ultimaPurga = DateTime.Now;
            try
            {
                var limite = DateTime.Now.AddDays(-RetencionDias);
                foreach (var f in Directory.GetFiles(dir, "app-*.log"))
                    if (File.GetLastWriteTime(f) < limite) File.Delete(f);
            }
            catch { }
        }
    }
}
