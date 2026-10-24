using ClosedXML.Excel;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Colores del tema de informe para Excel y correo (ver CLAUDE.md, «Tema de colores de los informes»). Los controladores
    /// resuelven el tema con <see cref="Resolver"/> a partir de lo que mandó el navegador (se valida SIEMPRE en el servidor; ante
    /// cualquier duda: Bufins, que reproduce los colores de siempre) y lo usan en lugar de colores fijos.
    /// </summary>
    internal static class ExcelTemaHelper
    {
        /// <summary>Tema para la petición actual. Sin datos o inválido → Bufins.</summary>
        public static TemaInforme Resolver(string tema, string principal, string secundario, string acento)
        {
            return new TemaInformeService().Resolver(UsuarioSesionHelper.UsuarioActual, tema, principal, secundario, acento);
        }

        public static XLColor Color(string hex) { return XLColor.FromHtml(hex); }
        /// <summary>Cabeceras de tabla (hoy RGB 99,102,241).</summary>
        public static XLColor Principal(TemaInforme t) { return Color(t.Primario); }
        /// <summary>Fondo de subtotales/zebra suave (hoy RGB 244,243,251).</summary>
        public static XLColor Suave(TemaInforme t) { return Color(t.Soft2); }
        /// <summary>Color de la serie i en el formato sin '#' que usa <c>ExcelChartHelper</c> (p. ej. "6366F1").</summary>
        public static string Serie(TemaInforme t, int i) { return TemaInforme.SinAlmohadilla(t.Serie[i % t.Serie.Length]); }
    }
}
