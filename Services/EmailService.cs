using bufinscustomers.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Configuration;
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
