using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;
using System;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// TempData por cookie (ASP.NET Core) solo admite texto, números, fechas y colecciones simples: guardar un objeto
    /// (p. ej. <c>ResultadoCargaExcel</c>) lanza «cannot serialize an object of type …» al terminar la petición.
    /// Estos métodos lo guardan como JSON y lo devuelven tipado. Mantener los objetos pequeños (la cookie ronda 4 KB).
    /// </summary>
    public static class TempDataObjetoHelper
    {
        public static void GuardarObjeto(this ITempDataDictionary tempData, string clave, object valor)
        {
            if (valor == null) { tempData.Remove(clave); return; }
            tempData[clave] = JsonConvert.SerializeObject(valor);
        }

        /// <summary>Lee (y consume) el objeto guardado con <see cref="GuardarObjeto"/>; null si no hay o no se puede leer.</summary>
        public static T ObtenerObjeto<T>(this ITempDataDictionary tempData, string clave) where T : class
        {
            var json = tempData[clave] as string;
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (Exception) { return null; }
        }
    }
}
