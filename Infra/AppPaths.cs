using System;
using System.IO;

namespace bufinscustomers
{
    /// <summary>
    /// Reemplaza a HostingEnvironment.MapPath("~/...") de .NET Framework: resuelve rutas virtuales contra la carpeta
    /// raíz del sitio. Se inicializa una vez en Program.cs, así los servicios estáticos pueden usarla desde cualquier hilo.
    /// </summary>
    public static class AppPaths
    {
        public static string Raiz { get; private set; } = AppContext.BaseDirectory;

        public static void Iniciar(string contentRoot) { Raiz = contentRoot; }

        /// <summary>"~/App_Data/pulso/x.json" → ruta física.</summary>
        public static string Map(string rutaVirtual)
        {
            var r = (rutaVirtual ?? "").Replace('\\', '/');
            if (r.StartsWith("~/")) r = r.Substring(2);
            else if (r == "~") r = "";
            return Path.GetFullPath(Path.Combine(Raiz, r.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
