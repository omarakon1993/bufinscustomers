using bufinscustomers.Models;
using bufinscustomers.Services;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;
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
        private const string PERMISOS_CACHE_KEY = "UsuarioPermisosCodigos";
        private const string MENU_SIDEBAR_KEY = "UsuarioMenuSidebar";

        // Obtener cadena de conexi�n desde Web.config (m�s seguro)
        private static readonly string cadena = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        // Servicio de opciones de menú para verificaciones
        private static readonly MenuOpcionesService _menuOpcionesService = new MenuOpcionesService();

        /// <summary>
        /// Obtiene el usuario actual de la sesi�n con validaci�n de expiraci�n mejorada
        /// </summary>
        public static Usuarios UsuarioActual
        {
            get
            {
                var context = HttpContext.Current;
                if (context?.Session == null) return null;

                // ========== VERIFICAR EXPIRACI�N DE SESI�N ==========
                if (EsSesionExpirada())
                {
                    LimpiarSesion();
                    return null;
                }

                // ========== OBTENER USUARIO DE SESI�N (SIN CONSULTA BD) ==========
                var usuario = context.Session[USUARIO_SESSION_KEY] as Usuarios;
                if (usuario != null)
                {
                    // Actualizar �ltima actividad
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
        /// Establece el usuario en la sesi�n con toda la informaci�n necesaria
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

            // Limpiar caché de permisos para que se recargue con el nuevo usuario
            context.Session.Remove(PERMISOS_CACHE_KEY);
            context.Session.Remove(MENU_SIDEBAR_KEY);
        }

        /// <summary>
        /// Verifica si la sesi�n ha expirado por inactividad
        /// </summary>
        private static bool EsSesionExpirada()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return true;

            var lastActivity = context.Session[LAST_ACTIVITY_KEY] as DateTime?;
            if (!lastActivity.HasValue) return false; // Si no hay marca, no ha expirado a�n

            var timeoutMinutos = context.Session.Timeout;
            var minutosInactivo = DateTime.Now.Subtract(lastActivity.Value).TotalMinutes;
            
            return minutosInactivo > timeoutMinutos;
        }

        /// <summary>
        /// Actualiza la marca de tiempo de �ltima actividad
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
        /// Obtiene informaci�n de actividad de la sesi�n
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
        /// Extiende la sesi�n actualizando la �ltima actividad
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
        /// Limpia completamente la sesi�n
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
        /// M�todo de debugging para verificar los datos del usuario en sesi�n
        /// </summary>
        public static string ObtenerInfoDebugUsuario()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return "Sin contexto de sesi�n";

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
                -- Si hay varias im�genes, puedes traer solo la m�s reciente:
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

        // ========== M�TODOS DE VERIFICACI�N DE ROLES Y PERMISOS ==========

        /// <summary>
        /// Verifica si el usuario actual es Usuario Normal (Admin = 0)
        /// </summary>
        public static bool EsUsuarioNormal()
        {
            return UsuarioActual?.Admin == 0;
        }

        /// <summary>
        /// Verifica si el usuario actual es Admin de Empresa (Admin = 1)
        /// </summary>
        public static bool EsAdminEmpresa()
        {
            return UsuarioActual?.Admin == 1;
        }

        /// <summary>
        /// Verifica si el usuario actual es Super Administrador (Admin = 2)
        /// </summary>
        public static bool EsSuperAdmin()
        {
            return UsuarioActual?.Admin == 2;
        }

        /// <summary>
        /// Verifica si el usuario tiene un permiso espec�fico
        /// Admin 2 siempre retorna true (tiene todos los permisos)
        /// Admin 0 y 1 verifican en la tabla de permisos
        /// </summary>
        public static bool TienePermiso(string codigoPermiso)
        {
            var usuario = UsuarioActual;
            if (usuario == null) return false;

            // Super Admin tiene todos los permisos automáticamente
            if (usuario.Admin == 2) return true;

            // Usar caché de permisos en sesión (una sola consulta, no 11)
            var codigos = HttpContext.Current?.Session[PERMISOS_CACHE_KEY] as HashSet<string>;
            if (codigos == null)
            {
                codigos = _menuOpcionesService.ObtenerCodigosPermisos(usuario.Id);
                HttpContext.Current.Session[PERMISOS_CACHE_KEY] = codigos;
            }
            return codigos.Contains(codigoPermiso);
        }

        /// <summary>
        /// Obtiene el menú del sidebar para el usuario actual, cacheado en sesión
        /// </summary>
        public static List<SidebarCategoriaViewModel> ObtenerMenuSidebar()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return new List<SidebarCategoriaViewModel>();

            var usuario = UsuarioActual;
            if (usuario == null) return new List<SidebarCategoriaViewModel>();

            // Intentar obtener de caché
            var menuCache = context.Session[MENU_SIDEBAR_KEY] as List<SidebarCategoriaViewModel>;
            if (menuCache != null) return menuCache;

            // Construir menú desde BD
            byte nivelAdmin = usuario.Admin ?? 0;
            var opciones = _menuOpcionesService.ObtenerMenuParaUsuario(usuario.Id, nivelAdmin);
            var menu = _menuOpcionesService.ConstruirMenuJerarquico(opciones);

            // Cachear en sesión
            context.Session[MENU_SIDEBAR_KEY] = menu;
            return menu;
        }

        /// <summary>
        /// Invalida la caché de permisos y menú del sidebar (llamar al guardar permisos)
        /// </summary>
        public static void InvalidarCachePermisos()
        {
            var context = HttpContext.Current;
            if (context?.Session == null) return;

            context.Session.Remove(PERMISOS_CACHE_KEY);
            context.Session.Remove(MENU_SIDEBAR_KEY);
        }

        /// <summary>
        /// Obtiene la etiqueta legible del rol del usuario actual
        /// </summary>
        public static string ObtenerNombreRol()
        {
            var usuario = UsuarioActual;
            if (usuario == null) return "Sin sesi�n";

            if (usuario.Admin == 0)
                return "Usuario";
            else if (usuario.Admin == 1)
                return "Administrador de Empresa";
            else if (usuario.Admin == 2)
                return "Super Administrador";
            else
                return "Desconocido";
        }
    }

    /// <summary>
    /// Informaci�n de la sesi�n actual
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
