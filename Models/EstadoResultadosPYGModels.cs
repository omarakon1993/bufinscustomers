using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Una fila cruda de dbo.ModeloPYG (resultado materializado de sp_ModeloPYG), ya con el
    /// flag de subtotal resuelto vía JOIN a dbo.Rel_PYG.</summary>
    public class PygFilaModelo
    {
        public byte IdEscenario { get; set; }
        public int Año { get; set; }
        public int Mes { get; set; }                 // 1-12 (columna Mes de dbo.ModeloPYG ya es int)
        public int Ord { get; set; }                 // = Rel_PYG.Id, orden de despliegue
        public string CuentaPUC { get; set; }
        public string Descripcion { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorAcumulado { get; set; }
        public decimal ValorForecast { get; set; }
        public decimal ValorFuturo { get; set; }
        public decimal ValorFuturoAcumulado { get; set; }
        public decimal ValorPresupuesto { get; set; }
        public decimal ValorPresupuestoAcumulado { get; set; }
        public decimal ValorPresupuestoConAjuste { get; set; }
        public bool EsSubtotal { get; set; }          // Rel_PYG.Tipo <> "Cuentas" (o sea "Calculo"/"Calculo SQL")
    }

    /// <summary>Una fila de la tabla PYG lista para la vista: bloque "Periodo seleccionado" (rango de
    /// meses elegido en el slider, sumado) + bloque "Acumulado del año" (hasta el mes final del rango).</summary>
    public class PygFilaReporte
    {
        public int Orden { get; set; }
        public string Descripcion { get; set; }
        public bool EsSubtotal { get; set; }
        /// <summary>true = línea de costo/gasto/impuesto, donde "Real > Presupuesto" es DESFAVORABLE
        /// (se gastó más de lo presupuestado) — controla el color rojo/verde de la variación en la UI.
        /// false = línea de ingreso/utilidad, donde "Real > Presupuesto" es favorable.</summary>
        public bool EsGastoOCosto { get; set; }

        public decimal PeriodoReal { get; set; }
        public decimal PeriodoPresupuesto { get; set; }
        public decimal PeriodoVariacionAbsoluta => PeriodoReal - PeriodoPresupuesto;
        public decimal? PeriodoVariacionPorcentual { get; set; }
        public decimal? PeriodoMargen { get; set; }

        public decimal AcumReal { get; set; }
        public decimal AcumPresupuesto { get; set; }
        public decimal AcumVariacionAbsoluta => AcumReal - AcumPresupuesto;
        public decimal? AcumVariacionPorcentual { get; set; }
        public decimal? AcumMargen { get; set; }
    }

    /// <summary>Un punto de la serie de tendencia (Ingresos/EBITDA/Utilidad Neta, Real vs Presupuesto).</summary>
    public class PygPuntoTendencia
    {
        public int Año { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
        public decimal Real { get; set; }
        public decimal Presupuesto { get; set; }
    }

    /// <summary>KPI de margen (Bruto/EBITDA/Neto) para las tarjetas superiores, Periodo y Acumulado.</summary>
    public class PygKpiMargen
    {
        public string Codigo { get; set; }
        public decimal PeriodoRealPct { get; set; }
        public decimal PeriodoPresupuestoPct { get; set; }
        public decimal AcumRealPct { get; set; }
        public decimal AcumPresupuestoPct { get; set; }
    }

    /// <summary>Resultado completo consumido por el controlador/vista del Estado de Resultados PYG.</summary>
    public class PygReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int Año { get; set; }
        /// <summary>Rango de meses elegido en el slider (ambos inclusive, 1-12). El bloque "Acumulado"
        /// siempre se toma en MesHasta (ValorAcumulado ya es un acumulado corrido hasta ese mes).</summary>
        public int MesDesde { get; set; }
        public int MesHasta { get; set; }
        public List<PygFilaReporte> Filas { get; set; } = new List<PygFilaReporte>();
        public List<PygKpiMargen> Kpis { get; set; } = new List<PygKpiMargen>();
        public List<PygPuntoTendencia> TendenciaIngresos { get; set; } = new List<PygPuntoTendencia>();
        public List<PygPuntoTendencia> TendenciaEbitda { get; set; } = new List<PygPuntoTendencia>();
        public List<PygPuntoTendencia> TendenciaUtilidadNeta { get; set; } = new List<PygPuntoTendencia>();
        public bool SinDatos { get; set; }
        public string Mensaje { get; set; }
        // EXTENSIÓN FUTURA: agregar aquí un List<PygLineaNegocioDesglose> alimentado por
        // dbo.ModeloLineasNegocio cuando se aborde la fase de Líneas de Negocio (fuera de alcance ahora).
    }
}
