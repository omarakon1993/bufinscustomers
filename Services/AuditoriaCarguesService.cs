using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class AuditoriaCarguesService : BaseService
    {
        /// <summary>
        /// El SP no devuelve el escenario: se completa con una lectura aparte de <c>AuditoriaCargues.IdEscenario</c>
        /// (columna incremental de Sql/004). Si la columna aún no existe, los cargues quedan sin escenario.
        /// </summary>
        private static void CompletarEscenario(SqlConnection connection, List<AuditoriaCargues> auditorias)
        {
            if (auditorias.Count == 0) return;
            try
            {
                var mapa = new Dictionary<int, int>();
                using (var cmd = new SqlCommand("SELECT Id, IdEscenario FROM dbo.AuditoriaCargues WHERE IdEscenario IS NOT NULL", connection))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        mapa[Convert.ToInt32(r["Id"])] = Convert.ToInt32(r["IdEscenario"]);

                foreach (var a in auditorias)
                    if (mapa.TryGetValue(a.Id, out int esc)) a.IdEscenario = esc;
            }
            catch (SqlException)
            {
                // Columna IdEscenario inexistente (BD sin Sql/004): se muestra sin escenario.
            }
        }

        public List<AuditoriaCargues> ObtenerAuditoriaCargues()
        {
            return ObtenerAuditoriaCargues(null);
        }

        public List<AuditoriaCargues> ObtenerAuditoriaCargues(int? idEmpresa)
        {
            List<AuditoriaCargues> auditorias = new List<AuditoriaCargues>();

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
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

                CompletarEscenario(connection, auditorias);
            }

            return auditorias;
        }
    }
}