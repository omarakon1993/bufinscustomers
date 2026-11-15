using System.Security.Cryptography;
using System.Text;

namespace AgenteBufins;

/// <summary>
/// Dónde vive la clave de la integración, en orden de preferencia:
///  1) variable de entorno <c>BUFINS_CLAVE</c>;
///  2) <c>clave.dat</c>, cifrada con DPAPI (ámbito de la máquina: la lee la cuenta del servicio, no se puede abrir en otro equipo) — se crea con <c>--guardar-clave</c>;
///  3) «clave» en agente.json (en claro; solo para pruebas).
/// La clave nunca se escribe en los registros.
/// </summary>
public static class ClaveStore
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("AgenteBufins.v1");
    private static string RutaDat => Path.Combine(AppContext.BaseDirectory, "clave.dat");

    public static (string? clave, string origen) Obtener(Configuracion cfg)
    {
        var env = Environment.GetEnvironmentVariable("BUFINS_CLAVE");
        if (!string.IsNullOrWhiteSpace(env)) return (env.Trim(), "variable de entorno BUFINS_CLAVE");

        if (OperatingSystem.IsWindows() && File.Exists(RutaDat))
        {
            try
            {
                var plano = ProtectedData.Unprotect(File.ReadAllBytes(RutaDat), Entropia, DataProtectionScope.LocalMachine);
                return (Encoding.UTF8.GetString(plano).Trim(), "clave.dat (cifrada con DPAPI)");
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("No se pudo leer clave.dat: se creó en otro equipo o está dañada. Vuelva a ejecutar --guardar-clave.");
            }
        }

        if (!string.IsNullOrWhiteSpace(cfg.Clave)) return (cfg.Clave.Trim(), "agente.json (en claro: se recomienda --guardar-clave)");
        return (null, "");
    }

    public static void Guardar(string clave)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("--guardar-clave requiere Windows (DPAPI). En otros sistemas use la variable BUFINS_CLAVE.");
        var cifrada = ProtectedData.Protect(Encoding.UTF8.GetBytes(clave.Trim()), Entropia, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(RutaDat, cifrada);
    }

    public static bool TieneFormatoValido(string clave) =>
        (clave.StartsWith("bfk_", StringComparison.Ordinal) || clave.StartsWith("bft_", StringComparison.Ordinal)) && clave.Length is > 20 and <= 100;
}
