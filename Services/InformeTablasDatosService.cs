using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    public class InformeTablasDatosService : BaseService
    {
        /// <summary>
        /// Diccionario de tablas disponibles con sus nombres amigables
        /// IMPORTANTE: Si se agregan o modifican tablas, actualizar este diccionario
        /// </summary>
        private static readonly Dictionary<string, TablaDatos> TablasDisponibles = new Dictionary<string, TablaDatos>
        {
            { "TableBalance_Datos_VT", new TablaDatos { NombreTabla = "TableBalance_Datos_VT", NombreAmigable = "Balance", Descripcion = "Datos de Balance" } },
            { "TablePYG_Datos_VT", new TablaDatos { NombreTabla = "TablePYG_Datos_VT", NombreAmigable = "P&G (Pérdidas y Ganancias)", Descripcion = "Datos de Pérdidas y Ganancias" } },
            { "TableEbitda_Datos_VT", new TablaDatos { NombreTabla = "TableEbitda_Datos_VT", NombreAmigable = "EBITDA", Descripcion = "Datos de EBITDA" } },
            { "TableFlujoCaja_Datos_VT", new TablaDatos { NombreTabla = "TableFlujoCaja_Datos_VT", NombreAmigable = "Flujo de Caja", Descripcion = "Datos de Flujo de Caja" } },
            { "TableFlujoTesoreria_Datos_VT", new TablaDatos { NombreTabla = "TableFlujoTesoreria_Datos_VT", NombreAmigable = "Flujo de Tesorería", Descripcion = "Datos de Flujo de Tesorería" } },
            { "TableGasFijosYVar_Datos_VT", new TablaDatos { NombreTabla = "TableGasFijosYVar_Datos_VT", NombreAmigable = "Gastos Fijos y Variables", Descripcion = "Datos de Gastos Fijos y Variables" } },
            { "TableTakeRate_Datos_VT", new TablaDatos { NombreTabla = "TableTakeRate_Datos_VT", NombreAmigable = "Take Rate", Descripcion = "Datos de Take Rate" } },
            { "TableIngCosGas_Datos_VT", new TablaDatos { NombreTabla = "TableIngCosGas_Datos_VT", NombreAmigable = "Ingresos, Costos y Gastos", Descripcion = "Datos de Ingresos, Costos y Gastos" } },
            { "TableIngLineasVenta_Datos_VT", new TablaDatos { NombreTabla = "TableIngLineasVenta_Datos_VT", NombreAmigable = "Ingresos por Líneas de Venta", Descripcion = "Datos de Ingresos por Líneas de Venta" } },
            { "TablePYGAjustado_Datos_VT", new TablaDatos { NombreTabla = "TablePYGAjustado_Datos_VT", NombreAmigable = "P&G Ajustado", Descripcion = "Datos de P&G Ajustado" } }
        };

        /// <summary>
        /// Obtiene la lista de tablas disponibles
        /// </summary>
        public List<TablaDatos> ObtenerTablasDisponibles()
        {
            return TablasDisponibles.Values.OrderBy(t => t.NombreAmigable).ToList();
        }

        /// <summary>
        /// Valida que el nombre de tabla sea válido (prevención de SQL injection)
        /// </summary>
        private bool ValidarNombreTabla(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla) && TablasDisponibles.ContainsKey(nombreTabla);
        }

        /// <summary>
        /// Obtiene los años disponibles en una tabla específica
        /// </summary>
        public List<int> ObtenerAñosDisponibles(string nombreTabla, int? idEmpresa = null)
        {
            if (!ValidarNombreTabla(nombreTabla))
                return new List<int>();

            List<int> años = new List<int>();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    // Usar QUOTENAME para seguridad adicional con el nombre de tabla
                    string query = string.Format("SELECT DISTINCT [Año] FROM dbo.{0} WHERE [Año] IS NOT NULL",
                        SqlHelper.EscapeIdentifier(nombreTabla));

                    if (idEmpresa.HasValue)
                    {
                        query += " AND [IdEmpresa] = @IdEmpresa";
                    }

                    query += " ORDER BY [Año] DESC";

                    SqlCommand cmd = new SqlCommand(query, cn);

                    if (idEmpresa.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa.Value);
                    }

                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                años.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener años: {ex.Message}");
            }

            return años;
        }

        /// <summary>
        /// Obtiene las variables disponibles en una tabla específica
        /// </summary>
        public List<string> ObtenerVariablesDisponibles(string nombreTabla, int? idEmpresa = null)
        {
            if (!ValidarNombreTabla(nombreTabla))
                return new List<string>();

            List<string> variables = new List<string>();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = string.Format("SELECT DISTINCT [Variable] FROM dbo.{0} WHERE [Variable] IS NOT NULL",
                        SqlHelper.EscapeIdentifier(nombreTabla));

                    if (idEmpresa.HasValue)
                    {
                        query += " AND [IdEmpresa] = @IdEmpresa";
                    }

                    query += " ORDER BY [Variable]";

                    SqlCommand cmd = new SqlCommand(query, cn);

                    if (idEmpresa.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa.Value);
                    }

                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                variables.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener variables: {ex.Message}");
            }

            return variables;
        }

        /// <summary>
        /// Consulta dinámica de datos con filtros
        /// </summary>
        public ResultadoInformeTablasDatos ConsultarDatos(FiltrosInformeTablasDatos filtros, bool esAdmin, int? idEmpresaUsuario = null)
        {
            ResultadoInformeTablasDatos resultado = new ResultadoInformeTablasDatos();

            if (string.IsNullOrWhiteSpace(filtros.NombreTabla) || !ValidarNombreTabla(filtros.NombreTabla))
            {
                return resultado;
            }

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    // Construir query dinámica con QUOTENAME para seguridad
                    string query = string.Format("SELECT * FROM dbo.{0} WHERE 1=1",
                        SqlHelper.EscapeIdentifier(filtros.NombreTabla));

                    List<SqlParameter> parametros = new List<SqlParameter>();

                    // Filtro de empresa (seguridad: usuarios no admin solo ven su empresa)
                    if (!esAdmin && idEmpresaUsuario.HasValue)
                    {
                        query += " AND [IdEmpresa] = @IdEmpresa";
                        parametros.Add(new SqlParameter("@IdEmpresa", idEmpresaUsuario.Value));
                    }
                    else if (filtros.IdEmpresa.HasValue)
                    {
                        query += " AND [IdEmpresa] = @IdEmpresa";
                        parametros.Add(new SqlParameter("@IdEmpresa", filtros.IdEmpresa.Value));
                    }

                    // Filtro de año
                    if (filtros.Año.HasValue)
                    {
                        query += " AND [Año] = @Año";
                        parametros.Add(new SqlParameter("@Año", filtros.Año.Value));
                    }

                    // Filtro de mes
                    if (filtros.Mes.HasValue)
                    {
                        query += " AND [Mes] = @Mes";
                        parametros.Add(new SqlParameter("@Mes", filtros.Mes.Value));
                    }

                    // Filtro de variable
                    if (!string.IsNullOrWhiteSpace(filtros.Variable))
                    {
                        query += " AND [Variable] LIKE @Variable";
                        parametros.Add(new SqlParameter("@Variable", $"%{filtros.Variable}%"));
                    }

                    query += " ORDER BY [Año] DESC, [Mes] DESC";

                    SqlCommand cmd = new SqlCommand(query, cn);
                    cmd.Parameters.AddRange(parametros.ToArray());
                    cmd.CommandTimeout = 60; // 60 segundos timeout

                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        // Obtener nombres y tipos de columnas
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string nombreColumna = reader.GetName(i);
                            Type tipoColumna = reader.GetFieldType(i);

                            resultado.Columnas.Add(nombreColumna);
                            resultado.TiposColumnas[nombreColumna] = tipoColumna;
                        }

                        // Leer datos
                        while (reader.Read())
                        {
                            Dictionary<string, object> fila = new Dictionary<string, object>();

                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string nombreColumna = reader.GetName(i);
                                object valor = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                fila[nombreColumna] = valor;
                            }

                            resultado.Filas.Add(fila);
                        }

                        resultado.TotalRegistros = resultado.Filas.Count;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al consultar datos: {ex.Message}");
                throw new Exception($"Error al consultar datos de la tabla {filtros.NombreTabla}: {ex.Message}", ex);
            }

            return resultado;
        }

        /// <summary>
        /// Mapea nombres técnicos de columnas a nombres amigables
        /// </summary>
        public string ObtenerNombreAmigableColumna(string nombreColumna)
        {
            Dictionary<string, string> mapeoColumnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "IdEmpresa", "ID Empresa" },
                { "Empresa", "Empresa" },
                { "Año", "Año" },
                { "Mes", "Mes" },
                { "Variable", "Variable" },
                { "Valor", "Valor" },
                { "Monto", "Monto" },
                { "Cantidad", "Cantidad" },
                { "Porcentaje", "Porcentaje" },
                { "Descripcion", "Descripción" },
                { "Categoria", "Categoría" },
                { "Tipo", "Tipo" },
                { "FechaCreacion", "Fecha de Creación" },
                { "FechaModificacion", "Fecha de Modificación" },
                { "UsuarioCreacion", "Usuario Creación" },
                { "UsuarioModificacion", "Usuario Modificación" },
                { "Activo", "Activo" },
                { "Estado", "Estado" }
            };

            return mapeoColumnas.ContainsKey(nombreColumna) ? mapeoColumnas[nombreColumna] : nombreColumna;
        }

        /// <summary>
        /// Obtiene las empresas disponibles
        /// </summary>
        public List<Empresas> ObtenerEmpresas()
        {
            List<Empresas> empresas = new List<Empresas>();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = "SELECT [EmpId], [EmpNombre] FROM dbo.[Empresas] ORDER BY [EmpNombre]";
                    SqlCommand cmd = new SqlCommand(query, cn);

                    cn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            empresas.Add(new Empresas
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener empresas: {ex.Message}");
            }

            return empresas;
        }
    }

    /// <summary>
    /// Helper para escapar identificadores SQL
    /// </summary>
    internal static class SqlHelper
    {
        public static string EscapeIdentifier(string identifier)
        {
            // Remover caracteres peligrosos y usar corchetes
            identifier = identifier.Replace("[", "").Replace("]", "").Replace(";", "");
            return $"[{identifier}]";
        }
    }
}
