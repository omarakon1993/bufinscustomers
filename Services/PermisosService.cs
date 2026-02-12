using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Servicio para gestionar opciones de menú y permisos de usuarios
    /// </summary>
    public class MenuOpcionesService : BaseService
    {
        /// <summary>
        /// Obtiene las opciones de menú de un usuario con estado (asignado o no asignado)
        /// Útil para la interfaz de gestión de opciones de menú
        /// </summary>
        public List<OpcionMenuUsuarioViewModel> ObtenerOpcionesConEstado(int idUsuario)
        {
            var opciones = new List<OpcionMenuUsuarioViewModel>();

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand cmd = new SqlCommand("sp_ObtenerOpcionesMenuUsuarioConEstado", cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var vm = new OpcionMenuUsuarioViewModel
                            {
                                IdMenuOpcion = Convert.ToInt32(reader["Id"]),
                                Codigo = reader["Codigo"].ToString(),
                                Nombre = reader["Nombre"].ToString(),
                                Descripcion = reader["Descripcion"] != DBNull.Value ? reader["Descripcion"].ToString() : null,
                                Categoria = reader["Categoria"].ToString(),
                                Icono = reader["Icono"] != DBNull.Value ? reader["Icono"].ToString() : null,
                                Controller = reader["Controller"] != DBNull.Value ? reader["Controller"].ToString() : null,
                                Action = reader["Action"] != DBNull.Value ? reader["Action"].ToString() : null,
                                Asignado = Convert.ToBoolean(reader["Asignado"])
                            };

                            try
                            {
                                vm.NombreGrupo = reader["NombreGrupo"] != DBNull.Value ? reader["NombreGrupo"].ToString() : null;
                                vm.IconoGrupo = reader["IconoGrupo"] != DBNull.Value ? reader["IconoGrupo"].ToString() : null;
                                vm.IconoCategoria = reader["IconoCategoria"] != DBNull.Value ? reader["IconoCategoria"].ToString() : null;
                                vm.OrdenCategoria = reader["OrdenCategoria"] != DBNull.Value ? Convert.ToInt32(reader["OrdenCategoria"]) : 0;
                            }
                            catch (IndexOutOfRangeException) { }

                            opciones.Add(vm);
                        }
                    }
                }
            }

            return opciones;
        }

        /// <summary>
        /// Lee una fila del reader y construye un objeto MenuOpciones con todas las columnas disponibles
        /// </summary>
        private MenuOpciones LeerMenuOpcion(SqlDataReader reader)
        {
            var opcion = new MenuOpciones
            {
                Id = Convert.ToInt32(reader["Id"]),
                Codigo = reader["Codigo"].ToString(),
                Nombre = reader["Nombre"].ToString(),
                Descripcion = reader["Descripcion"] != DBNull.Value ? reader["Descripcion"].ToString() : null,
                Categoria = reader["Categoria"].ToString(),
                Icono = reader["Icono"] != DBNull.Value ? reader["Icono"].ToString() : null,
                Orden = Convert.ToInt32(reader["Orden"]),
                Controller = reader["Controller"] != DBNull.Value ? reader["Controller"].ToString() : null,
                Action = reader["Action"] != DBNull.Value ? reader["Action"].ToString() : null,
                URL = reader["URL"] != DBNull.Value ? reader["URL"].ToString() : null,
                TienePadre = Convert.ToBoolean(reader["TienePadre"]),
                IdPadre = reader["IdPadre"] != DBNull.Value ? (int?)Convert.ToInt32(reader["IdPadre"]) : null,
                NivelMinimo = Convert.ToByte(reader["NivelMinimo"]),
                SoloSuperAdmin = Convert.ToBoolean(reader["SoloSuperAdmin"]),
                Activo = Convert.ToBoolean(reader["Activo"])
            };

            // Leer columnas del sidebar dinámico si existen
            try
            {
                opcion.NombreGrupo = reader["NombreGrupo"] != DBNull.Value ? reader["NombreGrupo"].ToString() : null;
                opcion.IconoGrupo = reader["IconoGrupo"] != DBNull.Value ? reader["IconoGrupo"].ToString() : null;
                opcion.IconoCategoria = reader["IconoCategoria"] != DBNull.Value ? reader["IconoCategoria"].ToString() : null;
                opcion.OrdenCategoria = reader["OrdenCategoria"] != DBNull.Value ? Convert.ToInt32(reader["OrdenCategoria"]) : 0;
            }
            catch (IndexOutOfRangeException)
            {
                // Las columnas del sidebar aún no existen en la BD
            }

            return opcion;
        }

        /// <summary>
        /// Obtiene las opciones de menú para el sidebar de un usuario (una sola consulta)
        /// Admin 2: todas las opciones activas. Admin 0/1: solo las asignadas.
        /// </summary>
        public List<MenuOpciones> ObtenerMenuParaUsuario(int idUsuario, byte nivelAdmin)
        {
            var opciones = new List<MenuOpciones>();

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand cmd = new SqlCommand("sp_ObtenerMenuUsuario", cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmd.Parameters.AddWithValue("@NivelAdmin", nivelAdmin);
                    cn.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            opciones.Add(LeerMenuOpcion(reader));
                        }
                    }
                }
            }

            return opciones;
        }

        /// <summary>
        /// Obtiene los códigos de permisos asignados a un usuario como HashSet (para caché en sesión)
        /// </summary>
        public HashSet<string> ObtenerCodigosPermisos(int idUsuario)
        {
            var codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand cmd = new SqlCommand("sp_ObtenerCodigosPermisosUsuario", cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            codigos.Add(reader["Codigo"].ToString());
                        }
                    }
                }
            }

            return codigos;
        }

        /// <summary>
        /// Construye la estructura jerárquica del sidebar: Categoría → Grupo → Items
        /// </summary>
        public List<SidebarCategoriaViewModel> ConstruirMenuJerarquico(List<MenuOpciones> opciones)
        {
            var categorias = new List<SidebarCategoriaViewModel>();

            // Agrupar por Categoria, mantener orden
            var grupos = opciones
                .GroupBy(o => new { o.Categoria, o.IconoCategoria, o.OrdenCategoria })
                .OrderBy(g => g.Key.OrdenCategoria);

            foreach (var catGroup in grupos)
            {
                var categoria = new SidebarCategoriaViewModel
                {
                    Nombre = catGroup.Key.Categoria,
                    Icono = catGroup.Key.IconoCategoria ?? "fas fa-folder",
                    OrdenCategoria = catGroup.Key.OrdenCategoria
                };

                // Agrupar por NombreGrupo dentro de la categoría
                var gruposPorNombre = catGroup
                    .GroupBy(o => new { o.NombreGrupo, o.IconoGrupo })
                    .OrderBy(g => g.Min(o => o.Orden));

                foreach (var grupoGroup in gruposPorNombre)
                {
                    var grupo = new SidebarGrupoViewModel
                    {
                        Nombre = grupoGroup.Key.NombreGrupo ?? "General",
                        Icono = grupoGroup.Key.IconoGrupo ?? "fas fa-circle"
                    };

                    foreach (var opcion in grupoGroup.OrderBy(o => o.Orden))
                    {
                        grupo.Items.Add(new SidebarItemViewModel
                        {
                            Id = opcion.Id,
                            Codigo = opcion.Codigo,
                            Nombre = opcion.Nombre,
                            Controller = opcion.Controller,
                            Action = opcion.Action,
                            URL = opcion.URL,
                            Icono = opcion.Icono
                        });
                    }

                    categoria.Grupos.Add(grupo);
                }

                categorias.Add(categoria);
            }

            return categorias;
        }

        /// <summary>
        /// Guarda todas las opciones de menú de un usuario (elimina anteriores y asigna nuevas)
        /// </summary>
        public void GuardarOpcionesUsuario(int idUsuario, List<int> idsOpciones, int usuarioAsigno)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction transaction = cn.BeginTransaction())
                {
                    try
                    {
                        // Eliminar opciones existentes
                        using (SqlCommand cmdDelete = new SqlCommand(
                            "DELETE FROM UsuarioMenuPermisos WHERE IdUsuario = @IdUsuario", cn, transaction))
                        {
                            cmdDelete.Parameters.AddWithValue("@IdUsuario", idUsuario);
                            cmdDelete.ExecuteNonQuery();
                        }

                        // Insertar nuevas opciones
                        if (idsOpciones != null && idsOpciones.Count > 0)
                        {
                            foreach (int idOpcion in idsOpciones)
                            {
                                using (SqlCommand cmdInsert = new SqlCommand(
                                    "INSERT INTO UsuarioMenuPermisos (IdUsuario, IdMenuOpcion, UsuarioAsigno) VALUES (@IdUsuario, @IdMenuOpcion, @UsuarioAsigno)",
                                    cn, transaction))
                                {
                                    cmdInsert.Parameters.AddWithValue("@IdUsuario", idUsuario);
                                    cmdInsert.Parameters.AddWithValue("@IdMenuOpcion", idOpcion);
                                    cmdInsert.Parameters.AddWithValue("@UsuarioAsigno", usuarioAsigno);
                                    cmdInsert.ExecuteNonQuery();
                                }
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
