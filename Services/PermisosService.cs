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
                                vm.SoloSuperAdmin = reader["SoloSuperAdmin"] != DBNull.Value && Convert.ToBoolean(reader["SoloSuperAdmin"]);
                                vm.SoloAdminEmpresa = reader["SoloAdminEmpresa"] != DBNull.Value && Convert.ToBoolean(reader["SoloAdminEmpresa"]);
                            }
                            catch (IndexOutOfRangeException) { }

                            opciones.Add(vm);
                        }
                    }
                }
            }

            // Menús personalizados por empresa: no ofrecer al usuario opciones de otra empresa.
            var noAplican = ObtenerIdsOpcionesNoAplicables(idUsuario);
            if (noAplican.Count > 0)
                opciones.RemoveAll(o => noAplican.Contains(o.IdMenuOpcion));

            return opciones;
        }

        /// <summary>
        /// Lee una fila del reader y construye un objeto MenuOpciones con todas las columnas disponibles
        /// </summary>
        private MenuOpciones LeerMenuOpcion(SqlDataReader reader)
        {
            var opcion = new MenuOpciones
            {
                Id             = Convert.ToInt32(reader["Id"]),
                Codigo         = reader["Codigo"].ToString(),
                Nombre         = reader["Nombre"].ToString(),
                Descripcion    = reader["Descripcion"] != DBNull.Value ? reader["Descripcion"].ToString() : null,
                Icono          = reader["Icono"] != DBNull.Value ? reader["Icono"].ToString() : null,
                Orden          = Convert.ToInt32(reader["Orden"]),
                Controller     = reader["Controller"] != DBNull.Value ? reader["Controller"].ToString() : null,
                Action         = reader["Action"] != DBNull.Value ? reader["Action"].ToString() : null,
                IdGrupo        = reader["IdGrupo"] != DBNull.Value ? (int?)Convert.ToInt32(reader["IdGrupo"]) : null,
                SoloSuperAdmin = reader["SoloSuperAdmin"] != DBNull.Value && Convert.ToBoolean(reader["SoloSuperAdmin"]),
                // Campos derivados via JOIN
                Categoria      = reader["Categoria"] != DBNull.Value ? reader["Categoria"].ToString() : null,
                NombreGrupo    = reader["NombreGrupo"] != DBNull.Value ? reader["NombreGrupo"].ToString() : null,
                IconoGrupo     = reader["IconoGrupo"] != DBNull.Value ? reader["IconoGrupo"].ToString() : null,
                IconoCategoria = reader["IconoCategoria"] != DBNull.Value ? reader["IconoCategoria"].ToString() : null,
                OrdenCategoria = reader["OrdenCategoria"] != DBNull.Value ? Convert.ToInt32(reader["OrdenCategoria"]) : 0
            };

            try { opcion.SoloAdminEmpresa = reader["SoloAdminEmpresa"] != DBNull.Value && Convert.ToBoolean(reader["SoloAdminEmpresa"]); }
            catch (IndexOutOfRangeException) { }

            try { opcion.IdCategoria = reader["IdCategoria"] != DBNull.Value ? (int?)Convert.ToInt32(reader["IdCategoria"]) : null; }
            catch (IndexOutOfRangeException) { }

            try { opcion.EsDestacado = reader["EsDestacado"] != DBNull.Value && Convert.ToBoolean(reader["EsDestacado"]); }
            catch (IndexOutOfRangeException) { }

            try { opcion.NombreEN         = reader["NombreEN"]         != DBNull.Value ? reader["NombreEN"].ToString()         : null; }
            catch (IndexOutOfRangeException) { }
            try { opcion.NombreCategoriaEN = reader["NombreCategoriaEN"] != DBNull.Value ? reader["NombreCategoriaEN"].ToString() : null; }
            catch (IndexOutOfRangeException) { }
            try { opcion.NombreGrupoEN    = reader["NombreGrupoEN"]    != DBNull.Value ? reader["NombreGrupoEN"].ToString()    : null; }
            catch (IndexOutOfRangeException) { }

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

            // Menús personalizados por empresa (Super Admin ve todo).
            if (nivelAdmin != 2)
            {
                var noAplican = ObtenerIdsOpcionesNoAplicables(idUsuario);
                if (noAplican.Count > 0)
                    opciones.RemoveAll(o => noAplican.Contains(o.Id));
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

            // Un permiso asignado de una opción personalizada de OTRA empresa no cuenta (p. ej. el usuario
            // cambió de empresa): el código también se descarta para TienePermiso().
            foreach (var codigoNoAplica in ObtenerOpcionesNoAplicablesDeUsuario(idUsuario).Select(x => x.Codigo))
                codigos.Remove(codigoNoAplica);

            return codigos;
        }

        // ================= Menús personalizados por empresa (tabla MenuOpcionesEmpresas, Sql/023) =================
        // Una opción sin filas en MenuOpcionesEmpresas es global. Con filas, solo aplica a los usuarios cuya
        // Usuarios.IdEmpresa esté en la lista. Todo es tolerante a que la tabla aún no exista (error 208).

        private static bool EsTablaInexistente(SqlException ex)
        {
            return ex.Number == 208;
        }

        /// <summary>Opciones personalizadas (Id, Codigo) que NO aplican al usuario por su empresa.</summary>
        private List<(int Id, string Codigo)> ObtenerOpcionesNoAplicablesDeUsuario(int idUsuario)
        {
            var lista = new List<(int Id, string Codigo)>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"SELECT m.Id, m.Codigo
                      FROM MenuOpciones m
                      WHERE EXISTS (SELECT 1 FROM MenuOpcionesEmpresas e WHERE e.IdMenuOpcion = m.Id)
                        AND NOT EXISTS (SELECT 1 FROM MenuOpcionesEmpresas e
                                        INNER JOIN Usuarios u ON u.IdEmpresa = e.IdEmpresa
                                        WHERE e.IdMenuOpcion = m.Id AND u.Id = @IdUsuario)", cn))
                {
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add((Convert.ToInt32(r["Id"]), r["Codigo"].ToString()));
                }
            }
            catch (SqlException ex) when (EsTablaInexistente(ex)) { }
            return lista;
        }

        /// <summary>Ids de las opciones personalizadas que no aplican al usuario (por su empresa).</summary>
        public HashSet<int> ObtenerIdsOpcionesNoAplicables(int idUsuario)
        {
            return new HashSet<int>(ObtenerOpcionesNoAplicablesDeUsuario(idUsuario).Select(x => x.Id));
        }

        /// <summary>Empresas de cada opción personalizada: IdMenuOpcion → ids de empresa. Las opciones globales no aparecen.</summary>
        public Dictionary<int, List<int>> ObtenerEmpresasPorOpcion()
        {
            var dic = new Dictionary<int, List<int>>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT IdMenuOpcion, IdEmpresa FROM MenuOpcionesEmpresas ORDER BY IdMenuOpcion, IdEmpresa", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            int idOp = Convert.ToInt32(r["IdMenuOpcion"]);
                            if (!dic.TryGetValue(idOp, out var l)) dic[idOp] = l = new List<int>();
                            l.Add(Convert.ToInt32(r["IdEmpresa"]));
                        }
                }
            }
            catch (SqlException ex) when (EsTablaInexistente(ex)) { }
            return dic;
        }

        /// <summary>Empresas a las que se restringe una opción (por código). Vacío = opción global.</summary>
        public List<int> ObtenerIdsEmpresasDeOpcion(string codigo)
        {
            var ids = new List<int>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"SELECT e.IdEmpresa FROM MenuOpcionesEmpresas e
                      INNER JOIN MenuOpciones m ON m.Id = e.IdMenuOpcion
                      WHERE m.Codigo = @Codigo", cn))
                {
                    cmd.Parameters.AddWithValue("@Codigo", codigo ?? "");
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["IdEmpresa"]));
                }
            }
            catch (SqlException ex) when (EsTablaInexistente(ex)) { }
            return ids;
        }

        /// <summary>Reemplaza (dentro de la transacción del llamador) las empresas de una opción. Lista vacía = global.</summary>
        private static void GuardarEmpresasDeOpcion(SqlConnection cn, SqlTransaction tx, int idOpcion, IEnumerable<int> idsEmpresas)
        {
            var ids = (idsEmpresas ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().ToList();
            try
            {
                using (var del = new SqlCommand("DELETE FROM MenuOpcionesEmpresas WHERE IdMenuOpcion = @Id", cn, tx))
                {
                    del.Parameters.AddWithValue("@Id", idOpcion);
                    del.ExecuteNonQuery();
                }
                foreach (int idEmp in ids)
                    using (var ins = new SqlCommand("INSERT INTO MenuOpcionesEmpresas (IdMenuOpcion, IdEmpresa) VALUES (@Id, @Emp)", cn, tx))
                    {
                        ins.Parameters.AddWithValue("@Id", idOpcion);
                        ins.Parameters.AddWithValue("@Emp", idEmp);
                        ins.ExecuteNonQuery();
                    }
            }
            catch (SqlException ex) when (EsTablaInexistente(ex))
            {
                // Sin la tabla no se puede restringir. Si no se pidió restricción, no pasa nada; si se pidió, se
                // aborta (NUNCA dejar una opción pensada como personalizada visible para todas las empresas).
                if (ids.Count > 0)
                    throw new InvalidOperationException("Falta la tabla MenuOpcionesEmpresas: ejecute Sql/023_MenuOpcionesEmpresas_HistPrecios.sql antes de personalizar menús por empresa.");
            }
        }

        private static string ResolverNombre(string nombreES, string nombreEN)
        {
            bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture
                                  .TwoLetterISOLanguageName == "en";
            return (esIngles && !string.IsNullOrEmpty(nombreEN)) ? nombreEN : nombreES;
        }

        /// <summary>
        /// Construye la estructura jerárquica del sidebar: Categoría → Grupo → Items
        /// </summary>
        public List<SidebarCategoriaViewModel> ConstruirMenuJerarquico(List<MenuOpciones> opciones)
        {
            var categorias = new List<SidebarCategoriaViewModel>();

            // Agrupar por Categoria (ES), mantener orden
            var grupos = opciones
                .GroupBy(o => new { o.Categoria, o.NombreCategoriaEN, o.IconoCategoria, o.OrdenCategoria })
                .OrderBy(g => g.Key.OrdenCategoria);

            foreach (var catGroup in grupos)
            {
                var categoria = new SidebarCategoriaViewModel
                {
                    Nombre = ResolverNombre(catGroup.Key.Categoria, catGroup.Key.NombreCategoriaEN),
                    Icono = catGroup.Key.IconoCategoria ?? "fas fa-folder",
                    OrdenCategoria = catGroup.Key.OrdenCategoria
                };

                // Agrupar por NombreGrupo (ES) dentro de la categoría
                var gruposPorNombre = catGroup
                    .GroupBy(o => new { o.NombreGrupo, o.NombreGrupoEN, o.IconoGrupo })
                    .OrderBy(g => g.Min(o => o.Orden));

                foreach (var grupoGroup in gruposPorNombre)
                {
                    bool esImplicito = string.IsNullOrEmpty(grupoGroup.Key.NombreGrupo);
                    var grupo = new SidebarGrupoViewModel
                    {
                        Nombre = esImplicito ? "" : ResolverNombre(grupoGroup.Key.NombreGrupo, grupoGroup.Key.NombreGrupoEN),
                        Icono = esImplicito ? "" : (grupoGroup.Key.IconoGrupo ?? "fas fa-circle"),
                        EsGrupoImplicito = esImplicito
                    };

                    foreach (var opcion in grupoGroup.OrderBy(o => o.Orden))
                    {
                        grupo.Items.Add(new SidebarItemViewModel
                        {
                            Id = opcion.Id,
                            Codigo = opcion.Codigo,
                            Nombre = ResolverNombre(opcion.Nombre, opcion.NombreEN),
                            Controller = opcion.Controller,
                            Action = opcion.Action,
                            Icono = opcion.Icono,
                            EsDestacado = opcion.EsDestacado
                        });
                    }

                    categoria.Grupos.Add(grupo);
                }

                categorias.Add(categoria);
            }

            return categorias;
        }

        /// <summary>
        /// Obtiene todas las opciones de menú activas (para la página CRUD del gestor)
        /// </summary>
        public List<MenuOpciones> ObtenerTodas()
        {
            var opciones = new List<MenuOpciones>();

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand cmd = new SqlCommand(
                    @"SELECT m.Id, m.Codigo, m.Nombre, m.NombreEN, m.Descripcion, m.Icono, m.Orden,
                             m.Controller, m.[Action], m.IdGrupo, m.IdCategoria, m.SoloSuperAdmin, m.SoloAdminEmpresa, m.EsDestacado,
                             g.Nombre  AS NombreGrupo,
                             g.NombreEN AS NombreGrupoEN,
                             g.Icono   AS IconoGrupo,
                             c.Nombre  AS Categoria,
                             c.NombreEN AS NombreCategoriaEN,
                             c.Icono   AS IconoCategoria,
                             c.Orden   AS OrdenCategoria
                      FROM   MenuOpciones m
                      LEFT JOIN GruposMenu    g ON m.IdGrupo    = g.Id
                      LEFT JOIN CategoriasMenu c ON COALESCE(g.IdCategoria, m.IdCategoria) = c.Id
                      WHERE  m.Activo = 1
                      ORDER BY c.Orden, m.Orden", cn))
                {
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
        /// Crea una nueva opción de menú
        /// </summary>
        public bool CrearMenuOpcion(MenuOpciones opcion)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                {
                    try
                    {
                        int nuevoId;
                        using (SqlCommand cmd = new SqlCommand(
                            @"INSERT INTO MenuOpciones
                                (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo, IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
                              OUTPUT INSERTED.Id
                              VALUES
                                (@Codigo, @Nombre, @NombreEN, @Descripcion, @Icono, @Orden, @Controller, @Action, 1, @IdGrupo, @IdCategoria, @SoloSuperAdmin, @SoloAdminEmpresa, @EsDestacado)", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Codigo",         opcion.Codigo ?? "");
                            cmd.Parameters.AddWithValue("@Nombre",         opcion.Nombre ?? "");
                            cmd.Parameters.AddWithValue("@NombreEN",       (object)opcion.NombreEN ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Descripcion",    (object)opcion.Descripcion ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Icono",          (object)opcion.Icono ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Orden",          opcion.Orden);
                            cmd.Parameters.AddWithValue("@Controller",     (object)opcion.Controller ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Action",         (object)opcion.Action ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@IdGrupo",        opcion.IdGrupo.HasValue ? (object)opcion.IdGrupo.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@IdCategoria",    opcion.IdCategoria.HasValue ? (object)opcion.IdCategoria.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@SoloSuperAdmin", opcion.SoloSuperAdmin);
                            cmd.Parameters.AddWithValue("@SoloAdminEmpresa", opcion.SoloAdminEmpresa);
                            cmd.Parameters.AddWithValue("@EsDestacado", opcion.EsDestacado);
                            nuevoId = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        opcion.Id = nuevoId;

                        // Opción y restricción por empresa en la MISMA transacción: o quedan las dos o ninguna.
                        GuardarEmpresasDeOpcion(cn, tx, nuevoId, opcion.IdsEmpresas);
                        tx.Commit();
                        return true;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Edita una opción de menú existente
        /// </summary>
        public bool EditarMenuOpcion(MenuOpciones opcion)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                {
                    try
                    {
                        bool editado;
                        using (SqlCommand cmd = new SqlCommand(
                            @"UPDATE MenuOpciones SET
                                Codigo = @Codigo, Nombre = @Nombre, NombreEN = @NombreEN, Descripcion = @Descripcion,
                                Icono = @Icono, Orden = @Orden,
                                Controller = @Controller, [Action] = @Action,
                                IdGrupo = @IdGrupo, IdCategoria = @IdCategoria,
                                SoloSuperAdmin = @SoloSuperAdmin, SoloAdminEmpresa = @SoloAdminEmpresa,
                                EsDestacado = @EsDestacado
                              WHERE Id = @Id", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Id",             opcion.Id);
                            cmd.Parameters.AddWithValue("@Codigo",         opcion.Codigo ?? "");
                            cmd.Parameters.AddWithValue("@Nombre",         opcion.Nombre ?? "");
                            cmd.Parameters.AddWithValue("@NombreEN",       (object)opcion.NombreEN ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Descripcion",    (object)opcion.Descripcion ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Icono",          (object)opcion.Icono ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Orden",          opcion.Orden);
                            cmd.Parameters.AddWithValue("@Controller",     (object)opcion.Controller ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Action",         (object)opcion.Action ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@IdGrupo",        opcion.IdGrupo.HasValue ? (object)opcion.IdGrupo.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@IdCategoria",    opcion.IdCategoria.HasValue ? (object)opcion.IdCategoria.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@SoloSuperAdmin", opcion.SoloSuperAdmin);
                            cmd.Parameters.AddWithValue("@SoloAdminEmpresa", opcion.SoloAdminEmpresa);
                            cmd.Parameters.AddWithValue("@EsDestacado", opcion.EsDestacado);
                            editado = cmd.ExecuteNonQuery() > 0;
                        }

                        if (editado)
                            GuardarEmpresasDeOpcion(cn, tx, opcion.Id, opcion.IdsEmpresas);

                        tx.Commit();
                        return editado;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Actualiza el campo Orden de varias opciones de menú a la vez (drag &amp; drop en el gestor).
        /// </summary>
        public bool ActualizarOrden(List<(int Id, int Orden)> pares)
        {
            if (pares == null || pares.Count == 0) return true;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                {
                    try
                    {
                        foreach (var par in pares)
                        {
                            using (SqlCommand cmd = new SqlCommand(
                                "UPDATE MenuOpciones SET Orden = @Orden WHERE Id = @Id", cn, tx))
                            {
                                cmd.Parameters.AddWithValue("@Orden", par.Orden);
                                cmd.Parameters.AddWithValue("@Id", par.Id);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                        return true;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Elimina una opción de menú (desactiva con Activo = 0)
        /// </summary>
        public bool EliminarMenuOpcion(int id)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand cmd = new SqlCommand(
                    "UPDATE MenuOpciones SET Activo = 0 WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
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

        /// <summary>
        /// Otorga una opción de menú a TODOS los usuarios elegibles que aún no la tengan, sin tocar el
        /// resto de sus permisos — a diferencia de GuardarOpcionesUsuario, que reemplaza el set completo
        /// de un usuario. Excluye Super Admin (Admin=2), que ya tiene acceso total sin necesitar fila
        /// en UsuarioMenuPermisos; si la opción es SoloAdminEmpresa, también excluye a Usuario Normal
        /// (Admin=0) — misma regla de elegibilidad que UsuarioController.AsignarPermisosPorDefecto y
        /// PermisosController.FiltrarOpcionesParaAsignacion. Devuelve cuántos usuarios recibieron el permiso.
        /// </summary>
        public int HabilitarOpcionParaTodosLosUsuarios(int idMenuOpcion, bool soloAdminEmpresa, int usuarioAsigno)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                // Si la opción es personalizada por empresa (MenuOpcionesEmpresas), solo la reciben los
                // usuarios de esas empresas. Sin la tabla (Sql/023 sin ejecutar) la opción es global.
                const string sqlBase = @"
                    INSERT INTO UsuarioMenuPermisos (IdUsuario, IdMenuOpcion, UsuarioAsigno)
                    SELECT u.Id, @IdMenuOpcion, @UsuarioAsigno
                    FROM Usuarios u
                    WHERE (u.Admin IS NULL OR u.Admin <> 2)
                      AND (@SoloAdminEmpresa = 0 OR u.Admin = 1)
                      AND NOT EXISTS (
                          SELECT 1 FROM UsuarioMenuPermisos p
                          WHERE p.IdUsuario = u.Id AND p.IdMenuOpcion = @IdMenuOpcion)";
                const string filtroEmpresas = @"
                      AND (NOT EXISTS (SELECT 1 FROM MenuOpcionesEmpresas me WHERE me.IdMenuOpcion = @IdMenuOpcion)
                           OR u.IdEmpresa IN (SELECT me2.IdEmpresa FROM MenuOpcionesEmpresas me2 WHERE me2.IdMenuOpcion = @IdMenuOpcion))";

                cn.Open();
                foreach (string sql in new[] { sqlBase + filtroEmpresas, sqlBase })
                {
                    try
                    {
                        using (var cmd = new SqlCommand(sql, cn))
                        {
                            cmd.Parameters.AddWithValue("@IdMenuOpcion", idMenuOpcion);
                            cmd.Parameters.AddWithValue("@UsuarioAsigno", usuarioAsigno);
                            cmd.Parameters.AddWithValue("@SoloAdminEmpresa", soloAdminEmpresa);
                            return cmd.ExecuteNonQuery();
                        }
                    }
                    catch (SqlException ex) when (EsTablaInexistente(ex)) { /* reintenta sin el filtro de empresas */ }
                }
                return 0;
            }
        }
    }
}
