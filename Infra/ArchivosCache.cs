using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace bufinscustomers
{
    /// <summary>
    /// Caché en memoria que se invalida sola cuando cambia alguno de los archivos de los que depende (reemplaza a
    /// HttpRuntime.Cache + CacheDependency). La firma de los archivos (fecha de escritura + tamaño) se revisa a lo sumo
    /// una vez cada 2 segundos, así el ETL puede publicar datos nuevos y el sitio los recarga solo, sin reiniciar.
    /// </summary>
    public static class ArchivosCache
    {
        private class Entrada { public object Valor; public string[] Archivos; public string Firma; public DateTime Revisado; }
        private static readonly ConcurrentDictionary<string, Entrada> Datos = new ConcurrentDictionary<string, Entrada>();
        private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

        private static string Firma(string[] archivos)
        {
            return string.Join("|", archivos.Select(a =>
            {
                try { var f = new FileInfo(a); return f.Exists ? f.LastWriteTimeUtc.Ticks + ":" + f.Length : "-"; }
                catch (Exception) { return "?"; }
            }));
        }

        /// <summary>Devuelve el valor guardado, o null si no existe o alguno de sus archivos cambió.</summary>
        public static T Obtener<T>(string clave) where T : class
        {
            Entrada e;
            if (!Datos.TryGetValue(clave, out e)) return null;
            var ahora = DateTime.UtcNow;
            if (ahora - e.Revisado < Intervalo) return e.Valor as T;
            var f = Firma(e.Archivos);
            if (f != e.Firma) { Entrada x; Datos.TryRemove(clave, out x); return null; }
            e.Revisado = ahora;
            return e.Valor as T;
        }

        public static void Guardar(string clave, object valor, params string[] archivos)
        {
            var a = archivos ?? new string[0];
            Datos[clave] = new Entrada { Valor = valor, Archivos = a, Firma = Firma(a), Revisado = DateTime.UtcNow };
        }
    }
}
