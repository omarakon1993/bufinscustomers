using System.Text;

namespace AgenteBufins;

/// <summary>Registro del agente: consola + un archivo por día (<c>logs/agente-AAAAMMDD.log</c>), con retención de 30 días. Nunca recibe la clave.</summary>
public static class Registro
{
    private static readonly object Candado = new();
    private static string _carpeta = Path.Combine(AppContext.BaseDirectory, "logs");

    public static void Iniciar(string? carpeta)
    {
        if (!string.IsNullOrWhiteSpace(carpeta)) _carpeta = carpeta;
        try
        {
            Directory.CreateDirectory(_carpeta);
            foreach (var f in Directory.EnumerateFiles(_carpeta, "agente-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)) File.Delete(f);
        }
        catch { /* sin carpeta de logs el agente sigue funcionando por consola */ }
    }

    public static void Info(string mensaje) => Escribir("INFO ", mensaje);
    public static void Aviso(string mensaje) => Escribir("AVISO", mensaje);
    public static void Error(string mensaje, Exception? ex = null) => Escribir("ERROR", ex == null ? mensaje : $"{mensaje} ({ex.GetType().Name}: {ex.Message})");

    private static void Escribir(string nivel, string mensaje)
    {
        string linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {nivel} {mensaje}";
        lock (Candado)
        {
            Console.WriteLine(linea);
            try { File.AppendAllText(Path.Combine(_carpeta, $"agente-{DateTime.Now:yyyyMMdd}.log"), linea + Environment.NewLine, Encoding.UTF8); }
            catch { /* no se interrumpe el trabajo por un fallo al escribir el log */ }
        }
    }
}
