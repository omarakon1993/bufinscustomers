using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class ModeloService : BaseService
    {
        // ── Helpers privados ─────────────────────────────────────────────

        private ModeloEjecucion MapModelo(SqlDataReader reader, bool incluirActivo = false)
        {
            var m = new ModeloEjecucion
            {
                Id          = reader.GetInt32(reader.GetOrdinal("Id")),
                Nombre      = reader["Nombre"].ToString(),
                NombreSP    = reader["NombreSP"].ToString(),
                Descripcion = reader["Descripcion"] == DBNull.Value ? "" : reader["Descripcion"].ToString(),
                Icono       = reader["Icono"] == DBNull.Value ? "fas fa-cog" : reader["Icono"].ToString(),
                Orden       = reader.GetInt32(reader.GetOrdinal("Orden"))
            };
            if (incluirActivo)
                m.Activo = reader.GetBoolean(reader.GetOrdinal("Activo"));
            return m;
        }

        // ── Lectura para el dropdown de ejecución (solo activos, vía SP) ─

        public List<ModeloEjecucion> ObtenerModelosActivos()
        {
            var lista = new List<ModeloEjecucion>();
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_ObtenerModelosEjecucion", cn);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                        lista.Add(MapModelo(reader));
            }
            return lista;
        }

        public ModeloEjecucion ObtenerModeloPorId(int id)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "SELECT Id, Nombre, NombreSP, Descripcion, Icono, Orden FROM ModelosEjecucion WHERE Id = @Id AND Activo = 1", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    if (reader.Read()) return MapModelo(reader);
            }
            return null;
        }

        // ── CRUD para el gestor administrativo ───────────────────────────

        public List<ModeloEjecucion> ObtenerTodos()
        {
            var lista = new List<ModeloEjecucion>();
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "SELECT Id, Nombre, NombreSP, Descripcion, Icono, Orden, Activo FROM ModelosEjecucion ORDER BY Orden ASC, Nombre ASC", cn);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                        lista.Add(MapModelo(reader, incluirActivo: true));
            }
            return lista;
        }

        public bool Crear(ModeloEjecucion modelo)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"INSERT INTO ModelosEjecucion (Nombre, NombreSP, Descripcion, Icono, Orden, Activo)
                      VALUES (@Nombre, @NombreSP, @Descripcion, @Icono, @Orden, 1)", cn);
                cmd.Parameters.AddWithValue("@Nombre",      modelo.Nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@NombreSP",    modelo.NombreSP?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(modelo.Descripcion) ? (object)DBNull.Value : modelo.Descripcion.Trim());
                cmd.Parameters.AddWithValue("@Icono",       string.IsNullOrWhiteSpace(modelo.Icono) ? (object)DBNull.Value : modelo.Icono.Trim());
                cmd.Parameters.AddWithValue("@Orden",       modelo.Orden);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Editar(ModeloEjecucion modelo)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"UPDATE ModelosEjecucion
                      SET Nombre = @Nombre, NombreSP = @NombreSP, Descripcion = @Descripcion,
                          Icono = @Icono, Orden = @Orden, Activo = @Activo
                      WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id",          modelo.Id);
                cmd.Parameters.AddWithValue("@Nombre",      modelo.Nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@NombreSP",    modelo.NombreSP?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(modelo.Descripcion) ? (object)DBNull.Value : modelo.Descripcion.Trim());
                cmd.Parameters.AddWithValue("@Icono",       string.IsNullOrWhiteSpace(modelo.Icono) ? (object)DBNull.Value : modelo.Icono.Trim());
                cmd.Parameters.AddWithValue("@Orden",       modelo.Orden);
                cmd.Parameters.AddWithValue("@Activo",      modelo.Activo);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Eliminar(int id)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "DELETE FROM ModelosEjecucion WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }
    }
}
