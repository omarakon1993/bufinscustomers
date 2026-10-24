using bufinscustomers.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Web;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Envio de correo via MailKit (soporta puerto 465 SSL implicito y 587 STARTTLS).
    /// Lee la cuenta predeterminada desde ConfiguracionCuentasCorreo (BD).
    /// Fallback a Web.config si no hay cuenta en BD.
    /// </summary>
    public class EmailService
    {
        private readonly string _host;
        private readonly int    _puerto;
        private readonly bool   _ssl;
        private readonly bool   _ignorarCert;
        private readonly string _usuario;
        private readonly string _contrasena;
        private readonly string _remitente;
        private readonly string _nombreRemitente;

        public EmailService()
        {
            var cuenta = new CuentasCorreoService().ObtenerPredeterminada();

            if (cuenta != null)
            {
                _host            = cuenta.Host;
                _puerto          = cuenta.Puerto;
                _ssl             = cuenta.Ssl;
                _ignorarCert     = cuenta.IgnorarCertificado;
                _usuario         = cuenta.Usuario;
                _contrasena      = cuenta.Contrasena;
                _remitente       = cuenta.Remitente;
                _nombreRemitente = cuenta.NombreRemitente;
            }
            else
            {
                // Fallback a Web.config (sin credenciales — solo valores no sensibles)
                _host            = Cfg("SmtpHost",   "bufins.com");
                _puerto          = int.TryParse(Cfg("SmtpPort", "465"), out var p) ? p : 465;
                _ssl             = !string.Equals(Cfg("SmtpSsl", "true"), "false", StringComparison.OrdinalIgnoreCase);
                _ignorarCert     = true;
                _usuario         = "";
                _contrasena      = "";
                _remitente       = Cfg("SmtpFrom",    "admin@bufins.com");
                _nombreRemitente = Cfg("SmtpFromName","Bufins");
            }
        }

        public void EnviarRecuperacionClave(string destinatario, string nombre, string enlace, bool esIngles)
        {
            string asunto = esIngles ? "Reset your password - Bufins" : "Restablecer contrasena - Bufins";
            Enviar(destinatario, asunto, ConstruirEmailHtml(nombre, enlace, esIngles));
        }

        /// <summary>
        /// Correo de bienvenida cuando se crea un usuario nuevo. No incluye la contrase&ntilde;a
        /// (nunca se env&iacute;a en texto plano) &mdash; solo datos informativos y c&oacute;mo
        /// recuperar/cambiar la clave.
        /// </summary>
        public void EnviarBienvenidaUsuario(string destinatario, string nombreCompleto, string nombreUsuario,
            string empresa, string telefono, string enlaceLogin, bool esIngles)
        {
            string asunto = esIngles ? "Your Bufins account was created" : "Se creó tu cuenta en Bufins";
            Enviar(destinatario, asunto,
                ConstruirEmailBienvenidaHtml(nombreCompleto, nombreUsuario, empresa, telefono, destinatario, enlaceLogin, esIngles, ModoCorreoCuenta.Creacion));
        }

        /// <summary>
        /// Reenv&iacute;a manualmente (bot&oacute;n "Enviar informaci&oacute;n por correo" del gestor)
        /// los mismos datos de cuenta que el usuario recibi&oacute; al crearse. No incluye la contrase&ntilde;a.
        /// </summary>
        public void EnviarReenvioInfoUsuario(string destinatario, string nombreCompleto, string nombreUsuario,
            string empresa, string telefono, string enlaceLogin, bool esIngles)
        {
            string asunto = esIngles ? "Your Bufins account information" : "Tu información de cuenta en Bufins";
            Enviar(destinatario, asunto,
                ConstruirEmailBienvenidaHtml(nombreCompleto, nombreUsuario, empresa, telefono, destinatario, enlaceLogin, esIngles, ModoCorreoCuenta.Reenvio));
        }

        /// <summary>
        /// Notifica al usuario que un administrador actualiz&oacute; su informaci&oacute;n de cuenta
        /// (nombre, correo, tel&eacute;fono, empresa, etc.). No incluye la contrase&ntilde;a.
        /// </summary>
        public void EnviarNotificacionUsuarioActualizado(string destinatario, string nombreCompleto, string nombreUsuario,
            string empresa, string telefono, string enlaceLogin, bool esIngles)
        {
            string asunto = esIngles ? "Your Bufins account information was updated" : "Tu información de cuenta en Bufins fue actualizada";
            Enviar(destinatario, asunto,
                ConstruirEmailBienvenidaHtml(nombreCompleto, nombreUsuario, empresa, telefono, destinatario, enlaceLogin, esIngles, ModoCorreoCuenta.Actualizacion));
        }

        /// <summary>
        /// Correo de alerta de seguridad para el equipo (texto ya resuelto por el controlador,
        /// que es quien tiene acceso a los recursos i18n). Plantilla de marca con acento rojo.
        /// Puede tardar por el handshake SMTP: convócalo en segundo plano, nunca en línea con
        /// un flujo crítico como el login.
        /// </summary>
        public void EnviarAlertaSeguridad(string destinatario, string asunto, string titulo, string mensaje, string enlace = null)
        {
            Enviar(destinatario, asunto, ConstruirEmailAlertaHtml(titulo, mensaje, enlace));
        }

        /// <summary>
        /// Env&iacute;a el Excel de modelos financieros exportados a uno o varios destinatarios
        /// (separados por coma o punto y coma) con una plantilla de marca. El handshake SMTP y
        /// la generaci&oacute;n del adjunto pueden tardar: ll&aacute;malo desde un flujo que ya
        /// muestra un overlay de carga, nunca en l&iacute;nea con un flujo cr&iacute;tico.
        /// </summary>
        public void EnviarModelosExportados(string destinatarios, string tituloEmpresa,
            IEnumerable<string> modelos, string mensajePersonal, string nombreQuienEnvia,
            string nombreArchivo, byte[] adjunto, bool esIngles)
        {
            string asunto = esIngles
                ? $"Financial models - {tituloEmpresa}"
                : $"Modelos financieros - {tituloEmpresa}";

            var adjuntos = new List<(string nombre, byte[] datos, string mime)>
            {
                (nombreArchivo, adjunto, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            };

            EnviarConAdjuntos(destinatarios, asunto,
                ConstruirEmailModelosHtml(tituloEmpresa, modelos, mensajePersonal, nombreQuienEnvia, nombreArchivo, esIngles),
                adjuntos);
        }

        /// <summary>
        /// Env&iacute;a el informe de validaci&oacute;n de un cargue en revisi&oacute;n (staging): resumen de
        /// errores/advertencias en el cuerpo y los adjuntos que arma el controlador (Excel y, si viene, PDF).
        /// </summary>
        public void EnviarHallazgosCargue(string destinatarios, string empresa, int anio, string modo, int escenario,
            string nombreArchivo, int totalErrores, int totalAdvertencias,
            List<(bool esError, string titulo, string mensaje)> hallazgos,
            string mensajePersonal, string nombreQuienEnvia,
            List<(string nombre, byte[] datos, string mime)> adjuntos, bool esIngles)
        {
            string asunto = esIngles
                ? $"Upload validation - {empresa} {anio} ({totalErrores} errors, {totalAdvertencias} warnings)"
                : $"Validación de cargue - {empresa} {anio} ({totalErrores} errores, {totalAdvertencias} advertencias)";

            EnviarConAdjuntos(destinatarios, asunto,
                ConstruirEmailHallazgosHtml(empresa, anio, modo, escenario, nombreArchivo, totalErrores, totalAdvertencias,
                    hallazgos, mensajePersonal, nombreQuienEnvia, esIngles),
                adjuntos);
        }

        private static string ConstruirEmailHallazgosHtml(string empresa, int anio, string modo, int escenario,
            string nombreArchivo, int totalErrores, int totalAdvertencias,
            List<(bool esError, string titulo, string mensaje)> hallazgos,
            string mensajePersonal, string nombreQuienEnvia, bool esIngles)
        {
            const int maxEnCuerpo = 30;
            var lista = hallazgos ?? new List<(bool esError, string titulo, string mensaje)>();

            string titulo   = esIngles ? "Upload validation report" : "Informe de validaci&oacute;n del cargue";
            string intro    = esIngles
                ? $"These are the findings of the validation of the file uploaded for <strong>{HttpUtility.HtmlEncode(empresa)}</strong>. The data has <strong>not</strong> been saved yet: it is waiting for review."
                : $"Estos son los hallazgos de la validaci&oacute;n del archivo cargado para <strong>{HttpUtility.HtmlEncode(empresa)}</strong>. Los datos <strong>a&uacute;n no</strong> se han guardado: est&aacute;n pendientes de revisi&oacute;n.";
            string lblErr   = esIngles ? (totalErrores == 1 ? "error" : "errors") : (totalErrores == 1 ? "error" : "errores");
            string lblAdv   = esIngles ? (totalAdvertencias == 1 ? "warning" : "warnings") : (totalAdvertencias == 1 ? "advertencia" : "advertencias");
            string lblSevE  = esIngles ? "ERROR" : "ERROR";
            string lblSevA  = esIngles ? "WARNING" : "ADVERTENCIA";
            string lblArch  = esIngles ? "File" : "Archivo";
            string lblAnio  = esIngles ? "Year" : "A&ntilde;o";
            string lblModo  = esIngles ? "Mode" : "Modo";
            string lblEsc   = esIngles ? "Scenario" : "Escenario";
            string lblDe    = esIngles ? "Sent by" : "Enviado por";
            string lblMas   = esIngles
                ? $"And {lista.Count - maxEnCuerpo} more finding(s): see the attached file."
                : $"Y {lista.Count - maxEnCuerpo} hallazgo(s) m&aacute;s: consulta el archivo adjunto.";
            string lblAdj   = esIngles ? "The full detail is attached (Excel / PDF)." : "El detalle completo va adjunto (Excel / PDF).";

            string filas = string.Join("", lista.Take(maxEnCuerpo).Select(h =>
            {
                string color = h.esError ? "#b91c1c" : "#92400e";
                string fondo = h.esError ? "#fef2f2" : "#fffbeb";
                string borde = h.esError ? "#ef4444" : "#f59e0b";
                string tit = string.IsNullOrWhiteSpace(h.titulo) ? "" :
                    $@"<span style=""font-size:14px;font-weight:700;color:#1e1b4b;"">{HttpUtility.HtmlEncode(h.titulo)}</span>";
                return $@"<tr><td style=""padding:0 0 8px;"">
                  <div style=""background:{fondo};border-left:4px solid {borde};border-radius:8px;padding:10px 14px;"">
                    <span style=""font-size:11px;font-weight:800;color:{color};letter-spacing:.5px;margin-right:8px;"">{(h.esError ? lblSevE : lblSevA)}</span>{tit}
                    <div style=""font-size:13px;color:#4b5563;line-height:1.5;margin-top:4px;"">{HttpUtility.HtmlEncode(h.mensaje ?? "")}</div>
                  </div></td></tr>";
            }));
            if (lista.Count > maxEnCuerpo)
                filas += $@"<tr><td style=""font-size:13px;color:#6b7280;padding:4px 0 0;"">{lblMas}</td></tr>";

            string bloqueMensaje = string.IsNullOrWhiteSpace(mensajePersonal) ? "" : $@"
    <div style=""background:#f8f9ff;border-left:4px solid #6366f1;padding:14px 18px;font-size:14px;color:#4b5563;line-height:1.6;margin:0 0 22px;white-space:pre-wrap;"">{HttpUtility.HtmlEncode(mensajePersonal)}</div>";

            string bloqueDe = string.IsNullOrWhiteSpace(nombreQuienEnvia) ? "" :
                $@"<p style=""font-size:13px;color:#9ca3af;margin:14px 0 0;"">{lblDe}: {HttpUtility.HtmlEncode(nombreQuienEnvia)}</p>";

            string Chip(string etiqueta, string valor) =>
                $@"<span style=""display:inline-block;font-size:12px;color:#5B5570;background:#fff;border:1px solid #e3e6f0;border-radius:999px;padding:4px 10px;margin:0 6px 6px 0;"">{etiqueta} <strong style=""color:#1e1b4b;"">{HttpUtility.HtmlEncode(valor ?? "")}</strong></span>";

            return $@"<!DOCTYPE html>
<html lang=""{(esIngles ? "en" : "es")}"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f2f8;font-family:'Segoe UI',Arial,sans-serif;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f2f8;padding:32px 16px;"">
 <tr><td align=""center"">
 <table width=""640"" cellpadding=""0"" cellspacing=""0"" style=""max-width:640px;width:100%;background:#fff;border-radius:16px;overflow:hidden;"">
  <tr><td style=""background:linear-gradient(135deg,#1e1b4b,#312e81,#4338ca);padding:36px 40px 28px;text-align:center;"">
    <div style=""font-size:28px;font-weight:900;color:#fff;letter-spacing:3px;"">bufins</div>
    <div style=""font-size:9px;color:rgba(255,255,255,0.6);letter-spacing:2px;text-transform:uppercase;"">Business Finance Always Everywhere</div>
  </td></tr>
  <tr><td style=""padding:36px 40px;"">
    <p style=""font-size:18px;font-weight:700;color:#1e1b4b;margin:0 0 12px;"">{titulo}</p>
    <p style=""font-size:15px;color:#4b5563;line-height:1.7;margin:0 0 18px;"">{intro}</p>
    <div style=""margin:0 0 14px;"">
      <span style=""display:inline-block;background:#fef2f2;border:1px solid #fecaca;color:#b91c1c;border-radius:10px;padding:8px 14px;margin:0 8px 8px 0;font-size:14px;""><strong style=""font-size:20px;"">{totalErrores}</strong> {lblErr}</span>
      <span style=""display:inline-block;background:#fffbeb;border:1px solid #fde68a;color:#92400e;border-radius:10px;padding:8px 14px;margin:0 8px 8px 0;font-size:14px;""><strong style=""font-size:20px;"">{totalAdvertencias}</strong> {lblAdv}</span>
    </div>
    <div style=""background:#f8f9fc;border-radius:10px;padding:10px 12px 4px;margin:0 0 22px;"">
      {Chip(lblArch, nombreArchivo)}{Chip(lblAnio, anio.ToString())}{Chip(lblModo, modo)}{Chip(lblEsc, escenario.ToString())}
    </div>
    {bloqueMensaje}
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"">{filas}</table>
    <p style=""font-size:13px;color:#6b7280;margin:14px 0 0;"">&#128206; {lblAdj}</p>
    {bloqueDe}
  </td></tr>
  <tr><td style=""background:#f8f9ff;padding:20px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #e5e7eb;"">
    &copy; Bufins &mdash; Business Finance Always Everywhere
  </td></tr>
 </table>
 </td></tr>
</table>
</body></html>";
        }

        /// <summary>
        /// Env&iacute;a un informe por correo (&uacute;nico formato para todos los informes; ver el bot&oacute;n Exportar
        /// est&aacute;ndar). El cuerpo trae un resumen opcional (chips de contexto, tarjetas de indicadores y una tabla
        /// corta) y el detalle completo va en los adjuntos que arma el controlador (Excel y, si viene, PDF).
        /// <paramref name="kpis"/> y <paramref name="filas"/> pueden ir vac&iacute;os. El signo (1 = sube, -1 = baja,
        /// 0 = neutro) solo decide el color de un indicador o de la &uacute;ltima celda de una fila.
        /// </summary>
        public void EnviarInforme(string destinatarios, string tituloInforme, string empresa, bool personalizado,
            List<(string etiqueta, string valor)> contexto,
            List<(string etiqueta, string valor, string detalle, int signo)> kpis,
            string tablaTitulo, string[] columnas, List<(string[] celdas, int signo)> filas,
            string mensajePersonal, string nombreQuienEnvia,
            List<(string nombre, byte[] datos, string mime)> adjuntos, bool esIngles, byte[] logoEmpresa = null)
        {
            string primero = contexto != null && contexto.Count > 0 ? contexto[0].valor : null;
            string asunto = string.IsNullOrWhiteSpace(primero) ? $"{tituloInforme} - {empresa}" : $"{tituloInforme} - {empresa} ({primero})";

            EnviarConAdjuntos(destinatarios, asunto,
                ConstruirEmailInformeHtml(tituloInforme, empresa, personalizado, contexto, kpis, tablaTitulo, columnas, filas,
                    mensajePersonal, nombreQuienEnvia, adjuntos, esIngles, logoEmpresa != null && logoEmpresa.Length > 0),
                adjuntos, logoEmpresa);
        }

        private static string ConstruirEmailInformeHtml(string tituloInforme, string empresa, bool personalizado,
            List<(string etiqueta, string valor)> contexto,
            List<(string etiqueta, string valor, string detalle, int signo)> kpis,
            string tablaTitulo, string[] columnas, List<(string[] celdas, int signo)> filas,
            string mensajePersonal, string nombreQuienEnvia,
            List<(string nombre, byte[] datos, string mime)> adjuntos, bool esIngles, bool conLogoEmpresa = false)
        {
            contexto = contexto ?? new List<(string etiqueta, string valor)>();
            kpis = kpis ?? new List<(string etiqueta, string valor, string detalle, int signo)>();
            filas = filas ?? new List<(string[] celdas, int signo)>();
            columnas = columnas ?? new string[0];

            string intro = esIngles
                ? $"Here is the <strong>{HttpUtility.HtmlEncode(tituloInforme)}</strong> report for <strong>{HttpUtility.HtmlEncode(empresa)}</strong>. " +
                  (kpis.Count > 0 || filas.Count > 0 ? "The key figures are below; the full detail is attached." : "The full detail is attached.")
                : $"Compartimos el informe <strong>{HttpUtility.HtmlEncode(tituloInforme)}</strong> de <strong>{HttpUtility.HtmlEncode(empresa)}</strong>. " +
                  (kpis.Count > 0 || filas.Count > 0 ? "Abajo van las cifras clave; el detalle completo va adjunto." : "El detalle completo va adjunto.");
            string lblDe   = esIngles ? "Sent by" : "Enviado por";
            string lblPers = esIngles ? "Custom report" : "Informe personalizado";

            bool hayExcel = adjuntos != null && adjuntos.Any(a => a.mime == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            bool hayPdf   = adjuntos != null && adjuntos.Any(a => a.mime == "application/pdf");
            string quePdfExcel = hayExcel && hayPdf ? "Excel + PDF" : (hayExcel ? "Excel" : (hayPdf ? "PDF" : ""));
            string lblAdj = string.IsNullOrEmpty(quePdfExcel) ? ""
                : (esIngles ? $"Attached: {quePdfExcel}." : $"Adjuntos: {quePdfExcel}.");

            string Color(int signo) => signo > 0 ? "#16a34a" : (signo < 0 ? "#dc2626" : "#6b7280");

            string Chip(string etiqueta, string valor) =>
                $@"<span style=""display:inline-block;font-size:12px;color:#5B5570;background:#fff;border:1px solid #e3e6f0;border-radius:999px;padding:4px 10px;margin:0 6px 6px 0;"">{HttpUtility.HtmlEncode(etiqueta)} <strong style=""color:#1e1b4b;"">{HttpUtility.HtmlEncode(valor ?? "")}</strong></span>";

            string bloqueContexto = contexto.Count == 0 ? "" : $@"
    <div style=""background:#f8f9fc;border-radius:10px;padding:10px 12px 4px;margin:0 0 18px;"">{string.Concat(contexto.Select(c => Chip(c.etiqueta, c.valor)))}</div>";

            // Indicadores: tarjetas de dos en dos (las tablas anidadas son lo que mejor soportan los clientes de correo).
            var celdas = kpis.Select(k => $@"<td width=""50%"" valign=""top"" style=""padding:0 6px 12px 0;"">
                <div style=""background:#f8f9fc;border:1px solid #e3e6f0;border-radius:12px;padding:12px 14px;"">
                  <div style=""font-size:11px;font-weight:700;color:#6b7280;text-transform:uppercase;letter-spacing:.4px;"">{HttpUtility.HtmlEncode(k.etiqueta)}</div>
                  <div style=""font-size:24px;font-weight:800;color:{(k.signo != 0 ? Color(k.signo) : "#1e1b4b")};margin:4px 0 2px;"">{HttpUtility.HtmlEncode(k.valor)}</div>
                  <div style=""font-size:12px;color:#6b7280;"">{HttpUtility.HtmlEncode(k.detalle ?? "")}</div>
                </div></td>").ToList();
            var filasKpi = new StringBuilder();
            for (int i = 0; i < celdas.Count; i += 2)
                filasKpi.Append("<tr>").Append(celdas[i]).Append(i + 1 < celdas.Count ? celdas[i + 1] : "<td width=\"50%\"></td>").Append("</tr>");
            string bloqueKpis = kpis.Count == 0 ? "" : $@"<table width=""100%"" cellpadding=""0"" cellspacing=""0"">{filasKpi}</table>";

            string bloqueTabla = "";
            if (filas.Count > 0 && columnas.Length > 0)
            {
                string encabezado = string.Concat(columnas.Select((c, i) =>
                    $@"<th align=""{(i == 0 ? "left" : "right")}"" style=""padding:8px 12px;font-size:11px;color:#6b7280;text-transform:uppercase;letter-spacing:.4px;"">{HttpUtility.HtmlEncode(c)}</th>"));
                string cuerpo = string.Concat(filas.Select((f, r) => $@"<tr style=""background:{(r % 2 == 0 ? "#ffffff" : "#f8f9fc")};"">" +
                    string.Concat(f.celdas.Select((c, i) =>
                        $@"<td style=""padding:8px 12px;font-size:13px;{(i == 0 ? "color:#1e1b4b;" : "text-align:right;color:#1e1b4b;")}{(i == f.celdas.Length - 1 && i > 0 && f.signo != 0 ? $"font-weight:700;color:{Color(f.signo)};" : "")}border-bottom:1px solid #eef0f6;"">{HttpUtility.HtmlEncode(c ?? "")}</td>")) +
                    "</tr>"));
                bloqueTabla = $@"
    {(string.IsNullOrWhiteSpace(tablaTitulo) ? "" : $@"<p style=""font-size:14px;font-weight:700;color:#1e1b4b;margin:6px 0 8px;"">{HttpUtility.HtmlEncode(tablaTitulo)}</p>")}
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e3e6f0;border-radius:10px;border-collapse:separate;overflow:hidden;"">
      <tr style=""background:#f4f3fb;"">{encabezado}</tr>{cuerpo}
    </table>";
            }

            string bloqueMensaje = string.IsNullOrWhiteSpace(mensajePersonal) ? "" : $@"
    <div style=""background:#f8f9ff;border-left:4px solid #6366f1;padding:14px 18px;font-size:14px;color:#4b5563;line-height:1.6;margin:0 0 22px;white-space:pre-wrap;"">{HttpUtility.HtmlEncode(mensajePersonal)}</div>";
            string bloqueAdj = string.IsNullOrEmpty(lblAdj) ? "" : $@"<p style=""font-size:13px;color:#6b7280;margin:16px 0 0;"">&#128206; {lblAdj}</p>";
            string bloqueDe = string.IsNullOrWhiteSpace(nombreQuienEnvia) ? "" :
                $@"<p style=""font-size:13px;color:#9ca3af;margin:14px 0 0;"">{lblDe}: {HttpUtility.HtmlEncode(nombreQuienEnvia)}</p>";
            // Logo de la empresa: placa blanca bajo la marca Bufins (el logo viaja incrustado: cid:logo-empresa).
            string bloqueLogo = !conLogoEmpresa ? "" : $@"
    <div style=""margin-top:18px;""><span style=""display:inline-block;background:#fff;border-radius:10px;padding:8px 14px;""><img src=""cid:logo-empresa"" height=""44"" alt=""{HttpUtility.HtmlEncode(empresa)}"" style=""display:block;height:44px;width:auto;max-width:240px;border:0;""></span></div>";
            string bloquePers = personalizado ? $@"<p style=""font-size:12px;font-weight:700;color:#583AFF;margin:0 0 12px;"">{lblPers}</p>" : "";

            return $@"<!DOCTYPE html>
<html lang=""{(esIngles ? "en" : "es")}"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f2f8;font-family:'Segoe UI',Arial,sans-serif;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f2f8;padding:32px 16px;"">
 <tr><td align=""center"">
 <table width=""640"" cellpadding=""0"" cellspacing=""0"" style=""max-width:640px;width:100%;background:#fff;border-radius:16px;overflow:hidden;"">
  <tr><td style=""background:linear-gradient(135deg,#1e1b4b,#312e81,#4338ca);padding:36px 40px 28px;text-align:center;"">
    <div style=""font-size:28px;font-weight:900;color:#fff;letter-spacing:3px;"">bufins</div>
    <div style=""font-size:9px;color:rgba(255,255,255,0.6);letter-spacing:2px;text-transform:uppercase;"">Business Finance Always Everywhere</div>{bloqueLogo}
  </td></tr>
  <tr><td style=""padding:36px 40px;"">
    <p style=""font-size:18px;font-weight:700;color:#1e1b4b;margin:0 0 4px;"">{HttpUtility.HtmlEncode(tituloInforme)}</p>
    {bloquePers}
    <p style=""font-size:15px;color:#4b5563;line-height:1.7;margin:0 0 18px;"">{intro}</p>
    {bloqueContexto}
    {bloqueMensaje}
    {bloqueKpis}
    {bloqueTabla}
    {bloqueAdj}
    {bloqueDe}
  </td></tr>
  <tr><td style=""background:#f8f9ff;padding:20px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #e5e7eb;"">
    &copy; Bufins &mdash; Business Finance Always Everywhere
  </td></tr>
 </table>
 </td></tr>
</table>
</body></html>";
        }

        private enum ModoCorreoCuenta { Creacion, Reenvio, Actualizacion }

        private void Enviar(string destinatario, string asunto, string htmlBody)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_nombreRemitente, _remitente));
            message.To.Add(MailboxAddress.Parse(destinatario));
            message.Subject = asunto;
            var body = new TextPart("html");
            body.SetText(Encoding.UTF8, htmlBody);
            message.Body = body;

            using (var client = new SmtpClient())
            {
                // Puerto 465 = SSL implicito desde la conexion
                // Puerto 587 = STARTTLS (upgrade a SSL despues del handshake)
                // Puerto 25  = Sin SSL
                SecureSocketOptions sslOpts;
                if (_ssl && _puerto == 465)
                    sslOpts = SecureSocketOptions.SslOnConnect;      // SSL implicito
                else if (_ssl)
                    sslOpts = SecureSocketOptions.StartTls;          // STARTTLS
                else
                    sslOpts = SecureSocketOptions.None;

                if (_ignorarCert)
                    client.ServerCertificateValidationCallback = (s, c, ch, e) => true;

                client.Connect(_host, _puerto, sslOpts);
                client.Authenticate(_usuario, _contrasena);
                client.Send(message);
                client.Disconnect(quit: true);
            }
        }

        /// <summary>
        /// Variante de <see cref="Enviar"/> que admite varios destinatarios (separados por coma
        /// o punto y coma) y adjuntos binarios.
        /// </summary>
        private void EnviarConAdjuntos(string destinatarios, string asunto, string htmlBody,
            List<(string nombre, byte[] datos, string mime)> adjuntos, byte[] logoIncrustado = null)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_nombreRemitente, _remitente));

            string normalizado = (destinatarios ?? "").Replace(";", ",");
            foreach (var addr in InternetAddressList.Parse(normalizado))
                message.To.Add(addr);

            if (message.To.Count == 0)
                throw new InvalidOperationException("No hay destinatarios validos.");

            message.Subject = asunto;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            if (adjuntos != null)
            {
                foreach (var a in adjuntos)
                {
                    if (a.datos == null || a.datos.Length == 0) continue;
                    builder.Attachments.Add(a.nombre, a.datos, ContentType.Parse(a.mime));
                }
            }
            if (logoIncrustado != null && logoIncrustado.Length > 0)
            {
                // Imagen incrustada (cid:logo-empresa): se ve en el cuerpo sin depender de que el cliente descargue imágenes externas.
                var recurso = builder.LinkedResources.Add("logo-empresa.png", logoIncrustado, ContentType.Parse("image/png"));
                recurso.ContentId = "logo-empresa";
            }
            message.Body = builder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                SecureSocketOptions sslOpts;
                if (_ssl && _puerto == 465)
                    sslOpts = SecureSocketOptions.SslOnConnect;
                else if (_ssl)
                    sslOpts = SecureSocketOptions.StartTls;
                else
                    sslOpts = SecureSocketOptions.None;

                if (_ignorarCert)
                    client.ServerCertificateValidationCallback = (s, c, ch, e) => true;

                client.Connect(_host, _puerto, sslOpts);
                client.Authenticate(_usuario, _contrasena);
                client.Send(message);
                client.Disconnect(quit: true);
            }
        }

        private static string Cfg(string key, string def) =>
            ConfigurationManager.AppSettings[key]?.Trim() ?? def;

        private static string ConstruirEmailHtml(string nombre, string enlace, bool esIngles)
        {
            string saludo  = esIngles
                ? $"Hello{(string.IsNullOrEmpty(nombre) ? "" : " " + nombre)},"
                : $"Hola{(string.IsNullOrEmpty(nombre) ? "" : " " + nombre)},";
            string cuerpo  = esIngles
                ? "We received a request to reset the password for your Bufins account. Click the button below to create a new password:"
                : "Recibimos una solicitud para restablecer la contrase&ntilde;a de tu cuenta Bufins. Haz clic en el bot&oacute;n para crear una nueva contrase&ntilde;a:";
            string btnTxt  = esIngles ? "Reset Password" : "Restablecer Contrase&ntilde;a";
            string expira  = esIngles
                ? "This link expires in <strong>1 hour</strong>."
                : "Este enlace expira en <strong>1 hora</strong>.";
            string ignorar = esIngles
                ? "If you didn't request this, you can safely ignore this email."
                : "Si no solicitaste este cambio, puedes ignorar este correo.";

            return $@"<!DOCTYPE html>
<html lang=""{(esIngles ? "en" : "es")}"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f2f8;font-family:'Segoe UI',Arial,sans-serif;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f2f8;padding:32px 16px;"">
 <tr><td align=""center"">
 <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#fff;border-radius:16px;overflow:hidden;"">
  <tr><td style=""background:linear-gradient(135deg,#1e1b4b,#312e81,#4338ca);padding:36px 40px 28px;text-align:center;"">
    <div style=""font-size:28px;font-weight:900;color:#fff;letter-spacing:3px;"">bufins</div>
    <div style=""font-size:9px;color:rgba(255,255,255,0.6);letter-spacing:2px;text-transform:uppercase;"">Business Finance Always Everywhere</div>
  </td></tr>
  <tr><td style=""padding:40px;"">
    <p style=""font-size:18px;font-weight:600;color:#1e1b4b;margin:0 0 12px;"">{saludo}</p>
    <p style=""font-size:15px;color:#4b5563;line-height:1.7;margin:0 0 28px;"">{cuerpo}</p>
    <table width=""100%"" cellpadding=""0"" cellspacing=""0""><tr><td align=""center"" style=""padding:0 0 28px;"">
      <a href=""{enlace}"" style=""display:inline-block;background:linear-gradient(135deg,#4338ca,#6d28d9);color:#fff;text-decoration:none;padding:16px 40px;border-radius:50px;font-size:15px;font-weight:700;"">{btnTxt}</a>
    </td></tr></table>
    <p style=""font-size:12px;color:#9ca3af;text-align:center;word-break:break-all;"">
      {(esIngles ? "Or copy this link:" : "O copia este enlace:")}<br>
      <a href=""{enlace}"" style=""color:#6366f1;"">{enlace}</a>
    </p>
    <div style=""background:#f8f9ff;border-left:4px solid #6366f1;padding:14px 18px;font-size:13px;color:#4b5563;margin:16px 0;"">{expira}</div>
    <p style=""font-size:13px;color:#9ca3af;text-align:center;"">{ignorar}</p>
  </td></tr>
  <tr><td style=""background:#f8f9ff;padding:20px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #e5e7eb;"">
    &copy; Bufins &mdash; Business Finance Always Everywhere
  </td></tr>
 </table>
 </td></tr>
</table>
</body></html>";
        }

        private static string ConstruirEmailAlertaHtml(string titulo, string mensaje, string enlace)
        {
            string bloqueEnlace = string.IsNullOrWhiteSpace(enlace) ? "" : $@"
    <table width=""100%"" cellpadding=""0"" cellspacing=""0""><tr><td align=""center"" style=""padding:4px 0 24px;"">
      <a href=""{enlace}"" style=""display:inline-block;background:linear-gradient(135deg,#4338ca,#6d28d9);color:#fff;text-decoration:none;padding:14px 36px;border-radius:50px;font-size:14px;font-weight:700;"">Abrir auditor&iacute;a</a>
    </td></tr></table>";

            return $@"<!DOCTYPE html>
<html lang=""es"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f2f8;font-family:'Segoe UI',Arial,sans-serif;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f2f8;padding:32px 16px;"">
 <tr><td align=""center"">
 <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#fff;border-radius:16px;overflow:hidden;"">
  <tr><td style=""background:linear-gradient(135deg,#7f1d1d,#b91c1c,#dc2626);padding:32px 40px 24px;text-align:center;"">
    <div style=""font-size:26px;font-weight:900;color:#fff;letter-spacing:3px;"">bufins</div>
    <div style=""font-size:10px;color:rgba(255,255,255,0.8);letter-spacing:2px;text-transform:uppercase;"">Alerta de seguridad</div>
  </td></tr>
  <tr><td style=""padding:36px 40px;"">
    <p style=""font-size:18px;font-weight:700;color:#7f1d1d;margin:0 0 14px;"">{HttpUtility.HtmlEncode(titulo)}</p>
    <p style=""font-size:15px;color:#4b5563;line-height:1.7;margin:0 0 24px;"">{HttpUtility.HtmlEncode(mensaje)}</p>
    {bloqueEnlace}
    <div style=""background:#fef2f2;border-left:4px solid #dc2626;padding:14px 18px;font-size:13px;color:#4b5563;margin:8px 0 0;"">
      Aviso autom&aacute;tico del sistema Bufins. No respondas a este correo.
    </div>
  </td></tr>
  <tr><td style=""background:#f8f9ff;padding:20px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #e5e7eb;"">
    &copy; Bufins &mdash; Business Finance Always Everywhere
  </td></tr>
 </table>
 </td></tr>
</table>
</body></html>";
        }

        private static string ConstruirEmailModelosHtml(string empresa, IEnumerable<string> modelos,
            string mensajePersonal, string nombreQuienEnvia, string nombreArchivo, bool esIngles)
        {
            var lista = (modelos ?? Enumerable.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m)).ToList();

            string titulo   = esIngles ? "Financial models export" : "Exportaci&oacute;n de modelos financieros";
            string intro    = esIngles
                ? $"You have received the export of the financial models for <strong>{HttpUtility.HtmlEncode(empresa)}</strong>. The Excel file is attached to this email."
                : $"Has recibido la exportaci&oacute;n de los modelos financieros de <strong>{HttpUtility.HtmlEncode(empresa)}</strong>. El archivo Excel va adjunto a este correo.";
            string lblLista = esIngles ? "Included models" : "Modelos incluidos";
            string lblArch  = esIngles ? "Attached file" : "Archivo adjunto";
            string lblDe    = esIngles ? "Sent by" : "Enviado por";

            string filasModelos = string.Join("", lista.Select(m =>
                $@"<tr><td style=""padding:6px 0;font-size:14px;color:#1e1b4b;"">
                     <span style=""color:#6366f1;font-weight:700;"">&bull;</span>&nbsp;{HttpUtility.HtmlEncode(m)}
                   </td></tr>"));

            string bloqueMensaje = string.IsNullOrWhiteSpace(mensajePersonal) ? "" : $@"
    <div style=""background:#f8f9ff;border-left:4px solid #6366f1;padding:14px 18px;font-size:14px;color:#4b5563;line-height:1.6;margin:0 0 22px;white-space:pre-wrap;"">{HttpUtility.HtmlEncode(mensajePersonal)}</div>";

            string bloqueDe = string.IsNullOrWhiteSpace(nombreQuienEnvia) ? "" :
                $@"<p style=""font-size:13px;color:#9ca3af;margin:4px 0 0;"">{lblDe}: {HttpUtility.HtmlEncode(nombreQuienEnvia)}</p>";

            return $@"<!DOCTYPE html>
<html lang=""{(esIngles ? "en" : "es")}"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f2f8;font-family:'Segoe UI',Arial,sans-serif;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f2f8;padding:32px 16px;"">
 <tr><td align=""center"">
 <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#fff;border-radius:16px;overflow:hidden;"">
  <tr><td style=""background:linear-gradient(135deg,#1e1b4b,#312e81,#4338ca);padding:36px 40px 28px;text-align:center;"">
    <div style=""font-size:28px;font-weight:900;color:#fff;letter-spacing:3px;"">bufins</div>
    <div style=""font-size:9px;color:rgba(255,255,255,0.6);letter-spacing:2px;text-transform:uppercase;"">Business Finance Always Everywhere</div>
  </td></tr>
  <tr><td style=""padding:40px;"">
    <p style=""font-size:18px;font-weight:700;color:#1e1b4b;margin:0 0 12px;"">{titulo}</p>
    <p style=""font-size:15px;color:#4b5563;line-height:1.7;margin:0 0 22px;"">{intro}</p>
    {bloqueMensaje}
    <p style=""font-size:13px;font-weight:700;color:#9ca3af;text-transform:uppercase;letter-spacing:1px;margin:0 0 6px;"">{lblLista}</p>
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f8f9ff;border-radius:12px;padding:12px 18px;margin:0 0 22px;"">
      <tr><td><table width=""100%"" cellpadding=""0"" cellspacing=""0"">{filasModelos}</table></td></tr>
    </table>
    <div style=""display:flex;align-items:center;gap:10px;background:#eef2ff;border-radius:10px;padding:14px 18px;"">
      <span style=""font-size:22px;"">&#128196;</span>
      <span style=""font-size:14px;color:#1e1b4b;font-weight:600;word-break:break-all;"">{lblArch}: {HttpUtility.HtmlEncode(nombreArchivo)}</span>
    </div>
    {bloqueDe}
  </td></tr>
  <tr><td style=""background:#f8f9ff;padding:20px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #e5e7eb;"">
    &copy; Bufins &mdash; Business Finance Always Everywhere
  </td></tr>
 </table>
 </td></tr>
</table>
</body></html>";
        }

        private static string ConstruirEmailBienvenidaHtml(string nombreCompleto, string nombreUsuario,
            string empresa, string telefono, string correo, string enlaceLogin, bool esIngles, ModoCorreoCuenta modo)
        {
            string saludo = esIngles
                ? $"Hello{(string.IsNullOrEmpty(nombreCompleto) ? "" : " " + nombreCompleto)},"
                : $"Hola{(string.IsNullOrEmpty(nombreCompleto) ? "" : " " + nombreCompleto)},";
            string cuerpo;
            switch (modo)
            {
                case ModoCorreoCuenta.Actualizacion:
                    cuerpo = esIngles
                        ? "An administrator updated your Bufins account information. Here are your current account details:"
                        : "Un administrador actualiz&oacute; la informaci&oacute;n de tu cuenta en Bufins. Estos son tus datos de cuenta actuales:";
                    break;
                case ModoCorreoCuenta.Reenvio:
                    cuerpo = esIngles
                        ? "As requested, here is your account information on Bufins:"
                        : "Como lo solicitaste, aqu&iacute; tienes de nuevo la informaci&oacute;n de tu cuenta en Bufins:";
                    break;
                default:
                    cuerpo = esIngles
                        ? "An account was created for you on Bufins. Here are your account details:"
                        : "Se cre&oacute; una cuenta para ti en Bufins. Estos son los datos de tu cuenta:";
                    break;
            }
            string lblEmpresa  = esIngles ? "Company" : "Empresa";
            string lblNombre   = esIngles ? "Name" : "Nombre";
            string lblCorreo   = esIngles ? "Email" : "Correo electr&oacute;nico";
            string lblCelular  = esIngles ? "Mobile phone" : "N&uacute;mero de celular";
            string lblUsuario  = esIngles ? "Username" : "Nombre de usuario";
            string valorVacio  = esIngles ? "Not provided" : "No registrado";
            string btnTxt      = esIngles ? "Go to login" : "Ir al inicio de sesi&oacute;n";
            string leyenda     = esIngles
                ? "If you want to <strong>recover</strong> your password, you can do so from the login screen (\"Forgot your password?\"). To <strong>change</strong> it, do so from the User Manager."
                : "Si deseas <strong>recuperar</strong> tu contrase&ntilde;a, puedes hacerlo desde la pantalla de inicio de sesi&oacute;n (\"&iquest;Olvidaste tu contrase&ntilde;a?\"). Para <strong>cambiarla</strong>, hazlo desde el Gestor de Usuarios.";

            string Fila(string etiqueta, string valor) => $@"
    <tr>
      <td style=""padding:8px 0;font-size:13px;color:#9ca3af;width:40%;"">{etiqueta}</td>
      <td style=""padding:8px 0;font-size:14px;color:#1e1b4b;font-weight:600;"">{HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(valor) ? valorVacio : valor)}</td>
    </tr>";

            return $@"<!DOCTYPE html>
<html lang=""{(esIngles ? "en" : "es")}"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f0f2f8;font-family:'Segoe UI',Arial,sans-serif;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f0f2f8;padding:32px 16px;"">
 <tr><td align=""center"">
 <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#fff;border-radius:16px;overflow:hidden;"">
  <tr><td style=""background:linear-gradient(135deg,#1e1b4b,#312e81,#4338ca);padding:36px 40px 28px;text-align:center;"">
    <div style=""font-size:28px;font-weight:900;color:#fff;letter-spacing:3px;"">bufins</div>
    <div style=""font-size:9px;color:rgba(255,255,255,0.6);letter-spacing:2px;text-transform:uppercase;"">Business Finance Always Everywhere</div>
  </td></tr>
  <tr><td style=""padding:40px;"">
    <p style=""font-size:18px;font-weight:600;color:#1e1b4b;margin:0 0 12px;"">{saludo}</p>
    <p style=""font-size:15px;color:#4b5563;line-height:1.7;margin:0 0 20px;"">{cuerpo}</p>
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f8f9ff;border-radius:12px;padding:18px 20px;margin:0 0 24px;"">
      <tr><td>
        <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;"">
          {Fila(lblEmpresa, empresa)}
          {Fila(lblNombre, nombreCompleto)}
          {Fila(lblCorreo, correo)}
          {Fila(lblCelular, telefono)}
          {Fila(lblUsuario, nombreUsuario)}
        </table>
      </td></tr>
    </table>
    <table width=""100%"" cellpadding=""0"" cellspacing=""0""><tr><td align=""center"" style=""padding:0 0 24px;"">
      <a href=""{enlaceLogin}"" style=""display:inline-block;background:linear-gradient(135deg,#4338ca,#6d28d9);color:#fff;text-decoration:none;padding:16px 40px;border-radius:50px;font-size:15px;font-weight:700;"">{btnTxt}</a>
    </td></tr></table>
    <div style=""background:#f8f9ff;border-left:4px solid #6366f1;padding:14px 18px;font-size:13px;color:#4b5563;line-height:1.6;margin:16px 0;"">{leyenda}</div>
  </td></tr>
  <tr><td style=""background:#f8f9ff;padding:20px;text-align:center;font-size:12px;color:#9ca3af;border-top:1px solid #e5e7eb;"">
    &copy; Bufins &mdash; Business Finance Always Everywhere
  </td></tr>
 </table>
 </td></tr>
</table>
</body></html>";
        }
    }
}
