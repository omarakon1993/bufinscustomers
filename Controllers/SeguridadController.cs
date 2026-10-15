using System;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// "Seguridad" del propio usuario: estado de la app autenticadora y passkeys (WebAuthn). Solo opera
    /// sobre el usuario de la sesión. La app autenticadora es el método base obligatorio (no se puede
    /// quitar); las passkeys son un método adicional. Agregar o quitar una passkey exige reconfirmar con
    /// un código vigente de la app (así una sesión robada no puede registrar una passkey propia).
    /// No hay códigos de recuperación: si el usuario pierde sus métodos, un Super Admin reinicia su 2FA.
    /// </summary>
    [ValidarSesion]
    public class SeguridadController : BaseController
    {
        private const string SESION_RETO_REGISTRO = "PasskeyRegistroReto";
        private const string SESION_REAUTH_HASTA = "SegReauthHasta";
        private const string SESION_REAUTH_PASO = "SegReauthPaso";
        private readonly DobleFactorService _df = new DobleFactorService();
        private readonly PasskeyService _pk = new PasskeyService();

        private bool Aplica(Usuarios u) { return DobleFactorService.Habilitado && DobleFactorService.AplicaA(u); }

        private string Nombre(Usuarios u) { return ((u.Nombre ?? "") + " " + (u.Apellidos ?? "")).Trim(); }

        /// <summary>
        /// Reconfirmación para acciones sensibles: exige un código TOTP vigente. Devuelve null si es
        /// válido, o el mensaje de error. Los fallos suman al límite por IP y quedan en la auditoría.
        /// </summary>
        private string ValidarReconfirmacion(Usuarios u, string codigo)
        {
            // Tras una reconfirmación correcta se da una ventana corta: si la operación se interrumpe
            // (p. ej. se cancela el aviso de huella/PIN) se puede reintentar sin escribir otro código.
            if (Session[SESION_REAUTH_HASTA] is DateTime hasta && hasta > DateTime.Now) return null;

            string ip = ClientIpHelper.ObtenerIp(Request);
            var rl = new RateLimitLoginService();
            var estadoIp = rl.Comprobar(ip);
            if (estadoIp.Bloqueada)
                return string.Format(R("Login_ErrorRateLimitIP"), Math.Max(1, estadoIp.MinutosRestantes));

            // La app autenticadora debe estar configurada: sin ella no hay código que reconfirmar.
            var estado = _df.Obtener(u.Id);
            if (!estado.Disponible || !estado.Activo || estado.Secreto == null)
            {
                AppLogger.Warn($"Reconfirmación sin app autenticadora utilizable (usuarioId={u.Id}, disponible={estado.Disponible}, activo={estado.Activo}).", "Seguridad.Reconfirmar");
                return R("Seg_Err_SinTotp");
            }

            string limpio = (codigo ?? "").Replace(" ", "").Trim();
            if (limpio.Length != TotpHelper.Digitos || !limpio.All(char.IsDigit))
            {
                AppLogger.Warn($"Reconfirmación sin un código de 6 dígitos (usuarioId={u.Id}, longitud recibida={limpio.Length}).", "Seguridad.Reconfirmar");
                return R("Login2FA_ErrorVacio");
            }
            long? ultimoPasoSesion = Session[SESION_REAUTH_PASO] as long?;
            long paso = _df.ValidarCodigoReconfirmacion(u.Id, limpio, ultimoPasoSesion);
            if (paso < 0)
            {
                rl.RegistrarFallo(ip);
                new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.LoginFallido,
                    "Código de doble factor incorrecto al reconfirmar un cambio de seguridad",
                    u.Id, Nombre(u), u.IdEmpresa);
                return R("Login2FA_ErrorCodigo");
            }

            Session[SESION_REAUTH_PASO] = paso;
            Session[SESION_REAUTH_HASTA] = DateTime.Now.AddMinutes(3);
            return null;
        }

        public ActionResult Index(bool agregar = false)
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            if (u == null || !Aplica(u)) return RedirectToAction("Index", "Home");

            var estado = _df.Obtener(u.Id);
            ViewBag.TotpActivo = estado.Disponible && estado.Activo;
            ViewBag.PasskeysHabilitadas = DobleFactorService.PasskeysHabilitadas;
            ViewBag.PasskeysMax = DobleFactorService.PasskeysMaximo;
            ViewBag.AutoAgregar = agregar && DobleFactorService.PasskeysHabilitadas;
            return View("~/Views/Seguridad/Index.cshtml", _pk.Listar(u.Id));
        }

        // ───────── Invitación a configurar una passkey (franja del Home) ─────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PosponerAvisoPasskey(bool definitivo)
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            if (u == null) return Json(new { ok = false });
            bool ok = _pk.PosponerAviso(u.Id, definitivo ? 0 : 7);
            return Json(new { ok });
        }

        // ───────── Registro de una passkey ─────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PasskeyRegistroOpciones(string codigo)
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            if (u == null || !Aplica(u) || !DobleFactorService.PasskeysHabilitadas)
                return Json(new { ok = false, mensaje = R("Seg_Pk_NoDisponible") });

            string error = ValidarReconfirmacion(u, codigo);
            if (error != null) return Json(new { ok = false, mensaje = error });

            var existentes = _pk.Listar(u.Id);
            if (existentes.Count >= DobleFactorService.PasskeysMaximo)
                return Json(new { ok = false, mensaje = string.Format(R("Seg_Pk_Maximo"), DobleFactorService.PasskeysMaximo) });

            string reto = WebAuthnHelper.NuevoReto();
            Session[SESION_RETO_REGISTRO] = reto;
            return Json(new
            {
                ok = true,
                challenge = reto,
                rpName = "Bufins",
                rpId = DobleFactorService.RpId(Request),
                userId = WebAuthnHelper.ABase64Url(Encoding.UTF8.GetBytes("bufins-user-" + u.Id)),
                userName = u.Correo ?? u.Usuario,
                userDisplayName = Nombre(u),
                timeout = 60000,
                excludeCredentials = existentes.Select(c => new { type = "public-key", id = WebAuthnHelper.ABase64Url(c.CredentialId) })
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PasskeyRegistroCompletar(string clientDataJSON, string attestationObject, string nombre)
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            if (u == null || !Aplica(u) || !DobleFactorService.PasskeysHabilitadas)
                return Json(new { ok = false, mensaje = R("Seg_Pk_NoDisponible") });

            // El reto solo existe si antes se reconfirmó con el código de la app (PasskeyRegistroOpciones).
            string reto = Session[SESION_RETO_REGISTRO] as string;
            Session.Remove(SESION_RETO_REGISTRO);   // un solo uso
            if (string.IsNullOrEmpty(reto)) return Json(new { ok = false, mensaje = R("Seg_Pk_ErrorRegistro") });

            nombre = (nombre ?? "").Trim();
            if (nombre.Length == 0) nombre = R("Seg_Pk_NombreDefecto");
            if (nombre.Length > 100) nombre = nombre.Substring(0, 100);

            WebAuthnHelper.ResultadoRegistro res;
            try
            {
                res = WebAuthnHelper.VerificarRegistro(WebAuthnHelper.DeBase64Url(clientDataJSON),
                    WebAuthnHelper.DeBase64Url(attestationObject), reto, DobleFactorService.Origen(Request),
                    DobleFactorService.RpId(Request));
            }
            catch (FormatException) { res = new WebAuthnHelper.ResultadoRegistro { Error = "Datos inválidos." }; }

            if (!res.Ok)
            {
                AppLogger.Warn($"Registro de passkey rechazado (usuarioId={u.Id}): {res.Error}", "Seguridad.Passkey");
                return Json(new { ok = false, mensaje = R("Seg_Pk_ErrorRegistro") });
            }

            if (!_pk.Agregar(u.Id, res.CredentialId, res.ClavePublicaCose, res.Algoritmo, res.Contador, nombre))
                return Json(new { ok = false, mensaje = R("Seg_Pk_ErrorGuardar") });

            PasskeyAvisoHelper.Invalidar();
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Crear,
                $"Passkey agregada ({nombre})", u.Id, Nombre(u), u.IdEmpresa);
            string titulo = R("Notif_Pk_AgregadaTitulo"), msg = string.Format(R("Notif_Pk_AgregadaMsg"), nombre);
            new NotificacionesService().Crear(u.Id, titulo, msg, "success", "/Seguridad");
            AlertaUsuarioCorreo.Enviar(u.Correo, titulo, msg);
            return Json(new { ok = true, mensaje = R("Seg_Pk_Agregada") });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult EliminarPasskey(int id, string codigo)
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            if (u == null || !Aplica(u)) return Json(new { ok = false, mensaje = R("Seg_Pk_NoDisponible") });

            string error = ValidarReconfirmacion(u, codigo);
            if (error != null) return Json(new { ok = false, mensaje = error });

            var cred = _pk.Listar(u.Id).FirstOrDefault(c => c.Id == id);
            if (cred == null || !_pk.Eliminar(u.Id, id))
                return Json(new { ok = false, mensaje = R("Seg_Pk_ErrorEliminar") });

            PasskeyAvisoHelper.Invalidar();
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Eliminar,
                $"Passkey eliminada ({cred.Nombre})", u.Id, Nombre(u), u.IdEmpresa);
            string titulo = R("Notif_Pk_EliminadaTitulo"), msg = string.Format(R("Notif_Pk_EliminadaMsg"), cred.Nombre);
            new NotificacionesService().Crear(u.Id, titulo, msg, "warning", "/Seguridad");
            AlertaUsuarioCorreo.Enviar(u.Correo, titulo, msg);
            return Json(new { ok = true, mensaje = R("Seg_Pk_Eliminada") });
        }
    }
}
