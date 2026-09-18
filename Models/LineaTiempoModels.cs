using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros para el informe "Línea de Tiempo Financiera" (serie de tiempo por indicador).</summary>
    public class FiltrosLineaTiempo
    {
        public string NombreTabla { get; set; }
        public int? IdEmpresa { get; set; }
        public int? IdEscenario { get; set; }

        /// <summary>Escenario a comparar contra el principal. Null = no comparar.</summary>
        public int? IdEscenarioComparar { get; set; }

        /// <summary>Valor de la columna Descripcion de la tabla elegida.</summary>
        public string Indicador { get; set; }

        /// <summary>Columna numérica a graficar (Valor, ValorPresupuesto, ValorAcumulado, etc.).</summary>
        public string Campo { get; set; }

        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>"Mes" | "Trimestre" | "Anio" — nivel de agrupación del eje X.</summary>
        public string Agrupar { get; set; }
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

    public class SerieLineaTiempoResultado
    {
        public List<PuntoLineaTiempo> Principal { get; set; } = new List<PuntoLineaTiempo>();
        public List<PuntoLineaTiempo> Comparacion { get; set; }
        public string NombreEscenarioPrincipal { get; set; }
        public string NombreEscenarioComparacion { get; set; }
    }
}
