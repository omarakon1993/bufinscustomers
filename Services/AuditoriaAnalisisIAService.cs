using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class AuditoriaAnalisisIAService : BaseService
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _cacheColumnas =
            new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();

        /// <summary>True si <c>AuditoriaAnalisisIA</c> tiene la columna indicada. Cacheado: permite
        /// convivir con esquemas donde aún no se han ejecutado las migraciones (S03 / N02).</summary>
        private bool TieneColumna(string nombre)
        {
            return _cacheColumnas.GetOrAdd(nombre, n =>
            {
                try
                {
                    using (var cn = new SqlConnection(CadenaConexion))
                    using (var cmd = new SqlCommand(
                        "SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditoriaAnalisisIA') AND name = @n", cn))
                    {
                        cmd.Parameters.AddWithValue("@n", n);
                        cn.Open();
                        return cmd.ExecuteScalar() != null;
                    }
                }
                catch { return false; }
            });
        }

        /// <summary>Inserta la fila de auditoría y devuelve su <c>Id</c> (0 si no se pudo obtener).</summary>
        public int Registrar(AuditoriaAnalisisIA auditoria)
        {
            bool conTokens = TieneColumna("TokensTotal");
            string cols = "IdUsuario, NombreUsuario, IdEmpresa, NombreEmpresa, NombreTabla, Filtros, Pregunta, Respuesta, FechaPregunta, FilasAnalizadas"
                        + (conTokens ? ", TokensTotal" : "");
            string vals = "@IdUsuario, @NombreUsuario, @IdEmpresa, @NombreEmpresa, @NombreTabla, @Filtros, @Pregunta, @Respuesta, @FechaPregunta, @FilasAnalizadas"
                        + (conTokens ? ", @TokensTotal" : "");

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    $"INSERT INTO AuditoriaAnalisisIA ({cols}) VALUES ({vals}); SELECT CAST(SCOPE_IDENTITY() AS INT);", cn);
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
                if (conTokens) cmd.Parameters.AddWithValue("@TokensTotal", auditoria.TokensTotal);
                cn.Open();
                var r = cmd.ExecuteScalar();
                return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
            }
        }

        /// <summary>
        /// Registra la valoración del usuario sobre una respuesta (N02). Solo el autor de la
        /// consulta puede valorarla. <c>1</c> = útil, <c>0</c> = no útil, <c>null</c> = quitar.
        /// Devuelve false si la columna no existe o la fila no es del usuario.
        /// </summary>
        public bool Valorar(int id, int idUsuario, int? valoracion)
        {
            if (!TieneColumna("Valoracion")) return false;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE AuditoriaAnalisisIA SET Valoracion = @v WHERE Id = @id AND IdUsuario = @u", cn))
                {
                    cmd.Parameters.AddWithValue("@v", (object)valoracion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@u", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaAnalisisIAService.Valorar] {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Tokens consumidos por la empresa en el mes en curso (base del presupuesto mensual).
        /// Lee el acumulado <c>dbo.IAUsoMensual</c> (lo mantiene IAGateway; no depende de que alguien
        /// limpie la auditoría). Si esa tabla aún no existe cae a sumar <c>AuditoriaAnalisisIA.TokensTotal</c>.
        /// <c>0</c> ante cualquier otro error — así un fallo de medición nunca bloquea a la empresa.
        /// </summary>
        public long SumarTokensMes(int idEmpresa)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    try
                    {
                        using (var cmd = new SqlCommand(
                            "SELECT ISNULL(SUM(Tokens), 0) FROM dbo.IAUsoMensual WHERE IdEmpresa = @IdEmpresa AND Periodo = @Periodo", cn))
                        {
                            cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                            cmd.Parameters.AddWithValue("@Periodo", DateTime.Now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
                            return Convert.ToInt64(cmd.ExecuteScalar());
                        }
                    }
                    catch (SqlException ex) when (ex.Number == 208) { /* IAUsoMensual aún no existe (Sql/013) */ }

                    using (var cmd = new SqlCommand(
                        @"SELECT ISNULL(SUM(CAST(TokensTotal AS BIGINT)), 0)
                          FROM AuditoriaAnalisisIA
                          WHERE IdEmpresa = @IdEmpresa
                            AND FechaPregunta >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)", cn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        var r = cmd.ExecuteScalar();
                        return r == null || r == DBNull.Value ? 0L : Convert.ToInt64(r);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaAnalisisIAService.SumarTokensMes] {0}", ex.Message);
                return 0L;
            }
        }

        /// <summary>
        /// Tokens consumidos por un conjunto de empresas desde <paramref name="desde"/> (inclusive) — se usa
        /// con día de corte propio y con pool de grupo. Suma <c>AuditoriaAnalisisIA.TokensTotal</c> (índice
        /// por empresa y fecha). <c>0</c> ante cualquier error: un fallo de medición nunca bloquea.
        /// </summary>
        public long SumarTokensDesde(List<int> idsEmpresa, DateTime desde)
        {
            if (idsEmpresa == null || idsEmpresa.Count == 0) return 0L;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand())
                {
                    var ps = new List<string>();
                    for (int i = 0; i < idsEmpresa.Count; i++)
                    {
                        ps.Add("@e" + i);
                        cmd.Parameters.AddWithValue("@e" + i, idsEmpresa[i]);
                    }
                    cmd.Parameters.AddWithValue("@Desde", desde);
                    cmd.Connection = cn;
                    cmd.CommandText = "SELECT ISNULL(SUM(CAST(TokensTotal AS BIGINT)), 0) FROM AuditoriaAnalisisIA " +
                                      "WHERE IdEmpresa IN (" + string.Join(",", ps) + ") AND FechaPregunta >= @Desde";
                    cn.Open();
                    var r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? 0L : Convert.ToInt64(r);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaAnalisisIAService.SumarTokensDesde] {0}", ex.Message);
                return 0L;
            }
        }

        /// <summary>Tokens consumidos hoy por un usuario dentro de una empresa (tope diario por usuario).</summary>
        public long SumarTokensUsuarioHoy(int idEmpresa, int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"SELECT ISNULL(SUM(CAST(TokensTotal AS BIGINT)), 0) FROM AuditoriaAnalisisIA
                      WHERE IdEmpresa = @IdEmpresa AND IdUsuario = @IdUsuario AND FechaPregunta >= CAST(GETDATE() AS DATE)", cn))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    var r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? 0L : Convert.ToInt64(r);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaAnalisisIAService.SumarTokensUsuarioHoy] {0}", ex.Message);
                return 0L;
            }
        }

        public List<AuditoriaAnalisisIA> ObtenerRegistros(int? idUsuario, List<int> idsEmpresa, DateTime? desde, DateTime? hasta)
        {
            var lista = new List<AuditoriaAnalisisIA>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                bool conTokens = TieneColumna("TokensTotal");
                bool conValoracion = TieneColumna("Valoracion");
                var sql = @"SELECT Id, IdUsuario, NombreUsuario, IdEmpresa, NombreEmpresa,
                                   NombreTabla, Filtros, Pregunta, Respuesta, FechaPregunta, FilasAnalizadas"
                          + (conTokens ? ", TokensTotal" : "") + (conValoracion ? ", Valoracion" : "")
                          + " FROM AuditoriaAnalisisIA WHERE 1=1";
                var cmd = new SqlCommand();

                if (idUsuario.HasValue)
                {
                    sql += " AND IdUsuario = @IdUsuario";
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario.Value);
                }
                if (idsEmpresa != null)
                {
                    var nombresParametros = new List<string>();
                    for (int i = 0; i < idsEmpresa.Count; i++)
                    {
                        var nombreParametro = "@IdEmpresa" + i;
                        nombresParametros.Add(nombreParametro);
                        cmd.Parameters.AddWithValue(nombreParametro, idsEmpresa[i]);
                    }
                    sql += idsEmpresa.Count > 0
                        ? " AND IdEmpresa IN (" + string.Join(",", nombresParametros) + ")"
                        : " AND 1 = 0";
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

                // Fecha descendente (más reciente primero); Id como desempate para un
                // orden estable cuando varias consultas comparten el mismo timestamp.
                sql += " ORDER BY FechaPregunta DESC, Id DESC";
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
                            FilasAnalizadas = Convert.ToInt32(reader["FilasAnalizadas"]),
                            TokensTotal     = conTokens && reader["TokensTotal"] != DBNull.Value ? Convert.ToInt32(reader["TokensTotal"]) : 0,
                            Valoracion      = conValoracion && reader["Valoracion"] != DBNull.Value ? Convert.ToInt32(reader["Valoracion"]) : (int?)null
                        });
                    }
                }
            }
            return lista;
        }

        /// <summary>
        /// Borra registros de auditoría. Si <paramref name="mesesConservar"/> es null o &lt;= 0,
        /// elimina TODA la tabla; en caso contrario conserva solo los registros de los últimos
        /// N meses. Devuelve la cantidad de filas eliminadas.
        /// </summary>
        public int LimpiarAuditoria(int? mesesConservar)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd;
                if (mesesConservar.HasValue && mesesConservar.Value > 0)
                {
                    cmd = new SqlCommand(
                        "DELETE FROM AuditoriaAnalisisIA WHERE FechaPregunta < DATEADD(MONTH, -@Meses, GETDATE())", cn);
                    cmd.Parameters.AddWithValue("@Meses", mesesConservar.Value);
                }
                else
                {
                    cmd = new SqlCommand("DELETE FROM AuditoriaAnalisisIA", cn);
                }
                cn.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        public List<UsuarioAuditoriaDto> ObtenerUsuariosDeEmpresa(List<int> idsEmpresa)
        {
            var lista = new List<UsuarioAuditoriaDto>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var sql = "SELECT DISTINCT IdUsuario, NombreUsuario FROM AuditoriaAnalisisIA WHERE 1=1";
                var cmd = new SqlCommand();

                if (idsEmpresa != null)
                {
                    var nombresParametros = new List<string>();
                    for (int i = 0; i < idsEmpresa.Count; i++)
                    {
                        var nombreParametro = "@IdEmpresa" + i;
                        nombresParametros.Add(nombreParametro);
                        cmd.Parameters.AddWithValue(nombreParametro, idsEmpresa[i]);
                    }
                    sql += idsEmpresa.Count > 0
                        ? " AND IdEmpresa IN (" + string.Join(",", nombresParametros) + ")"
                        : " AND 1 = 0";
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
