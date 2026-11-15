using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgenteBufins;

/// <summary>Configuración del agente (<c>agente.json</c>, junto al ejecutable). La clave NO debería ir aquí: ver <see cref="ClaveStore"/>.</summary>
public sealed class Configuracion
{
    /// <summary>Dirección base de la API, por ejemplo https://customers.bufins.com/api/v1</summary>
    public string Url { get; set; } = "https://customers.bufins.com/api/v1";

    /// <summary>Solo para pruebas; lo recomendado es la variable de entorno BUFINS_CLAVE o <c>--guardar-clave</c>.</summary>
    public string? Clave { get; set; }

    /// <summary>Carpeta que se vigila (solo los archivos directamente en ella; no las subcarpetas).</summary>
    public string Carpeta { get; set; } = @"C:\BUFINS\Entrada";

    /// <summary>Año por defecto: un número (2026) o "actual" (el año en curso). Un archivo llamado AAAA_modo_… lo sobrescribe.</summary>
    public string Anio { get; set; } = "actual";

    /// <summary>"ejecucion" o "historico". Un archivo llamado AAAA_modo_… lo sobrescribe.</summary>
    public string Modo { get; set; } = "ejecucion";

    public int IdEscenario { get; set; } = 1;

    /// <summary>Pide confirmar al subir. Solo funciona si la integración tiene permiso y el cargue queda limpio; si no, lo revisa una persona.</summary>
    public bool Confirmar { get; set; } = false;

    /// <summary>Tras un cargue confirmado, pide recalcular los modelos (requiere el permiso de confirmar).</summary>
    public bool EjecutarModelos { get; set; } = false;

    /// <summary>Cada cuántos segundos se revisa la carpeta.</summary>
    public int IntervaloSegundos { get; set; } = 60;

    /// <summary>Un archivo se envía solo cuando lleva tantos segundos sin modificarse (para no subir uno que aún se está copiando).</summary>
    public int EstableSegundos { get; set; } = 30;

    /// <summary>Carpeta de los registros (por defecto, «logs» junto al ejecutable).</summary>
    public string? CarpetaLogs { get; set; }

    public static string RutaPorDefecto => Path.Combine(AppContext.BaseDirectory, "agente.json");

    public static Configuracion Cargar(string? ruta)
    {
        ruta ??= RutaPorDefecto;
        if (!File.Exists(ruta))
            throw new InvalidOperationException($"No se encontró el archivo de configuración: {ruta}. Copie «agente.ejemplo.json» como «agente.json» y ajústelo.");
        try
        {
            var cfg = JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                      ?? throw new InvalidOperationException("El archivo de configuración está vacío.");
            cfg.Validar();
            return cfg;
        }
        catch (JsonException ex) { throw new InvalidOperationException($"El archivo de configuración no es un JSON válido ({ex.Message})."); }
    }

    private void Validar()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)) throw new InvalidOperationException("«url» no es una dirección válida.");
        bool local = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !local) throw new InvalidOperationException("«url» debe ser https (se permite http solo hacia localhost, para pruebas).");
        Url = Url.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(Carpeta)) throw new InvalidOperationException("«carpeta» es obligatoria.");
        Modo = (Modo ?? "").Trim().ToLowerInvariant();
        if (Modo != "ejecucion" && Modo != "historico") throw new InvalidOperationException("«modo» debe ser \"ejecucion\" o \"historico\".");
        if (!string.Equals(Anio, "actual", StringComparison.OrdinalIgnoreCase) && !(int.TryParse(Anio, out int a) && a is >= 2000 and <= 2100))
            throw new InvalidOperationException("«anio» debe ser un año (por ejemplo 2026) o \"actual\".");
        if (IdEscenario < 1) throw new InvalidOperationException("«idEscenario» debe ser 1 o mayor.");
        IntervaloSegundos = Math.Clamp(IntervaloSegundos, 5, 86400);
        EstableSegundos = Math.Clamp(EstableSegundos, 0, 3600);
    }

    private static readonly Regex NombreConAnio =
        new(@"^(?<anio>20\d{2})[_\- ](?:(?<modo>ejecucion|historico)[_\- ])?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Año y modo para un archivo: «2026_historico_cierre.xlsx» → (2026, historico); si el nombre no los trae, los de la configuración.</summary>
    public (int anio, string modo) InterpretarNombre(string nombreArchivo)
    {
        int anio = string.Equals(Anio, "actual", StringComparison.OrdinalIgnoreCase) ? DateTime.Now.Year : int.Parse(Anio);
        string modo = Modo;
        var m = NombreConAnio.Match(Path.GetFileName(nombreArchivo));
        if (m.Success)
        {
            anio = int.Parse(m.Groups["anio"].Value);
            if (m.Groups["modo"].Success) modo = m.Groups["modo"].Value.ToLowerInvariant();
        }
        return (anio, modo);
    }
}
