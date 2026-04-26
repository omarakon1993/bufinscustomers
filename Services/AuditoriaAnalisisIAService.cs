using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class AuditoriaAnalisisIAService : BaseService
    {
        public void Registrar(AuditoriaAnalisisIA auditoria)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO AuditoriaAnalisisIA
                        (IdUsuario, NombreUsuario, IdEmpresa, NombreEmpresa, NombreTabla, Filtros, Pregunta, Respuesta, FechaPregunta, FilasAnalizadas)
                    VALUES
                        (@IdUsuario, @NombreUsuario, @IdEmpresa, @NombreEmpresa, @NombreTabla, @Filtros, @Pregunta, @Respuesta, @FechaPregunta, @FilasAnalizadas)", cn);
                cmd.Parameters.AddWithValue("@IdUsuario", auditoria.IdUsuario);
                cmd.Parameters.AddWithValue("@NombreUsuario", auditoria.NombreUsuario);
                cmd.Parameters.AddWithValue("@IdEmpresa", auditoria.IdEmpresa);
                cmd.Parameters.AddWithValue("@NombreEmpresa", auditoria.NombreEmpresa);
                cmd.Parameters.AddWithValue("@NombreTabla", auditoria.NombreTabla);
                cmd.Parameters.AddWithValue("@Filtros", auditoria.Filtros);
                cmd.Parameters.AddWithValue("@Pregunta", (object)auditoria.Pregunta ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Respuesta", auditoria.Respuesta);
                cmd.Parameters.AddWithValue("@FechaPregunta", auditoria.FechaPregunta);
                cmd.Parameters.AddWithValue("@FilasAnalizadas", auditoria.FilasAnalizadas);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public List<AuditoriaAnalisisIA> ObtenerRegistros(int? idUsuario, int? idEmpresa, DateTime? desde, DateTime? hasta)
        {
            var lista = new List<AuditoriaAnalisisIA>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var sql = @"SELECT Id, IdUsuario, NombreUsuario, IdEmpresa, NombreEmpresa,
                                   NombreTabla, Filtros, Pregunta, Respuesta, FechaPregunta, FilasAnalizadas
                            FROM AuditoriaAnalisisIA WHERE 1=1";
                var cmd = new SqlCommand();

                if (idUsuario.HasValue)
                {
                    sql += " AND IdUsuario = @IdUsuario";
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario.Value);
                }
                if (idEmpresa.HasValue)
                {
                    sql += " AND IdEmpresa = @IdEmpresa";
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa.Value);
                }
                if (desde.HasValue)
                {
                    sql += " AND FechaPregunta >= @Desde";
                    cmd.Parameters.AddWithValue("@Desde", desde.Value);
                }
                if (hasta.HasValue)
                {
                    sql += " AND FechaPregunta < @Hasta";
                    cmd.Parameters.AddWithValue("@Hasta", hasta.Value.AddDays(1));
                }

                sql += " ORDER BY FechaPregunta DESC";
                cmd.CommandText = sql;
                cmd.Connection = cn;
                cn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new AuditoriaAnalisisIA
                        {
                            Id              = Convert.ToInt32(reader["Id"]),
                            IdUsuario       = Convert.ToInt32(reader["IdUsuario"]),
                            NombreUsuario   = reader["NombreUsuario"].ToString(),
                            IdEmpresa       = Convert.ToInt32(reader["IdEmpresa"]),
                            NombreEmpresa   = reader["NombreEmpresa"].ToString(),
                            NombreTabla     = reader["NombreTabla"].ToString(),
                            Filtros         = reader["Filtros"].ToString(),
                            Pregunta        = reader["Pregunta"] == DBNull.Value ? null : reader["Pregunta"].ToString(),
                            Respuesta       = reader["Respuesta"].ToString(),
                            FechaPregunta   = Convert.ToDateTime(reader["FechaPregunta"]),
                            FilasAnalizadas = Convert.ToInt32(reader["FilasAnalizadas"])
                        });
                    }
                }
            }
            return lista;
        }

        public List<UsuarioAuditoriaDto> ObtenerUsuariosDeEmpresa(int? idEmpresa)
        {
            var lista = new List<UsuarioAuditoriaDto>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var sql = "SELECT DISTINCT IdUsuario, NombreUsuario FROM AuditoriaAnalisisIA WHERE 1=1";
                var cmd = new SqlCommand();

                if (idEmpresa.HasValue)
                {
                    sql += " AND IdEmpresa = @IdEmpresa";
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa.Value);
                }

                sql += " ORDER BY NombreUsuario";
                cmd.CommandText = sql;
                cmd.Connection = cn;
                cn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new UsuarioAuditoriaDto
                        {
                            Id     = Convert.ToInt32(reader["IdUsuario"]),
                            Nombre = reader["NombreUsuario"].ToString()
                        });
                    }
                }
            }
            return lista;
        }
    }
}
