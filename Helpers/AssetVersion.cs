using System;
using System.IO;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// URL de un recurso estatico con la fecha de su archivo como version (?v=...): cuando el archivo cambia,
    /// el navegador deja de usar la copia en cache. Evita que un JS/CSS antiguo en cache rompa una pantalla
    /// nueva (p. ej. un .js que no envia un parametro que el servidor ya exige).
    /// </summary>
    public static class AssetVersion
    {
        public static string Url(string rutaVirtual)
        {
            string url = "/" + (rutaVirtual ?? "").TrimStart('~', '/');
            try
            {
                string fisica = AppPaths.Map(rutaVirtual);
                if (fisica != null && File.Exists(fisica))
                    return url + "?v=" + File.GetLastWriteTimeUtc(fisica).Ticks;
            }
            catch (Exception) { }
            return url;
        }
    }
}
