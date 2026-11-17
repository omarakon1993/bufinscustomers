using System;
using System.Data;
using System.Linq;
using bufinscustomers.Models;
using bufinscustomers.Helpers;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    // DobleFactor: parte de AccesoController (misma clase; solo se separa el codigo por responsabilidad).
    public partial class AccesoController
    {
        // ====== Doble factor (TOTP) ======
        // Tras validar usuario+clave se guarda un "pendiente 2FA" en caché (5 min, cookie _2fa HttpOnly)
        // y NO existe sesión hasta que el código es correcto. Mismo patrón que el token _lt.
        private const string COOKIE_2FA = "_2fa";

        private const int MAX_INTENTOS_2FA = 5;

        private class PendienteDobleFactor
        {
            public int IdUsuario;
            public bool Enrolar;                 // true = aún no tiene autenticador (alta obligatoria)
            public int Intentos;
            public byte[] SecretoNuevo;          // solo durante el alta; se guarda en BD al confirmar
            public string ChallengePasskey;      // reto WebAuthn vigente (un solo uso)
            public bool Oferta;                  // recién activó la app: se le ofrece agregar una passkey
        }

        private ActionResult IniciarDobleFactor(Usuarios usuario, bool yaActivo, string ip)
        {
            string token = Guid.NewGuid().ToString("N");
            HttpRuntime.Cache.Insert(
                "_2fa_" + token, new PendienteDobleFactor { IdUsuario = usuario.Id, Enrolar = !yaActivo }, null,
                DateTime.Now.AddMinutes(5), Cache.NoSlidingExpiration);
            Session.Abandon();
            Response.Cookies.Add(new HttpCookie(COOKIE_2FA, token)
                { HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
            return RedirectToAction(yaActivo ? "VerificarDobleFactor" : "ConfigurarDobleFactor");
        }

        private PendienteDobleFactor ObtenerPendiente2FA(out string cacheKey)
        {
            cacheKey = null;
            var c = Request.Cookies[COOKIE_2FA];
            if (c == null || string.IsNullOrEmpty(c)) return null;
            cacheKey = "_2fa_" + c;
            return HttpRuntime.Cache[cacheKey] as PendienteDobleFactor;
        }

        private void LimpiarPendiente2FA(string cacheKey)
        {
            if (cacheKey != null) HttpRuntime.Cache.Remove(cacheKey);
            Response.Cookies.Add(new HttpCookie(COOKIE_2FA)
                { Expires = DateTime.Now.AddDays(-1), HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
        }

        [HttpGet]
        public ActionResult VerificarDobleFactor()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || p.Enrolar) return RedirectToAction("Login");
            ViewBag.TienePasskey = TienePasskeyDisponible(p.IdUsuario);
            return View("~/Views/Acceso/VerificarDobleFactor.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VerificarDobleFactor(string codigo, bool agregarPasskey = false)
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || p.Enrolar)
                return RedirectToAction("Login", new { tokenExpirado = true });

            string ip = GetClientIp();
            var estadoIp = rateLimitLoginService.Comprobar(ip);
            if (estadoIp.Bloqueada)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = string.Format(R("Login_ErrorRateLimitIP"), Math.Max(1, estadoIp.MinutosRestantes));
                return RedirectToAction("Login");
            }

            var servicio = dobleFactorService;
            var estado = servicio.Obtener(p.IdUsuario);
            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            if (!estado.Disponible || !estado.Activo || estado.Secreto == null || usuario == null)
            {
                LimpiarPendiente2FA(key);
                return RedirectToAction("Login");
            }

            string limpio = (codigo ?? "").Replace(" ", "").Trim();
            bool ok = false;
            if (limpio.Length == TotpHelper.Digitos && limpio.All(char.IsDigit))
            {
                long paso = TotpHelper.Validar(estado.Secreto, limpio, estado.UltimoPaso);
                ok = paso >= 0 && servicio.RegistrarPaso(p.IdUsuario, paso);
            }

            if (ok)
            {
                LimpiarPendiente2FA(key);
                // «Agregar una passkey después de verificar»: igual que en OfrecerPasskey, abre Seguridad al entrar.
                if (agregarPasskey && DobleFactorService.PasskeysHabilitadas)
                    Response.Cookies.Add(new HttpCookie("_pkoferta", "1")
                        { HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/", Expires = DateTime.Now.AddMinutes(2) });
                return CompletarInicioDeSesion(usuario);
            }

            // Código incorrecto
            p.Intentos++;
            var rl = rateLimitLoginService.RegistrarFallo(ip);
            if (rl.ModoDefensivoActivado) NotificarModoDefensivo();
            auditoriaService.RegistrarSeguridad(AuditoriaAccion.LoginFallido,
                $"Código de doble factor incorrecto ({usuario.Correo}), intento {p.Intentos} de {MAX_INTENTOS_2FA}",
                usuario.Id, NombreCompleto(usuario), usuario.IdEmpresa);

            if (p.Intentos >= MAX_INTENTOS_2FA)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = R("Login2FA_ErrorDemasiadosIntentos");
                return RedirectToAction("Login");
            }

            ViewData["Error"] = R("Login2FA_ErrorCodigo");
            ViewBag.TienePasskey = TienePasskeyDisponible(p.IdUsuario);
            return View("~/Views/Acceso/VerificarDobleFactor.cshtml");
        }

        private bool TienePasskeyDisponible(int idUsuario)
        {
            return DobleFactorService.PasskeysHabilitadas && passkeyService.Listar(idUsuario).Count > 0;
        }

        // ── Passkey (WebAuthn) como alternativa en el segundo paso. La app autenticadora sigue siendo la principal. ──

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PasskeyOpciones()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || p.Enrolar || !DobleFactorService.PasskeysHabilitadas)
                return Json(new { ok = false, expirado = true });

            var creds = passkeyService.Listar(p.IdUsuario);
            if (creds.Count == 0) return Json(new { ok = false });

            p.ChallengePasskey = WebAuthnHelper.NuevoReto();
            return Json(new
            {
                ok = true,
                challenge = p.ChallengePasskey,
                rpId = DobleFactorService.RpId(Request),
                timeout = 60000,
                userVerification = "required",
                allowCredentials = creds.Select(c => new { type = "public-key", id = WebAuthnHelper.ABase64Url(c.CredentialId) })
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult PasskeyVerificar(string credentialId, string clientDataJSON, string authenticatorData, string signature)
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || p.Enrolar || !DobleFactorService.PasskeysHabilitadas)
                return Json(new { ok = false, expirado = true, redirect = Url.Action("Login", "Acceso") });

            string ip = GetClientIp();
            var estadoIp = rateLimitLoginService.Comprobar(ip);
            if (estadoIp.Bloqueada)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = string.Format(R("Login_ErrorRateLimitIP"), Math.Max(1, estadoIp.MinutosRestantes));
                return Json(new { ok = false, expirado = true, redirect = Url.Action("Login", "Acceso") });
            }

            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            string reto = p.ChallengePasskey;
            p.ChallengePasskey = null;   // un solo uso

            bool ok = false;
            PasskeyInfo cred = null;
            WebAuthnHelper.ResultadoAutenticacion res = null;
            if (usuario != null && !string.IsNullOrEmpty(reto))
            {
                try
                {
                    byte[] id = WebAuthnHelper.DeBase64Url(credentialId);
                    cred = passkeyService.Listar(p.IdUsuario).FirstOrDefault(c => c.CredentialId.SequenceEqual(id));
                    if (cred != null)
                    {
                        res = WebAuthnHelper.VerificarAutenticacion(
                            WebAuthnHelper.DeBase64Url(clientDataJSON), WebAuthnHelper.DeBase64Url(authenticatorData),
                            WebAuthnHelper.DeBase64Url(signature), reto, DobleFactorService.Origen(Request),
                            DobleFactorService.RpId(Request), cred.ClavePublica, cred.Algoritmo);
                        ok = res.Ok;
                    }
                }
                catch (FormatException) { ok = false; }
            }

            if (ok)
            {
                // El contador debería crecer; algunas passkeys sincronizadas siempre envían 0. Solo se avisa, no se bloquea.
                if ((cred.Contador > 0 || res.Contador > 0) && res.Contador <= cred.Contador)
                    Helpers.AppLogger.Warn($"Passkey con contador no creciente (usuarioId={usuario.Id}, guardado={cred.Contador}, recibido={res.Contador}).", "Acceso.Passkey");
                passkeyService.RegistrarUso(cred.Id, Math.Max(res.Contador, cred.Contador));
                LimpiarPendiente2FA(key);
                auditoriaService.RegistrarSeguridad(AuditoriaAccion.Login,
                    $"Segundo factor validado con passkey ({cred.Nombre})", usuario.Id, NombreCompleto(usuario), usuario.IdEmpresa);
                CompletarInicioDeSesion(usuario);   // deja listo el token _lt; el navegador sigue a FinalizarLogin
                return Json(new { ok = true, redirect = Url.Action("FinalizarLogin", "Acceso") });
            }

            p.Intentos++;
            var rl = rateLimitLoginService.RegistrarFallo(ip);
            if (rl.ModoDefensivoActivado) NotificarModoDefensivo();
            auditoriaService.RegistrarSeguridad(AuditoriaAccion.LoginFallido,
                $"Passkey rechazada ({usuario?.Correo}), intento {p.Intentos} de {MAX_INTENTOS_2FA}",
                usuario?.Id, usuario == null ? null : NombreCompleto(usuario), usuario?.IdEmpresa);

            if (p.Intentos >= MAX_INTENTOS_2FA)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = R("Login2FA_ErrorDemasiadosIntentos");
                return Json(new { ok = false, redirect = Url.Action("Login", "Acceso") });
            }
            return Json(new { ok = false, mensaje = R("Login2FA_ErrorPasskey") });
        }

        [HttpGet]
        public ActionResult ConfigurarDobleFactor()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || !p.Enrolar) return RedirectToAction("Login");
            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            if (usuario == null) return RedirectToAction("Login");

            if (p.SecretoNuevo == null) p.SecretoNuevo = TotpHelper.GenerarSecreto();
            string secreto = TotpHelper.ABase32(p.SecretoNuevo);
            ViewBag.SecretoBase32 = secreto;
            ViewBag.UriOtp = TotpHelper.UriOtpAuth("Bufins", usuario.Correo ?? usuario.Usuario, secreto);
            return View("~/Views/Acceso/ConfigurarDobleFactor.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfigurarDobleFactor(string codigo)
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || !p.Enrolar || p.SecretoNuevo == null)
                return RedirectToAction("Login", new { tokenExpirado = true });

            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            if (usuario == null) { LimpiarPendiente2FA(key); return RedirectToAction("Login"); }

            string ip = GetClientIp();
            var estadoIp = rateLimitLoginService.Comprobar(ip);
            if (estadoIp.Bloqueada)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = string.Format(R("Login_ErrorRateLimitIP"), Math.Max(1, estadoIp.MinutosRestantes));
                return RedirectToAction("Login");
            }

            long paso = TotpHelper.Validar(p.SecretoNuevo, codigo);
            if (paso < 0)
            {
                p.Intentos++;
                var rl = rateLimitLoginService.RegistrarFallo(ip);
                if (rl.ModoDefensivoActivado) NotificarModoDefensivo();
                if (p.Intentos >= MAX_INTENTOS_2FA)
                {
                    LimpiarPendiente2FA(key);
                    TempData["ErrorMessage"] = R("Login2FA_ErrorDemasiadosIntentos");
                    return RedirectToAction("Login");
                }
                ViewData["Error"] = R("Login2FA_ErrorCodigo");
                string secreto = TotpHelper.ABase32(p.SecretoNuevo);
                ViewBag.SecretoBase32 = secreto;
                ViewBag.UriOtp = TotpHelper.UriOtpAuth("Bufins", usuario.Correo ?? usuario.Usuario, secreto);
                return View("~/Views/Acceso/ConfigurarDobleFactor.cshtml");
            }

            var servicio = dobleFactorService;
            if (!servicio.Activar(p.IdUsuario, p.SecretoNuevo, paso))
            {
                ViewData["Error"] = R("Login2FA_ErrorGuardar");
                string secreto = TotpHelper.ABase32(p.SecretoNuevo);
                ViewBag.SecretoBase32 = secreto;
                ViewBag.UriOtp = TotpHelper.UriOtpAuth("Bufins", usuario.Correo ?? usuario.Usuario, secreto);
                return View("~/Views/Acceso/ConfigurarDobleFactor.cshtml");
            }

            auditoriaService.RegistrarSeguridad(AuditoriaAccion.Editar,
                "Doble factor de autenticación activado",
                usuario.Id, NombreCompleto(usuario), usuario.IdEmpresa);
            string tituloAct = R("Notif_2FA_ActivadoTitulo"), msgAct = R("Notif_2FA_ActivadoMsg");
            notificacionesService.Crear(usuario.Id, tituloAct, msgAct, "success");
            AlertaUsuarioCorreo.Enviar(usuario.Correo, tituloAct, msgAct);

            // Con passkeys habilitadas se le ofrece agregar una (momento ideal: ya está configurando su seguridad).
            if (DobleFactorService.PasskeysHabilitadas && passkeyService.TablaDisponible())
            {
                p.Enrolar = false;
                p.SecretoNuevo = null;
                p.Oferta = true;
                HttpRuntime.Cache.Insert(key, p, null, DateTime.Now.AddMinutes(10),
                    Cache.NoSlidingExpiration);
                return RedirectToAction("OfrecerPasskey");
            }

            LimpiarPendiente2FA(key);
            return CompletarInicioDeSesion(usuario);
        }

        [HttpGet]
        public ActionResult OfrecerPasskey()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || !p.Oferta) return RedirectToAction("Login");
            return View("~/Views/Acceso/OfrecerPasskey.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult OfrecerPasskey(string accion)
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || !p.Oferta) return RedirectToAction("Login");
            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            LimpiarPendiente2FA(key);
            if (usuario == null) return RedirectToAction("Login");

            if (accion == "agregar")
                Response.Cookies.Add(new HttpCookie("_pkoferta", "1")
                    { HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/", Expires = DateTime.Now.AddMinutes(2) });
            else
                passkeyService.PosponerAviso(usuario.Id, 7);   // "Ahora no": la franja del Home espera una semana

            return CompletarInicioDeSesion(usuario);
        }
    }
}
