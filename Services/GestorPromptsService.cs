using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class GestorPromptsService : BaseService
    {
        private PromptIA MapPrompt(SqlDataReader reader)
        {
            return new PromptIA
            {
                Id          = reader.GetInt32(reader.GetOrdinal("Id")),
                Codigo      = reader["Codigo"].ToString(),
                Nombre      = reader["Nombre"].ToString(),
                Descripcion = reader["Descripcion"] == DBNull.Value ? "" : reader["Descripcion"].ToString(),
                TextoPrompt = reader["TextoPrompt"].ToString(),
                Activo      = reader.GetBoolean(reader.GetOrdinal("Activo"))
            };
        }

        public List<PromptIA> ObtenerTodos()
        {
            var lista = new List<PromptIA>();
            Bd.Leer(CadenaConexion, "SELECT Id, Codigo, Nombre, Descripcion, TextoPrompt, Activo FROM GestorPrompts ORDER BY Nombre ASC",
                reader => lista.Add(MapPrompt(reader)));
            return lista;
        }

        public PromptIA ObtenerPorCodigo(string codigo)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "SELECT Id, Codigo, Nombre, Descripcion, TextoPrompt, Activo FROM GestorPrompts WHERE Codigo = @Codigo AND Activo = 1", cn);
                cmd.Parameters.AddWithValue("@Codigo", codigo);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    if (reader.Read()) return MapPrompt(reader);
            }
            return null;
        }

        public bool Crear(PromptIA prompt)
        {
            return Bd.Ejecutar(CadenaConexion, @"INSERT INTO GestorPrompts (Codigo, Nombre, Descripcion, TextoPrompt, Activo)
                      VALUES (@Codigo, @Nombre, @Descripcion, @TextoPrompt, 1)",
                new
                {
                    Codigo = prompt.Codigo?.Trim().ToUpper() ?? "",
                    Nombre = prompt.Nombre?.Trim() ?? "",
                    Descripcion = string.IsNullOrWhiteSpace(prompt.Descripcion) ? (object)DBNull.Value : prompt.Descripcion.Trim(),
                    TextoPrompt = prompt.TextoPrompt?.Trim() ?? ""
                }) > 0;
        }

        public bool Editar(PromptIA prompt)
        {
            return Bd.Ejecutar(CadenaConexion, @"UPDATE GestorPrompts
                      SET Codigo = @Codigo, Nombre = @Nombre, Descripcion = @Descripcion,
                          TextoPrompt = @TextoPrompt, Activo = @Activo
                      WHERE Id = @Id",
                new
                {
                    Id = prompt.Id,
                    Codigo = prompt.Codigo?.Trim().ToUpper() ?? "",
                    Nombre = prompt.Nombre?.Trim() ?? "",
                    Descripcion = string.IsNullOrWhiteSpace(prompt.Descripcion) ? (object)DBNull.Value : prompt.Descripcion.Trim(),
                    TextoPrompt = prompt.TextoPrompt?.Trim() ?? "",
                    Activo = prompt.Activo
                }) > 0;
        }

        public bool Eliminar(int id)
        {
            return Bd.Ejecutar(CadenaConexion, "UPDATE GestorPrompts SET Activo = 0 WHERE Id = @Id", new { Id = id }) > 0;
        }
    }
}
