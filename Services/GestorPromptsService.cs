using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

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
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "SELECT Id, Codigo, Nombre, Descripcion, TextoPrompt, Activo FROM GestorPrompts ORDER BY Nombre ASC", cn);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                        lista.Add(MapPrompt(reader));
            }
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
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"INSERT INTO GestorPrompts (Codigo, Nombre, Descripcion, TextoPrompt, Activo)
                      VALUES (@Codigo, @Nombre, @Descripcion, @TextoPrompt, 1)", cn);
                cmd.Parameters.AddWithValue("@Codigo",      prompt.Codigo?.Trim().ToUpper() ?? "");
                cmd.Parameters.AddWithValue("@Nombre",      prompt.Nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(prompt.Descripcion) ? (object)DBNull.Value : prompt.Descripcion.Trim());
                cmd.Parameters.AddWithValue("@TextoPrompt", prompt.TextoPrompt?.Trim() ?? "");
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Editar(PromptIA prompt)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"UPDATE GestorPrompts
                      SET Codigo = @Codigo, Nombre = @Nombre, Descripcion = @Descripcion,
                          TextoPrompt = @TextoPrompt, Activo = @Activo
                      WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id",          prompt.Id);
                cmd.Parameters.AddWithValue("@Codigo",      prompt.Codigo?.Trim().ToUpper() ?? "");
                cmd.Parameters.AddWithValue("@Nombre",      prompt.Nombre?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(prompt.Descripcion) ? (object)DBNull.Value : prompt.Descripcion.Trim());
                cmd.Parameters.AddWithValue("@TextoPrompt", prompt.TextoPrompt?.Trim() ?? "");
                cmd.Parameters.AddWithValue("@Activo",      prompt.Activo);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Eliminar(int id)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "UPDATE GestorPrompts SET Activo = 0 WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }
    }
}
