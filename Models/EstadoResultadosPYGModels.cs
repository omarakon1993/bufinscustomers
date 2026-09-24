using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros del informe PYG (consulta, insights IA y exportación a Excel usan los mismos).
    /// El rango es continuo y puede cruzar años (mismo modelo que el Informe de Línea de Tiempo).</summary>
    public class FiltrosPYG
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Contra qué se compara el Real — ver PygComparar. Por defecto el presupuesto.</summary>
        public string Comparar { get; set; } = PygComparar.Presupuesto;

        /// <summary>Segundo escenario a comparar contra el principal (columna extra). null = no comparar.</summary>
        public byte? IdEscenarioComparar { get; set; }

        /// <summary>"Mes" | "Trimestre" | "Anio" — agrupación de las gráficas de tendencia.</summary>
        public string Agrupar { get; set; } = "Mes";
    }

    /// <summary>Códigos de "Comparar contra" (la vista los traduce; el servicio no lee resx).</summary>
    public static class PygComparar
    {
        public const string Presupuesto = "ppto";
        public const string PresupuestoAjuste = "pptoAjuste";
        public const string Forecast = "forecast";

        public static string Normalizar(string valor)
        {
            if (string.Equals(valor, PresupuestoAjuste, System.StringComparison.OrdinalIgnoreCase)) return PresupuestoAjuste;
            if (string.Equals(valor, Forecast, System.StringComparison.OrdinalIgnoreCase)) return Forecast;
            return Presupuesto;
        }
    }

    /// <summary>Un mes con datos en dbo.ModeloPYG, para poblar el slider de rango.</summary>
    public class PygMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

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

        /// <summary>Real del escenario de comparación (FiltrosPYG.IdEscenarioComparar) y la variación del
        /// escenario principal contra él — null cuando no se pidió comparar escenarios.</summary>
        public decimal? PeriodoRealEscenario { get; set; }
        public decimal? PeriodoVariacionEscenarioPorcentual { get; set; }
        public decimal? AcumRealEscenario { get; set; }
        public decimal? AcumVariacionEscenarioPorcentual { get; set; }
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

    /// <summary>Un punto de la serie de tendencia (Ingresos/EBITDA/Utilidad Neta, Real vs comparativo),
    /// ya agregado al nivel pedido en FiltrosPYG.Agrupar (mes, trimestre o año).</summary>
    public class PygPuntoTendencia
    {
        public int Año { get; set; }
        /// <summary>Mes 1-12 (agrupado por Mes), trimestre 1-4 (por Trimestre) o 0 (por Año).</summary>
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
        public decimal Real { get; set; }
        /// <summary>Valor comparativo elegido en "Comparar contra" (presupuesto, presupuesto con ajuste o forecast).</summary>
        public decimal Presupuesto { get; set; }
        /// <summary>Real del escenario de comparación — null si no se pidió comparar escenarios.</summary>
        public decimal? RealEscenario { get; set; }
        /// <summary>true si el punto (mes/trimestre/año) toca el rango consultado — la vista lo sombrea.</summary>
        public bool EnRango { get; set; }
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

        /// <summary>Margen del escenario de comparación — null si no se pidió comparar escenarios.</summary>
        public decimal? PeriodoRealPctEscenario { get; set; }
        public decimal? AcumRealPctEscenario { get; set; }
    }

    /// <summary>Resultado completo consumido por el controlador/vista del Estado de Resultados PYG.</summary>
    public class PygReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        /// <summary>= AnioHasta (año del bloque "Acumulado"); se conserva por compatibilidad.</summary>
        public int Año { get; set; }
        /// <summary>Rango elegido en el slider (ambos inclusive), puede cruzar años. El bloque "Acumulado"
        /// es el acumulado del año AnioHasta hasta MesHasta (ValorAcumulado ya es un corrido hasta ese mes).</summary>
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Código PygComparar efectivamente usado para las columnas "Presupuesto"/variación.</summary>
        public string Comparar { get; set; }
        public string Agrupar { get; set; }
        /// <summary>true si se pidió comparar contra otro escenario y ese escenario tiene datos en el rango.</summary>
        public bool HayEscenarioComparar { get; set; }
        public byte? IdEscenarioComparar { get; set; }

        /// <summary>Líneas clave para KPIs y encabezados de tendencia (la vista no las busca por texto).</summary>
        public PygFilaReporte EbitdaFila { get; set; }
        public PygFilaReporte UtilidadNetaFila { get; set; }
        public List<PygFilaReporte> Filas { get; set; } = new List<PygFilaReporte>();
        public List<PygKpiMargen> Kpis { get; set; } = new List<PygKpiMargen>();
        /// <summary>Línea de Ingresos (Real/Ppto/año anterior del período y acumulado) para la tarjeta KPI
        /// "Ingresos" — se expone aparte porque su Descripcion puede variar entre empresas y la vista no
        /// debe buscarla por texto dentro de Filas.</summary>
        public PygFilaReporte IngresosFila { get; set; }
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
