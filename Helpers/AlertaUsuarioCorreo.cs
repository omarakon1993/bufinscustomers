using System;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Aviso de seguridad por correo AL PROPIO USUARIO (alta/baja de passkey, doble factor activado o
    /// reiniciado por un administrador…). En segundo plano y sin lanzar: nunca bloquea la operación.
    /// El texto llega ya traducido desde el controlador.
    /// </summary>
    public static class AlertaUsuarioCorreo
    {
        public static void Enviar(string destinatario, string titulo, string mensaje, string enlace = null)
        {
            if (string.IsNullOrWhiteSpace(destinatario)) return;
            string asunto = "[Bufins] " + titulo;
            System.Web.Hosting.HostingEnvironment.QueueBackgroundWorkItem(ct =>
            {
                try { new EmailService().EnviarAlertaSeguridad(destinatario.Trim(), asunto, titulo, mensaje, enlace); }
                catch (Exception ex) { AppLogger.Warn("No se pudo enviar el aviso de seguridad al usuario: " + ex.Message, "AlertaUsuarioCorreo"); }
            });
        }
    }
}
