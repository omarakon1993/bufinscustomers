using ClosedXML.Excel;
using System;
using System.Data;
using System.Linq;

namespace bufinscustomers.Helpers
{
    // Conversiones puras (sin BD ni estado) extraidas de CargueOrquestadorService para poder probarlas por separado.
    public static class CargueConversion
    {
        // ─── Existing helper methods ──────────────────────────────────────────────

        /// <summary>
        /// Celda → fecha para una columna DATE/DATETIME (hojas del Gestor de hojas de cargue).
        /// Vacía → DBNull; número → fecha serial de Excel; texto → cultura actual o AAAA-MM-DD.
        /// Devuelve <c>null</c> si el valor no es una fecha.
        /// </summary>
        public static object ConvertirCeldaFecha(object cellValue)
        {
            if (cellValue == null || cellValue == DBNull.Value) return DBNull.Value;
            if (cellValue is DateTime dtv) return dtv;
            if (cellValue is double d)
            {
                try { return DateTime.FromOADate(d); } catch { return null; }
            }
            string s = cellValue.ToString().Trim();
            if (s.Length == 0) return DBNull.Value;
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd" },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime iso))
                return iso;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out DateTime local))
                return local;
            return null;
        }

        /// <summary>
        /// Celda → Sí/No para una columna BIT (hojas del Gestor de hojas de cargue).
        /// Vacía → DBNull; acepta 1/0, Sí/No, Si, Verdadero/Falso, True/False, X. Devuelve <c>null</c> si no es válido.
        /// </summary>
        public static object ConvertirCeldaBit(object cellValue)
        {
            if (cellValue == null || cellValue == DBNull.Value) return DBNull.Value;
            if (cellValue is double d) return d != 0;
            string s = cellValue.ToString().Trim().ToLowerInvariant();
            if (s.Length == 0) return DBNull.Value;
            switch (s)
            {
                case "1": case "si": case "sí": case "s": case "x": case "true": case "verdadero": case "yes": case "y":
                    return true;
                case "0": case "no": case "n": case "false": case "falso":
                    return false;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Convierte cualquier valor de celda de Excel a decimal exacto para SqlBulkCopy.
        /// Para double nativo usa round-trip via string; para texto usa SanitizarValorNumerico.
        /// Nunca lanza excepción: retorna 0 ante cualquier caso inválido.
        /// </summary>
        public static decimal ExtraerDecimal(object cellValue)
        {
            if (cellValue == null) return 0m;

            if (cellValue is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) return 0m;
                // Round-trip via string para preservar los decimales del double sin ruido flotante
                string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out decimal dec)
                    ? dec : 0m;
            }

            if (cellValue is decimal dm) return dm;
            if (cellValue is int iv)    return (decimal)iv;
            if (cellValue is long lv)   return (decimal)lv;

            // Texto: pasar siempre por la heurística colombiana (coma=decimal, punto=miles)
            string str = SanitizarValorNumerico(cellValue.ToString()?.Trim() ?? "");
            return decimal.TryParse(str, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out decimal r)
                ? r : 0m;
        }

        public static string SanitizarValorNumerico(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return "0";
            var v = valor.Trim().Replace(" ", "").Replace("$", "");
            if (v == "-" || v == "–" || v == "—") return "0";
            if (string.IsNullOrEmpty(v)) return "0";

            int lastDot   = v.LastIndexOf('.');
            int lastComma = v.LastIndexOf(',');

            string s;
            if (lastDot >= 0 && lastComma >= 0)
            {
                // Ambos separadores: el último es el decimal
                s = lastDot > lastComma
                    ? v.Replace(",", "")                    // US: 15,549.00 → 15549.00
                    : v.Replace(".", "").Replace(",", "."); // Colombiano: 15.549,00 → 15549.00
            }
            else if (lastComma >= 0)
            {
                // En formato colombiano (es-CO) la coma es siempre separador decimal.
                // El separador de miles colombiano es el punto (manejado en el bloque de lastDot).
                // Ej: "86,751"→86.751  |  "15,549"→15.549  |  "15549,00"→15549.00
                s = v.Replace(",", ".");
            }
            else if (lastDot >= 0)
            {
                int dotCount = v.Count(c => c == '.');
                if (dotCount > 1)
                {
                    s = v.Replace(".", ""); // varios puntos = miles: 1.554.900 → 1554900
                }
                else
                {
                    // Único punto: miles si (1-2 dígitos numéricos antes Y exactamente 3 después)
                    // Ej: "15.549"→miles=15549  |  "150.534"→decimal  |  "15549.00"→decimal
                    string beforeDot = v.Substring(0, lastDot).TrimStart('-');
                    int afterDotLen  = v.Length - lastDot - 1;
                    bool esMiles = afterDotLen == 3
                                   && beforeDot.Length > 0 && beforeDot.Length <= 2
                                   && beforeDot != "0";
                    s = esMiles ? v.Replace(".", "") : v;
                }
            }
            else
            {
                s = v;
            }

            return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out _)
                ? s : "0";
        }

        private static readonly System.Text.RegularExpressions.Regex _regexCientifica =
            new System.Text.RegularExpressions.Regex(
                @"^-?\d+[\.,]?\d*[Ee][+\-]?\d+$",
                System.Text.RegularExpressions.RegexOptions.None);

        public static string DetectarErrorNumerico(string valor)
        {
            string v = valor.Trim();
            // Notación científica (ej: "1,489E-05"): rechazar, el usuario debe ajustar el formato en Excel
            if (_regexCientifica.IsMatch(v))
                return $"el valor '{valor}' está en notación científica. Amplíe el ancho de la columna en Excel y ajuste el formato a Número";
            if (v.Any(c => char.IsLetter(c)))
                return $"el valor '{valor}' contiene texto";
            if (v.Count(c => c == ',') > 1)
                return $"el valor '{valor}' tiene más de un separador decimal (coma)";
            return null;
        }
    }
}
