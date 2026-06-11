using bufinscustomers.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Configuration;
using System.Text;

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
    }
}
