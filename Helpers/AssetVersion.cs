using System;
using System.IO;
using System.Web;
using System.Web.Hosting;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// URL de un recurso estático con la fecha de su archivo como versión (?v=…): cuando el archivo cambia,
    /// el navegador deja de usar la copia en caché. Evita que un JS/CSS antiguo en caché rompa una pantalla
    /// nueva (p. ej. un .js que no envía un parámetro que el servidor ya exige).
    /// </summary>
    public static class AssetVersion
    {
        public static string Url(string rutaVirtual)
        {
            string url = VirtualPathUtility.ToAbsolute(rutaVirtual);
            try
            {
                string fisica = HostingEnvironment.MapPath(rutaVirtual);
                if (fisica != null && File.Exists(fisica))
                    return url + "?v=" + File.GetLastWriteTimeUtc(fisica).Ticks;
            }
            catch (Exception) { }
            return url;
        }
    }
}
