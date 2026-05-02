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
using OfficeOpenXml;
using System.IO;
using System.ComponentModel;
using bufinscustomers.Helpers;
using Newtonsoft.Json;

namespace bufinscustomers.Controllers
{
    public class AccesoController : BaseController
    {
        // GET: Acceso
        public ActionResult Login()
        {
            return View();
        }

        public ActionResult Registrar()
        {
            return View();
        }

        [HttpPost]
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
                oUsuario.Clave = ConvertirSha256(oUsuario.Clave);
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
        public ActionResult Login(Usuarios oUsuario)
        {
            // ⚡ Elimina espacios
            oUsuario.Clave = oUsuario.Clave.Trim();

            var Usuario = "";   
            var Correo = "";   

            if (!EsCorreoValido(oUsuario.Correo))
            {
                Usuario = oUsuario.Correo.Trim();
            }
            else
            {
                Correo = oUsuario.Correo.Trim();
            }

            // Luego convierte
            oUsuario.Clave = ConvertirSha256(oUsuario.Clave);

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_ValidarUsuario", cn);
                cmd.Parameters.AddWithValue("Usuario", Usuario);
                cmd.Parameters.AddWithValue("Correo", Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                oUsuario.Id = Convert.ToInt32(cmd.ExecuteScalar().ToString());
            }

            if (oUsuario.Id != 0)
            {
                // ========== CARGAR DATOS COMPLETOS DEL USUARIO ==========
                Usuarios usuarioCompleto = ObtenerUsuarioCompletoPorId(oUsuario.Id);
                
                if (usuarioCompleto != null)
                {
                    // ========== USAR NUEVO HELPER DE SESIÓN CON DATOS COMPLETOS ==========
                    UsuarioSesionHelper.EstablecerUsuarioEnSesion(usuarioCompleto);
                    
                    // Verificar si la sesión expiró por parámetro
                    if (Request.QueryString["expired"] == "true")
                    {
                        ViewData["Mensaje"] = "Su sesión anterior expiró. Ha iniciado sesión correctamente.";
                    }
                    
                    return RedirectToAction("index", "Home");
                }
                else
                {
                    ViewData["Mensaje"] = "Error al cargar los datos del usuario";
                    return View();
                }
            }
            else
            {
                ViewData["Mensaje"] = "usuario o clave incorrecta";
                return View();
            }
        }

        /// <summary>
        /// Método para extender la sesión vía AJAX
        /// </summary>
        [HttpPost]
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
        public ActionResult CerrarSesion()
        {
            UsuarioSesionHelper.LimpiarSesion();
            return RedirectToAction("Login", "Acceso");
        }

        /// <summary>
        /// Obtener información de la sesión actual (para debugging)
        /// </summary>
        [HttpGet]
        public JsonResult InfoSesion()
        {
            try
            {
                var sessionInfo = UsuarioSesionHelper.ObtenerInfoSesion();
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                return Json(new {
                    success = true,
                    estaAutenticado = UsuarioSesionHelper.EstaAutenticado(),
                    esAdmin = UsuarioSesionHelper.EsAdministrador(),
                    usuario = usuario?.Nombre + " " + usuario?.Apellidos,
                    usuarioCompleto = new {
                        id = usuario?.Id,
                        nombre = usuario?.Nombre,
                        apellidos = usuario?.Apellidos,
                        correo = usuario?.Correo,
                        telefono = usuario?.Telefono,
                        admin = usuario?.Admin,
                        idEmpresa = usuario?.IdEmpresa,
                        usuario = usuario?.Usuario
                    },
                    sessionInfo = sessionInfo
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = ex.Message 
                }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Método de debugging para verificar los datos del usuario en sesión
        /// </summary>
        [HttpGet]
        public JsonResult DebugUsuario()
        {
            try
            {
                var usuarioCompleto = Session["UsuarioCompleto"] as Usuarios;
                var usuarioCompatible = Session["usuario"] as Usuarios;
                var idUsuario = Session["IdUsuario"];

                return Json(new {
                    success = true,
                    usuarioCompleto = usuarioCompleto != null ? new {
                        id = usuarioCompleto.Id,
                        nombre = usuarioCompleto.Nombre,
                        apellidos = usuarioCompleto.Apellidos,
                        correo = usuarioCompleto.Correo,
                        usuario = usuarioCompleto.Usuario,
                        admin = usuarioCompleto.Admin,
                        idEmpresa = usuarioCompleto.IdEmpresa
                    } : null,
                    usuarioCompatible = usuarioCompatible != null ? new {
                        id = usuarioCompatible.Id,
                        nombre = usuarioCompatible.Nombre,
                        apellidos = usuarioCompatible.Apellidos,
                        correo = usuarioCompatible.Correo
                    } : null,
                    idUsuario = idUsuario,
                    sessionKeys = Session.Keys.Cast<string>().ToArray(),
                    debugInfo = UsuarioSesionHelper.ObtenerInfoDebugUsuario()
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = ex.Message 
                }, JsonRequestBehavior.AllowGet);
            }
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
                    ui.Id AS ImagenId,
                    ui.UsuarioId,
                    ui.ImagenBase64,
                    ui.TipoImagen
                FROM Usuarios u
                LEFT JOIN UsuarioImagenes ui ON u.Id = ui.UsuarioId
                WHERE u.Id = @IdUsuario
                ORDER BY ui.Id DESC"; // Obtener la imagen más reciente si hay varias

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
                                    UsuarioId = Convert.ToInt32(reader["UsuarioId"]),
                                    ImagenBase64 = reader["ImagenBase64"] != DBNull.Value ? Convert.ToString(reader["ImagenBase64"]) : null,
                                    TipoImagen = reader["TipoImagen"] != DBNull.Value ? Convert.ToString(reader["TipoImagen"]) : null
                                } : null
                            };
                        }
                    }
                }
            }

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