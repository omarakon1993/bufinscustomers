using AgenteBufins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string Version = "1.0.0";

try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* sin consola (servicio) */ }

const string Ayuda = @"
Agente de carpeta de BUFINS v" + Version + @"
Vigila una carpeta y sube a la API de BUFINS las plantillas Excel que aparezcan en ella.

Uso:
  AgenteBufins.exe                    Vigila la carpeta de forma continua (consola, o servicio de Windows).
  AgenteBufins.exe --una-vez          Hace una sola pasada y termina (para el Programador de tareas). Código de salida 0 = todo bien.
  AgenteBufins.exe --probar           Comprueba la configuración, la clave y la conexión, y termina.
  AgenteBufins.exe --guardar-clave    Pide la clave de la integración y la guarda cifrada (DPAPI) en clave.dat.
  AgenteBufins.exe --config <ruta>    Usa otro archivo de configuración (por defecto agente.json junto al ejecutable).
  AgenteBufins.exe --version | --ayuda

La clave se toma de: variable de entorno BUFINS_CLAVE, luego clave.dat, luego «clave» en agente.json.
Nombre de archivo opcional para indicar año y modo:  2026_historico_cierre.xlsx  (si no, se usan los de agente.json).
Subcarpetas que crea el agente:  procesados\ (recibidos)  y  errores\ (rechazados o con errores de validación, con el motivo).
";

var argumentos = args.Select(a => a.Trim()).ToList();
string? Valor(string opcion) { int i = argumentos.FindIndex(a => a.Equals(opcion, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < argumentos.Count ? argumentos[i + 1] : null; }
bool Tiene(string opcion) => argumentos.Any(a => a.Equals(opcion, StringComparison.OrdinalIgnoreCase));

if (Tiene("--ayuda") || Tiene("-h") || Tiene("--help")) { Console.WriteLine(Ayuda); return 0; }
if (Tiene("--version")) { Console.WriteLine(Version); return 0; }

if (Tiene("--guardar-clave"))
{
    Console.Write("Pegue la clave de la integración (no se muestra): ");
    string clave = LeerOculto();
    if (!ClaveStore.TieneFormatoValido(clave)) { Console.WriteLine("La clave no tiene el formato esperado (bfk_… o bft_…). No se guardó."); return 2; }
    try { ClaveStore.Guardar(clave); Console.WriteLine("Clave guardada cifrada en clave.dat (solo la puede leer este equipo). Restrinja el acceso a la carpeta del agente."); return 0; }
    catch (Exception ex) { Console.WriteLine("No se pudo guardar la clave: " + ex.Message); return 2; }
}

Configuracion cfg;
string? claveApi;
try
{
    cfg = Configuracion.Cargar(Valor("--config"));
    Registro.Iniciar(cfg.CarpetaLogs);
    var (c, origen) = ClaveStore.Obtener(cfg);
    claveApi = c;
    if (string.IsNullOrWhiteSpace(claveApi)) throw new InvalidOperationException("No hay clave de integración. Use --guardar-clave, la variable BUFINS_CLAVE o «clave» en agente.json.");
    if (!ClaveStore.TieneFormatoValido(claveApi)) throw new InvalidOperationException("La clave de integración no tiene el formato esperado (bfk_… o bft_…).");
    Registro.Info($"Configuración cargada. Clave tomada de: {origen}.");
}
catch (Exception ex)
{
    Console.WriteLine("ERROR de configuración: " + ex.Message);
    try { Registro.Error("Configuración inválida: " + ex.Message); } catch { }
    return 2;
}

using var cliente = new BufinsClient(cfg.Url, claveApi!);

if (Tiene("--probar"))
{
    var v = new Vigilante(cfg, cliente);
    v.PrepararCarpetas();
    return await v.VerificarConexionAsync(CancellationToken.None) ? 0 : 1;
}

if (Tiene("--una-vez"))
{
    var v = new Vigilante(cfg, cliente);
    v.PrepararCarpetas();
    if (!await v.VerificarConexionAsync(CancellationToken.None)) return 1;
    return await v.CicloAsync(CancellationToken.None) ? 0 : 1;
}

var builder = Host.CreateApplicationBuilder(argumentos.ToArray());
builder.Environment.ContentRootPath = AppContext.BaseDirectory;
builder.Services.AddWindowsService(o => o.ServiceName = "BufinsAgente");
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton(cliente);
builder.Services.AddHostedService<Vigilante>();
builder.Logging.ClearProviders();   // el agente tiene su propio registro (archivo + consola)
await builder.Build().RunAsync();
return 0;

static string LeerOculto()
{
    var sb = new System.Text.StringBuilder();
    ConsoleKeyInfo k;
    while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
        else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
    Console.WriteLine();
    return sb.ToString().Trim();
}
