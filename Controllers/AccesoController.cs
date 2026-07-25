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
using Newtonsoft.Json;

namespace bufinscustomers.Controllers
{
    public class AccesoController : BaseController
    {
        // Si el token anti-falsificación no se puede validar (típicamente porque el formulario
        // de login quedó abierto en el navegador desde antes de que el proceso del servidor
        // reiniciara/reciclara), no mostrar el error genérico: volver a Login con un mensaje claro.
        protected override void OnException(ExceptionContext filterContext)
        {
            if (filterContext.Exception is HttpAntiForgeryException)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "[AccesoController] Token anti-falsificación inválido en {0}. Redirigiendo a Login.",
                    filterContext.HttpContext.Request.Url);

                filterContext.ExceptionHandled = true;
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

            if (oUsuario.Clave == oUsuario.ConfirmarClave)
            {
                oUsuario.Clave = HashearContrasena(oUsuario.Clave);
            }
            else
            {
                ViewData["Mensaje"] = "Las contraseñas no coinciden";
                return View();
            }

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

            // A7: Rate limiting por IP — rechaza IPs con demasiados intentos fallidos
            string clientIp = GetClientIp();
            if (EstaIPBloqueada(clientIp))
            {
                ViewData["Mensaje"] = $"Demasiados intentos fallidos desde esta dirección. Intenta de nuevo en {RATE_LIMIT_MINUTOS} minuto(s).";
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

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                string sql = @"
                    SELECT TOP 1 Id, Clave, IntentosFallidos, BloqueadoHasta FROM Usuarios
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
                    }
                }
            }

            // Cuenta bloqueada — verificar antes de cualquier intento
            if (usuarioId > 0 && bloqueadoHasta.HasValue && bloqueadoHasta.Value > DateTime.Now)
            {
                int minutosRestantes = (int)Math.Ceiling((bloqueadoHasta.Value - DateTime.Now).TotalMinutes);
                ViewData["Mensaje"] = $"Cuenta bloqueada temporalmente. Intenta de nuevo en {minutosRestantes} minuto(s).";
                return View();
            }

            bool credencialesValidas = usuarioId > 0
                && hashAlmacenado != null
                && VerificarContrasena(oUsuario.Clave, hashAlmacenado);

            if (credencialesValidas)
            {
                ResetearIntentosFallidos(usuarioId);
                LimpiarContadorIP(clientIp); // A7: resetear rate limit IP en login exitoso

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
                RegistrarIntentoIP(clientIp); // A7

                if (nuevoBloqueadoHasta.HasValue)
                {
                    int minutosBloqueo = nuevosIntentos >= 15 ? 1440 : nuevosIntentos >= 10 ? 60 : 15;
                    ViewData["Mensaje"] = $"Cuenta bloqueada temporalmente. Intenta de nuevo en {minutosBloqueo} minuto(s).";
                }
                else
                {
                    ViewData["Mensaje"] = "Usuario o clave incorrecta.";
                }
                return View();
            }

            if (oUsuario.Id != 0)
            {
                Usuarios usuarioCompleto = ObtenerUsuarioCompletoPorId(oUsuario.Id);

                if (usuarioCompleto != null)
                {
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
                else
                {
                    ViewData["Mensaje"] = "Error al cargar los datos del usuario";
                    return View();
                }
            }
            else
            {
                RegistrarIntentoIP(clientIp); // A7: usuario no encontrado también cuenta
                ViewData["Mensaje"] = "Usuario o clave incorrecta.";
                return View();
            }
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
            return RedirectToAction("Index", "Home");
        }

        // ====== A7: Rate limiting por IP ======
        private const int RATE_LIMIT_MAX = 20;
        private const int RATE_LIMIT_MINUTOS = 15;

        private string GetClientIp()
        {
            return Request.UserHostAddress ?? "";
        }

        private bool EstaIPBloqueada(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            var entry = System.Web.HttpRuntime.Cache["_rl_" + ip] as int?;
            return entry.HasValue && entry.Value >= RATE_LIMIT_MAX;
        }

        private void RegistrarIntentoIP(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            var key = "_rl_" + ip;
            var actual = System.Web.HttpRuntime.Cache[key] as int? ?? 0;
            System.Web.HttpRuntime.Cache.Insert(key, actual + 1, null,
                DateTime.Now.AddMinutes(RATE_LIMIT_MINUTOS), System.Web.Caching.Cache.NoSlidingExpiration);
        }

        private void LimpiarContadorIP(string ip)
        {
            if (!string.IsNullOrEmpty(ip))
                System.Web.HttpRuntime.Cache.Remove("_rl_" + ip);
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
            if (string.IsNullOrWhiteSpace(nuevaClave) || nuevaClave.Length < 8)
            {
                ViewData["Token"] = token;
                ViewData["Error"] = nuevaClave?.Length < 8
                    ? R("Reset_NewPwd_ErrorLen") : R("Reset_NewPwd_ErrorEmpty");
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