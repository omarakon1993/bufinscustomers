using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    public class MenuEstructuraService : BaseService
    {
        // ===== CATEGORÍAS =====

        public List<CategoriaMenu> ObtenerCategorias()
        {
            var list = new List<CategoriaMenu>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("SELECT Id, Nombre, Icono, Orden FROM CategoriasMenu ORDER BY Orden, Nombre", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new CategoriaMenu
                        {
                            Id     = Convert.ToInt32(r["Id"]),
                            Nombre = r["Nombre"]?.ToString(),
                            Icono  = r["Icono"] == DBNull.Value ? null : r["Icono"].ToString(),
                            Orden  = Convert.ToInt32(r["Orden"])
                        });
            }
            return list;
        }

        public bool CrearCategoria(CategoriaMenu c)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "INSERT INTO CategoriasMenu (Nombre, Icono, Orden) VALUES (@Nombre, @Icono, @Orden)", cn);
                    cmd.Parameters.AddWithValue("@Nombre", c.Nombre ?? "");
                    cmd.Parameters.AddWithValue("@Icono",  (object)c.Icono ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Orden",  c.Orden);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public bool EditarCategoria(CategoriaMenu c)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "UPDATE CategoriasMenu SET Nombre=@Nombre, Icono=@Icono, Orden=@Orden WHERE Id=@Id", cn);
                    cmd.Parameters.AddWithValue("@Id",     c.Id);
                    cmd.Parameters.AddWithValue("@Nombre", c.Nombre ?? "");
                    cmd.Parameters.AddWithValue("@Icono",  (object)c.Icono ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Orden",  c.Orden);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public bool EliminarCategoria(int id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("DELETE FROM CategoriasMenu WHERE Id=@Id", cn);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        // ===== GRUPOS =====

        public List<GrupoMenu> ObtenerGrupos()
        {
            var list = new List<GrupoMenu>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"SELECT g.Id, g.Nombre, g.Icono, g.IdCategoria, g.Orden,
                             c.Nombre AS NombreCategoria
                      FROM GruposMenu g
                      JOIN CategoriasMenu c ON g.IdCategoria = c.Id
                      ORDER BY c.Orden, g.Orden, g.Nombre", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new GrupoMenu
                        {
                            Id              = Convert.ToInt32(r["Id"]),
                            Nombre          = r["Nombre"]?.ToString(),
                            Icono           = r["Icono"] == DBNull.Value ? null : r["Icono"].ToString(),
                            IdCategoria     = Convert.ToInt32(r["IdCategoria"]),
                            NombreCategoria = r["NombreCategoria"]?.ToString(),
                            Orden           = Convert.ToInt32(r["Orden"])
                        });
            }
            return list;
        }

        public bool CrearGrupo(GrupoMenu g)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "INSERT INTO GruposMenu (Nombre, Icono, IdCategoria, Orden) VALUES (@Nombre, @Icono, @IdCategoria, @Orden)", cn);
                    cmd.Parameters.AddWithValue("@Nombre",      g.Nombre ?? "");
                    cmd.Parameters.AddWithValue("@Icono",       (object)g.Icono ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdCategoria", g.IdCategoria);
                    cmd.Parameters.AddWithValue("@Orden",       g.Orden);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public bool EditarGrupo(GrupoMenu g)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "UPDATE GruposMenu SET Nombre=@Nombre, Icono=@Icono, IdCategoria=@IdCategoria, Orden=@Orden WHERE Id=@Id", cn);
                    cmd.Parameters.AddWithValue("@Id",          g.Id);
                    cmd.Parameters.AddWithValue("@Nombre",      g.Nombre ?? "");
                    cmd.Parameters.AddWithValue("@Icono",       (object)g.Icono ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdCategoria", g.IdCategoria);
                    cmd.Parameters.AddWithValue("@Orden",       g.Orden);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public bool EliminarGrupo(int id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("DELETE FROM GruposMenu WHERE Id=@Id", cn);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }
    }
}
