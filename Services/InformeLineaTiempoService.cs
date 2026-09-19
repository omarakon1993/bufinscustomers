using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe "Línea de Tiempo Financiera": serie de tiempo por indicador sobre las 8 tablas
    /// dbo.Modelo* (ver InformeTablasDatosService.ObtenerTablasModelos()). Reutiliza esa clase
    /// para todo lo de catálogo (tablas/variables/nombres amigables) y solo agrega lo propio de
    /// serie de tiempo: qué columnas numéricas tiene cada tabla, qué meses tienen datos, y la
    /// consulta agregada mes a mes.
    /// </summary>
    public class InformeLineaTiempoService : BaseService
    {
        private readonly InformeTablasDatosService _catalogo = new InformeTablasDatosService();

        // Las 8 tablas Modelo* siempre tienen [Mes] INT (1-12) — este arreglo es solo para
        // construir la etiqueta de pantalla ("Mar 2026"), nunca para comparar en SQL.
        private static readonly string[] MesesAbrev = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        private static string EtiquetaMes(int anio, int mes)
        {
            string abrev = (mes >= 1 && mes <= 12) ? MesesAbrev[mes - 1] : mes.ToString();
            return $"{abrev} {anio}";
        }

        private static readonly HashSet<string> ColumnasExcluidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Año", "Mes", "Ord", "IdEmpresa", "IdEscenario", "IdUsuario", "Id"
        };

        private static readonly HashSet<string> TiposNumericos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "decimal", "numeric", "float", "real", "money", "smallmoney", "int", "bigint", "smallint", "tinyint"
        };

        // Debe mantenerse igual al array LT_PALETTE del JS (InformeLineaTiempo.cshtml) y al
        // maximumSelectionLength del select2 de #ltIndicador — es el tope de colores distintos
        // que la paleta de la gráfica puede asignar sin repetir.
        private const int MaxVariables = 8;

        private bool ValidarTabla(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla)
                && _catalogo.ObtenerTablasModelos().Any(t => t.NombreTabla == nombreTabla);
        }

        /// <summary>
        /// Columnas numéricas graficables de la tabla (Valor, ValorPresupuesto, ValorAcumulado,
        /// etc.), excluyendo llaves/dimensiones técnicas. "Valor" siempre existe y se ordena
        /// primero — es el default cuando el cliente no manda un Campo válido.
        /// </summary>
        public List<CampoNumerico> ObtenerCamposNumericos(string nombreTabla)
        {
            var resultado = new List<CampoNumerico>();
            if (!ValidarTabla(nombreTabla))
                return resultado;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT COLUMN_NAME, DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @TableName
                    ORDER BY ORDINAL_POSITION";

                SqlCommand cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddWithValue("@TableName", nombreTabla);

                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columna = reader.GetString(0);
                        string tipo = reader.GetString(1);

                        if (ColumnasExcluidas.Contains(columna) || columna.StartsWith("Id", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!TiposNumericos.Contains(tipo))
                            continue;

                        resultado.Add(new CampoNumerico
                        {
                            NombreTecnico = columna,
                            NombreAmigable = _catalogo.ObtenerNombreAmigableColumna(columna)
                        });
                    }
                }
            }

            return resultado
                .OrderBy(c => string.Equals(c.NombreTecnico, "Valor", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => c.NombreTecnico)
                .ToList();
        }

        /// <summary>Meses con datos para la combinación tabla/empresa/escenario, para poblar Desde/Hasta.</summary>
        public List<MesDisponible> ObtenerMesesDisponibles(string nombreTabla, int? idEmpresa, int? idEscenario)
        {
            var resultado = new List<MesDisponible>();
            if (!ValidarTabla(nombreTabla))
                return resultado;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                string query = string.Format(
                    "SELECT DISTINCT [Año], [Mes] FROM dbo.{0} WHERE [Año] IS NOT NULL AND [Mes] IS NOT NULL",
                    SqlHelper.EscapeIdentifier(nombreTabla));

                var parametros = new List<SqlParameter>();
                if (idEmpresa.HasValue)
                {
                    query += " AND [IdEmpresa] = @IdEmpresa";
                    parametros.Add(new SqlParameter("@IdEmpresa", idEmpresa.Value));
                }
                if (idEscenario.HasValue)
                {
                    query += " AND [IdEscenario] = @IdEscenario";
                    parametros.Add(new SqlParameter("@IdEscenario", idEscenario.Value));
                }

                SqlCommand cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddRange(parametros.ToArray());

                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                        int anio = reader.GetInt32(0);
                        int mes = reader.GetInt32(1);
                        if (mes < 1 || mes > 12) continue;

                        resultado.Add(new MesDisponible { Anio = anio, Mes = mes, Etiqueta = EtiquetaMes(anio, mes) });
                    }
                }
            }

            return resultado
                .OrderBy(m => m.Anio).ThenBy(m => m.Mes)
                .ToList();
        }

        /// <summary>
        /// Valores de la columna [Descripcion] (el indicador/línea del informe, ej. "Utilidad neta",
        /// "Ingresos") con datos para la empresa/escenario, en el orden natural del estado financiero
        /// ([Ord]) en vez de alfabético.
        /// </summary>
        public List<string> ObtenerIndicadoresDisponibles(string nombreTabla, int? idEmpresa, int? idEscenario)
        {
            var resultado = new List<string>();
            if (!ValidarTabla(nombreTabla))
                return resultado;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                string query = string.Format(@"
                    SELECT [Descripcion], MIN([Ord]) AS OrdMin
                    FROM dbo.{0}
                    WHERE [Descripcion] IS NOT NULL AND [Descripcion] <> ''",
                    SqlHelper.EscapeIdentifier(nombreTabla));

                var parametros = new List<SqlParameter>();
                if (idEmpresa.HasValue)
                {
                    query += " AND [IdEmpresa] = @IdEmpresa";
                    parametros.Add(new SqlParameter("@IdEmpresa", idEmpresa.Value));
                }
                if (idEscenario.HasValue)
                {
                    query += " AND [IdEscenario] = @IdEscenario";
                    parametros.Add(new SqlParameter("@IdEscenario", idEscenario.Value));
                }

                query += " GROUP BY [Descripcion] ORDER BY OrdMin";

                SqlCommand cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddRange(parametros.ToArray());

                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0)) continue;
                        resultado.Add(reader.GetString(0));
                    }
                }
            }

            return resultado;
        }

        private List<PuntoLineaTiempo> ConsultarSerieMensual(SqlConnection cn, FiltrosLineaTiempo filtros, int idEscenario, string indicador)
        {
            // filtros.Campo ya fue validado contra ObtenerCamposNumericos en ObtenerSerieTiempo
            // antes de llegar aquí, así que es seguro interpolarlo (con EscapeIdentifier igual).
            string query = string.Format(@"
                SELECT t.[Año], t.[Mes], SUM(t.{1}) AS Valor
                FROM dbo.{0} t
                WHERE t.[IdEmpresa] = @IdEmpresa
                  AND t.[IdEscenario] = @IdEscenario
                  AND t.[Descripcion] = @Indicador
                  AND (t.[Año] > @AnioDesde OR (t.[Año] = @AnioDesde AND t.[Mes] >= @MesDesde))
                  AND (t.[Año] < @AnioHasta OR (t.[Año] = @AnioHasta AND t.[Mes] <= @MesHasta))
                GROUP BY t.[Año], t.[Mes]",
                SqlHelper.EscapeIdentifier(filtros.NombreTabla),
                SqlHelper.EscapeIdentifier(filtros.Campo));

            SqlCommand cmd = new SqlCommand(query, cn);
            cmd.Parameters.AddWithValue("@IdEmpresa", filtros.IdEmpresa ?? 0);
            cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
            cmd.Parameters.AddWithValue("@Indicador", indicador ?? string.Empty);
            cmd.Parameters.AddWithValue("@AnioDesde", filtros.AnioDesde);
            cmd.Parameters.AddWithValue("@MesDesde", filtros.MesDesde);
            cmd.Parameters.AddWithValue("@AnioHasta", filtros.AnioHasta);
            cmd.Parameters.AddWithValue("@MesHasta", filtros.MesHasta);
            cmd.CommandTimeout = 60;

            var puntos = new List<PuntoLineaTiempo>();
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                    int anio = reader.GetInt32(0);
                    int mes = reader.GetInt32(1);
                    if (mes < 1 || mes > 12) continue;
                    decimal valor = reader.IsDBNull(2) ? 0 : Convert.ToDecimal(reader.GetValue(2));

                    puntos.Add(new PuntoLineaTiempo { Anio = anio, Mes = mes, Etiqueta = EtiquetaMes(anio, mes), Valor = valor });
                }
            }

            return puntos.OrderBy(p => p.Anio).ThenBy(p => p.Mes).ToList();
        }

        /// <summary>Agrupa una serie mensual al nivel pedido en "Agrupar" ("Mes" | "Trimestre" | "Anio").</summary>
        public static List<PuntoLineaTiempo> Agregar(List<PuntoLineaTiempo> mensual, string agrupar)
        {
            if (mensual == null || mensual.Count == 0)
                return new List<PuntoLineaTiempo>();

            if (string.Equals(agrupar, "Anio", StringComparison.OrdinalIgnoreCase))
            {
                return mensual
                    .GroupBy(p => p.Anio)
                    .OrderBy(g => g.Key)
                    .Select(g => new PuntoLineaTiempo { Anio = g.Key, Mes = 0, Etiqueta = g.Key.ToString(), Valor = g.Sum(p => p.Valor) })
                    .ToList();
            }

            if (string.Equals(agrupar, "Trimestre", StringComparison.OrdinalIgnoreCase))
            {
                return mensual
                    .GroupBy(p => new { p.Anio, Trimestre = ((p.Mes - 1) / 3) + 1 })
                    .OrderBy(g => g.Key.Anio).ThenBy(g => g.Key.Trimestre)
                    .Select(g => new PuntoLineaTiempo { Anio = g.Key.Anio, Mes = g.Key.Trimestre, Etiqueta = $"T{g.Key.Trimestre} {g.Key.Anio}", Valor = g.Sum(p => p.Valor) })
                    .ToList();
            }

            return mensual; // "Mes": ya viene a nivel mensual
        }

        /// <summary>
        /// <paramref name="idEmpresaConsulta"/> ya viene resuelto y validado por el controlador
        /// (mismo criterio que InformeTablasDatosService.ConsultarDatos): para un no-admin es
        /// siempre una empresa a la que EmpresaAccesoHelper.TieneAcceso ya dio luz verde, así que
        /// este método no vuelve a comprobar acceso, solo usa el valor que le llega.
        /// </summary>
        public SerieLineaTiempoResultado ObtenerSerieTiempo(FiltrosLineaTiempo filtros, int? idEmpresaConsulta)
        {
            var resultado = new SerieLineaTiempoResultado();

            var indicadores = (filtros.Indicadores ?? new List<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct()
                .Take(MaxVariables)
                .ToList();

            if (!ValidarTabla(filtros.NombreTabla) || indicadores.Count == 0)
                return resultado;

            if (!idEmpresaConsulta.HasValue)
                return resultado;
            filtros.IdEmpresa = idEmpresaConsulta;

            var camposValidos = ObtenerCamposNumericos(filtros.NombreTabla);
            if (camposValidos.Count == 0)
                return resultado;
            if (!camposValidos.Any(c => c.NombreTecnico == filtros.Campo))
                filtros.Campo = camposValidos[0].NombreTecnico; // default: "Valor" (siempre primero, ver ObtenerCamposNumericos)

            var escenarios = EscenarioCacheHelper.ObtenerEscenariosCacheados();

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                int idEscenarioPrincipal = filtros.IdEscenario ?? 1;
                foreach (var indicador in indicadores)
                {
                    var mensual = ConsultarSerieMensual(cn, filtros, idEscenarioPrincipal, indicador);
                    resultado.Series.Add(new SerieVariable { Indicador = indicador, Puntos = Agregar(mensual, filtros.Agrupar) });
                }
                resultado.NombreEscenarioPrincipal = escenarios.FirstOrDefault(e => e.Id == idEscenarioPrincipal)?.Nombre;

                // Comparar contra otro escenario solo tiene sentido con una única variable en pantalla
                // (con varias, el frente ya oculta el panel) — esta es la validación de respaldo en servidor.
                if (indicadores.Count == 1 && filtros.IdEscenarioComparar.HasValue && filtros.IdEscenarioComparar.Value != idEscenarioPrincipal)
                {
                    var mensualComparacion = ConsultarSerieMensual(cn, filtros, filtros.IdEscenarioComparar.Value, indicadores[0]);
                    resultado.Comparacion = Agregar(mensualComparacion, filtros.Agrupar);
                    resultado.NombreEscenarioComparacion = escenarios.FirstOrDefault(e => e.Id == filtros.IdEscenarioComparar.Value)?.Nombre;
                }
            }

            return resultado;
        }
    }
}
