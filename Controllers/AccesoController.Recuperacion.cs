using System;
using bufinscustomers.Models;
using System.Text.RegularExpressions;
using bufinscustomers.Helpers;

namespace bufinscustomers.Controllers
{
    // Recuperacion: parte de AccesoController (misma clase; solo se separa el codigo por responsabilidad).
    public partial class AccesoController
    {
        // ── Solicitud de reinicio de métodos de autenticación (el usuario perdió su app y sus passkeys) ──
        // Solo se ofrece desde la pantalla de verificación del 2FA: quien llega ahí ya validó su contraseña,
        // así que no sirve para enumerar usuarios ni para enviar spam sin credenciales.
        [HttpGet]
        public ActionResult SolicitarReinicio()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || p.Enrolar) return RedirectToAction("Login");
            return View("~/Views/Acceso/SolicitarReinicio.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SolicitarReinicio(string mensaje, string telefono)
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || p.Enrolar) return RedirectToAction("Login", new { tokenExpirado = true });

            string ip = GetClientIp();
            var estadoIp = rateLimitLoginService.Comprobar(ip);
            if (estadoIp.Bloqueada)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = string.Format(R("Login_ErrorRateLimitIP"), Math.Max(1, estadoIp.MinutosRestantes));
                return RedirectToAction("Login");
            }

            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            if (usuario == null) { LimpiarPendiente2FA(key); return RedirectToAction("Login"); }

            mensaje = (mensaje ?? "").Trim();
            telefono = (telefono ?? "").Trim();
            if (mensaje.Length > 500) mensaje = mensaje.Substring(0, 500);
            if (telefono.Length > 40 || !Regex.IsMatch(telefono, @"^[0-9+()\-\s.]*$"))
            {
                ViewData["Error"] = R("SolSeg_ErrorTelefono");
                return View("~/Views/Acceso/SolicitarReinicio.cshtml");
            }

            var resultado = solicitudReinicioAuthService.Crear(usuario.Id, mensaje, telefono, ip, Request.UserAgent);
            if (!resultado.Disponible)
            {
                ViewData["Error"] = R("SolSeg_NoDisponible");
                return View("~/Views/Acceso/SolicitarReinicio.cshtml");
            }

            if (resultado.Creada)
            {
                string nombre = NombreCompleto(usuario);
                auditoriaService.RegistrarSeguridad(AuditoriaAccion.Crear,
                    "Solicitud de reinicio de métodos de autenticación enviada por el usuario",
                    usuario.Id, nombre, usuario.IdEmpresa);

                // Aviso a TODOS los Super Admin: notificación interna + correo a su correo registrado.
                string titulo = R("Notif_SolSeg_NuevaTitulo");
                string msg = string.Format(R("Notif_SolSeg_NuevaMsg"), string.IsNullOrWhiteSpace(nombre) ? usuario.Usuario : nombre, usuario.Correo);
                string enlace = null;
                try { enlace = Url.Action("Index", "SolicitudesSeguridad", null, Request.Url.Scheme); } catch { }
                var notif = notificacionesService;
                foreach (var admin in solicitudReinicioAuthService.ObtenerSuperAdminsConCorreo())
                {
                    notif.Crear(admin.Key, titulo, msg, "warning", "/SolicitudesSeguridad");
                    AlertaUsuarioCorreo.Enviar(admin.Value, titulo, msg, enlace);
                }
            }

            LimpiarPendiente2FA(key);   // sin métodos no puede continuar; vuelve a entrar cuando se los reinicien
            ViewBag.Enviada = true;
            ViewBag.Existente = !resultado.Creada;
            return View("~/Views/Acceso/SolicitarReinicio.cshtml");
        }

        // ====== RECUPERACIÓN DE CONTRASEÑA ======

        [HttpGet]
        public ActionResult SolicitarReset()
        {
            return View("~/Views/Acceso/SolicitarReset.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SolicitarReset(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo) || !EsCorreoValido(correo.Trim()))
            {
                ViewData["Error"] = R("Reset_ErrorEmail");
                return View("~/Views/Acceso/SolicitarReset.cshtml");
            }

            correo = correo.Trim().ToLower();

            try
            {
                // Buscar usuario — siempre mostrar mensaje de éxito (anti-enumeración)
                int usuarioId = 0;
                string nombreUsuario = null;
                string correoReal = null;

                Bd.LeerPrimera(CadenaConexion, "SELECT TOP 1 Id, Nombre, Correo FROM Usuarios WHERE LOWER(Correo) = @Correo",
                    new { Correo = correo },
                    r =>
                    {
                        usuarioId    = Convert.ToInt32(r["Id"]);
                        nombreUsuario = r["Nombre"].ToString();
                        correoReal   = r["Correo"].ToString();
                    });

                if (usuarioId > 0)
                {
                    // Generar token seguro (32 bytes aleatorios, base64url)
                    var bytes = new byte[32];
                    using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                        rng.GetBytes(bytes);
                    string token = Convert.ToBase64String(bytes)
                        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
                    DateTime expiry = DateTime.Now.AddHours(1);

                    // Guardar token y expiración en BD
                    Bd.Ejecutar(CadenaConexion, "UPDATE Usuarios SET ResetToken = @Token, ResetTokenExpiry = @Expiry WHERE Id = @Id",
                        new { Token = token, Expiry = expiry, Id = usuarioId });

                    // Construir enlace y enviar correo
                    string enlace   = Url.Action("RestablecerClave", "Acceso",
                        new { token = token }, Request.Url.Scheme);
                    bool esIngles   = System.Threading.Thread.CurrentThread.CurrentUICulture
                                          .TwoLetterISOLanguageName == "en";
                    emailService.EnviarRecuperacionClave(
                        correoReal, nombreUsuario, enlace, esIngles);
                }
            }
            catch (Exception ex)
            {
                // Log error internamente sin exponerlo al cliente (anti-enumeración)
                System.Diagnostics.Trace.TraceError("[SolicitarReset] {0}", MensajeError(ex));
            }

            // Siempre mostrar pantalla de éxito independientemente del resultado (anti-enumeración)
            ViewData["Enviado"] = true;
            return View("~/Views/Acceso/SolicitarReset.cshtml");
        }

        [HttpGet]
        public ActionResult RestablecerClave(string token)
        {
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Login");

            // Verificar que el token existe y no expiró
            bool valido = false;
            valido = Bd.Escalar<int>(CadenaConexion, @"SELECT COUNT(1) FROM Usuarios
                      WHERE ResetToken = @Token AND ResetTokenExpiry > GETDATE()",
                new { Token = token }) > 0;

            if (!valido)
            {
                ViewData["TokenInvalido"] = true;
                return View("~/Views/Acceso/RestablecerClave.cshtml");
            }

            ViewData["Token"] = token;
            return View("~/Views/Acceso/RestablecerClave.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RestablecerClave(string token, string nuevaClave, string confirmarClave)
        {
            if (string.IsNullOrEmpty(token))
                return RedirectToAction("Login");

            // Validaciones cliente se repiten en servidor
            if (string.IsNullOrWhiteSpace(nuevaClave))
            {
                ViewData["Token"] = token;
                ViewData["Error"] = R("Reset_NewPwd_ErrorEmpty");
                return View("~/Views/Acceso/RestablecerClave.cshtml");
            }
            // A13: política de contraseñas centralizada (longitud mínima 12, complejidad,
            // claves comunes y comprobación contra filtraciones — Have I Been Pwned).
            if (!Helpers.PoliticaContrasena.Validar(nuevaClave.Trim(), null, out string errClave))
            {
                ViewData["Token"] = token;
                ViewData["Error"] = R(errClave);
                return View("~/Views/Acceso/RestablecerClave.cshtml");
            }
            if (nuevaClave != confirmarClave)
            {
                ViewData["Token"] = token;
                ViewData["Error"] = R("Reset_NewPwd_ErrorMatch");
                return View("~/Views/Acceso/RestablecerClave.cshtml");
            }

            // Buscar usuario por token válido
            int usuarioId = 0;
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"SELECT TOP 1 Id FROM Usuarios
                      WHERE ResetToken = @Token AND ResetTokenExpiry > GETDATE()", cn);
                cmd.Parameters.AddWithValue("@Token", token);
                cn.Open();
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    usuarioId = Convert.ToInt32(result);
            }

            if (usuarioId == 0)
            {
                ViewData["TokenInvalido"] = true;
                return View("~/Views/Acceso/RestablecerClave.cshtml");
            }

            // Actualizar contraseña y limpiar token
            string hash = HashearContrasena(nuevaClave.Trim());
            Bd.Ejecutar(CadenaConexion, @"UPDATE Usuarios
                      SET Clave = @Clave, ResetToken = NULL, ResetTokenExpiry = NULL,
                          IntentosFallidos = 0, BloqueadoHasta = NULL
                      WHERE Id = @Id",
                new { Clave = hash, Id = usuarioId });

            ViewData["Exito"] = true;
            return View("~/Views/Acceso/RestablecerClave.cshtml");
        }

        // ====== Cambio de contraseña obligatorio (primer inicio de sesión) ======
        [HttpGet]
        [bufinscustomers.Permisos.ValidarSesion]
        public ActionResult CambiarClaveInicial()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || !usuario.DebeCambiarClave)
                return RedirectToAction("Index", "Home");
            return View("~/Views/Acceso/CambiarClaveInicial.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [bufinscustomers.Permisos.ValidarSesion]
        public ActionResult CambiarClaveInicial(string nuevaClave, string confirmarClave)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || !usuario.DebeCambiarClave)
                return RedirectToAction("Index", "Home");

            nuevaClave = (nuevaClave ?? "").Trim();
            confirmarClave = (confirmarClave ?? "").Trim();

            string error = null;
            if (nuevaClave.Length == 0) error = R("Reset_NewPwd_ErrorEmpty");
            else if (nuevaClave != confirmarClave) error = R("Reset_NewPwd_ErrorMatch");
            else if (!Helpers.PoliticaContrasena.Validar(nuevaClave, usuario.Usuario, out string errClave)) error = R(errClave);
            else if (VerificarContrasena(nuevaClave, usuario.Clave)) error = R("CambioClaveInicial_ErrorIgual");

            if (error != null)
            {
                ViewData["Error"] = error;
                return View("~/Views/Acceso/CambiarClaveInicial.cshtml");
            }

            var servicio = claveInicialService;
            // Un solo hash: BCrypt da un resultado distinto en cada llamada y la sesión debe guardar el mismo que la base.
            string nuevoHash = HashearContrasena(nuevaClave);
            if (!servicio.GuardarClaveYQuitarMarca(usuario.Id, nuevoHash))
            {
                ViewData["Error"] = R("CambioClaveInicial_ErrorGuardar");
                return View("~/Views/Acceso/CambiarClaveInicial.cshtml");
            }

            usuario.DebeCambiarClave = false;
            usuario.Clave = nuevoHash;
            // El respaldo de la sesión (dbo.SesionesActivas) guarda la huella de la clave: se actualiza a la nueva.
            sesionPersistenteService.Registrar(Session.SessionID, usuario.Id, nuevoHash,
                (UsuarioSesionHelper.ObtenerInfoSesion()?.TiempoLogin) ?? DateTime.Now);
            auditoriaService.RegistrarSeguridad(AuditoriaAccion.Editar,
                "Cambio de contraseña obligatorio completado en el primer inicio de sesión",
                usuario.Id, ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim(), usuario.IdEmpresa);

            SetSuccessMessage(R("CambioClaveInicial_Exito"));
            return RedirectToAction("Index", "Home");
        }
    }
}
