using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class AuditoriaCarguesService
    {
        private readonly string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        public List<AuditoriaCargues> ObtenerAuditoriaCargues()
        {
            return ObtenerAuditoriaCargues(null);
        }

        public List<AuditoriaCargues> ObtenerAuditoriaCargues(int? idEmpresa)
        {
            List<AuditoriaCargues> auditorias = new List<AuditoriaCargues>();

            using (SqlConnection connection = new SqlConnection(cadena))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerAuditoriaCarguesPorEmpresa", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IdEmpresa", idEmpresa.HasValue ? (object)idEmpresa.Value : DBNull.Value);
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AuditoriaCargues auditoria = new AuditoriaCargues();
                            auditoria.Id = (int)reader["Id"];
                            auditoria.FechaCargue = (DateTime)reader["FechaCargue"];
                            auditoria.IdUsuario = (int)reader["IdUsuario"];
                            auditoria.Usuario = (string)reader["Usuario"];
                            auditoria.IdEmpresa = (int)reader["IdEmpresa"];
                            auditoria.NombreEmpresa = (string)reader["NombreEmpresa"];
                            auditoria.NombreArchivo = (string)reader["NombreArchivo"];
                            auditorias.Add(auditoria);
                        }
                    }
                }
            }

            return auditorias;
        }
    }
}