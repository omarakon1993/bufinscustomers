using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Text;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class CuentasCorreoController : BaseController
    {
        private readonly CuentasCorreoService _svc = new CuentasCorreoService();

        public ActionResult Index()
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            return View("~/Views/Configuracion/CuentasCorreo.cshtml", _svc.ObtenerTodas());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(CuentaCorreo cuenta)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");
            try
            {
                if (!Valido(cuenta, requiereContrasena: true))
                {
                    SetErrorMessage(R("Correo_ErrorCamposReq"));
                    return RedirectToAction("Index");
                }
                cuenta.Activa = true;
                _svc.Crear(cuenta);
                SetSuccessMessage(R("Correo_CreadaOk"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Correo_ErrorCrear") + ": " + ex.Message);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(CuentaCorreo cuenta)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");
            try
            {
                if (!Valido(cuenta, requiereContrasena: false))
                {
                    SetErrorMessage(R("Correo_ErrorCamposReq"));
                    return RedirectToAction("Index");
                }
                _svc.Editar(cuenta);
                SetSuccessMessage(R("Correo_EditadaOk"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Correo_ErrorEditar") + ": " + ex.Message);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");
            try
            {
                _svc.Eliminar(id);
                SetSuccessMessage(R("Correo_EliminadaOk"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Correo_ErrorEliminar") + ": " + ex.Message);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EstablecerPredeterminada(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");
            try
            {
                _svc.EstablecerPredeterminada(id);
                SetSuccessMessage(R("Correo_PredeterminadaOk"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Correo_ErrorPredeterminada") + ": " + ex.Message);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ToggleActiva(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return Json(new { success = false, message = "Sin permisos" });
            try
            {
                _svc.ToggleActiva(id, out bool nuevaActiva);
                return Json(new { success = true, activa = nuevaActiva });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Prueba la conexión SMTP con los datos de una cuenta existente o con datos directos.
        /// Envía un correo de prueba a <correoDestino>.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ProbarConexion(int? id, string correoDestino,
            string host, int? puerto, bool? ssl, bool? ignorarCert,
            string usuario, string contrasena, string remitente, string nombreRemitente)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return Json(new { success = false, message = "Sin permisos" });

            try
            {
                // Si viene un id, usar los datos de BD (la contraseña no viaja por el form)
                if (id.HasValue && id.Value > 0)
                {
                    var cuenta = _svc.ObtenerPorId(id.Value);
                    if (cuenta == null) return Json(new { success = false, message = "Cuenta no encontrada" });
                    host            = cuenta.Host;
                    puerto          = cuenta.Puerto;
                    ssl             = cuenta.Ssl;
                    ignorarCert     = cuenta.IgnorarCertificado;
                    usuario         = cuenta.Usuario;
                    contrasena      = cuenta.Contrasena;
                    remitente       = cuenta.Remitente;
                    nombreRemitente = cuenta.NombreRemitente;
                }

                // Resolver valores con defaults seguros
                int  puertoVal      = puerto      ?? 465;
                bool sslVal         = ssl         ?? true;
                bool ignorarCertVal = ignorarCert ?? true;

                if (string.IsNullOrWhiteSpace(correoDestino))
                    correoDestino = remitente;

                // MailKit: soporta SSL implicito (465) y STARTTLS (587) correctamente
                SecureSocketOptions sslOpts;
                if (sslVal && puertoVal == 465) sslOpts = SecureSocketOptions.SslOnConnect;
                else if (sslVal)                sslOpts = SecureSocketOptions.StartTls;
                else                            sslOpts = SecureSocketOptions.None;

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(nombreRemitente, remitente));
                message.To.Add(MailboxAddress.Parse(correoDestino));
                message.Subject = "[Test] Conexion SMTP correcta - Bufins";
                var testBody = new TextPart("html");
                testBody.SetText(Encoding.UTF8, $@"<div style='font-family:Arial;padding:24px;background:#f0f2f8;'>
<div style='max-width:480px;margin:0 auto;background:#fff;border-radius:12px;padding:28px;'>
<h2 style='color:#059669;'>&#10003; Conexion SMTP exitosa</h2>
<p style='color:#4b5563;'>Cuenta <strong>{System.Web.HttpUtility.HtmlEncode(nombreRemitente)}</strong> configurada correctamente.</p>
<table style='font-size:13px;color:#6b7280;border-collapse:collapse;width:100%;'>
<tr><td style='padding:4px 8px;background:#f8f9ff;font-weight:600;'>Servidor</td><td style='padding:4px 8px;'>{System.Web.HttpUtility.HtmlEncode(host)}</td></tr>
<tr><td style='padding:4px 8px;background:#f8f9ff;font-weight:600;'>Puerto</td><td style='padding:4px 8px;'>{puertoVal}</td></tr>
<tr><td style='padding:4px 8px;background:#f8f9ff;font-weight:600;'>SSL</td><td style='padding:4px 8px;'>{sslOpts}</td></tr>
<tr><td style='padding:4px 8px;background:#f8f9ff;font-weight:600;'>Usuario</td><td style='padding:4px 8px;'>{System.Web.HttpUtility.HtmlEncode(usuario)}</td></tr>
</table></div></div>");
                message.Body = testBody;

                using (var client = new SmtpClient())
                {
                    if (ignorarCertVal)
                        client.ServerCertificateValidationCallback = (s, c, ch, e) => true;
                    client.Connect(host, puertoVal, sslOpts);
                    client.Authenticate(usuario, contrasena);
                    client.Send(message);
                    client.Disconnect(quit: true);
                }

                return Json(new { success = true, message = "Correo de prueba enviado a " + correoDestino });
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                if (ex.InnerException != null) err += " => " + ex.InnerException.Message;
                return Json(new { success = false, message = err });
            }
        }

        private static bool Valido(CuentaCorreo c, bool requiereContrasena) =>
            !string.IsNullOrWhiteSpace(c.Nombre)
            && !string.IsNullOrWhiteSpace(c.Host)
            && c.Puerto > 0
            && !string.IsNullOrWhiteSpace(c.Usuario)
            && !string.IsNullOrWhiteSpace(c.Remitente)
            && (!requiereContrasena || !string.IsNullOrWhiteSpace(c.Contrasena));
    }
}
