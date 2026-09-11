using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// CRUD del catálogo dbo.Escenarios (ver Sql/001_Escenarios_CreateTable.sql). Id no es
    /// IDENTITY: es un catálogo pequeño y estable (hoy 2 filas) referenciado por FK desde las
    /// 9 tablas Ini_*, HistorialVersionesCargues y AuditoriaCargues, así que Eliminar es
    /// siempre un soft-delete (Activo = 0) — nunca se borra físicamente una fila.
    /// </summary>
    public class EscenarioService : BaseService
    {
        private static Models.Escenario Map(SqlDataReader reader)
        {
            return new Models.Escenario
            {
                Id = Convert.ToInt32(reader["Id"]),
                Nombre = reader["Nombre"].ToString(),
                Orden = Convert.ToInt32(reader["Orden"]),
                Activo = Convert.ToBoolean(reader["Activo"])
            };
        }

        /// <summary>Escenarios activos, para poblar los &lt;select&gt; de la aplicación.</summary>
        public List<Models.Escenario> ObtenerActivos()
        {
            var lista = new List<Models.Escenario>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    "SELECT Id, Nombre, Orden, Activo FROM dbo.Escenarios WHERE Activo = 1 ORDER BY Orden ASC, Id ASC", cn);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                        lista.Add(Map(reader));
            }
            return lista;
        }

        /// <summary>Todos los escenarios (activos e inactivos), para el gestor administrativo.</summary>
        public List<Models.Escenario> ObtenerTodos()
        {
            var lista = new List<Models.Escenario>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    "SELECT Id, Nombre, Orden, Activo FROM dbo.Escenarios ORDER BY Orden ASC, Id ASC", cn);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                        lista.Add(Map(reader));
            }
            return lista;
        }

        public bool Crear(string nombre)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(@"
                    DECLARE @NuevoId TINYINT = (SELECT ISNULL(MAX(Id), 0) + 1 FROM dbo.Escenarios);
                    DECLARE @NuevoOrden INT = (SELECT ISNULL(MAX(Orden), 0) + 1 FROM dbo.Escenarios);
                    INSERT INTO dbo.Escenarios (Id, Nombre, Orden, Activo)
                    VALUES (@NuevoId, @Nombre, @NuevoOrden, 1);", cn);
                cmd.Parameters.AddWithValue("@Nombre", nombre?.Trim() ?? "");
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Editar(int id, string nombre, int orden, bool activo)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    "UPDATE dbo.Escenarios SET Nombre = @Nombre, Orden = @Orden, Activo = @Activo WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Nombre", nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Orden", orden);
                cmd.Parameters.AddWithValue("@Activo", activo);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>Soft-delete: nunca se borra físicamente (referenciado por FK desde varias tablas).</summary>
        public bool Desactivar(int id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("UPDATE dbo.Escenarios SET Activo = 0 WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }
    }
}
