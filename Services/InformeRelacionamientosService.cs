using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    public class InformeRelacionamientosService : BaseService
    {
        /// <summary>
        /// Diccionario de tablas de relacionamiento disponibles
        /// </summary>
        private static readonly Dictionary<string, TablaRelacionamiento> TablasDisponibles = new Dictionary<string, TablaRelacionamiento>
        {
            { "Rel_Balance", new TablaRelacionamiento { NombreTabla = "Rel_Balance", NombreAmigable = "Balance", Descripcion = "Relacionamientos de Balance" } },
            { "Rel_PYG", new TablaRelacionamiento { NombreTabla = "Rel_PYG", NombreAmigable = "P&G", Descripcion = "Relacionamientos de PYG" } }
        };

        /// <summary>
        /// Obtiene la lista de tablas disponibles
        /// </summary>
        public List<TablaRelacionamiento> ObtenerTablasDisponibles()
        {
            return TablasDisponibles.Values.OrderBy(t => t.NombreAmigable).ToList();
        }

        /// <summary>
        /// Valida que el nombre de tabla sea valido (prevencion de SQL injection)
        /// </summary>
        private bool ValidarNombreTabla(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla) && TablasDisponibles.ContainsKey(nombreTabla);
        }

        /// <summary>
        /// Obtiene los tipos disponibles en una tabla especifica
        /// </summary>
        public List<string> ObtenerTiposDisponibles(string nombreTabla)
        {
            if (!ValidarNombreTabla(nombreTabla))
                return new List<string>();

            List<string> tipos = new List<string>();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = string.Format(
                        "SELECT DISTINCT [Tipo] FROM dbo.{0} WHERE [Tipo] IS NOT NULL ORDER BY [Tipo]",
                        SqlHelper.EscapeIdentifier(nombreTabla));

                    SqlCommand cmd = new SqlCommand(query, cn);
                    cn.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                tipos.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener tipos: {ex.Message}");
            }

            return tipos;
        }

        /// <summary>
        /// Obtiene las descripciones disponibles, opcionalmente filtradas por tipo
        /// </summary>
        public List<string> ObtenerDescripcionesDisponibles(string nombreTabla, string tipo = null)
        {
            if (!ValidarNombreTabla(nombreTabla))
                return new List<string>();

            List<string> descripciones = new List<string>();

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    string query = string.Format(
                        "SELECT DISTINCT [Descripcion] FROM dbo.{0} WHERE [Descripcion] IS NOT NULL",
                        SqlHelper.EscapeIdentifier(nombreTabla));

                    List<SqlParameter> parametros = new List<SqlParameter>();

                    if (!string.IsNullOrWhiteSpace(tipo))
                    {
                        query += " AND [Tipo] = @Tipo";
                        parametros.Add(new SqlParameter("@Tipo", tipo));
                    }

                    query += " ORDER BY [Descripcion]";

                    SqlCommand cmd = new SqlCommand(query, cn);
                    cmd.Parameters.AddRange(parametros.ToArray());
                    cn.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                descripciones.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al obtener descripciones: {ex.Message}");
            }

            return descripciones;
        }

        // CuentaPUC_Calc se filtra por texto libre (LIKE) para evitar cargar miles de valores en dropdown

        /// <summary>
        /// Consulta dinamica de datos con filtros
        /// </summary>
        public ResultadoInformeRelacionamientos ConsultarDatos(FiltrosInformeRelacionamientos filtros)
        {
            ResultadoInformeRelacionamientos resultado = new ResultadoInformeRelacionamientos();

            if (string.IsNullOrWhiteSpace(filtros.NombreTabla) || !ValidarNombreTabla(filtros.NombreTabla))
            {
                return resultado;
            }

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    // Detectar columnas de la tabla
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

                    // Construir query
                    string query = string.Format("SELECT * FROM dbo.{0} WHERE 1=1",
                        SqlHelper.EscapeIdentifier(filtros.NombreTabla));

                    List<SqlParameter> parametros = new List<SqlParameter>();

                    // Filtro de tipo
                    if (!string.IsNullOrWhiteSpace(filtros.Tipo))
                    {
                        query += " AND [Tipo] = @Tipo";
                        parametros.Add(new SqlParameter("@Tipo", filtros.Tipo));
                    }

                    // Filtro de descripcion
                    if (!string.IsNullOrWhiteSpace(filtros.Descripcion))
                    {
                        query += " AND [Descripcion] = @Descripcion";
                        parametros.Add(new SqlParameter("@Descripcion", filtros.Descripcion));
                    }

                    // Filtro de cuenta PUC Calc (LIKE para busqueda parcial)
                    if (!string.IsNullOrWhiteSpace(filtros.CuentaPucCalc))
                    {
                        query += " AND [CuentaPUC_Calc] LIKE @CuentaPucCalc";
                        parametros.Add(new SqlParameter("@CuentaPucCalc", $"%{filtros.CuentaPucCalc}%"));
                    }

                    // Ordenar por Id
                    bool tieneId = columnasTabla.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase));

                    if (tieneId)
                    {
                        query += " ORDER BY [Id]";
                    }

                    // Ejecutar consulta
                    SqlCommand cmd = new SqlCommand(query, cn);
                    cmd.Parameters.AddRange(parametros.ToArray());
                    cmd.CommandTimeout = 60;

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
        /// Mapea nombres tecnicos de columnas a nombres amigables
        /// </summary>
        public string ObtenerNombreAmigableColumna(string nombreColumna)
        {
            Dictionary<string, string> mapeoColumnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tipo", "Tipo" },
                { "Descripcion", "Variable/Descripción" },
                { "CuentaPUC_Calc", "Cuenta PUC Calc" },
                { "CuentaPuc", "Cuenta PUC" },
                { "NombreCuenta", "Nombre Cuenta" },
                { "Signo", "Signo" },
                { "Orden", "Orden" },
                { "Grupo", "Grupo" },
                { "SubGrupo", "Sub Grupo" },
                { "Nivel", "Nivel" },
                { "Estado", "Estado" },
                { "Activo", "Activo" },
                { "Id", "Id" },
                { "Codigo", "Código" },
                { "Nombre", "Nombre" },
                { "Naturaleza", "Naturaleza" },
                { "Clase", "Clase" },
                { "Formula", "Fórmula" },
                { "Observaciones", "Observaciones" },
                { "FechaCreacion", "Fecha Creación" },
                { "FechaModificacion", "Fecha Modificación" }
            };

            if (mapeoColumnas.ContainsKey(nombreColumna))
            {
                return mapeoColumnas[nombreColumna];
            }

            // Convertir PascalCase a espacios
            return ConvertirPascalCaseAEspacios(nombreColumna);
        }

        /// <summary>
        /// Convierte nombres en PascalCase a nombres con espacios
        /// </summary>
        private string ConvertirPascalCaseAEspacios(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return texto;

            System.Text.StringBuilder resultado = new System.Text.StringBuilder();

            for (int i = 0; i < texto.Length; i++)
            {
                char caracter = texto[i];

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
    }
}
