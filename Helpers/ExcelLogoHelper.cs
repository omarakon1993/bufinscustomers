using System;
using System.IO;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>Logo de la empresa listo para incrustar en un libro de Excel.</summary>
    internal class LogoExcel
    {
        public byte[] Png { get; set; }
        public int Ancho { get; set; }
        public int Alto { get; set; }
    }

    /// <summary>
    /// Logo de la empresa en las exportaciones a Excel (ver CLAUDE.md, "Logo de la empresa"). Una sola implementación para
    /// todos los informes: <see cref="Obtener"/> devuelve el logo (o null si la empresa no tiene, y entonces el Excel queda
    /// EXACTAMENTE como siempre) y <see cref="Colocar"/> lo pone como imagen flotante en la zona del título. Nunca lanza:
    /// un problema con el logo no debe impedir entregar el Excel.
    /// </summary>
    internal static class ExcelLogoHelper
    {
        /// <summary>Filas que se reservan arriba cuando una hoja no trae título propio (título, subtítulo y una en blanco).</summary>
        public const int FilasBloque = 3;
        private const int AltoPx = 48;
        private const int AnchoMaxPx = 200;

        public static LogoExcel Obtener(int idEmpresa)
        {
            try
            {
                var l = new EmpresaLogoService().Obtener(idEmpresa);
                return l == null ? null : new LogoExcel { Png = l.Imagen, Ancho = l.Ancho, Alto = l.Alto };
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ExcelLogoHelper.Obtener");
                return null;
            }
        }

        /// <summary>Solo los bytes del PNG (para incrustarlo en el correo); null si la empresa no tiene logo o no hay empresa.</summary>
        public static byte[] Png(int? idEmpresa)
        {
            return idEmpresa.HasValue ? Obtener(idEmpresa.Value)?.Png : null;
        }

        /// <summary>Título y subtítulo en las dos primeras filas (para hojas que no los tenían, solo cuando hay logo).</summary>
        public static void EscribirTitulo(IXLWorksheet ws, string titulo, string subtitulo)
        {
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = subtitulo;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        /// <summary>Pone el logo flotante con su esquina superior izquierda en (fila, columna), sin mover ni redimensionar celdas.</summary>
        public static void Colocar(IXLWorksheet ws, LogoExcel logo, int columna = 2, int fila = 1)
        {
            if (ws == null || logo == null || logo.Png == null || logo.Alto <= 0) return;
            try
            {
                int alto = AltoPx;
                int ancho = (int)Math.Round(alto * (double)logo.Ancho / logo.Alto);
                if (ancho > AnchoMaxPx) { ancho = AnchoMaxPx; alto = (int)Math.Round(ancho * (double)logo.Alto / logo.Ancho); }

                var flujo = new MemoryStream(logo.Png);   // ClosedXML lo lee al guardar: no se cierra aquí
                var pic = ws.AddPicture(flujo, XLPictureFormat.Png, "LogoEmpresa");
                pic.MoveTo(ws.Cell(fila, columna), 8, 4);
                pic.WithSize(ancho, alto);
                pic.Placement = XLPicturePlacement.Move;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ExcelLogoHelper.Colocar");
            }
        }

        /// <summary>Lo coloca en todas las hojas del libro (las hojas con bloque de título propio: título en A1 y subtítulo en A2).</summary>
        public static void ColocarEnTodas(IXLWorkbook libro, LogoExcel logo, int columna = 2, int fila = 1)
        {
            if (libro == null || logo == null) return;
            foreach (var ws in libro.Worksheets) Colocar(ws, logo, columna, fila);
        }
    }
}
