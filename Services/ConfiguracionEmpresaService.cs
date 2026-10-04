using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Servicio para gestionar configuraciones empresariales
    /// Maneja todas las operaciones CRUD de configuraciones y sus detalles
    /// </summary>
    public class ConfiguracionEmpresaService : BaseService
    {
        #region M�todos Principales

        /// <summary>
        /// Obtiene la configuraci�n completa de una empresa por su IdEmpresa
        /// Incluye todas las listas de detalle (empresas, pa�ses, categor�as, etc.)
        /// </summary>
        public ConfiguracionEmpresa ObtenerConfiguracionPorEmpresa(int idEmpresa)
        {
            ConfiguracionEmpresa configuracion = null;

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerConfiguracionPorEmpresa", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        // Resultado 1: Configuraci�n principal
                        if (reader.Read())
                        {
                            configuracion = new ConfiguracionEmpresa
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                IdEmpresa = Convert.ToInt32(reader["IdEmpresa"]),
                                AnioEjecucion = Convert.ToInt32(reader["AnioEjecucion"]),
                                SignoCreditos = reader["SignoCreditos"]?.ToString() ?? string.Empty,
                                Moneda = reader["Moneda"]?.ToString() ?? "CO$",
                                Unidad = reader["Unidad"]?.ToString() ?? "MILES",
                                FechaCreacion = Convert.ToDateTime(reader["FechaCreacion"]),
                                FechaModificacion = reader["FechaModificacion"] != DBNull.Value 
                                    ? Convert.ToDateTime(reader["FechaModificacion"]) 
                                    : (DateTime?)null,
                                UsuarioModificacion = reader["UsuarioModificacion"] != DBNull.Value 
                                    ? Convert.ToInt32(reader["UsuarioModificacion"]) 
                                    : (int?)null,
                                NombreEmpresa = reader["NombreEmpresa"]?.ToString()
                            };
                        }

                        if (configuracion == null)
                            return null;

                        // Resultado 2: Empresas a consolidar
                        if (reader.NextResult())
                        {
                            configuracion.EmpresasConsolidar = new List<ConfigEmpresaConsolidar>();
                            while (reader.Read())
                            {
                                configuracion.EmpresasConsolidar.Add(new ConfigEmpresaConsolidar
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreEmpresa = reader["NombreEmpresa"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 3: Pa�ses
                        if (reader.NextResult())
                        {
                            configuracion.Paises = new List<ConfigPais>();
                            while (reader.Read())
                            {
                                configuracion.Paises.Add(new ConfigPais
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombrePais = reader["NombrePais"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 4: Categor�as
                        if (reader.NextResult())
                        {
                            configuracion.Categorias = new List<ConfigCategoria>();
                            while (reader.Read())
                            {
                                configuracion.Categorias.Add(new ConfigCategoria
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreCategoria = reader["NombreCategoria"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 5: Tipos
                        if (reader.NextResult())
                        {
                            configuracion.Tipos = new List<ConfigTipo>();
                            while (reader.Read())
                            {
                                configuracion.Tipos.Add(new ConfigTipo
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreTipo = reader["NombreTipo"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 6: L�neas de negocio
                        if (reader.NextResult())
                        {
                            configuracion.LineasNegocio = new List<ConfigLineaNegocio>();
                            while (reader.Read())
                            {
                                configuracion.LineasNegocio.Add(new ConfigLineaNegocio
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreLinea = reader["NombreLinea"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 7: Ajuste 1
                        if (reader.NextResult())
                        {
                            configuracion.Ajuste1 = new List<ConfigAjuste1>();
                            while (reader.Read())
                            {
                                configuracion.Ajuste1.Add(new ConfigAjuste1
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreAjuste = reader["NombreAjuste"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 8: Ajuste 2
                        if (reader.NextResult())
                        {
                            configuracion.Ajuste2 = new List<ConfigAjuste2>();
                            while (reader.Read())
                            {
                                configuracion.Ajuste2.Add(new ConfigAjuste2
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreAjuste = reader["NombreAjuste"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }

                        // Resultado 9: Años históricos
                        if (reader.NextResult())
                        {
                            configuracion.AnosHistoricos = new List<ConfigAnoHistorico>();
                            while (reader.Read())
                            {
                                configuracion.AnosHistoricos.Add(new ConfigAnoHistorico
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    IdConfiguracion = Convert.ToInt32(reader["IdConfiguracion"]),
                                    NombreAno = reader["NombreAno"]?.ToString(),
                                    Orden = Convert.ToInt32(reader["Orden"])
                                });
                            }
                        }
                    }
                }
            }

            return configuracion;
        }

        /// <summary>
        /// Guarda o actualiza la configuraci�n b�sica de una empresa
        /// </summary>
        public bool GuardarConfiguracionBasica(ConfiguracionEmpresa config, int idUsuario, out string mensaje)
        {
            bool resultado = false;
            mensaje = string.Empty;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_GuardarConfiguracionBasica", cn);
                cmd.CommandType = CommandType.StoredProcedure;
                
                cmd.Parameters.AddWithValue("@IdEmpresa", config.IdEmpresa);
                cmd.Parameters.AddWithValue("@AnioEjecucion", config.AnioEjecucion);
                cmd.Parameters.AddWithValue("@SignoCreditos", config.SignoCreditos ?? string.Empty);
                cmd.Parameters.AddWithValue("@Moneda", config.Moneda);
                cmd.Parameters.AddWithValue("@Unidad", config.Unidad);
                cmd.Parameters.AddWithValue("@UsuarioModificacion", idUsuario);
                
                cmd.Parameters.Add("@Resultado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@Mensaje", SqlDbType.VarChar, 255).Direction = ParameterDirection.Output;

                cn.Open();
                cmd.ExecuteNonQuery();
                
                resultado = Convert.ToBoolean(cmd.Parameters["@Resultado"].Value);
                mensaje = cmd.Parameters["@Mensaje"].Value?.ToString() ?? string.Empty;
            }

            return resultado;
        }

        #endregion

        #region M�todos para Items de Configuraci�n

        /// <summary>
        /// Agrega un �tem a una lista de configuraci�n
        /// </summary>
        public bool AgregarItemConfiguracion(string tipo, int idConfiguracion, string valor, out string mensaje, out int idInsertado)
        {
            bool resultado = false;
            mensaje = string.Empty;
            idInsertado = 0;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_AgregarItemConfiguracion", cn);
                cmd.CommandType = CommandType.StoredProcedure;
                
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@IdConfiguracion", idConfiguracion);
                cmd.Parameters.AddWithValue("@Valor", valor);
                
                cmd.Parameters.Add("@Resultado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@Mensaje", SqlDbType.VarChar, 255).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@IdInsertado", SqlDbType.Int).Direction = ParameterDirection.Output;

                cn.Open();
                cmd.ExecuteNonQuery();
                
                resultado = Convert.ToBoolean(cmd.Parameters["@Resultado"].Value);
                mensaje = cmd.Parameters["@Mensaje"].Value?.ToString() ?? string.Empty;
                
                if (cmd.Parameters["@IdInsertado"].Value != DBNull.Value)
                    idInsertado = Convert.ToInt32(cmd.Parameters["@IdInsertado"].Value);
            }

            return resultado;
        }

        /// <summary>
        /// Elimina un �tem de una lista de configuraci�n
        /// </summary>
        public bool EliminarItemConfiguracion(string tipo, int id, out string mensaje)
        {
            bool resultado = false;
            mensaje = string.Empty;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_EliminarItemConfiguracion", cn);
                cmd.CommandType = CommandType.StoredProcedure;
                
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@Id", id);
                
                cmd.Parameters.Add("@Resultado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@Mensaje", SqlDbType.VarChar, 255).Direction = ParameterDirection.Output;

                cn.Open();
                cmd.ExecuteNonQuery();
                
                resultado = Convert.ToBoolean(cmd.Parameters["@Resultado"].Value);
                mensaje = cmd.Parameters["@Mensaje"].Value?.ToString() ?? string.Empty;
            }

            return resultado;
        }

        /// <summary>
        /// Actualiza el orden de los �tems en una lista
        /// </summary>
        public bool ActualizarOrdenConfiguracion(string tipo, string idsOrdenados, out string mensaje)
        {
            bool resultado = false;
            mensaje = string.Empty;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_ActualizarOrdenConfiguracion", cn);
                cmd.CommandType = CommandType.StoredProcedure;
                
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@IdsOrdenados", idsOrdenados);
                
                cmd.Parameters.Add("@Resultado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@Mensaje", SqlDbType.VarChar, 255).Direction = ParameterDirection.Output;

                cn.Open();
                cmd.ExecuteNonQuery();
                
                resultado = Convert.ToBoolean(cmd.Parameters["@Resultado"].Value);
                mensaje = cmd.Parameters["@Mensaje"].Value?.ToString() ?? string.Empty;
            }

            return resultado;
        }

        #endregion

        public int? ObtenerAnioEjecucion(int idConfiguracion)
        {
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            using (SqlCommand cmd = new SqlCommand(
                "SELECT AnioEjecucion FROM ConfiguracionesEmpresas WHERE Id = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", idConfiguracion);
                cn.Open();
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? (int?)Convert.ToInt32(result) : null;
            }
        }

        #region M�todos de Utilidad

        /// <summary>
        /// Obtiene lista de empresas disponibles para el dropdown
        /// </summary>
        public List<Empresas> ObtenerEmpresas()
        {
            List<Empresas> empresas = new List<Empresas>();

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerEmpresas", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            empresas.Add(new Empresas
                            {
                                Id = Convert.ToInt32(reader["EmpId"]),
                                Nombre = reader["EmpNombre"]?.ToString()
                            });
                        }
                    }
                }
            }

            return empresas;
        }

        #endregion
    }
}
