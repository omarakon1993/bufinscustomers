using bufinscustomers.Models;
using System.Data.SqlClient;
using System;
using System.Web;
using System.Configuration;

namespace bufinscustomers.Helpers
{
    public static class UsuarioSesionHelper
    {
        // ========== CONSTANTES PARA KEYS DE SESIÓN ==========
        private const string USUARIO_SESSION_KEY = "UsuarioCompleto";
        private const string LAST_ACTIVITY_KEY = "LastActivity";
        private const string LOGIN_TIME_KEY = "LoginTime";
        
        // Obtener cadena de conexión desde Web.config (más seguro)
        private static readonly string cadena = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        /// <summary>
        /// Obtiene el usuario actual de la sesión con validación de expiración mejorada
        /// </summary>
        public static Usuarios UsuarioActual
        {
            get
            {
                var context = HttpContext.Current;
                if (context?.Session == null) return null;

                // ========== VERIFICAR EXPIRACIÓN DE SESIÓN ==========
                if (EsSesionExpirada())
                {
                    LimpiarSesion();
                    return null;
                }

                // ========== OBTENER USUARIO DE SESIÓN (SIN CONSULTA BD) ==========
                var usuario = context.Session[USUARIO_SESSION_KEY] as Usuarios;
                if (usuario != null)
                {
                    // Actualizar última actividad
                    ActualizarUltimaActividad();
                    return usuario;
                }

                // ========== BACKWARD COMPATIBILITY ==========
                // Si no hay usuario completo, intentar con IdUsuario (solo una vez)
                var idUsuario = context.Session["IdUsuario"] as int?;
                if (idUsuario.HasValue)
                {
                    usuario = ObtenerUsuarioPorId(idUsuario.Value);
                    if (usuario != null)
                    {
                        EstablecerUsuarioEnSesion(usuario);
                        return usuario;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Establece el usuario en la sesión con toda la información necesaria
        /// </summary>
        public static void EstablecerUsuarioEnSesion(Usuarios usuario)
        {
            var context = HttpContext.Current;
            if (context?.Session == null || usuario == null) return;

            var now = DateTime.Now;
            
            // Almacenar usuario completo en sesión
            context.Session[USUARIO_SESSION_KEY] = usuario;
            context.Session["IdUsuario"] = usuario.Id; // Mantener por compatibilidad
            context.Session["usuario"] = usuario; // Mantener por compatibilidad
            context.Session[LAST_ACTIVITY_KEY] = now;
            context.Session[LOGIN_TIME_KEY] = now;
        }

        /// <summary>
        /// Verifica si la sesión ha expirado por inactividad
        /// </summary>
        private static bool EsSesionExpirada()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return true;

            var lastActivity = context.Session[LAST_ACTIVITY_KEY] as DateTime?;
            if (!lastActivity.HasValue) return false; // Si no hay marca, no ha expirado aún

            var timeoutMinutos = context.Session.Timeout;
            var minutosInactivo = DateTime.Now.Subtract(lastActivity.Value).TotalMinutes;
            
            return minutosInactivo > timeoutMinutos;
        }

        /// <summary>
        /// Actualiza la marca de tiempo de última actividad
        /// </summary>
        private static void ActualizarUltimaActividad()
        {
            var context = HttpContext.Current;
            if (context?.Session != null)
            {
                context.Session[LAST_ACTIVITY_KEY] = DateTime.Now;
            }
        }

        /// <summary>
        /// Obtiene información de actividad de la sesión
        /// </summary>
        public static SessionInfo ObtenerInfoSesion()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return null;

            var lastActivity = context.Session[LAST_ACTIVITY_KEY] as DateTime?;
            var loginTime = context.Session[LOGIN_TIME_KEY] as DateTime?;
            
            return new SessionInfo
            {
                UltimaActividad = lastActivity,
                TiempoLogin = loginTime,
                TimeoutMinutos = context.Session.Timeout,
                MinutosRestantes = lastActivity?.AddMinutes(context.Session.Timeout).Subtract(DateTime.Now).TotalMinutes ?? 0
            };
        }

        /// <summary>
        /// Extiende la sesión actualizando la última actividad
        /// </summary>
        public static bool ExtenderSesion()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return false;

            var usuario = context.Session[USUARIO_SESSION_KEY] as Usuarios;
            if (usuario == null) return false;

            ActualizarUltimaActividad();
            return true;
        }

        /// <summary>
        /// Limpia completamente la sesión
        /// </summary>
        public static void LimpiarSesion()
        {
            var context = HttpContext.Current;
            if (context?.Session != null)
            {
                context.Session.Clear();
                context.Session.Abandon();
            }
        }

        /// <summary>
        /// Verifica si hay un usuario autenticado
        /// </summary>
        public static bool EstaAutenticado()
        {
            return UsuarioActual != null;
        }

        /// <summary>
        /// Verifica si el usuario actual es administrador
        /// </summary>
        public static bool EsAdministrador()
        {
            var usuario = UsuarioActual;
            return usuario?.Admin == 1;
        }

        /// <summary>
        /// Método de debugging para verificar los datos del usuario en sesión
        /// </summary>
        public static string ObtenerInfoDebugUsuario()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return "Sin contexto de sesión";

            var usuario = context.Session[USUARIO_SESSION_KEY] as Usuarios;
            var usuarioCompatible = context.Session["usuario"] as Usuarios;
            var idUsuario = context.Session["IdUsuario"];

            return $"UsuarioCompleto: {(usuario != null ? $"Id:{usuario.Id}, Nombre:{usuario.Nombre}, Apellidos:{usuario.Apellidos}, Correo:{usuario.Correo}" : "NULL")} | " +
                   $"UsuarioCompatible: {(usuarioCompatible != null ? $"Id:{usuarioCompatible.Id}, Nombre:{usuarioCompatible.Nombre}, Apellidos:{usuarioCompatible.Apellidos}" : "NULL")} | " +
                   $"IdUsuario: {idUsuario}";
        }

        private static Usuarios ObtenerUsuarioPorId(int idUsuario)
        {
            Usuarios usuario = null;

            using (SqlConnection connection = new SqlConnection(cadena))
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
                -- Si hay varias imágenes, puedes traer solo la más reciente:
                -- AND ui.Id = (SELECT TOP 1 Id FROM UsuarioImagenes WHERE UsuarioId = u.Id ORDER BY Id DESC)
            ";

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
    }

    /// <summary>
    /// Información de la sesión actual
    /// </summary>
    public class SessionInfo
    {
        public DateTime? UltimaActividad { get; set; }
        public DateTime? TiempoLogin { get; set; }
        public int TimeoutMinutos { get; set; }
        public double MinutosRestantes { get; set; }
        public bool EstaPorExpirar => MinutosRestantes <= 5 && MinutosRestantes > 0;
    }
}
