using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Filtros para consultas de informes de tablas de datos
    /// </summary>
    public class FiltrosInformeTablasDatos
    {
        public string NombreTabla { get; set; }
        public int? Año { get; set; }
        public int? Mes { get; set; }
        public int? IdEmpresa { get; set; }
        public string Variable { get; set; }
        public int? IdEscenario { get; set; }

        // Propiedad alternativa para evitar problemas de encoding en JavaScript
        public int? Anio
        {
            get { return Año; }
            set { Año = value; }
        }
    }

    /// <summary>
    /// Resultado de consulta de informe con datos dinámicos
    /// </summary>
    public class ResultadoInformeTablasDatos
    {
        public List<Dictionary<string, object>> Filas { get; set; }
        public List<string> Columnas { get; set; }
        public Dictionary<string, Type> TiposColumnas { get; set; }
        public int TotalRegistros { get; set; }
        public bool ResultadosTruncados { get; set; }

        public ResultadoInformeTablasDatos()
        {
            Filas = new List<Dictionary<string, object>>();
            Columnas = new List<string>();
            TiposColumnas = new Dictionary<string, Type>();
            TotalRegistros = 0;
            ResultadosTruncados = false;
        }
    }

    /// <summary>
    /// Información de una tabla de datos disponible
    /// </summary>
    public class TablaDatos
    {
        public string NombreTabla { get; set; }
        public string NombreAmigable { get; set; }
        public string Descripcion { get; set; }

        /// <summary>
        /// true si la tabla tiene columna IdEscenario (tablas de resultados de Ejecución de
        /// Modelos) — controla si el selector de Escenario se muestra en Análisis IA.
        /// </summary>
        public bool TieneEscenario { get; set; }

        /// <summary>Ícono FontAwesome para la tarjeta en "Informe de Modelos" (null → ícono genérico).</summary>
        public string Icono { get; set; }
    }
}
