using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros para el informe "Línea de Tiempo Financiera" (serie de tiempo por indicador).</summary>
    public class FiltrosLineaTiempo
    {
        public string NombreTabla { get; set; }
        public int? IdEmpresa { get; set; }
        public int? IdEscenario { get; set; }

        /// <summary>Escenario a comparar contra el principal. Solo se aplica cuando Indicadores tiene un único valor. Null = no comparar.</summary>
        public int? IdEscenarioComparar { get; set; }

        /// <summary>Valores de la columna Descripcion de la tabla elegida — una o varias, cada una se pinta como su propia serie/color.</summary>
        public List<string> Indicadores { get; set; }

        /// <summary>Columna numérica a graficar (Valor, ValorPresupuesto, ValorAcumulado, etc.).</summary>
        public string Campo { get; set; }

        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>"Mes" | "Trimestre" | "Anio" — nivel de agrupación del eje X.</summary>
        public string Agrupar { get; set; }
    }

    /// <summary>
    /// Estado visual de la pantalla al exportar a Excel, para que el archivo refleje lo que el usuario
    /// ve (tipo de gráfico, unidad, series ocultas, títulos). Todo opcional: si falta, se exporta
    /// como línea, en pesos, con todas las series.
    /// </summary>
    public class OpcionesExportLineaTiempo
    {
        /// <summary>"line" | "bar".</summary>
        public string TipoGrafico { get; set; }

        /// <summary>"pesos" | "miles" | "millones".</summary>
        public string Unidad { get; set; }

        /// <summary>Indicadores ocultados desde la tira de series.</summary>
        public List<string> SeriesOcultas { get; set; }

        public string Titulo { get; set; }
        public string Subtitulo { get; set; }

        /// <summary>Nombre amigable del Campo graficado (encabezado de la columna de valor).</summary>
        public string CampoTexto { get; set; }
    }

    /// <summary>Un punto de la serie de tiempo, ya agregado al nivel pedido en "Agrupar".</summary>
    public class PuntoLineaTiempo
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
        public decimal Valor { get; set; }
    }

    /// <summary>Un mes concreto con datos, para poblar los selectores Desde/Hasta.</summary>
    public class MesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    public class CampoNumerico
    {
        public string NombreTecnico { get; set; }
        public string NombreAmigable { get; set; }
    }

    /// <summary>Serie de tiempo de un indicador (escenario principal), ya agregada.</summary>
    public class SerieVariable
    {
        public string Indicador { get; set; }
        public List<PuntoLineaTiempo> Puntos { get; set; } = new List<PuntoLineaTiempo>();
    }

    public class SerieLineaTiempoResultado
    {
        /// <summary>Una entrada por cada indicador seleccionado (escenario principal).</summary>
        public List<SerieVariable> Series { get; set; } = new List<SerieVariable>();

        /// <summary>Solo se llena cuando Series tiene exactamente 1 elemento e IdEscenarioComparar fue enviado.</summary>
        public List<PuntoLineaTiempo> Comparacion { get; set; }
        public string NombreEscenarioPrincipal { get; set; }
        public string NombreEscenarioComparacion { get; set; }
    }
}
