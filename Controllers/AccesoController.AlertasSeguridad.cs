using System;
using System.Data;
using System.Linq;
using bufinscustomers.Helpers;

namespace bufinscustomers.Controllers
{
    // AlertasSeguridad: parte de AccesoController (misma clase; solo se separa el codigo por responsabilidad).
    public partial class AccesoController
    {
        // A12: control de ruido en la auditoría de seguridad.
        private const int AUDIT_THROTTLE_MIN = 10;              // 1 fila por IP cada N min (identificador inexistente)

        private const int AUDIT_SEGURIDAD_RETENCION_DIAS = 90;  // se purgan los intentos anónimos más antiguos

        private static DateTime _ultimaPurgaAuditoria = DateTime.MinValue;

        private static readonly object _purgaAuditoriaLock = new object();

        /// <summary>
        /// A14: avisa a los Super Admin de que el rate-limit de login entró en MODO DEFENSIVO
        /// (pico global de intentos fallidos). Se llama como mucho una vez cada 30 min — el
        /// servicio ya lo controla con la bandera <c>ModoDefensivoActivado</c>. Nunca lanza.
        /// </summary>
        private void NotificarModoDefensivo()
        {
            try
            {
                Helpers.AppLogger.Warn(
                    "Rate-limit de login en MODO DEFENSIVO: pico global de intentos fallidos, umbral por IP endurecido.",
                    "Acceso.Login");

                var notif = notificacionesService;
                string titulo = R("Notif_ModoDefensivoTitulo");
                string msg = R("Notif_ModoDefensivoMsg");
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    notif.Crear(idAdmin, titulo, msg, "error", "/Auditoria");

                EnviarAlertaSeguridadPorCorreo(titulo, msg);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[NotificarModoDefensivo] {0}", MensajeError(ex)); }
        }

        /// <summary>
        /// Envía por correo (en segundo plano) una alerta de seguridad a los destinatarios de
        /// <c>appSettings["SeguridadAlertasDestino"]</c> — cada token separado por comas es un
        /// correo (si contiene "@") o un nombre de usuario que se resuelve a su correo. Vacío =
        /// no se envía correo (solo la notificación interna). Fire-and-forget: nunca bloquea el login.
        /// </summary>
        private void EnviarAlertaSeguridadPorCorreo(string titulo, string mensaje)
        {
            var correos = ObtenerCorreosAlertaSeguridad();
            if (correos.Count == 0) return;

            string enlace = null;
            try { enlace = Url.Action("Index", "Auditoria", null, Request.Url.Scheme); } catch { }
            string asunto = "[Bufins] " + titulo;

            HostingEnvironment.QueueBackgroundWorkItem(ct =>
            {
                try
                {
                    var email = emailService;
                    foreach (var c in correos)
                    {
                        try { email.EnviarAlertaSeguridad(c, asunto, titulo, mensaje, enlace); }
                        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[EnviarAlertaSeguridad] {0}: {1}", c, MensajeError(ex)); }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[EnviarAlertaSeguridad] {0}", MensajeError(ex)); }
            });
        }

        private System.Collections.Generic.List<string> ObtenerCorreosAlertaSeguridad()
        {
            var lista = new System.Collections.Generic.List<string>();
            string destino = bufinscustomers.Compat.ConfigurationManager.AppSettings["SeguridadAlertasDestino"];
            if (string.IsNullOrWhiteSpace(destino)) return lista;

            var usuarios = new System.Collections.Generic.List<string>();
            foreach (var raw in destino.Split(','))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (t.Contains('@')) { if (!lista.Contains(t)) lista.Add(t); }
                else if (!usuarios.Contains(t)) usuarios.Add(t);
            }

            if (usuarios.Count > 0)
            {
                try
                {
                    var pars = usuarios.Select((u, i) => "@u" + i).ToArray();
                    using (var cn = new SqlConnection(CadenaConexion))
                    {
                        var cmd = new SqlCommand(
                            "SELECT Correo FROM Usuarios WHERE Usuario IN (" + string.Join(",", pars) +
                            ") AND Correo IS NOT NULL AND LTRIM(RTRIM(Correo)) <> ''", cn);
                        for (int i = 0; i < usuarios.Count; i++) cmd.Parameters.AddWithValue(pars[i], usuarios[i]);
                        cn.Open();
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                var c = r["Correo"].ToString().Trim();
                                if (c.Length > 0 && !lista.Contains(c)) lista.Add(c);
                            }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[ObtenerCorreosAlertaSeguridad] {0}", MensajeError(ex)); }
            }
            return lista;
        }

        // ====== A12: ruido / alertas de la auditoría de seguridad ======

        /// <summary>
        /// True si se debe escribir una fila en <c>Auditoria</c> por un intento con identificador
        /// inexistente desde <paramref name="ip"/>. Limita a 1 fila por IP cada
        /// <see cref="AUDIT_THROTTLE_MIN"/> minutos (los repetidos solo van al log de aplicación).
        /// </summary>
        private static bool DebeAuditarFalloAnonimo(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return true;
            string key = "_afa_" + ip;
            if (HttpRuntime.Cache[key] != null) return false;
            HttpRuntime.Cache.Insert(key, 1, null,
                DateTime.Now.AddMinutes(AUDIT_THROTTLE_MIN), Cache.NoSlidingExpiration);
            return true;
        }

        /// <summary>Purga los intentos de seguridad anónimos antiguos, como mucho una vez al día.</summary>
        private void PurgarAuditoriaSeguridadOportunista()
        {
            if ((DateTime.Now - _ultimaPurgaAuditoria).TotalHours < 24) return;
            lock (_purgaAuditoriaLock)
            {
                if ((DateTime.Now - _ultimaPurgaAuditoria).TotalHours < 24) return;
                _ultimaPurgaAuditoria = DateTime.Now;
            }
            try { auditoriaService.PurgarSeguridadAnonimaAntigua(AUDIT_SEGURIDAD_RETENCION_DIAS); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[PurgarAuditoriaSeguridad] {0}", MensajeError(ex)); }
        }

        /// <summary>
        /// Avisa de que una cuenta REAL acaba de bloquearse por intentos fallidos: log de
        /// aplicación + notificación interna al propio usuario y a cada Super Admin. Nunca lanza.
        /// </summary>
        private void NotificarBloqueoCuenta(int usuarioId, string identificador, int intentos)
        {
            try
            {
                Helpers.AppLogger.Warn(
                    $"Cuenta bloqueada por {intentos} intentos fallidos: usuarioId={usuarioId} ({identificador})",
                    "Acceso.Login");

                var notif = notificacionesService;
                notif.Crear(usuarioId, R("Notif_CuentaBloqueadaTitulo"), R("Notif_CuentaBloqueadaMsg"), "warning");

                string msgAdmin = string.Format(R("Notif_CuentaBloqueadaAdminMsg"), identificador, intentos);
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    if (idAdmin != usuarioId)
                        notif.Crear(idAdmin, R("Notif_CuentaBloqueadaAdminTitulo"), msgAdmin, "error", "/Auditoria");
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[NotificarBloqueoCuenta] {0}", MensajeError(ex)); }
        }

        private System.Collections.Generic.List<int> ObtenerIdsSuperAdmin()
        {
            var ids = new System.Collections.Generic.List<int>();
            try
            {
                Bd.Leer(CadenaConexion, "SELECT Id FROM Usuarios WHERE Admin = 2", r => ids.Add(Convert.ToInt32(r["Id"])));
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[ObtenerIdsSuperAdmin] {0}", MensajeError(ex)); }
            return ids;
        }
    }
}
