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
                    cn.Open();

                    // Primero verificar qué columna existe (Variables o Variable)
                    const string checkQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_NAME = @TableName AND COLUMN_NAME = 'Variables'";

                    SqlCommand checkCmd = new SqlCommand(checkQuery, cn);
                    checkCmd.Parameters.AddWithValue("@TableName", nombreTabla);
                    int tieneVariablesPlural = (int)checkCmd.ExecuteScalar();

                    string columnaNombre = tieneVariablesPlural > 0 ? "Variables" : "Variable";

                    // Construir query usando la columna correcta
                    string query = string.Format("SELECT DISTINCT [{0}] FROM dbo.{1} WHERE [{0}] IS NOT NULL",
                        columnaNombre,
                        SqlHelper.EscapeIdentifier(nombreTabla));

                    if (idEmpresa.HasValue)
                    {
                        query += " AND [IdEmpresa] = @IdEmpresa";
                    }

                    query += $" ORDER BY [{columnaNombre}]";

                    SqlCommand cmd = new SqlCommand(query, cn);

                    if (idEmpresa.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa.Value);
                    }

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
        /// Convierte número de mes a abreviatura en español
        /// </summary>
        private string ConvertirNumeroAMes(int mes)
        {
            string[] meses = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
            return (mes >= 1 && mes <= 12) ? meses[mes - 1] : null;
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

            int maxFilas = 5000;
            if (int.TryParse(System.Configuration.ConfigurationManager.AppSettings["MaxFilasConsulta"], out int maxCfg) && maxCfg > 0)
                maxFilas = maxCfg;

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    // Primero verificar qué columnas tiene la tabla
                    List<string> columnasTabla = new List<string>();
                    string querySchema = string.Format("SELECT TOP 0 * FROM dbo.{0}",
                        SqlHelper.EscapeIdentifier(filtros.NombreTabla));

                    using (SqlCommand cmdSchema = new SqlCommand(querySchema, cn))
                    using (SqlDataReader schemaReader = cmdSchema.ExecuteReader())
                    {
                        for (int i = 0; i < schemaReader.FieldCount; i++)
                        {
                            columnasTabla.Add(schemaReader.GetName(i));
                        }
                    }

                    // Verificar si tiene columna IdEmpresa para hacer JOIN
                    bool tieneIdEmpresa = columnasTabla.Any(c => c.Equals("IdEmpresa", StringComparison.OrdinalIgnoreCase));
                    bool tieneUsuarioEjecucion = columnasTabla.Any(c => c.Equals("UsuarioEjecucion", StringComparison.OrdinalIgnoreCase));

                    // Construir lista de columnas excluyendo IdEmpresa y UsuarioEjecucion
                    List<string> columnasSeleccion = new List<string>();
                    foreach (var col in columnasTabla)
                    {
                        // Excluir IdEmpresa y UsuarioEjecucion porque se reemplazarán con JOINs
                        if (!col.Equals("IdEmpresa", StringComparison.OrdinalIgnoreCase) &&
                            !col.Equals("UsuarioEjecucion", StringComparison.OrdinalIgnoreCase))
                        {
                            columnasSeleccion.Add($"t.[{col}]");
                        }
                    }

                    // Construir query con JOINs necesarios
                    string query;
                    if (tieneIdEmpresa || tieneUsuarioEjecucion)
                    {
                        string seleccion = string.Join(", ", columnasSeleccion);

                        // Agregar columnas de JOINs
                        if (tieneIdEmpresa)
                        {
                            seleccion += ", e.EmpNombre AS NombreEmpresa";
                        }
                        if (tieneUsuarioEjecucion)
                        {
                            seleccion += ", COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(u.Nombre, '') + ' ' + ISNULL(u.Apellidos, ''))), ''), u.Correo, 'Sin usuario') AS NombreUsuario";
                        }

                        query = string.Format(@"
                            SELECT TOP ({2}) {1}
                            FROM dbo.{0} t",
                            SqlHelper.EscapeIdentifier(filtros.NombreTabla),
                            seleccion,
                            maxFilas);

                        if (tieneIdEmpresa)
                        {
                            query += " LEFT JOIN dbo.Empresas e ON t.IdEmpresa = e.EmpId";
                        }
                        if (tieneUsuarioEjecucion)
                        {
                            query += " LEFT JOIN dbo.Usuarios u ON t.UsuarioEjecucion = u.Id";
                        }

                        query += " WHERE 1=1";
                    }
                    else
                    {
                        query = string.Format("SELECT TOP ({1}) * FROM dbo.{0} WHERE 1=1",
                            SqlHelper.EscapeIdentifier(filtros.NombreTabla),
                            maxFilas);
                    }

                    List<SqlParameter> parametros = new List<SqlParameter>();

                    // Determinar si se debe usar prefijo de tabla basándose en si hay JOINs
                    bool usarPrefijo = tieneIdEmpresa || tieneUsuarioEjecucion;
                    string prefijo = usarPrefijo ? "t." : "";

                    // Filtro de empresa (seguridad: usuarios no admin solo ven su empresa)
                    if (tieneIdEmpresa)
                    {
                        if (!esAdmin && idEmpresaUsuario.HasValue)
                        {
                            query += $" AND {prefijo}[IdEmpresa] = @IdEmpresa";
                            parametros.Add(new SqlParameter("@IdEmpresa", idEmpresaUsuario.Value));
                        }
                        else if (filtros.IdEmpresa.HasValue)
                        {
                            query += $" AND {prefijo}[IdEmpresa] = @IdEmpresa";
                            parametros.Add(new SqlParameter("@IdEmpresa", filtros.IdEmpresa.Value));
                        }
                    }

                    // Filtro de año
                    if (filtros.Año.HasValue && columnasTabla.Any(c => c.Equals("Año", StringComparison.OrdinalIgnoreCase)))
                    {
                        query += $" AND {prefijo}[Año] = @Año";
                        parametros.Add(new SqlParameter("@Año", filtros.Año.Value));
                    }

                    // Filtro de mes - convertir número a abreviatura
                    if (filtros.Mes.HasValue && columnasTabla.Any(c => c.Equals("Mes", StringComparison.OrdinalIgnoreCase)))
                    {
                        string mesAbrev = ConvertirNumeroAMes(filtros.Mes.Value);
                        if (!string.IsNullOrEmpty(mesAbrev))
                        {
                            query += $" AND {prefijo}[Mes] = @Mes";
                            parametros.Add(new SqlParameter("@Mes", mesAbrev));
                        }
                    }

                    // Filtro de variable (puede ser "Variable" o "Variables")
                    if (!string.IsNullOrWhiteSpace(filtros.Variable))
                    {
                        bool tieneVariablesPlural = columnasTabla.Any(c => c.Equals("Variables", StringComparison.OrdinalIgnoreCase));
                        bool tieneVariableSingular = columnasTabla.Any(c => c.Equals("Variable", StringComparison.OrdinalIgnoreCase));

                        if (tieneVariablesPlural)
                        {
                            query += $" AND {prefijo}[Variables] LIKE @Variable";
                            parametros.Add(new SqlParameter("@Variable", $"%{filtros.Variable}%"));
                        }
                        else if (tieneVariableSingular)
                        {
                            query += $" AND {prefijo}[Variable] LIKE @Variable";
                            parametros.Add(new SqlParameter("@Variable", $"%{filtros.Variable}%"));
                        }
                    }

                    // Ordenar por año y mes si existen esas columnas
                    string orderBy = "";
                    if (columnasTabla.Any(c => c.Equals("Año", StringComparison.OrdinalIgnoreCase)))
                    {
                        orderBy = $" ORDER BY {prefijo}[Año] DESC";

                        if (columnasTabla.Any(c => c.Equals("Mes", StringComparison.OrdinalIgnoreCase)))
                        {
                            orderBy += $", {prefijo}[Mes] DESC";
                        }
                    }

                    query += orderBy;

                    // Ejecutar consulta
                    SqlCommand cmd = new SqlCommand(query, cn);
                    cmd.Parameters.AddRange(parametros.ToArray());
                    cmd.CommandTimeout = 60;

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        // Obtener nombres y tipos de columnas (excluyendo IdEmpresa y UsuarioEjecucion)
                        List<int> indicesColumnas = new List<int>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string nombreColumna = reader.GetName(i);

                            // Excluir IdEmpresa y UsuarioEjecucion de los resultados
                            if (!nombreColumna.Equals("IdEmpresa", StringComparison.OrdinalIgnoreCase) &&
                                !nombreColumna.Equals("UsuarioEjecucion", StringComparison.OrdinalIgnoreCase))
                            {
                                Type tipoColumna = reader.GetFieldType(i);
                                resultado.Columnas.Add(nombreColumna);
                                resultado.TiposColumnas[nombreColumna] = tipoColumna;
                                indicesColumnas.Add(i);
                            }
                        }

                        // Leer datos
                        while (reader.Read())
                        {
                            Dictionary<string, object> fila = new Dictionary<string, object>();

                            foreach (int indice in indicesColumnas)
                            {
                                string nombreColumna = reader.GetName(indice);
                                object valor = reader.IsDBNull(indice) ? null : reader.GetValue(indice);
                                fila[nombreColumna] = valor;
                            }

                            resultado.Filas.Add(fila);
                        }

                        resultado.TotalRegistros = resultado.Filas.Count;
                        resultado.ResultadosTruncados = resultado.Filas.Count >= maxFilas;
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

        public System.Threading.Tasks.Task<ResultadoInformeTablasDatos> ConsultarDatosAsync(
            FiltrosInformeTablasDatos filtros, bool esAdmin, int? idEmpresaUsuario = null)
        {
            return System.Threading.Tasks.Task.Run(() => ConsultarDatos(filtros, esAdmin, idEmpresaUsuario));
        }

        /// <summary>
        /// Mapea nombres técnicos de columnas a nombres amigables
        /// </summary>
        public string ObtenerNombreAmigableColumna(string nombreColumna)
        {
            Dictionary<string, string> mapeoColumnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Información de empresa y organización
                { "NombreEmpresa", "Empresa" },
                { "Empresa", "Empresa" },

                // Información temporal
                { "Año", "Año" },
                { "Mes", "Mes" },

                // Información de variables y valores
                { "Variable", "Variable" },
                { "Valor", "Valor" },
                { "ValorPresupuesto", "Valor Presupuesto" },
                { "ValorReal", "Valor Real" },
                { "ValorProyectado", "Valor Proyectado" },
                { "ValorAnterior", "Valor Anterior" },
                { "ValorActual", "Valor Actual" },
                { "ValorPpto", "Valor Presupuesto" },

                // Montos y cantidades
                { "Monto", "Monto" },
                { "MontoPresupuesto", "Monto Presupuesto" },
                { "MontoReal", "Monto Real" },
                { "Cantidad", "Cantidad" },
                { "CantidadPresupuesto", "Cantidad Presupuesto" },
                { "CantidadReal", "Cantidad Real" },

                // Porcentajes y ratios
                { "Porcentaje", "Porcentaje" },
                { "PorcentajeEjecutado", "Porcentaje Ejecutado" },
                { "PorcentajeVariacion", "Porcentaje Variación" },

                // Descripciones y categorías
                { "Descripcion", "Descripción" },
                { "Categoria", "Categoría" },
                { "Tipo", "Tipo" },
                { "TipoVariable", "Tipo Variable" },
                { "TipoDato", "Tipo Dato" },

                // Fechas
                { "FechaEjecucion", "Fecha Ejecución" },
                { "FechaCreacion", "Fecha Creación" },
                { "FechaModificacion", "Fecha Modificación" },
                { "FechaCargue", "Fecha Cargue" },

                // Usuarios
                { "NombreUsuario", "Usuario Ejecución" },
                { "UsuarioEjecucion", "Usuario Ejecución" },
                { "UsuarioCreacion", "Usuario Creación" },
                { "UsuarioModificacion", "Usuario Modificación" },

                // Estados
                { "Activo", "Activo" },
                { "Estado", "Estado" },
                { "EstadoProceso", "Estado Proceso" },

                // Consolidaciones y ajustes
                { "EsConsolidado", "Es Consolidado" },
                { "Consolidado", "Consolidado" },
                { "Ajuste", "Ajuste" },
                { "Ajuste1", "Ajuste 1" },
                { "Ajuste2", "Ajuste 2" }
            };

            // Si encuentra el nombre en el diccionario, retornarlo
            if (mapeoColumnas.ContainsKey(nombreColumna))
            {
                return mapeoColumnas[nombreColumna];
            }

            // Si no está en el diccionario, convertir PascalCase a nombre con espacios
            return ConvertirPascalCaseAEspacios(nombreColumna);
        }

        /// <summary>
        /// Convierte nombres en PascalCase a nombres con espacios
        /// Ejemplo: "ValorPresupuesto" -> "Valor Presupuesto"
        /// </summary>
        private string ConvertirPascalCaseAEspacios(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return texto;

            // Insertar espacio antes de cada letra mayúscula que esté seguida de una minúscula
            // o que esté precedida por una minúscula
            System.Text.StringBuilder resultado = new System.Text.StringBuilder();

            for (int i = 0; i < texto.Length; i++)
            {
                char caracter = texto[i];

                // Agregar espacio antes de mayúsculas si:
                // - No es el primer carácter
                // - El carácter anterior es minúscula
                // - O el siguiente carácter es minúscula (para casos como "XMLParser" -> "XML Parser")
                if (i > 0 && char.IsUpper(caracter))
                {
                    char anterior = texto[i - 1];
                    bool siguienteEsMinuscula = (i < texto.Length - 1) && char.IsLower(texto[i + 1]);

                    if (char.IsLower(anterior) || siguienteEsMinuscula)
                    {
                        resultado.Append(' ');
                    }
                }

                resultado.Append(caracter);
            }

            return resultado.ToString();
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
