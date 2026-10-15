using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO;
using System.ComponentModel;
using bufinscustomers.Helpers;
using bufinscustomers.Services;
using Newtonsoft.Json;

namespace bufinscustomers.Controllers
{
    public class AccesoController : BaseController
    {
        // A11: hash BCrypt (cost 12) señuelo — vector de prueba público del algoritmo. Solo se
        // usa para gastar el mismo tiempo de CPU que una verificación real cuando el identificador
        // no existe, de modo que "usuario inexistente" y "clave incorrecta" tarden lo mismo.
        private const string HASH_SENUELO = "$2a$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jWMUW";

        // A12: control de ruido en la auditoría de seguridad.
        private const int AUDIT_THROTTLE_MIN = 10;              // 1 fila por IP cada N min (identificador inexistente)
        private const int AUDIT_SEGURIDAD_RETENCION_DIAS = 90;  // se purgan los intentos anónimos más antiguos
        private static DateTime _ultimaPurgaAuditoria = DateTime.MinValue;
        private static readonly object _purgaAuditoriaLock = new object();

        // Si el token anti-falsificación no se puede validar (típicamente porque el formulario
        // de login quedó abierto en el navegador desde antes de que el proceso del servidor
        // reiniciara/reciclara), no mostrar el error genérico: volver a Login con un mensaje claro.
        protected override void OnException(ExceptionContext filterContext)
        {
            if (filterContext.Exception is HttpAntiForgeryException)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "[AccesoController] Token anti-falsificación inválido en {0}.",
                    filterContext.HttpContext.Request.Url);

                filterContext.ExceptionHandled = true;

                // Peticiones AJAX (p. ej. ExtenderSesion): nunca redirigir — el cliente
                // recibiría el HTML del login y lo interpretaría como una respuesta válida.
                // Devolver JSON que el JS del layout ya sabe manejar.
                if (filterContext.HttpContext.Request.IsAjaxRequest())
                {
                    filterContext.HttpContext.Response.StatusCode = 200;
                    filterContext.Result = new JsonResult
                    {
                        Data = new
                        {
                            success = false,
                            sessionExpired = true,
                            message = "",
                            redirectUrl = Url.Action("Login", "Acceso")
                        },
                        JsonRequestBehavior = JsonRequestBehavior.AllowGet
                    };
                    return;
                }

                filterContext.Result = new RedirectResult(
                    Url.Action("Login", "Acceso") + "?tokenExpirado=true");
                return;
            }

