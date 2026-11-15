using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace bufinscustomers.Api
{
    /// <summary>
    /// Dirección propia de cada empresa en la API: <c>https://sitio/api/v1/{empresa}</c> (por ejemplo <c>/api/v1/churido</c>).
    /// El nombre en la dirección es una ayuda para el cliente y una verificación extra (la clave ya ata la conexión a UNA empresa):
    /// si una clave se usa con la dirección de otra empresa, la API responde 404 con la dirección correcta. La dirección sin nombre
    /// (<c>/api/v1</c>) sigue funcionando. Se calcula del nombre de la empresa: no se guarda en la base.
    /// </summary>
    public static class ApiDireccion
    {
        /// <summary>Primeros segmentos de ruta de la API: no son nombres de empresa.</summary>
        private static readonly HashSet<string> Reservados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ping", "esquemas", "cargues", "modelos", "ejecuciones", "openapi.json", "manual", "docs" };

        public static bool EsReservado(string segmento) => Reservados.Contains(segmento ?? "");

        /// <summary>«Comercializadora Surtinegocios S.A.S.» → «comercializadora-surtinegocios-s-a-s».</summary>
        public static string Normalizar(string nombre)
        {
            var d = (nombre ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-');
            }
            var s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
            if (s.Length > 40) s = s.Substring(0, 40).Trim('-');
            return s.Length == 0 ? "empresa" : s;
        }

        /// <summary>Nombre en la dirección de una empresa; si otra empresa daría el mismo, se agrega su número para que sea único.</summary>
        public static string SlugDe(int idEmpresa, IEnumerable<(int Id, string Nombre)> empresas)
        {
            var lista = empresas.ToList();
            var propia = lista.FirstOrDefault(e => e.Id == idEmpresa);
            string slug = Normalizar(propia.Nombre);
            bool repetido = lista.Any(e => e.Id != idEmpresa && Normalizar(e.Nombre) == slug) || EsReservado(slug);
            return repetido ? slug + "-" + idEmpresa : slug;
        }

        public const string ItemSlug = "ApiSlug";

        /// <summary>
        /// Va ANTES del enrutamiento: si la ruta es <c>/api/v1/{empresa}/…</c> guarda el nombre en <c>HttpContext.Items</c> y deja la ruta
        /// como <c>/api/v1/…</c>, de modo que los mismos endpoints atienden las dos formas. Después, <c>ApiV1Middleware</c> comprueba que
        /// el nombre corresponda a la empresa de la clave.
        /// </summary>
        public static void Reescribir(Microsoft.AspNetCore.Http.HttpContext ctx)
        {
            if (!ctx.Request.Path.StartsWithSegments("/api/v1", out var resto) || !resto.HasValue) return;
            string r = resto.Value.TrimStart('/');
            if (r.Length == 0) return;
            int i = r.IndexOf('/');
            string primero = i < 0 ? r : r.Substring(0, i);
            if (EsReservado(primero)) return;
            ctx.Items[ItemSlug] = primero;
            ctx.Request.Path = "/api/v1" + (i < 0 ? "" : r.Substring(i));
        }
    }
}
