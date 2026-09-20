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

        /// <summary>Comparación interanual (año contra año) — null cuando el año anterior no tiene datos
        /// cargados para esta empresa/escenario, o cuando el valor base del año anterior es cero.</summary>
        public decimal? PeriodoRealAnioAnterior { get; set; }
        public decimal? PeriodoVariacionYoYPorcentual { get; set; }
        public decimal? AcumRealAnioAnterior { get; set; }
        public decimal? AcumVariacionYoYPorcentual { get; set; }
    }

    /// <summary>Un tramo de la cascada (waterfall) Ingresos → Utilidad Neta del período seleccionado
    /// (valores Real). "Etiqueta" es un código fijo (no texto traducido — la vista lo traduce, mismo
    /// patrón que PygKpiMargen.Codigo) de la lista: Ingresos, CostoVentas, GastosOperacionales,
    /// OtrosEImpuestos, UtilidadNeta.</summary>
    public class PygCascadaBarra
    {
        public string Etiqueta { get; set; }
        /// <summary>"total" (barra de piso, Ingresos/Utilidad Neta) | "positivo" | "negativo".</summary>
        public string Tipo { get; set; }
        public decimal Desde { get; set; }
        public decimal Hasta { get; set; }
        /// <summary>Solo para tramos intermedios (Tipo positivo/negativo): Hasta - Desde con signo real
        /// (antes de tomar Math.Min/Max para las coordenadas de la barra flotante).</summary>
        public decimal? Delta { get; set; }
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

        /// <summary>Margen del mismo período del año anterior — null si ese año no tiene datos.</summary>
        public decimal? PeriodoRealPctAnioAnterior { get; set; }
        public decimal? AcumRealPctAnioAnterior { get; set; }
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

        /// <summary>Cascada Ingresos → Utilidad Neta (Real) del período seleccionado — ver PygCascadaBarra.</summary>
        public List<PygCascadaBarra> CascadaPeriodo { get; set; } = new List<PygCascadaBarra>();
        /// <summary>Utilidad Neta presupuestada del mismo período, para la línea de referencia del waterfall.</summary>
        public decimal CascadaPresupuestoUtilidadNeta { get; set; }

        /// <summary>true si el año anterior (Año-1) tiene datos cargados para esta empresa/escenario —
        /// controla si la comparación interanual (YoY) se puede mostrar.</summary>
        public bool HayAnioAnterior { get; set; }
        public int AnioAnterior { get; set; }

        public bool SinDatos { get; set; }
        public string Mensaje { get; set; }
        // EXTENSIÓN FUTURA: agregar aquí un List<PygLineaNegocioDesglose> alimentado por
        // dbo.ModeloLineasNegocio cuando se aborde la fase de Líneas de Negocio (fuera de alcance ahora).
    }
}
