using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace bufinscustomers.Helpers
{
    /// <summary>Resultado de validar/normalizar el logo de una empresa. Los errores son CLAVES de recurso (el controlador las traduce).</summary>
    public class ResultadoLogo
    {
        public bool Ok { get; set; }
        /// <summary>Clave de <c>Strings.resx</c> (<c>EmpLogo_Err_*</c>).</summary>
        public string ErrorKey { get; set; }
        /// <summary>Valores para <c>string.Format</c> del mensaje (p. ej. tamaño mínimo).</summary>
        public object[] ErrorArgs { get; set; }
        /// <summary>PNG normalizado, listo para guardar.</summary>
        public byte[] Png { get; set; }
        public int Ancho { get; set; }
        public int Alto { get; set; }
        /// <summary>Hash corto del PNG final: cambia si cambia la imagen (sirve para el caché del navegador).</summary>
        public string Version { get; set; }
        /// <summary>Aviso no bloqueante (clave de recurso), p. ej. "sin transparencia".</summary>
        public string AvisoKey { get; set; }

        internal static ResultadoLogo Error(string key, params object[] args)
        {
            return new ResultadoLogo { Ok = false, ErrorKey = key, ErrorArgs = args };
        }
    }

    /// <summary>
    /// Reglas del logo de empresa (gestor de Empresas) — UNA sola definición para servidor y textos de ayuda.
    /// Se valida por el CONTENIDO real del archivo (firma de bytes), nunca por extensión ni tipo MIME enviado, y
    /// la imagen se RE-DIBUJA y se guarda como PNG normalizado: eso descarta metadatos y cualquier carga oculta
    /// (archivos políglota), y deja un logo liviano y nítido para pantalla, PDF, Excel y correo.
    /// SVG no se acepta a propósito: puede llevar scripts.
    /// </summary>
    public static class EmpresaLogoHelper
    {
        public const int MaxBytesArchivo = 2 * 1024 * 1024;   // 2 MB subido
        public const int MinAncho = 200;                       // px
        public const int MinAlto = 50;                         // px
        public const int MaxLado = 4000;                       // px (ancho o alto)
        public const long MaxPixeles = 12000000L;              // protege la memoria del servidor
        public const double RatioMin = 0.9;                    // ancho/alto: cuadrado (o casi)…
        public const double RatioMax = 6.0;                    // …hasta muy horizontal (6:1)
        public const int SalidaMaxAncho = 600;                 // px del PNG guardado (no se amplía nunca)
        public const int SalidaMaxAlto = 200;

        private static readonly byte[] FirmaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static ResultadoLogo Procesar(byte[] datos)
        {
            if (datos == null || datos.Length == 0) return ResultadoLogo.Error("EmpLogo_Err_Vacio");
            if (datos.Length > MaxBytesArchivo) return ResultadoLogo.Error("EmpLogo_Err_Peso", MaxBytesArchivo / (1024 * 1024));

            bool esPng = datos.Length > FirmaPng.Length;
            if (esPng) for (int i = 0; i < FirmaPng.Length; i++) if (datos[i] != FirmaPng[i]) { esPng = false; break; }
            bool esJpg = datos.Length > 3 && datos[0] == 0xFF && datos[1] == 0xD8 && datos[2] == 0xFF;
            if (!esPng && !esJpg) return ResultadoLogo.Error("EmpLogo_Err_Formato");
            if (!TerminaCompleto(datos, esPng)) return ResultadoLogo.Error("EmpLogo_Err_Invalido");   // archivo cortado a medias

            try
            {
                using (var msIn = new MemoryStream(datos))
                using (var origen = Image.FromStream(msIn, false, true))
                {
                    int w = origen.Width, h = origen.Height;
                    if (w <= 0 || h <= 0) return ResultadoLogo.Error("EmpLogo_Err_Invalido");
                    if (w > MaxLado || h > MaxLado || (long)w * h > MaxPixeles) return ResultadoLogo.Error("EmpLogo_Err_Grande", MaxLado);
                    if (w < MinAncho || h < MinAlto) return ResultadoLogo.Error("EmpLogo_Err_Pequeno", MinAncho, MinAlto, w, h);

                    double ratio = (double)w / h;
                    if (ratio < RatioMin || ratio > RatioMax) return ResultadoLogo.Error("EmpLogo_Err_Proporcion", w, h);

                    // Se pasa a ARGB para trabajar igual con PNG indexado, JPEG o PNG con transparencia.
                    using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        bmp.SetResolution(96f, 96f);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.DrawImage(origen, 0, 0, w, h);
                        }
                        AplicarOrientacionExif(origen, bmp);

                        // Tamaño de salida: cabe en 600x200 conservando proporción; nunca se amplía.
                        double escala = Math.Min(1.0, Math.Min((double)SalidaMaxAncho / bmp.Width, (double)SalidaMaxAlto / bmp.Height));
                        int nw = Math.Max(1, (int)Math.Round(bmp.Width * escala));
                        int nh = Math.Max(1, (int)Math.Round(bmp.Height * escala));

                        using (var salida = new Bitmap(nw, nh, PixelFormat.Format32bppArgb))
                        {
                            salida.SetResolution(96f, 96f);
                            using (var g = Graphics.FromImage(salida))
                            {
                                g.Clear(Color.Transparent);
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = SmoothingMode.HighQuality;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.CompositingQuality = CompositingQuality.HighQuality;
                                // TileFlipXY evita el borde semitransparente que el reescalado deja en los bordes de la imagen.
                                using (var atr = new ImageAttributes())
                                {
                                    atr.SetWrapMode(WrapMode.TileFlipXY);
                                    g.DrawImage(bmp, new Rectangle(0, 0, nw, nh), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, atr);
                                }
                            }

                            // Se revisa la imagen ya reducida (≤600x200 px): pixel por pixel, sin perder trazos finos.
                            bool tieneTransparencia;
                            if (EsImagenUniforme(salida, out tieneTransparencia)) return ResultadoLogo.Error("EmpLogo_Err_Uniforme");

                            byte[] png;
                            using (var msOut = new MemoryStream())
                            {
                                salida.Save(msOut, ImageFormat.Png);
                                png = msOut.ToArray();
                            }
                            if (png.Length > MaxBytesArchivo) return ResultadoLogo.Error("EmpLogo_Err_Peso", MaxBytesArchivo / (1024 * 1024));

                            string version;
                            using (var sha = SHA256.Create())
                                version = BitConverter.ToString(sha.ComputeHash(png), 0, 8).Replace("-", "").ToLowerInvariant();

                            return new ResultadoLogo
                            {
                                Ok = true, Png = png, Ancho = nw, Alto = nh, Version = version,
                                AvisoKey = tieneTransparencia ? null : "EmpLogo_Aviso_SinTransparencia"
                            };
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is ExternalException)
            {
                // GDI+ lanza estas excepciones con archivos corruptos o truncados.
                return ResultadoLogo.Error("EmpLogo_Err_Invalido");
            }
        }

        /// <summary>PNG: debe cerrar con el bloque IEND. JPEG: debe cerrar con el marcador FFD9 (se toleran bytes de relleno al final).</summary>
        private static bool TerminaCompleto(byte[] d, bool esPng)
        {
            if (esPng)
            {
                int n = d.Length;
                return n >= 12 && d[n - 8] == 0x49 && d[n - 7] == 0x45 && d[n - 6] == 0x4E && d[n - 5] == 0x44;   // "IEND" + CRC
            }
            for (int i = d.Length - 2, tope = Math.Max(0, d.Length - 64); i >= tope; i--)
                if (d[i] == 0xFF && d[i + 1] == 0xD9) return true;
            return false;
        }

        /// <summary>Gira/voltea la imagen según la orientación EXIF (fotos de celular en JPEG).</summary>
        private static void AplicarOrientacionExif(Image origen, Bitmap bmp)
        {
            try
            {
                if (Array.IndexOf(origen.PropertyIdList, 0x0112) < 0) return;
                var item = origen.GetPropertyItem(0x0112);
                if (item == null || item.Value == null || item.Value.Length < 2) return;
                switch (BitConverter.ToUInt16(item.Value, 0))
                {
                    case 2: bmp.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                    case 3: bmp.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 4: bmp.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                    case 5: bmp.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                    case 6: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 7: bmp.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                    case 8: bmp.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                }
            }
            catch (Exception) { /* sin EXIF legible: se deja como está */ }
        }

        /// <summary>
        /// true si la imagen es de un solo color liso o está totalmente transparente (archivo en blanco). Un logo de un
        /// solo color sobre fondo transparente NO es uniforme. <paramref name="tieneTransparencia"/> indica si algún
        /// punto es (semi)transparente.
        /// </summary>
        private static bool EsImagenUniforme(Bitmap bmp, out bool tieneTransparencia)
        {
            tieneTransparencia = false;
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                var buf = new byte[stride * bmp.Height];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                bool hayVisible = false, hayTransparente = false, distintos = false;
                int r0 = 0, g0 = 0, b0 = 0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    int fila = y * stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int i = fila + x * 4;             // Format32bppArgb en memoria: B, G, R, A
                        byte b = buf[i], g = buf[i + 1], r = buf[i + 2], a = buf[i + 3];
                        if (a < 250) tieneTransparencia = true;
                        if (a < 16) { hayTransparente = true; continue; }
                        if (!hayVisible) { hayVisible = true; r0 = r; g0 = g; b0 = b; }
                        else if (Math.Abs(r - r0) + Math.Abs(g - g0) + Math.Abs(b - b0) > 24) distintos = true;
                    }
                }
                if (!hayVisible) return true;
                if (hayTransparente) return false;
                return !distintos;
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
