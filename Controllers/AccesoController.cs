using System;
using System.Data;
using bufinscustomers.Models;
using System.Text.RegularExpressions;
using bufinscustomers.Helpers;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    public partial class AccesoController(
        RateLimitLoginService rateLimitLoginService,
        AuditoriaService auditoriaService,
        DobleFactorService dobleFactorService,
        PasskeyService passkeyService,
        SolicitudReinicioAuthService solicitudReinicioAuthService,
        NotificacionesService notificacionesService,
        EmailService emailService,
        bufinscustomers.Services.ClaveInicialService claveInicialService,
        SesionPersistenteService sesionPersistenteService) : BaseController
    {
        // A11: hash BCrypt (cost 12) señuelo — vector de prueba público del algoritmo. Solo se
        // usa para gastar el mismo tiempo de CPU que una verificación real cuando el identificador
        // no existe, de modo que "usuario inexistente" y "clave incorrecta" tarden lo mismo.
        private const string HASH_SENUELO = "$2a$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jWMUW";

        // Token anti-falsificación inválido (formulario abierto desde antes de un reinicio del sitio):
        // lo atiende el filtro global Filters/AntiforgeryFallidoFilter (vuelve a Login?tokenExpirado=true).
        // En ASP.NET Core un filtro implementado en el controlador NO se ejecuta en ese caso.

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
            // ASP.NET Core convierte los campos vacíos en null: sin esto, un envío sin clave o sin
            // correo (p. ej. página restaurada con «atrás» que salta la validación JS) daba 500.
            oUsuario ??= new Usuarios();
            oUsuario.Correo = (oUsuario.Correo ?? "").Trim();
            oUsuario.Clave = (oUsuario.Clave ?? "").Trim();
            if (oUsuario.Correo.Length == 0)
            {
                ViewData["Mensaje"] = R("Login_ErrorEmail");
                return View();
            }
            if (oUsuario.Clave.Length == 0)
            {
                ViewData["Mensaje"] = R("Login_ErrorPassword");
                return View();
            }

            // A7 / A14: Rate limiting por IP (persistente en BD) — rechaza IPs con demasiados
            // intentos fallidos.
            string clientIp = GetClientIp();
            var estadoIp = rateLimitLoginService.Comprobar(clientIp);
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
                auditoriaService.RegistrarSeguridad(AuditoriaAccion.Bloqueo,
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
                rateLimitLoginService.Limpiar(clientIp); // A7/A14: resetear rate limit IP en login exitoso

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
                var rlReal = rateLimitLoginService.RegistrarFallo(clientIp); // A7/A14
                if (rlReal.ModoDefensivoActivado) NotificarModoDefensivo();

                auditoriaService.RegistrarSeguridad(
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
                        var df = dobleFactorService.Obtener(usuarioCompleto.Id);
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
                var rlAnon = rateLimitLoginService.RegistrarFallo(clientIp); // A7/A14: usuario no encontrado también cuenta
                if (rlAnon.ModoDefensivoActivado) NotificarModoDefensivo();

                // A12: los intentos contra identificadores inexistentes son ruido de bots. Se
                // deja rastro completo en el log de aplicación SIEMPRE, pero en la tabla
                // Auditoria solo 1 fila por IP cada AUDIT_THROTTLE_MIN minutos, para no ahogar
                // la bitácora de seguridad ni inflar la tabla.
                Helpers.AppLogger.Warn(
                    $"Login: identificador inexistente ({oUsuario.Correo}) desde ip={clientIp}", "Acceso.Login");

                if (DebeAuditarFalloAnonimo(clientIp))
                {
                    auditoriaService.RegistrarSeguridad(AuditoriaAccion.LoginFallido,
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
            auditoriaService.RegistrarSeguridad(AuditoriaAccion.Login,
                "Inicio de sesión exitoso",
                usuarioCompleto.Id,
                ((usuarioCompleto.Nombre ?? "") + " " + (usuarioCompleto.Apellidos ?? "")).Trim(),
                usuarioCompleto.IdEmpresa);

            // A6: Regenerar SessionID para prevenir session fixation
            string loginToken = Guid.NewGuid().ToString("N");
            HttpRuntime.Cache.Insert(
                "_lt_" + loginToken, usuarioCompleto.Id, null,
                DateTime.Now.AddSeconds(60), Cache.NoSlidingExpiration);
            Session.Abandon();
            Response.Cookies.Add(new HttpCookie("_lt", loginToken)
                { HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
            return RedirectToAction("FinalizarLogin");
        }

        // Panel izquierdo de las pantallas de acceso («Ingresando como …»): solo UI, no toca la lógica de cada acción.
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            base.OnActionExecuted(filterContext);
            if (!(filterContext.Result is ViewResult)) return;
            string accion = (string)filterContext.RouteData.Values["action"];
            try
            {
                string correo = null;
                if (accion == "CambiarClaveInicial")
                {
                    correo = UsuarioSesionHelper.UsuarioActual?.Correo;
                }
                else if (accion == "VerificarDobleFactor" || accion == "ConfigurarDobleFactor"
                      || accion == "OfrecerPasskey" || accion == "SolicitarReinicio")
                {
                    var p = ObtenerPendiente2FA(out _);
                    if (p != null)
                    {
                        var u = ObtenerUsuarioCompletoPorId(p.IdUsuario);
                        correo = u?.Correo;
                        // El stepper del alta solo muestra «Nueva contraseña» si aún le falta cambiarla (se pide después del 2FA).
                        ViewBag.RequiereCambioClave = u != null && u.DebeCambiarClave;
                        if (accion == "VerificarDobleFactor")
                            ViewBag.PuedeCrearPasskey = DobleFactorService.PasskeysHabilitadas && !TienePasskeyDisponible(p.IdUsuario);
                    }
                }
                ViewBag.CorreoEnmascarado = EnmascararCorreo(correo);
            }
            catch (Exception ex) { AppLogger.Warn("Acceso: correo enmascarado del panel: " + ex.Message); }
        }

        /// <summary>Primeros 2 caracteres + "•••@" + dominio (ej.: om•••@empresa.com).</summary>
        private static string EnmascararCorreo(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo)) return "";
            correo = correo.Trim();
            int arroba = correo.IndexOf('@');
            if (arroba <= 0) return correo.Length <= 2 ? correo : string.Concat(correo.AsSpan(0, 2), "•••");
            string local = correo.Substring(0, arroba);
            return string.Concat(local.Length <= 2 ? local : local.Substring(0, 2), "•••", correo.AsSpan(arroba));
        }

        private string NombreCompleto(Usuarios u)
        {
            return ((u.Nombre ?? "") + " " + (u.Apellidos ?? "")).Trim();
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

            Bd.Ejecutar(CadenaConexion, "UPDATE Usuarios SET IntentosFallidos = @Intentos, BloqueadoHasta = @BloqueadoHasta WHERE Id = @Id",
                new { Intentos = nuevosIntentos, BloqueadoHasta = nuevoBloqueadoHasta, Id = usuarioId });
        }

        private void ResetearIntentosFallidos(int usuarioId)
        {
            Bd.Ejecutar(CadenaConexion, "UPDATE Usuarios SET IntentosFallidos = 0, BloqueadoHasta = NULL WHERE Id = @Id",
                new { Id = usuarioId });
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
            });
        }

        // A6: Completa el login en una nueva sesión (session ID regenerado)
        [HttpGet]
        public ActionResult FinalizarLogin()
        {
            var cookie = Request.Cookies["_lt"];
            if (cookie == null || string.IsNullOrEmpty(cookie))
                return RedirectToAction("Login");

            var cacheKey = "_lt_" + cookie;
            var cached   = HttpRuntime.Cache[cacheKey];

            // Invalidar token (uso único)
            Response.Cookies.Add(new HttpCookie("_lt")
                { Expires = DateTime.Now.AddDays(-1), HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
            HttpRuntime.Cache.Remove(cacheKey);

            if (cached == null) return RedirectToAction("Login");

            Usuarios usuarioCompleto = ObtenerUsuarioCompletoPorId((int)cached);
            if (usuarioCompleto == null) return RedirectToAction("Login");

            UsuarioSesionHelper.EstablecerUsuarioEnSesion(usuarioCompleto);

            // Eligió "Agregar passkey ahora" tras activar el 2FA: lo lleva a Seguridad con el alta abierta.
            var cOferta = Request.Cookies["_pkoferta"];
            if (cOferta != null)
            {
                Response.Cookies.Add(new HttpCookie("_pkoferta")
                    { Expires = DateTime.Now.AddDays(-1), HttpOnly = true, Secure = Request.IsSecureConnection, Path = "/" });
                if (!usuarioCompleto.DebeCambiarClave)
                    return RedirectToAction("Index", "Seguridad", new { agregar = true });
            }

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

            if (usuario != null) usuario.DebeCambiarClave = claveInicialService.DebeCambiar(usuario.Id);
            return usuario;
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