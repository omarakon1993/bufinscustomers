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
                    "SELECT Id, Nombre, Icono, NombreCategoria, Orden FROM GruposMenu ORDER BY NombreCategoria, Orden, Nombre", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new GrupoMenu
                        {
                            Id              = Convert.ToInt32(r["Id"]),
                            Nombre          = r["Nombre"]?.ToString(),
                            Icono           = r["Icono"] == DBNull.Value ? null : r["Icono"].ToString(),
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
                        "INSERT INTO GruposMenu (Nombre, Icono, NombreCategoria, Orden) VALUES (@Nombre, @Icono, @NombreCategoria, @Orden)", cn);
                    cmd.Parameters.AddWithValue("@Nombre",          g.Nombre ?? "");
                    cmd.Parameters.AddWithValue("@Icono",           (object)g.Icono ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@NombreCategoria", g.NombreCategoria ?? "");
                    cmd.Parameters.AddWithValue("@Orden",           g.Orden);
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
                    cn.Open();

                    // Obtener nombre anterior para sincronizar MenuOpciones
                    var cmdOld = new SqlCommand("SELECT Nombre FROM GruposMenu WHERE Id=@Id", cn);
                    cmdOld.Parameters.AddWithValue("@Id", g.Id);
                    var nombreAnterior = cmdOld.ExecuteScalar()?.ToString() ?? g.Nombre;

                    // Actualizar GruposMenu
                    var cmd = new SqlCommand(
                        "UPDATE GruposMenu SET Nombre=@Nombre, Icono=@Icono, NombreCategoria=@NombreCategoria, Orden=@Orden WHERE Id=@Id", cn);
                    cmd.Parameters.AddWithValue("@Id",              g.Id);
                    cmd.Parameters.AddWithValue("@Nombre",          g.Nombre ?? "");
                    cmd.Parameters.AddWithValue("@Icono",           (object)g.Icono ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@NombreCategoria", g.NombreCategoria ?? "");
                    cmd.Parameters.AddWithValue("@Orden",           g.Orden);
                    cmd.ExecuteNonQuery();

                    // Sincronizar icono (y nombre si cambió) en MenuOpciones
                    var cmdSync = new SqlCommand(
                        "UPDATE MenuOpciones SET NombreGrupo=@NuevoNombre, IconoGrupo=@Icono WHERE NombreGrupo=@NombreAnterior", cn);
                    cmdSync.Parameters.AddWithValue("@NuevoNombre",    g.Nombre ?? "");
                    cmdSync.Parameters.AddWithValue("@Icono",          (object)g.Icono ?? DBNull.Value);
                    cmdSync.Parameters.AddWithValue("@NombreAnterior", nombreAnterior);
                    cmdSync.ExecuteNonQuery();
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
