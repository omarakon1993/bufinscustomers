using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Services;
using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using System.IO;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class UsuarioController : BaseController
    {
        private EmpresaService _empresaService = new EmpresaService();

        // GET: Usuario
        [RequierePermiso("ADMIN_USUARIOS_GESTOR")]
        public ActionResult Usuarios()
        {
            var empresas = _empresaService.ObtenerEmpresas();
            var usuarioActual = UsuarioSesionHelper.UsuarioActual;

            // Admin 0/1 ve su propia empresa y las de su mismo grupo empresarial en los dropdowns
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var idsPermitidosDropdown = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuarioActual) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidosDropdown.Contains(e.Id)).ToList();
            }

            ViewBag.Empresas = empresas;

            List<Usuarios> usuarios;

            if (UsuarioSesionHelper.EsSuperAdmin())
            {
                // Admin 2 ve todos los usuarios
                usuarios = GetUsuariosFromStoredProcedure();
            }
            else
            {
                // Admin 0 o 1: consulta directa filtrada por empresa(s), evita cargar todos los usuarios
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuarioActual) ?? new List<int>();
                usuarios = GetUsuariosPorEmpresas(idsPermitidos);
            }

            // Pasar información de permisos al ViewBag para la vista
            ViewBag.EsSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();
            ViewBag.EsAdminEmpresa = UsuarioSesionHelper.EsAdminEmpresa();
            ViewBag.PuedeCrear = UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.TienePermiso("ADMIN_USUARIOS_GESTOR");
            ViewBag.PuedeEditar = UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.TienePermiso("ADMIN_USUARIOS_GESTOR");
            ViewBag.PuedeEliminar = UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.TienePermiso("ADMIN_USUARIOS_GESTOR");

            return View("~/Views/Configuracion/Usuarios.cshtml", usuarios);
        }

        private List<Usuarios> GetUsuariosPorEmpresas(List<int> idsEmpresa)
        {
            var lista = new List<Usuarios>();
            if (idsEmpresa == null || idsEmpresa.Count == 0)
                return lista;

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var nombresParametros = new List<string>();
                var cmd = new SqlCommand();
                for (int i = 0; i < idsEmpresa.Count; i++)
                {
                    var nombreParametro = "@IdEmpresa" + i;
                    nombresParametros.Add(nombreParametro);
                    cmd.Parameters.AddWithValue(nombreParametro, idsEmpresa[i]);
                }

                cmd.CommandText = @"
                    SELECT u.Id, u.Usuario, u.Clave, u.Nombre, u.Apellidos, u.Correo,
                           u.Telefono, u.Admin, u.IdEmpresa,
                           CASE WHEN EXISTS (SELECT 1 FROM UsuarioImagenes WHERE UsuarioId = u.Id)
                                THEN 1 ELSE NULL END AS ImagenBase64,
                           ISNULL(u.LimiteConsultasIA, 0) AS LimiteConsultasIA
                    FROM Usuarios u
                    WHERE u.IdEmpresa IN (" + string.Join(",", nombresParametros) + @")
                      AND (u.Admin IS NULL OR u.Admin <> 2)
                    ORDER BY u.Id";
                cmd.Connection = cn;
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Usuarios
                        {
                            Id        = Convert.ToInt32(reader["Id"]),
                            Usuario   = reader["Usuario"].ToString(),
                            Clave     = reader["Clave"].ToString(),
                            Nombre    = reader["Nombre"].ToString(),
                            Apellidos = reader["Apellidos"].ToString(),
                            Correo    = reader["Correo"].ToString(),
                            Telefono  = reader["Telefono"].ToString(),
                            Admin     = reader["Admin"] != DBNull.Value ? (byte?)reader["Admin"] : null,
                            IdEmpresa = reader["IdEmpresa"] != DBNull.Value ? (int?)reader["IdEmpresa"] : null,
                            Imagen    = reader["ImagenBase64"] != DBNull.Value
                                            ? new ImagenUsuario { UsuarioId = Convert.ToInt32(reader["Id"]) }
                                            : null,
                            LimiteConsultasIA = reader["LimiteConsultasIA"] != DBNull.Value
                                            ? (int?)Convert.ToInt32(reader["LimiteConsultasIA"])
                                            : 0
                        });
                    }
                }
            }
            return lista;
        }

        private List<Usuarios> GetUsuariosFromStoredProcedure()
        {
            List<Usuarios> usuarios = new List<Usuarios>();

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerUsuarios", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Usuarios usuario = new Usuarios();
                            usuario.Id = (int)reader["Id"];
                            usuario.Usuario = (string)reader["Usuario"];
                            usuario.Clave = (string)reader["Clave"];
                            usuario.Nombre = (string)reader["Nombre"];
                            usuario.Apellidos = (string)reader["Apellidos"];
                            usuario.Correo = (string)reader["Correo"];
                            usuario.Telefono = (string)reader["Telefono"];
                            usuario.Admin = reader["Admin"] != DBNull.Value ? (byte?)reader["Admin"] : null;
                            usuario.IdEmpresa = reader["IdEmpresa"] != DBNull.Value ? (int?)reader["IdEmpresa"] : null;

                            // Detectar si el usuario tiene imagen (sin cargar el Base64 en memoria)
                            try
                            {
                                reader.GetOrdinal("ImagenBase64"); // valida que la columna existe
                                if (reader["ImagenBase64"] != DBNull.Value)
                                {
                                    usuario.Imagen = new ImagenUsuario { UsuarioId = usuario.Id };
                                }
                            }
                            catch (IndexOutOfRangeException)
                            {
                                usuario.Imagen = null;
                            }

                            try
                            {
                                reader.GetOrdinal("LimiteConsultasIA");
                                usuario.LimiteConsultasIA = reader["LimiteConsultasIA"] != DBNull.Value
                                    ? (int?)Convert.ToInt32(reader["LimiteConsultasIA"]) : 0;
                            }
                            catch (IndexOutOfRangeException)
                            {
                                usuario.LimiteConsultasIA = 0;
                            }

                            usuarios.Add(usuario);
                        }
                    }
                }
            }

            return usuarios.OrderBy(u => u.Id).ToList();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_USUARIOS_GESTOR")]
        public ActionResult EliminarUsuario(int idUsuario)
        {
            try
            {
                // Si no es Admin 2, verificar que el usuario a eliminar sea de su empresa
                if (!UsuarioSesionHelper.EsSuperAdmin())
                {
                    var usuarioService = new UsuarioService();
                    var usuarioAEliminar = usuarioService.ObtenerUsuarioPorId(idUsuario);

                    if (usuarioAEliminar != null)
                    {
                        bool tieneAcceso = usuarioAEliminar.IdEmpresa.HasValue
                            && EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, usuarioAEliminar.IdEmpresa.Value);
                        if (!tieneAcceso)
                        {
                            SetErrorMessage("No tienes permisos para eliminar usuarios de otras empresas.");
                            return RedirectToAction("Usuarios");
                        }

                        // No permitir eliminar Admin 2
                        if (usuarioAEliminar.Admin == 2)
                        {
                            SetErrorMessage("No puedes eliminar un Super Administrador.");
                            return RedirectToAction("Usuarios");
                        }
                    }
                }

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                {
                    using (SqlCommand command = new SqlCommand("sp_EliminarUsuario", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Id", idUsuario);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                SetSuccessMessage("Usuario eliminado correctamente.");
                new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_UsuarioEliminado"), null, "warning", "/Usuario/Usuarios");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Usuarios, AuditoriaAccion.Eliminar,
                    "Usuarios", idUsuario.ToString(), $"Usuario eliminado (Id {idUsuario})");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al eliminar el usuario: " + ex.Message);
            }

            return RedirectToAction("Usuarios");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_USUARIOS_GESTOR")]
        public ActionResult EditarUsuario(Usuarios oUsuario, HttpPostedFileBase ImagenUsuario)
        {
            // Solo Admin 2 puede asignar Admin 2
            if (oUsuario.Admin == 2 && !UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("Solo los Super Administradores pueden asignar el rol de Super Administrador.");
                return RedirectToAction("Usuarios");
            }

            // Si no es Admin 2, solo puede editar usuarios de su empresa o de su mismo grupo empresarial
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                bool tieneAcceso = oUsuario.IdEmpresa.HasValue
                    && EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, oUsuario.IdEmpresa.Value);
                if (!tieneAcceso)
                {
                    SetErrorMessage("No tienes permisos para editar usuarios de otras empresas.");
                    return RedirectToAction("Usuarios");
                }
            }

            oUsuario.Telefono = oUsuario.Telefono == null ? "" : oUsuario.Telefono;

            // Capturar el rol actual antes de actualizar, para detectar si cambia
            var usuarioService = new UsuarioService();
            var usuarioAntesDeEditar = usuarioService.ObtenerUsuarioPorId(oUsuario.Id);
            byte? adminAnterior = usuarioAntesDeEditar?.Admin ?? 0;

            try
            {
                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                {
                    using (SqlCommand command = new SqlCommand("sp_EditarUsuario", connection))
                    {
                        if (oUsuario.Admin == null)
                        {
                            oUsuario.Admin = 0;
                        }

                        // Validar que Admin esté en rango válido (0, 1, 2)
                        if (oUsuario.Admin < 0 || oUsuario.Admin > 2)
                        {
                            oUsuario.Admin = 0;
                        }

                        // Super Admin → empresa principal (Bufins) por defecto si no se indicó otra.
                        if (oUsuario.Admin == 2 && (oUsuario.IdEmpresa ?? 0) == 0)
                        {
                            var idPrincipal = new EmpresaService().ObtenerIdEmpresaPrincipal();
                            if (idPrincipal.HasValue) oUsuario.IdEmpresa = idPrincipal.Value;
                        }

                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Id", oUsuario.Id);
                        command.Parameters.AddWithValue("@Nombre", oUsuario.Nombre);
                        command.Parameters.AddWithValue("@Apellidos", oUsuario.Apellidos);
                        command.Parameters.AddWithValue("@Correo", oUsuario.Correo);
                        command.Parameters.AddWithValue("@Telefono", oUsuario.Telefono);
                        command.Parameters.AddWithValue("@Admin", oUsuario.Admin);
                        command.Parameters.AddWithValue("@IdEmpresa", oUsuario.IdEmpresa);
                        command.Parameters.AddWithValue("@Usuario", oUsuario.Usuario);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }

                    // Si el rol cambió, reiniciar los permisos de menú a los del nuevo rol por defecto
                    if (oUsuario.Admin != adminAnterior)
                    {
                        AsignarPermisosPorDefecto(oUsuario.Id, oUsuario.Admin);
                        new NotificacionesService().Crear(
                            UsuarioSesionHelper.UsuarioActual?.Id ?? 0,
                            R("Notif_PermisosReiniciados"),
                            $"{oUsuario.Nombre} {oUsuario.Apellidos}".Trim(),
                            "warning",
                            "/Usuario/Usuarios");
                    }

                    // Actualizar límite de consultas IA (solo admins pueden setearlo, max 10)
                    if (oUsuario.LimiteConsultasIA.HasValue &&
                        (UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.EsAdminEmpresa()))
                    {
                        int limite = Math.Min(10, Math.Max(0, oUsuario.LimiteConsultasIA.Value));
                        using (SqlCommand cmdLimite = new SqlCommand(
                            "UPDATE Usuarios SET LimiteConsultasIA = @Limite WHERE Id = @Id", connection))
                        {
                            cmdLimite.Parameters.AddWithValue("@Limite", limite);
                            cmdLimite.Parameters.AddWithValue("@Id", oUsuario.Id);
                            cmdLimite.ExecuteNonQuery();
                        }
                    }

                    // Procesar imagen si se cargó una
                    if (ImagenUsuario != null && ImagenUsuario.ContentLength > 0)
                    {
                        GuardarImagenUsuario(oUsuario.Id, ImagenUsuario);
                    }
                }

                SetSuccessMessage("Usuario actualizado correctamente.");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Usuarios, AuditoriaAccion.Editar,
                    "Usuarios", oUsuario.Id.ToString(),
                    $"Usuario editado: {oUsuario.Correo} (rol {oUsuario.Admin})", null,
                    new { oUsuario.Id, oUsuario.Nombre, oUsuario.Apellidos, oUsuario.Correo, oUsuario.Usuario, oUsuario.Admin, oUsuario.IdEmpresa },
                    idEmpresa: oUsuario.IdEmpresa);
                EnviarCorreoUsuarioActualizado(oUsuario);
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al actualizar el usuario: " + ex.Message);
            }

            return RedirectToAction("Usuarios");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_USUARIOS_GESTOR")]
        public ActionResult Registrar(Usuarios oUsuario, HttpPostedFileBase ImagenUsuario)
        {
            bool registrado;
            string mensaje;
            int usuarioId = 0;

            if (!EsUsuarioValido(oUsuario.Usuario))
            {
                SetErrorMessage("El nombre de usuario debe ser en minúsculas, sin espacios, puede contener números y puntos, y debe tener entre 4 y 20 caracteres.");
                return RedirectToAction("Usuarios");
            }

            // Solo Admin 2 puede crear Admin 2
            if (oUsuario.Admin == 2 && !UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage("Solo los Super Administradores pueden crear Super Administradores.");
                return RedirectToAction("Usuarios");
            }

            // Si no es Admin 2, solo puede crear usuarios de su empresa o de su mismo grupo empresarial
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var usuarioActual = UsuarioSesionHelper.UsuarioActual;
                bool idEmpresaValida = oUsuario.IdEmpresa.HasValue
                    && EmpresaAccesoHelper.TieneAcceso(usuarioActual, oUsuario.IdEmpresa.Value);
                if (!idEmpresaValida)
                {
                    oUsuario.IdEmpresa = usuarioActual.IdEmpresa; // Empresa principal por defecto
                }
            }

            // Elimina espacios de campos
            oUsuario.Nombre = oUsuario.Nombre == null ? "" : oUsuario.Nombre.Trim();
            oUsuario.Apellidos = oUsuario.Apellidos == null ? "" : oUsuario.Apellidos.Trim();
            oUsuario.Correo = oUsuario.Correo == null ? "" : oUsuario.Correo.Trim();
            oUsuario.Telefono = oUsuario.Telefono == null ? "" : oUsuario.Telefono;
            oUsuario.IdEmpresa = oUsuario.IdEmpresa == null ? 0 : oUsuario.IdEmpresa;

            oUsuario.Usuario = oUsuario.Usuario.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();
            oUsuario.ConfirmarClave = oUsuario.ConfirmarClave.Trim();

            if (oUsuario.Clave != oUsuario.ConfirmarClave)
            {
                SetErrorMessage("Las contraseñas no coinciden");
                return RedirectToAction("Usuarios");
            }

            string mensajeClave;
            if (!EsClaveSegura(oUsuario.Clave, oUsuario.Usuario, out mensajeClave))
            {
                SetErrorMessage(mensajeClave);
                return RedirectToAction("Usuarios");
            }

            oUsuario.Clave = HashearContrasena(oUsuario.Clave);

            if (oUsuario.Admin == null)
            {
                oUsuario.Admin = 0;
            }

            // Validar que Admin esté en rango válido (0, 1, 2)
            if (oUsuario.Admin < 0 || oUsuario.Admin > 2)
            {
                oUsuario.Admin = 0;
            }

            // Los Super Admin se asocian por defecto a la empresa principal (Bufins)
            // cuando no se indicó otra.
            if (oUsuario.Admin == 2 && (oUsuario.IdEmpresa ?? 0) == 0)
            {
                var idPrincipal = new EmpresaService().ObtenerIdEmpresaPrincipal();
                if (idPrincipal.HasValue) oUsuario.IdEmpresa = idPrincipal.Value;
            }

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarUsuario", cn);
                cmd.Parameters.AddWithValue("Usuario", oUsuario.Usuario);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.Parameters.AddWithValue("Nombre", oUsuario.Nombre);
                cmd.Parameters.AddWithValue("Apellidos", oUsuario.Apellidos);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Telefono", oUsuario.Telefono);
                cmd.Parameters.AddWithValue("Admin", oUsuario.Admin);
                cmd.Parameters.AddWithValue("IdEmpresa", oUsuario.IdEmpresa);
                cmd.Parameters.Add("Registrado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("Mensaje", SqlDbType.VarChar, 100).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                registrado = Convert.ToBoolean(cmd.Parameters["Registrado"].Value);
                mensaje = cmd.Parameters["Mensaje"].Value.ToString();
                if (registrado)
                {
                    // SP_RegistrarUsuario no devuelve el Id; lo obtenemos con una consulta directa
                    using (SqlCommand cmdGetId = new SqlCommand(
                        "SELECT Id FROM Usuarios WHERE Usuario = @Usuario", cn))
                    {
                        cmdGetId.Parameters.AddWithValue("@Usuario", oUsuario.Usuario);
                        var idResult = cmdGetId.ExecuteScalar();
                        if (idResult != null && idResult != DBNull.Value)
                            usuarioId = Convert.ToInt32(idResult);
                    }

                    // Asignar límite de consultas IA al usuario recién creado
                    if (usuarioId > 0 && oUsuario.LimiteConsultasIA.HasValue &&
                        (UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.EsAdminEmpresa()))
                    {
                        int limite = Math.Min(10, Math.Max(0, oUsuario.LimiteConsultasIA.Value));
                        using (SqlCommand cmdLimite = new SqlCommand(
                            "UPDATE Usuarios SET LimiteConsultasIA = @Limite WHERE Id = @Id", cn))
                        {
                            cmdLimite.Parameters.AddWithValue("@Limite", limite);
                            cmdLimite.Parameters.AddWithValue("@Id", usuarioId);
                            cmdLimite.ExecuteNonQuery();
                        }
                    }
                }
            }

            if (registrado)
            {
                // Asignar automáticamente los permisos de menú por defecto según el tipo de usuario
                if (usuarioId > 0)
                {
                    AsignarPermisosPorDefecto(usuarioId, oUsuario.Admin);
                }

                // Procesar imagen si se cargó una y el usuario se registró correctamente
                if (ImagenUsuario != null && ImagenUsuario.ContentLength > 0 && usuarioId > 0)
                {
                    try
                    {
                        GuardarImagenUsuario(usuarioId, ImagenUsuario);
                    }
                    catch (Exception ex)
                    {
                        SetErrorMessage("Usuario creado pero error al guardar imagen: " + ex.Message);
                        return RedirectToAction("Usuarios");
                    }
                }
                SetSuccessMessage(mensaje);
                new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_UsuarioCreado"), $"{oUsuario.Nombre} {oUsuario.Apellidos}".Trim(), "success", "/Usuario/Usuarios");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Usuarios, AuditoriaAccion.Crear,
                    "Usuarios", usuarioId.ToString(),
                    $"Usuario creado: {oUsuario.Correo} (rol {oUsuario.Admin})", null,
                    new { Id = usuarioId, oUsuario.Nombre, oUsuario.Apellidos, oUsuario.Correo, oUsuario.Usuario, oUsuario.Admin, oUsuario.IdEmpresa },
                    idEmpresa: oUsuario.IdEmpresa);
                EnviarCorreoBienvenida(oUsuario);
            }
            else
            {
                SetErrorMessage(mensaje);
            }

            return RedirectToAction("Usuarios");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_USUARIOS_GESTOR")]
        public ActionResult CambiarClave(int idUsuario, string nuevaClave, string confirmarNuevaClave)
        {
            var usuarioService = new UsuarioService();
            var usuarioDestino = usuarioService.ObtenerUsuarioPorId(idUsuario);

            // Si no es SuperAdmin, solo puede cambiar claves de usuarios de su empresa o de su mismo grupo empresarial
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                bool tieneAcceso = usuarioDestino != null && usuarioDestino.IdEmpresa.HasValue
                    && EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, usuarioDestino.IdEmpresa.Value);
                if (!tieneAcceso)
                {
                    SetErrorMessage("No tienes permisos para cambiar la clave de usuarios de otras empresas.");
                    return RedirectToAction("Usuarios");
                }
                if (usuarioDestino.Admin == 2)
                {
                    SetErrorMessage("No puedes cambiar la clave de un Super Administrador.");
                    return RedirectToAction("Usuarios");
                }
            }

            // Validar que las claves coincidan
            if (nuevaClave.Trim() != confirmarNuevaClave.Trim())
            {
                SetErrorMessage("Las contraseñas no coinciden.");
                return RedirectToAction("Usuarios");
            }

            string mensajeClave;
            if (!EsClaveSegura(nuevaClave.Trim(), usuarioDestino?.Usuario, out mensajeClave))
            {
                SetErrorMessage(mensajeClave);
                return RedirectToAction("Usuarios");
            }

            try
            {
                string claveEncriptada = HashearContrasena(nuevaClave.Trim());

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                {
                    using (SqlCommand command = new SqlCommand("sp_CambiarClaveUsuario", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@IdUsuario", idUsuario);
                        command.Parameters.AddWithValue("@NuevaClave", claveEncriptada);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }

                SetSuccessMessage("Clave actualizada correctamente.");
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al cambiar la clave: " + ex.Message);
            }

            return RedirectToAction("Usuarios");
        }

        // Botón manual "Enviar información por correo" en el gestor: reenvía al usuario los mismos
        // datos de cuenta que recibió al crearse (sin contraseña).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_USUARIOS_GESTOR")]
        public JsonResult EnviarInfoCorreo(int idUsuario)
        {
            var usuarioService = new UsuarioService();
            var usuarioDestino = usuarioService.ObtenerUsuarioPorId(idUsuario);

            if (usuarioDestino == null)
            {
                return Json(new { success = false, message = R("Usr_ErrorNoEncontrado") });
            }

            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                bool tieneAcceso = usuarioDestino.IdEmpresa.HasValue
                    && EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, usuarioDestino.IdEmpresa.Value);
                if (!tieneAcceso)
                {
                    return Json(new { success = false, message = R("Usr_ErrorSinPermiso") });
                }
            }

            if (string.IsNullOrWhiteSpace(usuarioDestino.Correo))
            {
                return Json(new { success = false, message = R("Usr_ErrorSinCorreo") });
            }

            bool enviado = EnviarCorreoBienvenida(usuarioDestino, esReenvio: true);

            return Json(new
            {
                success = enviado,
                message = enviado ? R("Usr_InfoCorreoEnviado") : R("Usr_ErrorInfoCorreo")
            });
        }

        /// <summary>
        /// Asigna al usuario todas las opciones de menú que le apliquen por defecto según su rol.
        /// Reemplaza cualquier asignación previa (usado tanto en creación como al cambiar de rol).
        /// Admin 2 (Super Admin) no requiere asignación: siempre tiene acceso total.
        /// </summary>
        private void AsignarPermisosPorDefecto(int idUsuario, byte? nivelAdmin)
        {
            if (nivelAdmin == 2) return;

            var menuService = new MenuOpcionesService();
            var todasLasOpciones = menuService.ObtenerTodas();

            List<int> idsOpcionesPorDefecto = (nivelAdmin == 1)
                // Admin de Empresa: todas las opciones excepto las exclusivas de Super Admin
                ? todasLasOpciones.Where(o => !o.SoloSuperAdmin).Select(o => o.Id).ToList()
                // Usuario Normal: todas las opciones excepto las de Super Admin y Admin de Empresa
                : todasLasOpciones.Where(o => !o.SoloSuperAdmin && !o.SoloAdminEmpresa).Select(o => o.Id).ToList();

            menuService.GuardarOpcionesUsuario(idUsuario, idsOpcionesPorDefecto, UsuarioSesionHelper.UsuarioActual?.Id ?? 0);
        }

        private bool EsUsuarioValido(string usuario)
        {
            // Solo letras minúsculas, números y puntos, sin espacios, empieza con letra, 4-20 caracteres
            return System.Text.RegularExpressions.Regex.IsMatch(usuario, @"^[a-z][a-z0-9.]{3,19}$");
        }

        // Política de contraseñas centralizada en Helpers.PoliticaContrasena (longitud mínima 12,
        // complejidad, sin 3+ repetidos, sin claves comunes ni el nombre de usuario, y sin
        // aparecer en filtraciones conocidas — Have I Been Pwned). Devuelve el mensaje ya
        // traducido a la cultura activa.
        private bool EsClaveSegura(string clave, string usuario, out string mensajeError)
        {
            if (PoliticaContrasena.Validar(clave, usuario, out string errKey))
            {
                mensajeError = null;
                return true;
            }
            mensajeError = R(errKey);
            return false;
        }

        /// <summary>
        /// Envía al correo del usuario sus datos de cuenta (sin la contraseña — nunca se envía
        /// en texto plano) más la leyenda de cómo recuperar/cambiar la clave. Usado tanto al
        /// crear el usuario (fire-and-forget, <paramref name="esReenvio"/>=false) como desde el
        /// botón manual "Enviar información por correo" del gestor (<paramref name="esReenvio"/>=true,
        /// donde sí importa el resultado). Cada caso usa su propio asunto/texto introductorio.
        /// Nunca lanza excepción.
        /// </summary>
        private bool EnviarCorreoBienvenida(Usuarios oUsuario, bool esReenvio = false)
        {
            if (string.IsNullOrWhiteSpace(oUsuario.Correo))
                return false;

            try
            {
                string nombreEmpresa = _empresaService.ObtenerEmpresas()
                    .FirstOrDefault(e => e.Id == oUsuario.IdEmpresa)?.Nombre;
                string nombreCompleto = $"{oUsuario.Nombre} {oUsuario.Apellidos}".Trim();
                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string enlaceLogin = Url.Action("Login", "Acceso", null, Request.Url.Scheme);

                var emailService = new EmailService();
                if (esReenvio)
                {
                    emailService.EnviarReenvioInfoUsuario(
                        oUsuario.Correo, nombreCompleto, oUsuario.Usuario, nombreEmpresa, oUsuario.Telefono, enlaceLogin, esIngles);
                }
                else
                {
                    emailService.EnviarBienvenidaUsuario(
                        oUsuario.Correo, nombreCompleto, oUsuario.Usuario, nombreEmpresa, oUsuario.Telefono, enlaceLogin, esIngles);
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("[UsuarioController] Error al enviar correo de bienvenida: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Notifica al usuario que un administrador actualizó su información de cuenta.
        /// Fire-and-forget: nunca lanza excepción.
        /// </summary>
        private bool EnviarCorreoUsuarioActualizado(Usuarios oUsuario)
        {
            if (oUsuario == null || string.IsNullOrWhiteSpace(oUsuario.Correo))
                return false;

            try
            {
                string nombreEmpresa = _empresaService.ObtenerEmpresas()
                    .FirstOrDefault(e => e.Id == oUsuario.IdEmpresa)?.Nombre;
                string nombreCompleto = $"{oUsuario.Nombre} {oUsuario.Apellidos}".Trim();
                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string enlaceLogin = Url.Action("Login", "Acceso", null, Request.Url.Scheme);

                new EmailService().EnviarNotificacionUsuarioActualizado(
                    oUsuario.Correo, nombreCompleto, oUsuario.Usuario, nombreEmpresa, oUsuario.Telefono, enlaceLogin, esIngles);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("[UsuarioController] Error al enviar correo de usuario actualizado: {0}", ex.Message);
                return false;
            }
        }

        private void GuardarImagenUsuario(int usuarioId, HttpPostedFileBase imagenArchivo)
        {
            if (imagenArchivo != null && imagenArchivo.ContentLength > 0)
            {
                // Convertir la imagen a base64
                using (var ms = new MemoryStream())
                {
                    imagenArchivo.InputStream.CopyTo(ms);
                    var bytes = ms.ToArray();
                    var base64 = Convert.ToBase64String(bytes);
                    var tipoImagen = imagenArchivo.ContentType;
                    var nombreImagen = Path.GetFileName(imagenArchivo.FileName);

                    using (SqlConnection connection = new SqlConnection(CadenaConexion))
                    {
                        using (SqlCommand command = new SqlCommand("sp_GuardarImagenUsuario", connection))
                        {
                            command.CommandType = CommandType.StoredProcedure;
                            command.Parameters.AddWithValue("@UsuarioId", usuarioId);
                            command.Parameters.AddWithValue("@NombreImagen", nombreImagen);
                            command.Parameters.AddWithValue("@TipoImagen", tipoImagen);
                            command.Parameters.AddWithValue("@ImagenBase64", base64);

                            connection.Open();
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CargarImagenUsuario(HttpPostedFileBase ImagenUsuario)
        {
            if (ImagenUsuario != null && ImagenUsuario.ContentLength > 0)
            {
                var usuarioId = UsuarioSesionHelper.UsuarioActual.Id;
                GuardarImagenUsuario(usuarioId, ImagenUsuario);
                return Json(new { success = true, message = "Imagen de usuario actualizada correctamente.", usuarioId });
            }
            return Json(new { success = false, message = "Por favor, selecciona una imagen válida." });
        }

        [HttpGet]
        public ActionResult Imagen(int id)
        {
            if (UsuarioSesionHelper.UsuarioActual == null)
                return new HttpStatusCodeResult(System.Net.HttpStatusCode.Unauthorized);

            string base64 = null;
            string tipoImagen = null;

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    "SELECT TOP 1 ImagenBase64, TipoImagen FROM UsuarioImagenes WHERE UsuarioId = @Id ORDER BY Id DESC",
                    cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        base64 = reader["ImagenBase64"] != DBNull.Value ? reader["ImagenBase64"].ToString() : null;
                        tipoImagen = reader["TipoImagen"] != DBNull.Value ? reader["TipoImagen"].ToString() : null;
                    }
                }
            }

            if (string.IsNullOrEmpty(base64) || string.IsNullOrEmpty(tipoImagen))
                return HttpNotFound();

            var bytes = Convert.FromBase64String(base64);
            Response.Cache.SetCacheability(HttpCacheability.Private);
            Response.Cache.SetMaxAge(TimeSpan.FromHours(1));
            return File(bytes, tipoImagen);
        }
    }
}
