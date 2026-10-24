using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Tema de colores de un informe — ÚNICA fuente de verdad (ver CLAUDE.md, «Tema de colores de los informes»).
    /// Entran 3 colores (Principal y Secundario obligatorios, Acento opcional) y de ahí se DERIVAN todos los demás:
    /// degradado, color de texto/acento fuerte, tintes, serie de 8 colores para gráficos y colores del PDF.
    /// El tema «Bufins» reproduce EXACTAMENTE los valores que los informes traían antes de existir los temas.
    /// Los temas distintos de Bufins usan COLORES FIJOS (sin degradados): lo que se ve es exactamente lo que el usuario eligió y todo
    /// color derivado sale solo de sus 3 colores (principal, secundario y acento), nunca de otros matices.
    /// Los colores semánticos (verde/rojo de variaciones, semáforo de indicadores, cascada positiva/negativa) NO forman
    /// parte del tema: nunca cambian.
    /// El navegador no reimplementa el algoritmo: pide los tokens a <c>InformeTemaController.Resolver</c> y el servidor
    /// (Excel y correo) usa esta misma clase.
    /// </summary>
    public sealed class TemaInforme
    {
        public string Id { get; private set; }
        /// <summary>Clave de recurso con el nombre (presets) o el nombre ya resuelto (corporativas/personalizada).</summary>
        public string Nombre { get; private set; }
        public string Primario { get; private set; }
        public string Secundario { get; private set; }
        public string Acento { get; private set; }

        // ---- derivados ----
        /// <summary>Color fuerte para títulos/etiquetas activas sobre blanco (hoy #583AFF); contraste ≥ 4,5 con blanco.</summary>
        public string Strong { get; private set; }
        /// <summary>Tono oscuro del fuerte (texto sobre fondos suaves; hoy [59,47,191]).</summary>
        public string StrongOscuro { get; private set; }
        /// <summary>Tercer color de línea/serie (hoy #0EA5E9).</summary>
        public string Tercero { get; private set; }
        /// <summary>Color de la línea/serie de COMPARACIÓN (presupuesto, año anterior…). Bufins: ámbar de siempre; temas: el SECUNDARIO.</summary>
        public string Comp { get; private set; }
        /// <summary>Secundario oscurecido hasta contraste ≥ 3:1 con blanco: iconos y títulos pequeños sobre fondo blanco.</summary>
        public string SText { get; private set; }
        /// <summary>Color del texto/icono que va SOBRE el secundario (blanco u oscuro, según su luminosidad).</summary>
        public string SOn { get; private set; }
        /// <summary>4 paradas. Bufins: el degradado de siempre. Resto de temas: las 4 iguales (color fijo, sin degradado).</summary>
        public string[] Degradado { get; private set; }
        /// <summary>Tonos del principal, de más claro a más oscuro (reemplazan los azules/violetas claros fijos de Bufins).</summary>
        public string T1 { get; private set; }
        public string T2 { get; private set; }
        public string T3 { get; private set; }
        public string T4 { get; private set; }
        /// <summary>Muestra para fichas y vistas previas: bloques fijos Principal | Secundario | Acento (Bufins: su degradado).</summary>
        public string MuestraCss { get; private set; }
        public string Soft { get; private set; }             // chip principal / total (hoy #EEF2FF)
        public string Soft2 { get; private set; }            // zebra / fondos suaves (hoy #F4F3FB)
        public string Soft3 { get; private set; }            // fondo muy suave (hoy #F8F7FD)
        public string[] Serie { get; private set; }          // 8 colores
        /// <summary>3 paradas del degradado oscuro de la cabecera del correo (hoy #1e1b4b, #312e81, #4338ca).</summary>
        public string[] CorreoDegradado { get; private set; }

        private TemaInforme() { }

        // ======================================================================================================
        //  Presets
        // ======================================================================================================

        public const string IdBufins = "bufins";

        private static readonly TemaInforme _bufins = new TemaInforme
        {
            Id = IdBufins, Nombre = "Tema_Preset_bufins",
            Primario = "#6366F1", Secundario = "#01E1D7", Acento = "#D97706",
            Strong = "#583AFF", StrongOscuro = "#3B2FBF", Tercero = "#0EA5E9", Comp = "#D97706", SText = "#0891B2", SOn = "#160933",
            Degradado = new[] { "#6366F1", "#3B82F6", "#06B6D4", "#01E1D7" },
            Soft = "#EEF2FF", Soft2 = "#F4F3FB", Soft3 = "#F8F7FD",
            T1 = "#E0E7FF", T2 = "#C7D2FE", T3 = "#A5B4FC", T4 = "#818CF8",
            MuestraCss = "linear-gradient(90deg,#6366F1 0%,#3B82F6 35%,#06B6D4 70%,#01E1D7 100%)",
            Serie = new[] { "#6366F1", "#D97706", "#06B6D4", "#16A34A", "#DC2626", "#8B5CF6", "#0EA5E9", "#F59E0B" },
            CorreoDegradado = new[] { "#1E1B4B", "#312E81", "#4338CA" }
        };

        /// <summary>Tema por defecto: los colores Bufins de siempre.</summary>
        public static TemaInforme Bufins { get { return _bufins; } }

        private static readonly IList<TemaInforme> _presets = new List<TemaInforme> { _bufins };
        /// <summary>Estilos predeterminados: por ahora SOLO Bufins (las paletas propias son las corporativas de cada empresa y la personalizada).</summary>
        public static IList<TemaInforme> Presets { get { return _presets; } }

        public static TemaInforme PresetPorClave(string clave)
        {
            return _bufins;
        }

        // ======================================================================================================
        //  Validación
        // ======================================================================================================

        private static readonly Regex RxHex = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        /// <summary>Normaliza a #RRGGBB mayúsculas. Acepta con o sin '#'. false si no es un color hexadecimal de 6 dígitos.</summary>
        public static bool TryHex(string valor, out string hex)
        {
            hex = null;
            if (string.IsNullOrWhiteSpace(valor)) return false;
            string v = valor.Trim();
            if (!v.StartsWith("#")) v = "#" + v;
            if (!RxHex.IsMatch(v)) return false;
            hex = v.ToUpperInvariant();
            return true;
        }

        /// <summary>
        /// Valida los tres colores. Devuelve la clave de recurso del error (<c>Tema_Err_*</c>) o null si es válido;
        /// <paramref name="avisoKey"/> trae un aviso no bloqueante (<c>Tema_Aviso_*</c>).
        /// El Principal debe leerse con texto blanco encima (cabeceras, botones): contraste ≥ 3:1 con blanco.
        /// </summary>
        public static string Validar(string principal, string secundario, string acento, out string avisoKey)
        {
            avisoKey = null;
            string p, s, a;
            if (!TryHex(principal, out p) || !TryHex(secundario, out s)) return "Tema_Err_Hex";
            if (!string.IsNullOrWhiteSpace(acento) && !TryHex(acento, out a)) return "Tema_Err_Hex";

            double c = Contraste(p, "#FFFFFF");
            if (c < 3.0) return "Tema_Err_Contraste";
            if (c < 4.5) avisoKey = "Tema_Aviso_Contraste";
            if (Distancia(p, s) < 40) avisoKey = avisoKey ?? "Tema_Aviso_Parecidos";
            return null;
        }

        // ======================================================================================================
        //  Derivación (personalizada, corporativa y presets distintos de Bufins)
        // ======================================================================================================

        /// <summary>Construye un tema completo a partir de 3 colores YA validados. Acento nulo → ámbar #D97706.</summary>
        public static TemaInforme Derivar(string id, string nombre, string principal, string secundario, string acento)
        {
            string p, s, a;
            if (!TryHex(principal, out p) || !TryHex(secundario, out s)) return _bufins;
            if (!TryHex(acento, out a)) a = "#D97706";

            // Fuerte: el principal, oscurecido hasta leerse bien sobre blanco (texto, etiquetas activas).
            string strong = p;
            for (int i = 0; i < 12 && Contraste(strong, "#FFFFFF") < 4.5; i++) strong = Mezclar(strong, "#000000", 0.08);
            Func<string, string> textoSobreBlanco = c => { string x = c; for (int i = 0; i < 12 && Contraste(x, "#FFFFFF") < 3.0; i++) x = Mezclar(x, "#000000", 0.08); return x; };

            var t = new TemaInforme
            {
                Id = id, Nombre = nombre, Primario = p, Secundario = s, Acento = a,
                Strong = strong,
                StrongOscuro = Mezclar(strong, "#000000", 0.25),
                Tercero = a,                                            // serie de escenario comparado: el ACENTO
                Comp = s,                                               // serie de comparación (presupuesto…): el SECUNDARIO
                SText = textoSobreBlanco(s),
                SOn = Contraste(s, "#FFFFFF") >= 3.0 ? "#FFFFFF" : "#160933",
                Degradado = new[] { p, p, p, p },                       // color fijo, sin degradado
                Soft = Mezclar(p, "#FFFFFF", 0.92),
                Soft2 = Mezclar(p, "#FFFFFF", 0.955),
                Soft3 = Mezclar(p, "#FFFFFF", 0.975),
                T1 = Mezclar(p, "#FFFFFF", 0.86),
                T2 = Mezclar(p, "#FFFFFF", 0.78),
                T3 = Mezclar(p, "#FFFFFF", 0.60),
                T4 = Mezclar(p, "#FFFFFF", 0.30),
                MuestraCss = "linear-gradient(90deg," + p + " 0 34%," + s + " 34% 67%," + a + " 67% 100%)",
                // Cabecera del correo: un solo tono oscuro del principal.
                CorreoDegradado = new[] { Mezclar(strong, "#000000", 0.35), Mezclar(strong, "#000000", 0.35), Mezclar(strong, "#000000", 0.35) }
            };
            // Serie para gráficos: SOLO tonos de los tres colores elegidos (nunca matices ajenos a la paleta).
            t.Serie = new[]
            {
                p, s, a,
                Mezclar(p, "#000000", 0.30), Mezclar(s, "#000000", 0.30), Mezclar(a, "#000000", 0.25),
                Mezclar(p, "#FFFFFF", 0.45), Mezclar(s, "#FFFFFF", 0.45)
            };
            return t;
        }

        // ======================================================================================================
        //  Salida
        // ======================================================================================================

        /// <summary>Tokens para el navegador (CSS, Chart.js y PDF). Estructura estable: la consume Assets/js/informe-tema.js.</summary>
        public object ParaCliente(string nombreMostrar)
        {
            return new
            {
                id = Id, nombre = nombreMostrar ?? Nombre,
                p = Primario, s = Secundario, a = Acento,
                strong = Strong, strongOscuro = StrongOscuro, tercero = Tercero,
                soft = Soft, soft2 = Soft2, soft3 = Soft3, t1 = T1, t2 = T2, t3 = T3, t4 = T4, dk = StrongOscuro, muestra = MuestraCss, comp = Comp, sText = SText, sOn = SOn,
                pRgb = Rgb(Primario),
                grad = Degradado,
                gradCss = "linear-gradient(90deg," + Degradado[0] + " 0%," + Degradado[1] + " 35%," + Degradado[2] + " 70%," + Degradado[3] + " 100%)",
                serie = Serie,
                pdf = new
                {
                    marca = Rgb(Primario), morado = Rgb(Strong), totalTx = Rgb(StrongOscuro),
                    totalBg = Rgb(Soft), suave = Rgb(Soft2), suave2 = Rgb(Soft3),
                    ambar = Rgb(Comp), cian = Rgb(Tercero),
                    degradado = Degradado.Select(Rgb).ToArray()
                }
            };
        }

        // ---- utilidades de color (públicas: las usan Excel y correo) ----

        public static int[] Rgb(string hex)
        {
            return new[] { Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16) };
        }

        public static string Hex(int r, int g, int b)
        {
            return "#" + Clamp(r).ToString("X2") + Clamp(g).ToString("X2") + Clamp(b).ToString("X2");
        }

        /// <summary>Hex sin '#', como los espera <c>ExcelChartHelper</c> (p. ej. "6366F1").</summary>
        public static string SinAlmohadilla(string hex) { return hex.TrimStart('#'); }

        public static string Mezclar(string a, string b, double t)
        {
            var x = Rgb(a); var y = Rgb(b);
            return Hex((int)Math.Round(x[0] + (y[0] - x[0]) * t), (int)Math.Round(x[1] + (y[1] - x[1]) * t), (int)Math.Round(x[2] + (y[2] - x[2]) * t));
        }

        public static double Contraste(string a, string b)
        {
            double la = Luminancia(a), lb = Luminancia(b);
            double max = Math.Max(la, lb), min = Math.Min(la, lb);
            return (max + 0.05) / (min + 0.05);
        }

        private static double Luminancia(string hex)
        {
            var c = Rgb(hex);
            Func<int, double> f = v => { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
        }

        private static double Distancia(string a, string b)
        {
            var x = Rgb(a); var y = Rgb(b);
            return Math.Sqrt(Math.Pow(x[0] - y[0], 2) + Math.Pow(x[1] - y[1], 2) + Math.Pow(x[2] - y[2], 2)) / 4.41; // 0..100 aprox.
        }

        private static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }
    }
}
