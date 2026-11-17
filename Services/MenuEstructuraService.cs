using bufinscustomers.Helpers;
using System;
using System.Collections.Generic;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    public class MenuEstructuraService : BaseService
    {
        // ===== CATEGORÍAS =====

        public List<CategoriaMenu> ObtenerCategorias()
        {
            var list = new List<CategoriaMenu>();
            Bd.Leer(CadenaConexion, "SELECT Id, Nombre, NombreEN, Icono, Orden FROM CategoriasMenu ORDER BY Orden, Nombre",
                r => list.Add(new CategoriaMenu
                    {
                        Id       = Convert.ToInt32(r["Id"]),
                        Nombre   = r["Nombre"]?.ToString(),
                        NombreEN = r["NombreEN"] == DBNull.Value ? null : r["NombreEN"].ToString(),
                        Icono    = r["Icono"] == DBNull.Value ? null : r["Icono"].ToString(),
                        Orden    = Convert.ToInt32(r["Orden"])
                    }));
            return list;
        }

        public bool CrearCategoria(CategoriaMenu c)
        {
            try
            {
                Bd.Ejecutar(CadenaConexion, "INSERT INTO CategoriasMenu (Nombre, NombreEN, Icono, Orden) VALUES (@Nombre, @NombreEN, @Icono, @Orden)",
                    new { Nombre = c.Nombre ?? "", NombreEN = c.NombreEN, Icono = c.Icono, Orden = c.Orden });
                return true;
            }
            catch { return false; }
        }

        public bool EditarCategoria(CategoriaMenu c)
        {
            try
            {
                Bd.Ejecutar(CadenaConexion, "UPDATE CategoriasMenu SET Nombre=@Nombre, NombreEN=@NombreEN, Icono=@Icono, Orden=@Orden WHERE Id=@Id",
                    new { Id = c.Id, Nombre = c.Nombre ?? "", NombreEN = c.NombreEN, Icono = c.Icono, Orden = c.Orden });
                return true;
            }
            catch { return false; }
        }

        public bool EliminarCategoria(int id)
        {
            try
            {
                Bd.Ejecutar(CadenaConexion, "DELETE FROM CategoriasMenu WHERE Id=@Id", new { Id = id });
                return true;
            }
            catch { return false; }
        }

        // ===== GRUPOS =====

        public List<GrupoMenu> ObtenerGrupos()
        {
            var list = new List<GrupoMenu>();
            Bd.Leer(CadenaConexion, @"SELECT g.Id, g.Nombre, g.NombreEN, g.Icono, g.IdCategoria, g.Orden,
                             c.Nombre AS NombreCategoria
                      FROM GruposMenu g
                      JOIN CategoriasMenu c ON g.IdCategoria = c.Id
                      ORDER BY c.Orden, g.Orden, g.Nombre",
                r => list.Add(new GrupoMenu
                    {
                        Id              = Convert.ToInt32(r["Id"]),
                        Nombre          = r["Nombre"]?.ToString(),
                        NombreEN        = r["NombreEN"] == DBNull.Value ? null : r["NombreEN"].ToString(),
                        Icono           = r["Icono"] == DBNull.Value ? null : r["Icono"].ToString(),
                        IdCategoria     = Convert.ToInt32(r["IdCategoria"]),
                        NombreCategoria = r["NombreCategoria"]?.ToString(),
                        Orden           = Convert.ToInt32(r["Orden"])
                    }));
            return list;
        }

        public bool CrearGrupo(GrupoMenu g)
        {
            try
            {
                Bd.Ejecutar(CadenaConexion, "INSERT INTO GruposMenu (Nombre, NombreEN, Icono, IdCategoria, Orden) VALUES (@Nombre, @NombreEN, @Icono, @IdCategoria, @Orden)",
                    new
                    {
                        Nombre = g.Nombre ?? "",
                        NombreEN = g.NombreEN,
                        Icono = g.Icono,
                        IdCategoria = g.IdCategoria,
                        Orden = g.Orden
                    });
                return true;
            }
            catch { return false; }
        }

        public bool EditarGrupo(GrupoMenu g)
        {
            try
            {
                Bd.Ejecutar(CadenaConexion, "UPDATE GruposMenu SET Nombre=@Nombre, NombreEN=@NombreEN, Icono=@Icono, IdCategoria=@IdCategoria, Orden=@Orden WHERE Id=@Id",
                    new
                    {
                        Id = g.Id,
                        Nombre = g.Nombre ?? "",
                        NombreEN = g.NombreEN,
                        Icono = g.Icono,
                        IdCategoria = g.IdCategoria,
                        Orden = g.Orden
                    });
                return true;
            }
            catch { return false; }
        }

        public bool EliminarGrupo(int id)
        {
            try
            {
                Bd.Ejecutar(CadenaConexion, "DELETE FROM GruposMenu WHERE Id=@Id", new { Id = id });
                return true;
            }
            catch { return false; }
        }
    }
}
