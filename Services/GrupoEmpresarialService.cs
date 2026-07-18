using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class GrupoEmpresarialService : BaseService
    {
        private GrupoEmpresarial MapGrupo(SqlDataReader reader)
        {
            return new GrupoEmpresarial
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Nombre = reader["Nombre"].ToString(),
                Descripcion = reader["Descripcion"] == DBNull.Value ? "" : reader["Descripcion"].ToString(),
                Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                FechaCreacion = reader.GetDateTime(reader.GetOrdinal("FechaCreacion")),
                TotalEmpresas = reader.GetInt32(reader.GetOrdinal("TotalEmpresas"))
            };
        }

        public List<GrupoEmpresarial> ObtenerTodos()
        {
            var lista = new List<GrupoEmpresarial>();
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"SELECT g.Id, g.Nombre, g.Descripcion, g.Activo, g.FechaCreacion,
                             (SELECT COUNT(*) FROM Empresas e WHERE e.IdGrupoEmpresarial = g.Id) AS TotalEmpresas
                      FROM GruposEmpresariales g
                      ORDER BY g.Nombre ASC", cn);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                        lista.Add(MapGrupo(reader));
            }
            return lista;
        }

        public GrupoEmpresarial ObtenerPorId(int id)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"SELECT g.Id, g.Nombre, g.Descripcion, g.Activo, g.FechaCreacion,
                             (SELECT COUNT(*) FROM Empresas e WHERE e.IdGrupoEmpresarial = g.Id) AS TotalEmpresas
                      FROM GruposEmpresariales g
                      WHERE g.Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    if (reader.Read()) return MapGrupo(reader);
            }
            return null;
        }

        public bool Crear(GrupoEmpresarial grupo)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"INSERT INTO GruposEmpresariales (Nombre, Descripcion, Activo, FechaCreacion)
                      VALUES (@Nombre, @Descripcion, 1, GETDATE())", cn);
                cmd.Parameters.AddWithValue("@Nombre", grupo.Nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(grupo.Descripcion) ? (object)DBNull.Value : grupo.Descripcion.Trim());
                cn.Open();
                bool ok = cmd.ExecuteNonQuery() > 0;
                if (ok) EmpresaCacheHelper.Invalidar();
                return ok;
            }
        }

        public bool Editar(GrupoEmpresarial grupo)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"UPDATE GruposEmpresariales
                      SET Nombre = @Nombre, Descripcion = @Descripcion, Activo = @Activo
                      WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", grupo.Id);
                cmd.Parameters.AddWithValue("@Nombre", grupo.Nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(grupo.Descripcion) ? (object)DBNull.Value : grupo.Descripcion.Trim());
                cmd.Parameters.AddWithValue("@Activo", grupo.Activo);
                cn.Open();
                bool ok = cmd.ExecuteNonQuery() > 0;
                if (ok) EmpresaCacheHelper.Invalidar();
                return ok;
            }
        }

        // Soft-delete del grupo; las empresas que quedaban en él vuelven a quedar sin grupo.
        public bool Eliminar(int id)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                {
                    try
                    {
                        SqlCommand cmdLimpiar = new SqlCommand(
                            "UPDATE Empresas SET IdGrupoEmpresarial = NULL WHERE IdGrupoEmpresarial = @Id", cn, tx);
                        cmdLimpiar.Parameters.AddWithValue("@Id", id);
                        cmdLimpiar.ExecuteNonQuery();

                        SqlCommand cmdEliminar = new SqlCommand(
                            "UPDATE GruposEmpresariales SET Activo = 0 WHERE Id = @Id", cn, tx);
                        cmdEliminar.Parameters.AddWithValue("@Id", id);
                        int filas = cmdEliminar.ExecuteNonQuery();

                        tx.Commit();
                        if (filas > 0) EmpresaCacheHelper.Invalidar();
                        return filas > 0;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public List<Empresas> ObtenerEmpresasDelGrupo(int idGrupo)
        {
            var lista = new List<Empresas>();
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"SELECT EmpId AS Id, EmpNombre AS Nombre, EmpAbreviatura AS Abreviatura
                      FROM Empresas WHERE IdGrupoEmpresarial = @IdGrupo ORDER BY EmpNombre", cn);
                cmd.Parameters.AddWithValue("@IdGrupo", idGrupo);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Empresas
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            Nombre = reader["Nombre"].ToString(),
                            Abreviatura = reader["Abreviatura"] == DBNull.Value ? "" : reader["Abreviatura"].ToString()
                        });
                    }
                }
            }
            return lista;
        }

        /// <summary>
        /// Reemplazo completo de la asignación de empresas del grupo: las seleccionadas
        /// quedan con IdGrupoEmpresarial = idGrupo, las que estaban y ya no vienen
        /// seleccionadas quedan sin grupo. Es el único método que escribe esta columna,
        /// lo que garantiza que una empresa nunca quede en más de un grupo.
        /// </summary>
        public bool AsignarEmpresas(int idGrupo, List<int> idsEmpresas)
        {
            idsEmpresas = idsEmpresas ?? new List<int>();

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                {
                    try
                    {
                        SqlCommand cmdQuitar = new SqlCommand(
                            "UPDATE Empresas SET IdGrupoEmpresarial = NULL WHERE IdGrupoEmpresarial = @IdGrupo", cn, tx);
                        cmdQuitar.Parameters.AddWithValue("@IdGrupo", idGrupo);
                        cmdQuitar.ExecuteNonQuery();

                        foreach (int idEmpresa in idsEmpresas)
                        {
                            SqlCommand cmdAsignar = new SqlCommand(
                                "UPDATE Empresas SET IdGrupoEmpresarial = @IdGrupo WHERE EmpId = @IdEmpresa", cn, tx);
                            cmdAsignar.Parameters.AddWithValue("@IdGrupo", idGrupo);
                            cmdAsignar.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                            cmdAsignar.ExecuteNonQuery();
                        }

                        tx.Commit();
                        EmpresaCacheHelper.Invalidar();
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
    }
}
