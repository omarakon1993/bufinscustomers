using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Piezas comunes del "Enviar por correo" del botón Exportar de los informes (ver CLAUDE.md, "Botón Exportar
    /// desplegable"): validación de destinatarios, validación del PDF que arma el navegador y armado de adjuntos.
    /// Cada informe solo aporta su Excel, su PDF y el resumen del cuerpo; el envío usa
    /// <c>EmailService.EnviarInforme</c>.
    /// </summary>
    public static class InformeCorreoHelper
    {
        public const string MimeExcel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public const string MimePdf = "application/pdf";
        public const int MaxDestinatarios = 10;
        private const int MaxBytesPdf = 8 * 1024 * 1024;

        private static readonly Regex _rxCorreo = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        /// <summary>
        /// Arma la lista de destinatarios (correos escritos y/o el registrado del usuario), sin repetidos. Si algo
        /// no es válido devuelve false con el mensaje ya traducido (<paramref name="R"/> = BaseController.R).
        /// </summary>
        public static bool TryDestinatarios(Usuarios usuario, string correos, bool incluirmeAMi, Func<string, string> R,
            out List<string> lista, out string error)
        {
            error = null;
            lista = (correos ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (incluirmeAMi && usuario != null && !string.IsNullOrWhiteSpace(usuario.Correo))
                lista.Insert(0, usuario.Correo.Trim());
            lista = lista.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (lista.Count == 0) { error = R("RevisarCargue_Correo_ValDestino"); return false; }
            var invalidos = lista.Where(c => !_rxCorreo.IsMatch(c)).ToList();
            if (invalidos.Any()) { error = R("RevisarCargue_Correo_ValDestino") + " (" + string.Join(", ", invalidos) + ")"; return false; }
            if (lista.Count > MaxDestinatarios) { error = R("RevisarCargue_Correo_ValMaximo"); return false; }
            return true;
        }

        /// <summary>PDF generado en el navegador (data URI o base64): se acepta solo si de verdad es un PDF y no pasa de 8 MB; si no, null.</summary>
        public static byte[] PdfDesdeBase64(string pdfBase64)
        {
            if (string.IsNullOrWhiteSpace(pdfBase64)) return null;
            try
            {
                string b64 = pdfBase64.Contains(",") ? pdfBase64.Substring(pdfBase64.IndexOf(',') + 1) : pdfBase64;
                var bytes = Convert.FromBase64String(b64);
                return bytes.Length > 4 && bytes.Length <= MaxBytesPdf
                       && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' ? bytes : null;
            }
            catch (FormatException) { return null; }
        }

        /// <summary>Adjuntos del correo: el Excel y, si vino un PDF válido, el PDF (mismo nombre base).</summary>
        public static List<(string nombre, byte[] datos, string mime)> Adjuntos(string nombreExcel, byte[] excel, byte[] pdf)
        {
            var adjuntos = new List<(string nombre, byte[] datos, string mime)>();
            if (excel != null) adjuntos.Add((nombreExcel, excel, MimeExcel));
            if (pdf != null) adjuntos.Add((System.IO.Path.ChangeExtension(nombreExcel, ".pdf"), pdf, MimePdf));
            return adjuntos;
        }
    }
}
