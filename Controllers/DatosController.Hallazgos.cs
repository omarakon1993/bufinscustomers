using bufinscustomers.Helpers;
using bufinscustomers.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace bufinscustomers.Controllers
{
    // Hallazgos: parte de DatosController (misma clase; solo se separa el codigo por responsabilidad).
    public partial class DatosController
    {
        // ── Exportar / enviar los hallazgos de validación (pantalla Revisar cargue) ──────────
        // Mismas reglas de acceso que RevisarCargue. Solo lectura: no cambian el estado del lote.

        private CargueLote ObtenerLoteParaExportarHallazgos(long idLote)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var lote = stagingService.ObtenerLote(idLote);
            if (usuario == null || lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
                return null;
            if (!PersonalizacionesEmpresaHelper.PuedeOperarCargue(usuario, lote.IdEmpresa))
                return null;
            return lote;
        }

        private static string NombreArchivoHallazgos(CargueLote lote, string extension)
        {
            string emp = new string((lote.NombreEmpresa ?? "Empresa").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            if (emp.Length > 40) emp = emp.Substring(0, 40);
            return $"Validacion_Cargue_{emp}_{lote.Anio}_Esc{lote.IdEscenario}_{DateTime.Now:yyyyMMdd_HHmm}.{extension}";
        }

        /// <summary>Excel de los hallazgos de un lote: cabecera con el contexto del cargue + una fila por hallazgo.</summary>
        private byte[] GenerarExcelHallazgos(CargueLote lote, List<CargueLoteHallazgo> hallazgos, bool esIngles)
        {
            var ordenados = hallazgos.OrderBy(x => x.Severidad == CargueLoteSeveridad.Error ? 0 : 1).ToList();
            var morado = XLColor.FromHtml("#583AFF");

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(R("RevisarCargue_Exp_HojaExcel"));

                ws.Cell(1, 1).Value = R("RevisarCargue_Exp_TituloInforme");
                ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(morado);

                var meta = new List<(string, string)>
                {
                    (R("RevisarCargue_Empresa"), lote.NombreEmpresa),
                    (R("RevisarCargue_Anio"), lote.Anio.ToString()),
                    (R("RevisarCargue_Modo"), lote.Modo == 0 ? R("RevisarCargue_ModoEjecucion") : R("RevisarCargue_ModoHistorico")),
                    (R("RevisarCargue_Escenario"), lote.IdEscenario.ToString()),
                    (R("RevisarCargue_Exp_Archivo"), lote.NombreArchivo),
                    (R("RevisarCargue_Exp_Fecha"), (lote.FechaValidacion ?? lote.FechaCreacion).ToString("yyyy-MM-dd HH:mm")),
                    (R("RevisarCargue_Filtro_Errores"), lote.TotalErrores.ToString()),
                    (R("RevisarCargue_Filtro_Advertencias"), lote.TotalAdvertencias.ToString())
                };
                int fila = 3;
                foreach (var m in meta)
                {
                    ws.Cell(fila, 1).Value = m.Item1;
                    ws.Cell(fila, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#5B5570"));
                    ws.Cell(fila, 2).Value = m.Item2 ?? "";
                    fila++;
                }

                fila++;
                int filaEnc = fila;
                string[] encabezados =
                {
                    "#", R("RevisarCargue_Exp_ColSeveridad"), R("RevisarCargue_Exp_ColTitulo"), R("RevisarCargue_Exp_ColMensaje"),
                    R("RevisarCargue_Exp_ColHoja"), R("RevisarCargue_Exp_ColFila"), R("RevisarCargue_Val_ColPeriodo"), R("RevisarCargue_Val_ColCuenta"),
                    R("RevisarCargue_Val_ColDiferencia"), R("RevisarCargue_Exp_ColColumna"), R("RevisarCargue_Exp_ColRegla")
                };
                string[] mesesAbr = esIngles
                    ? new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }
                    : new[] { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
                for (int c = 0; c < encabezados.Length; c++)
                    ws.Cell(filaEnc, c + 1).Value = encabezados[c];
                var rngEnc = ws.Range(filaEnc, 1, filaEnc, encabezados.Length);
                rngEnc.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(morado);

                int n = 0;
                foreach (var h in ordenados)
                {
                    fila++; n++;
                    bool esError = h.Severidad == CargueLoteSeveridad.Error;
                    string titulo = esIngles && !string.IsNullOrWhiteSpace(h.TituloEn) ? h.TituloEn : h.Titulo;
                    string mensaje = esIngles && !string.IsNullOrWhiteSpace(h.MensajeEn) ? h.MensajeEn : h.Mensaje;

                    ws.Cell(fila, 1).Value = n;
                    ws.Cell(fila, 2).Value = esError ? R("RevisarCargue_SeveridadError") : R("RevisarCargue_SeveridadAdvertencia");
                    ws.Cell(fila, 2).Style.Font.SetBold()
                        .Font.SetFontColor(XLColor.FromHtml(esError ? "#B91C1C" : "#92400E"))
                        .Fill.SetBackgroundColor(XLColor.FromHtml(esError ? "#FEF2F2" : "#FFFBEB"));
                    ws.Cell(fila, 3).Value = titulo ?? "";
                    ws.Cell(fila, 4).Value = mensaje ?? "";
                    ws.Cell(fila, 5).Value = h.NombreHoja ?? "";
                    if (h.NumeroFilaExcel.HasValue) ws.Cell(fila, 6).Value = h.NumeroFilaExcel.Value;

                    // Periodo 'AAAA-MM' → «Jun 2026»; referencia '@N4' → «Nivel 4 dígitos».
                    string periodo = h.Periodo ?? "";
                    if (periodo.Length == 7 && periodo[4] == '-' && int.TryParse(periodo.AsSpan(5, 2), out int mm) && mm >= 1 && mm <= 12)
                        periodo = string.Concat(mesesAbr[mm - 1], " ", periodo.AsSpan(0, 4));
                    ws.Cell(fila, 7).Value = periodo;
                    string referencia = h.Referencia ?? "";
                    if (referencia.StartsWith("@N")) referencia = string.Format(R("RevisarCargue_Val_Nivel"), referencia.Substring(2));
                    ws.Cell(fila, 8).Value = referencia;
                    if (h.Diferencia.HasValue)
                    {
                        ws.Cell(fila, 9).Value = (double)h.Diferencia.Value;
                        ws.Cell(fila, 9).Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00";
                    }
                    ws.Cell(fila, 10).Value = h.Columna ?? "";
                    ws.Cell(fila, 11).Value = h.CodigoRegla ?? "";
                }

                var rngDatos = ws.Range(filaEnc, 1, fila, encabezados.Length);
                rngDatos.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetOutsideBorderColor(XLColor.FromHtml("#E3E6F0"))
                    .Border.SetInsideBorderColor(XLColor.FromHtml("#E3E6F0"))
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(filaEnc, 1, fila, encabezados.Length).SetAutoFilter();

                ws.Columns(1, encabezados.Length).AdjustToContents();
                ws.Column(1).Width = Math.Max(ws.Column(1).Width, 16);
                ws.Column(3).Width = Math.Min(Math.Max(ws.Column(3).Width, 24), 45);
                ws.Column(4).Width = 80;
                ws.Column(4).Style.Alignment.SetWrapText(true);
                ws.SheetView.Freeze(filaEnc, 0);

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>Descarga en Excel los hallazgos de validación de un lote en revisión.</summary>
        [HttpGet]
        public ActionResult ExportarHallazgosExcel(long idLote)
        {
            var lote = ObtenerLoteParaExportarHallazgos(idLote);
            if (lote == null)
                return new HttpStatusCodeResult(403);

            try
            {
                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                var bytes = GenerarExcelHallazgos(lote, stagingService.ObtenerHallazgos(idLote), esIngles);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", NombreArchivoHallazgos(lote, "xlsx"));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"ExportarHallazgosExcel: lote {idLote}");
                return new HttpStatusCodeResult(500);
            }
        }

        /// <summary>
        /// Envía por correo el informe de validación de un lote: resumen en el cuerpo + Excel adjunto, y el PDF
        /// (generado en el navegador, el mismo que se descarga) si la vista lo manda. Destinatarios = los escritos
        /// y/o el correo registrado del usuario en Bufins.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarHallazgosPorCorreo(long idLote, string correos, bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var lote = ObtenerLoteParaExportarHallazgos(idLote);
            if (usuario == null || lote == null)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Exp_SinAcceso") });

            var listaCorreos = (correos ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();
            if (incluirmeAMi && !string.IsNullOrWhiteSpace(usuario.Correo))
                listaCorreos.Insert(0, usuario.Correo.Trim());
            listaCorreos = listaCorreos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (listaCorreos.Count == 0)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_ValDestino") });
            var invalidos = listaCorreos.Where(c => !_rxCorreo.IsMatch(c)).ToList();
            if (invalidos.Count != 0)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_ValDestino") + " (" + string.Join(", ", invalidos) + ")" });
            if (listaCorreos.Count > 10)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_ValMaximo") });

            // PDF opcional generado en el navegador: se acepta solo si de verdad es un PDF y no excede 8 MB.
            byte[] pdfBytes = null;
            if (!string.IsNullOrWhiteSpace(pdfBase64))
            {
                try
                {
                    string b64 = pdfBase64.Contains(',') ? pdfBase64.Substring(pdfBase64.IndexOf(',') + 1) : pdfBase64;
                    var bytes = Convert.FromBase64String(b64);
                    if (bytes.Length <= 8 * 1024 * 1024 && bytes.Length > 4
                        && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F')
                        pdfBytes = bytes;
                }
                catch (FormatException) { /* se envía sin PDF */ }
            }

            try
            {
                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                var hallazgos = stagingService.ObtenerHallazgos(idLote)
                    .OrderBy(x => x.Severidad == CargueLoteSeveridad.Error ? 0 : 1).ToList();

                var adjuntos = new List<(string nombre, byte[] datos, string mime)>
                {
                    (NombreArchivoHallazgos(lote, "xlsx"), GenerarExcelHallazgos(lote, hallazgos, esIngles),
                     "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                };
                if (pdfBytes != null)
                    adjuntos.Add((NombreArchivoHallazgos(lote, "pdf"), pdfBytes, "application/pdf"));

                // Cuerpo del correo: una línea por validación (no por caso; el detalle completo va en el Excel adjunto).
                var items = hallazgos.GroupBy(h => h.CodigoRegla ?? "").Select(g =>
                {
                    var h = g.First();
                    string regla = esIngles ? (h.ReglaEn ?? h.Regla) : h.Regla;
                    string msg = !string.IsNullOrWhiteSpace(regla) ? regla
                        : (esIngles && !string.IsNullOrWhiteSpace(h.MensajeEn) ? h.MensajeEn : h.Mensaje);
                    if (g.Count() > 1) msg += " (" + string.Format(R("RevisarCargue_Val_Casos"), g.Count().ToString("N0")) + ")";
                    return (
                        esError: h.Severidad == CargueLoteSeveridad.Error,
                        titulo: esIngles && !string.IsNullOrWhiteSpace(h.TituloEn) ? h.TituloEn : h.Titulo,
                        mensaje: msg);
                }).ToList();

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                string modoTexto = lote.Modo == 0 ? R("RevisarCargue_ModoEjecucion") : R("RevisarCargue_ModoHistorico");

                emailService.EnviarHallazgosCargue(
                    string.Join(", ", listaCorreos), lote.NombreEmpresa, lote.Anio, modoTexto, lote.IdEscenario,
                    lote.NombreArchivo, lote.TotalErrores, lote.TotalAdvertencias, items,
                    mensaje, quienEnvia, adjuntos, esIngles);

                auditoriaService.RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Enviar, "CargueLote", idLote.ToString(),
                    $"Informe de validación del cargue enviado por correo a {string.Join(", ", listaCorreos)}: '{lote.NombreEmpresa}' — año {lote.Anio}, escenario {lote.IdEscenario}, archivo '{lote.NombreArchivo}' ({lote.TotalErrores} errores, {lote.TotalAdvertencias} advertencias).",
                    null,
                    new { empresa = lote.NombreEmpresa, anio = lote.Anio, modo = lote.Modo, escenario = lote.IdEscenario, archivo = lote.NombreArchivo, destinatarios = listaCorreos, conPdf = pdfBytes != null },
                    lote.IdEmpresa, entidadNombre: lote.NombreEmpresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("RevisarCargue_Correo_Exito"), string.Join(", ", listaCorreos)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"EnviarHallazgosPorCorreo: lote {idLote}");
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_Error") });
            }
        }
    }
}