            base.OnException(filterContext);
        }

        // GET: Acceso
        public ActionResult Login(bool tokenExpirado = false)
        {
            if (tokenExpirado)
            {
                ViewData["Mensaje"] = R("Login_ErrorTokenExpirado");
            }
            else if (TempData["ErrorMessage"] != null)
            {
                // Mensajes desde el flujo de doble factor (demasiados intentos, IP bloqueada…).
                ViewData["Mensaje"] = TempData["ErrorMessage"];
            }
            return View();
        }

        public ActionResult Registrar()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Registrar(Usuarios oUsuario)
        {
            bool registrado;
            string mensaje;

            // ⚡ Elimina espacios de correo y clave
            oUsuario.Correo = oUsuario.Correo.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();
            oUsuario.ConfirmarClave = oUsuario.ConfirmarClave.Trim();

            if (oUsuario.Clave != oUsuario.ConfirmarClave)
            {
                ViewData["Mensaje"] = R("Login_ErrorClavesNoCoinciden");
                return View();
            }

            // A13: política de contraseñas centralizada (longitud, complejidad, claves comunes,
            // y comprobación contra filtraciones conocidas — Have I Been Pwned).
            if (!Helpers.PoliticaContrasena.Validar(oUsuario.Clave, null, out string errClave))
            {
                ViewData["Mensaje"] = R(errClave);
                return View();
            }

            oUsuario.Clave = HashearContrasena(oUsuario.Clave);

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarUsuario", cn);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.Parameters.Add("Registrado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("Mensaje", SqlDbType.VarChar, 100).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                registrado = Convert.ToBoolean(cmd.Parameters["Registrado"].Value);
                mensaje = cmd.Parameters["Mensaje"].Value.ToString();
            }

            ViewData["Mensaje"] = mensaje;
            if (registrado)
            {
                return RedirectToAction("Login", "Acceso");
            }
            else
            {
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(Usuarios oUsuario)
        {
            oUsuario.Clave = oUsuario.Clave.Trim();

            // A7 / A14: Rate limiting por IP (persistente en BD) — rechaza IPs con demasiados
            // intentos fallidos.
            string clientIp = GetClientIp();
            var estadoIp = new RateLimitLoginService().Comprobar(clientIp);
            if (estadoIp.Bloqueada)
            {
                ViewData["Mensaje"] = string.Format(R("Login_ErrorRateLimitIP"),
                    Math.Max(1, estadoIp.MinutosRestantes));
                return View();
            }

            string inputUsuario = "";
            string inputCorreo = "";

            if (!EsCorreoValido(oUsuario.Correo))
                inputUsuario = oUsuario.Correo.Trim();
            else
                inputCorreo = oUsuario.Correo.Trim();

            int usuarioId = 0;
            string hashAlmacenado = null;
            int intentosFallidos = 0;
            DateTime? bloqueadoHasta = null;
            int? idEmpresaLogin = null;   // empresa del usuario, para la auditoría de seguridad

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                string sql = @"
                    SELECT TOP 1 Id, Clave, IntentosFallidos, BloqueadoHasta, IdEmpresa FROM Usuarios
                    WHERE (@Usuario <> '' AND Usuario = @Usuario)
                       OR (@Correo  <> '' AND Correo  = @Correo)";
                SqlCommand cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Usuario", inputUsuario);
                cmd.Parameters.AddWithValue("@Correo", inputCorreo);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        usuarioId = Convert.ToInt32(reader["Id"]);
                        hashAlmacenado = reader["Clave"].ToString();
                        try
                        {
                            intentosFallidos = reader["IntentosFallidos"] != DBNull.Value
                                ? Convert.ToInt32(reader["IntentosFallidos"]) : 0;
                            bloqueadoHasta = reader["BloqueadoHasta"] != DBNull.Value
                                ? (DateTime?)Convert.ToDateTime(reader["BloqueadoHasta"]) : null;
                        }
                        catch (IndexOutOfRangeException) { }
                        try
                        {
                            idEmpresaLogin = reader["IdEmpresa"] != DBNull.Value
                                ? (int?)Convert.ToInt32(reader["IdEmpresa"]) : null;
                        }
                        catch (IndexOutOfRangeException) { }
                    }
                }
            }

            // Cuenta bloqueada — verificar antes de cualquier intento
            if (usuarioId > 0 && bloqueadoHasta.HasValue && bloqueadoHasta.Value > DateTime.Now)
            {
                int minutosRestantes = (int)Math.Ceiling((bloqueadoHasta.Value - DateTime.Now).TotalMinutes);
                new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Bloqueo,
                    $"Intento de inicio de sesión con cuenta bloqueada ({oUsuario.Correo})", usuarioId, null, idEmpresaLogin);
                ViewData["Mensaje"] = string.Format(R("Login_ErrorCuentaBloqueada"), minutosRestantes);
                return View();
            }

            // A11: verificar SIEMPRE contra un hash BCrypt (real o señuelo) para que el tiempo de
            // respuesta no delate si el identificador existe (enumeración de usuarios por
            // temporización — cuando no había usuario, antes se saltaba el BCrypt y la respuesta
            // volvía mucho más rápido).
            string hashParaVerificar = hashAlmacenado ?? HASH_SENUELO;
            bool hashCoincide = VerificarContrasena(oUsuario.Clave, hashParaVerificar);
            bool credencialesValidas = usuarioId > 0 && hashAlmacenado != null && hashCoincide;

            if (credencialesValidas)
            {
                ResetearIntentosFallidos(usuarioId);
                new RateLimitLoginService().Limpiar(clientIp); // A7/A14: resetear rate limit IP en login exitoso

                // Migrate SHA256 → BCrypt on first successful login
                if (!EsHashBCrypt(hashAlmacenado))
                {
                    string nuevoHash = HashearContrasena(oUsuario.Clave);
                    using (SqlConnection cn = new SqlConnection(CadenaConexion))
                    {
                        SqlCommand cmd = new SqlCommand(
                            "UPDATE Usuarios SET Clave = @Clave WHERE Id = @Id", cn);
                        cmd.Parameters.AddWithValue("@Clave", nuevoHash);
                        cmd.Parameters.AddWithValue("@Id", usuarioId);
                        cn.Open();
                        // A10: verificar que el UPDATE afectó exactamente 1 fila
                        int rowsActualizados = cmd.ExecuteNonQuery();
                        if (rowsActualizados != 1)
                            throw new InvalidOperationException($"Error en migración SHA256→BCrypt: {rowsActualizados} filas afectadas para usuario {usuarioId}.");
                    }
                }

                oUsuario.Id = usuarioId;
            }
            else if (usuarioId > 0)
            {
                RegistrarIntentoFallido(usuarioId, intentosFallidos,
                    out int nuevosIntentos, out DateTime? nuevoBloqueadoHasta);
                var rlReal = new RateLimitLoginService().RegistrarFallo(clientIp); // A7/A14
                if (rlReal.ModoDefensivoActivado) NotificarModoDefensivo();

                new AuditoriaService().RegistrarSeguridad(
                    nuevoBloqueadoHasta.HasValue ? AuditoriaAccion.Bloqueo : AuditoriaAccion.LoginFallido,
                    nuevoBloqueadoHasta.HasValue
                        ? $"Cuenta bloqueada tras {nuevosIntentos} intentos fallidos ({oUsuario.Correo})"
                        : $"Credenciales incorrectas ({oUsuario.Correo}), intento {nuevosIntentos}",
                    usuarioId, null, idEmpresaLogin);

                if (nuevoBloqueadoHasta.HasValue)
                {
                    // A12: una cuenta REAL acaba de bloquearse — avisar (a diferencia del ruido
                    // de bots contra identificadores inexistentes, esto sí merece atención).
                    NotificarBloqueoCuenta(usuarioId, oUsuario.Correo, nuevosIntentos);

                    int minutosBloqueo = nuevosIntentos >= 15 ? 1440 : nuevosIntentos >= 10 ? 60 : 15;
                    ViewData["Mensaje"] = string.Format(R("Login_ErrorCuentaBloqueada"), minutosBloqueo);
                }
                else
                {
                    ViewData["Mensaje"] = R("Login_ErrorCredenciales");
                }
                return View();
            }

            if (oUsuario.Id != 0)
            {
                Usuarios usuarioCompleto = ObtenerUsuarioCompletoPorId(oUsuario.Id);

                if (usuarioCompleto != null)
                {
                    // 2FA: con credenciales correctas todavía NO se abre sesión; se exige el segundo factor.
                    if (DobleFactorService.Habilitado && DobleFactorService.AplicaA(usuarioCompleto))
                    {
                        var df = new DobleFactorService().Obtener(usuarioCompleto.Id);
                        if (df.Disponible)
                        {
                            if (df.Activo && df.Secreto == null)
                            {
                                // Secreto ilegible (p. ej. cambió la machineKey): nunca re-enrolar solo con la clave.
                                Helpers.AppLogger.Error("2FA: secreto no legible para usuarioId=" + usuarioCompleto.Id, null, "Acceso.Login");
                                ViewData["Mensaje"] = R("Login2FA_ErrorSecreto");
                                return View();
                            }
                            return IniciarDobleFactor(usuarioCompleto, df.Activo, clientIp);
                        }
                        Helpers.AppLogger.Warn("TwoFactorHabilitado=true pero falta el script Sql/026: el login NO exige 2FA.", "Acceso.Login");
                    }

                    return CompletarInicioDeSesion(usuarioCompleto);
                }
                else
                {
                    ViewData["Mensaje"] = R("Login_ErrorCargarUsuario");
                    return View();
                }
            }
            else
            {
                var rlAnon = new RateLimitLoginService().RegistrarFallo(clientIp); // A7/A14: usuario no encontrado también cuenta
                if (rlAnon.ModoDefensivoActivado) NotificarModoDefensivo();

                // A12: los intentos contra identificadores inexistentes son ruido de bots. Se
                // deja rastro completo en el log de aplicación SIEMPRE, pero en la tabla
                // Auditoria solo 1 fila por IP cada AUDIT_THROTTLE_MIN minutos, para no ahogar
                // la bitácora de seguridad ni inflar la tabla.
                Helpers.AppLogger.Warn(
                    $"Login: identificador inexistente ({oUsuario.Correo}) desde ip={clientIp}", "Acceso.Login");

                if (DebeAuditarFalloAnonimo(clientIp))
                {
                    new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.LoginFallido,
                        $"Intento de inicio de sesión con identificador inexistente ({oUsuario.Correo}) " +
                        $"[se omiten repeticiones de la misma IP durante {AUDIT_THROTTLE_MIN} min]",
                        null, null);
                }

                PurgarAuditoriaSeguridadOportunista();
                ViewData["Mensaje"] = R("Login_ErrorCredenciales");
                return View();
            }
        }


        /// <summary>
        /// Último paso del login (credenciales y, si aplica, segundo factor ya validados): audita el
        /// acceso y entrega el token de un solo uso que abre la sesión nueva en FinalizarLogin.
        /// </summary>
        private ActionResult CompletarInicioDeSesion(Usuarios usuarioCompleto)
        {
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Login,
                "Inicio de sesión exitoso",
                usuarioCompleto.Id,
                ((usuarioCompleto.Nombre ?? "") + " " + (usuarioCompleto.Apellidos ?? "")).Trim(),
                usuarioCompleto.IdEmpresa);

            // A6: Regenerar SessionID para prevenir session fixation
            string loginToken = Guid.NewGuid().ToString("N");
            System.Web.HttpRuntime.Cache.Insert(
                "_lt_" + loginToken, usuarioCompleto.Id, null,
                DateTime.Now.AddSeconds(60), System.Web.Caching.Cache.NoSlidingExpiration);
            Session.Abandon();
            Response.Cookies.Add(new HttpCookie("_lt", loginToken)
                { HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
            return RedirectToAction("FinalizarLogin");
        }

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
            public List<string> CodigosMostrar;  // códigos de recuperación recién generados (se muestran una vez)
        }

        private ActionResult IniciarDobleFactor(Usuarios usuario, bool yaActivo, string ip)
        {
            string token = Guid.NewGuid().ToString("N");
            System.Web.HttpRuntime.Cache.Insert(
                "_2fa_" + token, new PendienteDobleFactor { IdUsuario = usuario.Id, Enrolar = !yaActivo }, null,
                DateTime.Now.AddMinutes(5), System.Web.Caching.Cache.NoSlidingExpiration);
            Session.Abandon();
            Response.Cookies.Add(new HttpCookie(COOKIE_2FA, token)
                { HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
            return RedirectToAction(yaActivo ? "VerificarDobleFactor" : "ConfigurarDobleFactor");
        }

        private PendienteDobleFactor ObtenerPendiente2FA(out string cacheKey)
        {
            cacheKey = null;
            var c = Request.Cookies[COOKIE_2FA];
            if (c == null || string.IsNullOrEmpty(c.Value)) return null;
            cacheKey = "_2fa_" + c.Value;
            return System.Web.HttpRuntime.Cache[cacheKey] as PendienteDobleFactor;
        }

        private void LimpiarPendiente2FA(string cacheKey)
        {
            if (cacheKey != null) System.Web.HttpRuntime.Cache.Remove(cacheKey);
            Response.Cookies.Add(new HttpCookie(COOKIE_2FA)
                { Expires = DateTime.Now.AddDays(-1), HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
        }

        private string NombreCompleto(Usuarios u)
        {
            return ((u.Nombre ?? "") + " " + (u.Apellidos ?? "")).Trim();
        }

        [HttpGet]
        public ActionResult VerificarDobleFactor()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || p.Enrolar) return RedirectToAction("Login");
            return View("~/Views/Acceso/VerificarDobleFactor.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VerificarDobleFactor(string codigo)
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || p.Enrolar)
                return RedirectToAction("Login", new { tokenExpirado = true });

            string ip = GetClientIp();
            var estadoIp = new RateLimitLoginService().Comprobar(ip);
            if (estadoIp.Bloqueada)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = string.Format(R("Login_ErrorRateLimitIP"), Math.Max(1, estadoIp.MinutosRestantes));
                return RedirectToAction("Login");
            }

            var servicio = new DobleFactorService();
            var estado = servicio.Obtener(p.IdUsuario);
            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            if (!estado.Disponible || !estado.Activo || estado.Secreto == null || usuario == null)
            {
                LimpiarPendiente2FA(key);
                return RedirectToAction("Login");
            }

            string limpio = (codigo ?? "").Replace(" ", "").Replace("-", "").Trim();
            bool esTotp = limpio.Length == TotpHelper.Digitos && limpio.All(char.IsDigit);
            bool ok = false, usoRecuperacion = false;

            if (esTotp)
            {
                long paso = TotpHelper.Validar(estado.Secreto, limpio, estado.UltimoPaso);
                ok = paso >= 0 && servicio.RegistrarPaso(p.IdUsuario, paso);
            }
            else if (DobleFactorService.PareceCodigoRecuperacion(codigo))
            {
                ok = servicio.ConsumirCodigoRecuperacion(p.IdUsuario, codigo);
                usoRecuperacion = ok;
            }

            if (ok)
            {
                LimpiarPendiente2FA(key);
                if (usoRecuperacion)
                {
                    int quedan = servicio.CodigosRestantes(p.IdUsuario);
                    new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Login,
                        $"Inicio de sesión con código de recuperación del doble factor (quedan {quedan})",
                        usuario.Id, NombreCompleto(usuario), usuario.IdEmpresa);
                    new NotificacionesService().Crear(usuario.Id, R("Notif_2FA_RecuperacionTitulo"),
                        string.Format(R("Notif_2FA_RecuperacionMsg"), quedan), "warning");
                }
                return CompletarInicioDeSesion(usuario);
            }

            // Código incorrecto
            p.Intentos++;
            var rl = new RateLimitLoginService().RegistrarFallo(ip);
            if (rl.ModoDefensivoActivado) NotificarModoDefensivo();
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.LoginFallido,
                $"Código de doble factor incorrecto ({usuario.Correo}), intento {p.Intentos} de {MAX_INTENTOS_2FA}",
                usuario.Id, NombreCompleto(usuario), usuario.IdEmpresa);

            if (p.Intentos >= MAX_INTENTOS_2FA)
            {
                LimpiarPendiente2FA(key);
                TempData["ErrorMessage"] = R("Login2FA_ErrorDemasiadosIntentos");
                return RedirectToAction("Login");
            }

            ViewData["Error"] = R("Login2FA_ErrorCodigo");
            return View("~/Views/Acceso/VerificarDobleFactor.cshtml");
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
            var estadoIp = new RateLimitLoginService().Comprobar(ip);
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
                var rl = new RateLimitLoginService().RegistrarFallo(ip);
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

            var servicio = new DobleFactorService();
            var codigos = servicio.GenerarCodigosRecuperacion();
            if (!servicio.Activar(p.IdUsuario, p.SecretoNuevo, paso, codigos))
            {
                ViewData["Error"] = R("Login2FA_ErrorGuardar");
                string secreto = TotpHelper.ABase32(p.SecretoNuevo);
                ViewBag.SecretoBase32 = secreto;
                ViewBag.UriOtp = TotpHelper.UriOtpAuth("Bufins", usuario.Correo ?? usuario.Usuario, secreto);
                return View("~/Views/Acceso/ConfigurarDobleFactor.cshtml");
            }

            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Editar,
                "Doble factor de autenticación activado",
                usuario.Id, NombreCompleto(usuario), usuario.IdEmpresa);
            new NotificacionesService().Crear(usuario.Id, R("Notif_2FA_ActivadoTitulo"), R("Notif_2FA_ActivadoMsg"), "success");

            // Pasa a la pantalla (una sola vez) con los códigos de recuperación.
            p.Enrolar = false;
            p.SecretoNuevo = null;
            p.CodigosMostrar = codigos;
            System.Web.HttpRuntime.Cache.Insert(key, p, null, DateTime.Now.AddMinutes(15),
                System.Web.Caching.Cache.NoSlidingExpiration);
            return RedirectToAction("CodigosRecuperacion");
        }

        [HttpGet]
        public ActionResult CodigosRecuperacion()
        {
            var p = ObtenerPendiente2FA(out _);
            if (p == null || p.CodigosMostrar == null) return RedirectToAction("Login");
            ViewBag.Codigos = p.CodigosMostrar;
            return View("~/Views/Acceso/CodigosRecuperacion.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CodigosRecuperacionContinuar()
        {
            var p = ObtenerPendiente2FA(out string key);
            if (p == null || p.CodigosMostrar == null) return RedirectToAction("Login");
            var usuario = ObtenerUsuarioCompletoPorId(p.IdUsuario);
            LimpiarPendiente2FA(key);
            if (usuario == null) return RedirectToAction("Login");
            return CompletarInicioDeSesion(usuario);
        }

        private void RegistrarIntentoFallido(int usuarioId, int intentosActuales,
            out int nuevosIntentos, out DateTime? nuevoBloqueadoHasta)
        {
            nuevosIntentos = intentosActuales + 1;
            nuevoBloqueadoHasta = null;

            if (nuevosIntentos >= 15)
                nuevoBloqueadoHasta = DateTime.Now.AddHours(24);
            else if (nuevosIntentos >= 10)
                nuevoBloqueadoHasta = DateTime.Now.AddHours(1);
            else if (nuevosIntentos >= 5)
                nuevoBloqueadoHasta = DateTime.Now.AddMinutes(15);

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "UPDATE Usuarios SET IntentosFallidos = @Intentos, BloqueadoHasta = @BloqueadoHasta WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Intentos", nuevosIntentos);
                cmd.Parameters.AddWithValue("@BloqueadoHasta", (object)nuevoBloqueadoHasta ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", usuarioId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void ResetearIntentosFallidos(int usuarioId)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "UPDATE Usuarios SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", usuarioId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Método para extender la sesión vía AJAX
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ExtenderSesion()
        {
            try
            {
                bool exito = UsuarioSesionHelper.ExtenderSesion();
                
                if (exito)
                {
                    var sessionInfo = UsuarioSesionHelper.ObtenerInfoSesion();
                    return Json(new { 
                        success = true, 
                        message = "Sesión extendida correctamente",
                        minutosRestantes = sessionInfo?.MinutosRestantes ?? 30
                    });
                }
                else
                {
                    return Json(new { 
                        success = false, 
                        message = "No se pudo extender la sesión",
                        redirectUrl = Url.Action("Login", "Acceso")
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = "Error al extender la sesión: " + ex.Message 
                });
            }
        }

        /// <summary>
        /// Método para cerrar sesión mejorado
        /// </summary>
        public ActionResult CerrarSesion(bool expired = false)
        {
            UsuarioSesionHelper.LimpiarSesion();
            return expired
                ? RedirectToAction("Login", "Acceso", new { expired = true })
                : RedirectToAction("Login", "Acceso");
        }

        // A3: Endpoint mínimo y seguro para verificar estado de sesión desde JS
        [HttpGet]
        public JsonResult VerificarSesion()
        {
            var sessionInfo = UsuarioSesionHelper.ObtenerInfoSesion();
            return Json(new
            {
                success          = true,
                estaAutenticado  = UsuarioSesionHelper.EstaAutenticado(),
                minutosRestantes = sessionInfo?.MinutosRestantes ?? 0,
                estaPorExpirar   = sessionInfo?.EstaPorExpirar ?? false
            }, JsonRequestBehavior.AllowGet);
        }

        // A6: Completa el login en una nueva sesión (session ID regenerado)
        [HttpGet]
        public ActionResult FinalizarLogin()
        {
            var cookie = Request.Cookies["_lt"];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value))
                return RedirectToAction("Login");

            var cacheKey = "_lt_" + cookie.Value;
            var cached   = System.Web.HttpRuntime.Cache[cacheKey];

            // Invalidar token (uso único)
            Response.Cookies.Add(new HttpCookie("_lt")
                { Expires = DateTime.Now.AddDays(-1), HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
            System.Web.HttpRuntime.Cache.Remove(cacheKey);

            if (cached == null) return RedirectToAction("Login");

            Usuarios usuarioCompleto = ObtenerUsuarioCompletoPorId((int)cached);
            if (usuarioCompleto == null) return RedirectToAction("Login");

            UsuarioSesionHelper.EstablecerUsuarioEnSesion(usuarioCompleto);
            if (usuarioCompleto.DebeCambiarClave)
                return RedirectToAction("CambiarClaveInicial");
            return RedirectToAction("Index", "Home");
        }

        // ====== A7 / A14: Rate limiting por IP ======
        // El estado (contador y bloqueo por IP + detección de pico global "modo defensivo")
        // vive en RateLimitLoginService (tabla dbo.IntentosLoginIP, con caché en memoria de
        // respaldo). Aquí solo queda la resolución de la IP.

        private string GetClientIp()
        {
            // A11: resuelve la IP real detrás de un proxy inverso / CDN cuando Web.config
            // declara los proxies de confianza (TrustedProxies). Sin esa config, equivale
            // a Request.UserHostAddress.
            return Helpers.ClientIpHelper.ObtenerIp(Request);
        }

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

                var notif = new NotificacionesService();
                string titulo = R("Notif_ModoDefensivoTitulo");
                string msg = R("Notif_ModoDefensivoMsg");
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    notif.Crear(idAdmin, titulo, msg, "error", "/Auditoria");

                EnviarAlertaSeguridadPorCorreo(titulo, msg);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[NotificarModoDefensivo] {0}", ex.Message); }
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

            System.Web.Hosting.HostingEnvironment.QueueBackgroundWorkItem(ct =>
            {
                try
                {
                    var email = new EmailService();
                    foreach (var c in correos)
                    {
                        try { email.EnviarAlertaSeguridad(c, asunto, titulo, mensaje, enlace); }
                        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[EnviarAlertaSeguridad] {0}: {1}", c, ex.Message); }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[EnviarAlertaSeguridad] {0}", ex.Message); }
            });
        }

        private System.Collections.Generic.List<string> ObtenerCorreosAlertaSeguridad()
        {
            var lista = new System.Collections.Generic.List<string>();
            string destino = System.Configuration.ConfigurationManager.AppSettings["SeguridadAlertasDestino"];
            if (string.IsNullOrWhiteSpace(destino)) return lista;

            var usuarios = new System.Collections.Generic.List<string>();
            foreach (var raw in destino.Split(','))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (t.Contains("@")) { if (!lista.Contains(t)) lista.Add(t); }
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
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[ObtenerCorreosAlertaSeguridad] {0}", ex.Message); }
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
            if (System.Web.HttpRuntime.Cache[key] != null) return false;
            System.Web.HttpRuntime.Cache.Insert(key, 1, null,
                DateTime.Now.AddMinutes(AUDIT_THROTTLE_MIN), System.Web.Caching.Cache.NoSlidingExpiration);
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
            try { new AuditoriaService().PurgarSeguridadAnonimaAntigua(AUDIT_SEGURIDAD_RETENCION_DIAS); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[PurgarAuditoriaSeguridad] {0}", ex.Message); }
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

                var notif = new NotificacionesService();
                notif.Crear(usuarioId, R("Notif_CuentaBloqueadaTitulo"), R("Notif_CuentaBloqueadaMsg"), "warning");

                string msgAdmin = string.Format(R("Notif_CuentaBloqueadaAdminMsg"), identificador, intentos);
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    if (idAdmin != usuarioId)
                        notif.Crear(idAdmin, R("Notif_CuentaBloqueadaAdminTitulo"), msgAdmin, "error", "/Auditoria");
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[NotificarBloqueoCuenta] {0}", ex.Message); }
        }

        private System.Collections.Generic.List<int> ObtenerIdsSuperAdmin()
        {
            var ids = new System.Collections.Generic.List<int>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("SELECT Id FROM Usuarios WHERE Admin = 2", cn);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["Id"]));
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[ObtenerIdsSuperAdmin] {0}", ex.Message); }
            return ids;
        }

        private bool EsCorreoValido(string correo)
        {
            // Expresión regular para validar correo
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            Regex regex = new Regex(pattern);
            return regex.IsMatch(correo);
        }

        /// <summary>
        /// Obtiene los datos completos del usuario por ID
        /// </summary>
        private Usuarios ObtenerUsuarioCompletoPorId(int idUsuario)
        {
            Usuarios usuario = null;

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                string query = @"
                SELECT
                    u.Id,
                    u.Usuario,
                    u.Clave,
                    u.Nombre,
                    u.Apellidos,
                    u.Correo,
                    u.Telefono,
                    u.Admin,
                    u.IdEmpresa,
                    (SELECT TOP 1 Id FROM UsuarioImagenes WHERE UsuarioId = u.Id ORDER BY Id DESC) AS ImagenId
                FROM Usuarios u
                WHERE u.Id = @IdUsuario";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            usuario = new Usuarios
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Usuario = Convert.ToString(reader["Usuario"]),
                                Clave = Convert.ToString(reader["Clave"]),
                                Nombre = Convert.ToString(reader["Nombre"]),
                                Apellidos = Convert.ToString(reader["Apellidos"]),
                                Correo = Convert.ToString(reader["Correo"]),
                                Telefono = Convert.ToString(reader["Telefono"]),
                                Admin = reader["Admin"] != DBNull.Value ? Convert.ToByte(reader["Admin"]) : (byte?)null,
                                IdEmpresa = reader["IdEmpresa"] != DBNull.Value ? Convert.ToInt32(reader["IdEmpresa"]) : (int?)null,
                                Imagen = reader["ImagenId"] != DBNull.Value ? new ImagenUsuario
                                {
                                    Id = Convert.ToInt32(reader["ImagenId"]),
                                    UsuarioId = idUsuario
                                } : null
                            };
                        }
                    }
                }
            }

            if (usuario != null) usuario.DebeCambiarClave = new bufinscustomers.Services.ClaveInicialService().DebeCambiar(usuario.Id);
            return usuario;
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

                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "SELECT TOP 1 Id, Nombre, Correo FROM Usuarios WHERE LOWER(Correo) = @Correo", cn);
                    cmd.Parameters.AddWithValue("@Correo", correo);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            usuarioId    = Convert.ToInt32(r["Id"]);
                            nombreUsuario = r["Nombre"].ToString();
                            correoReal   = r["Correo"].ToString();
                        }
                    }
                }

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
                    using (var cn = new SqlConnection(CadenaConexion))
                    {
                        var cmd = new SqlCommand(
                            "UPDATE Usuarios SET ResetToken = @Token, ResetTokenExpiry = @Expiry WHERE Id = @Id", cn);
                        cmd.Parameters.AddWithValue("@Token",  token);
                        cmd.Parameters.AddWithValue("@Expiry", expiry);
                        cmd.Parameters.AddWithValue("@Id",     usuarioId);
                        cn.Open();
                        cmd.ExecuteNonQuery();
                    }

                    // Construir enlace y enviar correo
                    string enlace   = Url.Action("RestablecerClave", "Acceso",
                        new { token = token }, Request.Url.Scheme);
                    bool esIngles   = System.Threading.Thread.CurrentThread.CurrentUICulture
                                          .TwoLetterISOLanguageName == "en";
                    new Services.EmailService().EnviarRecuperacionClave(
                        correoReal, nombreUsuario, enlace, esIngles);
                }
            }
            catch (Exception ex)
            {
                // Log error internamente sin exponerlo al cliente (anti-enumeración)
                System.Diagnostics.Trace.TraceError("[SolicitarReset] {0}", ex.Message);
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
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"SELECT COUNT(1) FROM Usuarios
                      WHERE ResetToken = @Token AND ResetTokenExpiry > GETDATE()", cn);
                cmd.Parameters.AddWithValue("@Token", token);
                cn.Open();
                valido = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }

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
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"UPDATE Usuarios
                      SET Clave = @Clave, ResetToken = NULL, ResetTokenExpiry = NULL,
                          IntentosFallidos = 0, BloqueadoHasta = NULL
                      WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Clave", hash);
                cmd.Parameters.AddWithValue("@Id",    usuarioId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }

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

            var servicio = new ClaveInicialService();
            if (!servicio.GuardarClaveYQuitarMarca(usuario.Id, HashearContrasena(nuevaClave)))
            {
                ViewData["Error"] = R("CambioClaveInicial_ErrorGuardar");
                return View("~/Views/Acceso/CambiarClaveInicial.cshtml");
            }

            usuario.DebeCambiarClave = false;
            usuario.Clave = HashearContrasena(nuevaClave);
            new AuditoriaService().RegistrarSeguridad(AuditoriaAccion.Editar,
                "Cambio de contraseña obligatorio completado en el primer inicio de sesión",
                usuario.Id, ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim(), usuario.IdEmpresa);

            SetSuccessMessage(R("CambioClaveInicial_Exito"));
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public ActionResult SetLanguage(string lang, string returnUrl)
        {
            if (lang == "es-CO" || lang == "en-US")
            {
                var cookie = new HttpCookie("lang", lang)
                {
                    Expires = DateTime.Now.AddYears(1),
                    HttpOnly = true
                };
                Response.SetCookie(cookie);
            }

            if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl))
                returnUrl = "/";

            return Redirect(returnUrl);
        }
    }
}