using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Servicio para consulta del informe de Variables PBI (tabla OrdenVariables)
    /// </summary>
    public class VariablesPBIService : BaseService
    {
        /// <summary>
        /// Obtiene los nombres de tabla distintos presentes en OrdenVariables
        /// </summary>
        public List<string> ObtenerTablasDisponibles()
        {
            var tablas = new List<string>();
            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = "SELECT DISTINCT NombreTabla FROM OrdenVariables ORDER BY NombreTabla";
                    SqlCommand cmd = new SqlCommand(query, cn);
                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            tablas.Add(reader.GetString(0));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error ObtenerTablasDisponibles: {ex.Message}");
            }
            return tablas;
        }

        /// <summary>
        /// Obtiene el estado actual de la tabla (conteo, auditoria)
        /// </summary>
        public EstadoVariablesPBI ObtenerEstadoActual()
        {
            var estado = new EstadoVariablesPBI();
            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = @"SELECT COUNT(*) AS Total,
                                            COUNT(DISTINCT NombreTabla) AS TotalTablas,
                                            MAX(UsuarioCargo) AS UsuarioCargo,
                                            MAX(FechaCarga) AS FechaCarga
                                     FROM OrdenVariables";
                    SqlCommand cmd = new SqlCommand(query, cn);
                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            estado.TotalRegistros = reader["Total"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Total"]);
                            estado.TotalTablas = reader["TotalTablas"] == DBNull.Value ? 0 : Convert.ToInt32(reader["TotalTablas"]);
                            estado.UsuarioCargo = reader["UsuarioCargo"] == DBNull.Value ? null : reader["UsuarioCargo"].ToString();
                            estado.FechaCarga = reader["FechaCarga"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["FechaCarga"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error ObtenerEstadoActual: {ex.Message}");
            }
            return estado;
        }

        /// <summary>
        /// Consulta registros con filtros opcionales
        /// </summary>
        public ResultadoVariablesPBI ConsultarDatos(FiltrosVariablesPBI filtros)
        {
            var resultado = new ResultadoVariablesPBI();
            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = @"SELECT Id, NombreTabla, Variable, OrdenVariable, SubtotalVariable,
                                            VariablePadre, VariableIndicador, ClaseVariable,
                                            AgrupacionKEY, AgrupacionKEYABR, AgrupacionKEYOrden,
                                            VariablePadreReal, VariablePadreAbr, VariablePadreRealOrden,
                                            UsuarioCargo, FechaCarga
                                     FROM OrdenVariables
                                     WHERE 1=1";

                    var parametros = new List<SqlParameter>();

                    if (!string.IsNullOrWhiteSpace(filtros?.NombreTabla))
                    {
                        query += " AND NombreTabla = @NombreTabla";
                        parametros.Add(new SqlParameter("@NombreTabla", filtros.NombreTabla));
                    }

                    if (!string.IsNullOrWhiteSpace(filtros?.Variable))
                    {
                        query += " AND Variable LIKE @Variable";
                        parametros.Add(new SqlParameter("@Variable", $"%{filtros.Variable}%"));
                    }

                    if (filtros?.SoloSubtotales == true)
                        query += " AND SubtotalVariable = 1";

                    query += " ORDER BY NombreTabla, OrdenVariable";

                    SqlCommand cmd = new SqlCommand(query, cn);
                    cmd.Parameters.AddRange(parametros.ToArray());
                    cmd.CommandTimeout = 60;
                    cn.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var ov = MapearDesdeReader(reader);
                            resultado.Filas.Add(ov);
                            if (resultado.UsuarioCargo == null && ov.UsuarioCargo != null)
                            {
                                resultado.UsuarioCargo = ov.UsuarioCargo;
                                resultado.FechaCarga = ov.FechaCarga;
                            }
                        }
                    }
                    resultado.TotalRegistros = resultado.Filas.Count;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al consultar OrdenVariables: {ex.Message}", ex);
            }
            return resultado;
        }

        /// <summary>
        /// Lee todos los registros de la tabla (para generar diff antes de reemplazar)
        /// </summary>
        public List<OrdenVariable> ObtenerTodos()
        {
            var lista = new List<OrdenVariable>();
            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = @"SELECT Id, NombreTabla, Variable, OrdenVariable, SubtotalVariable,
                                            VariablePadre, VariableIndicador, ClaseVariable,
                                            AgrupacionKEY, AgrupacionKEYABR, AgrupacionKEYOrden,
                                            VariablePadreReal, VariablePadreAbr, VariablePadreRealOrden,
                                            UsuarioCargo, FechaCarga
                                     FROM OrdenVariables
                                     ORDER BY NombreTabla, OrdenVariable";
                    SqlCommand cmd = new SqlCommand(query, cn);
                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            lista.Add(MapearDesdeReader(reader));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error ObtenerTodos: {ex.Message}");
            }
            return lista;
        }

        internal OrdenVariable MapearDesdeReader(SqlDataReader reader)
        {
            return new OrdenVariable
            {
                Id = reader["Id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Id"]),
                NombreTabla = reader["NombreTabla"]?.ToString(),
                Variable = reader["Variable"]?.ToString(),
                OrdenVariable_ = reader["OrdenVariable"] == DBNull.Value ? 0 : Convert.ToInt32(reader["OrdenVariable"]),
                SubtotalVariable = reader["SubtotalVariable"] != DBNull.Value && Convert.ToBoolean(reader["SubtotalVariable"]),
                VariablePadre = reader["VariablePadre"] == DBNull.Value ? null : reader["VariablePadre"].ToString(),
                VariableIndicador = reader["VariableIndicador"] == DBNull.Value ? null : reader["VariableIndicador"].ToString(),
                ClaseVariable = reader["ClaseVariable"] == DBNull.Value ? null : reader["ClaseVariable"].ToString(),
                AgrupacionKEY = reader["AgrupacionKEY"] == DBNull.Value ? null : reader["AgrupacionKEY"].ToString(),
                AgrupacionKEYABR = reader["AgrupacionKEYABR"] == DBNull.Value ? null : reader["AgrupacionKEYABR"].ToString(),
                AgrupacionKEYOrden = reader["AgrupacionKEYOrden"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["AgrupacionKEYOrden"]),
                VariablePadreReal = reader["VariablePadreReal"] == DBNull.Value ? null : reader["VariablePadreReal"].ToString(),
                VariablePadreAbr = reader["VariablePadreAbr"] == DBNull.Value ? null : reader["VariablePadreAbr"].ToString(),
                VariablePadreRealOrden = reader["VariablePadreRealOrden"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["VariablePadreRealOrden"]),
                UsuarioCargo = reader["UsuarioCargo"] == DBNull.Value ? null : reader["UsuarioCargo"].ToString(),
                FechaCarga = reader["FechaCarga"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["FechaCarga"])
            };
        }
    }
}
