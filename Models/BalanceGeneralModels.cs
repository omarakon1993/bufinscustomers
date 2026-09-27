using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros del Informe de Balance General (consulta, insights IA y exportación a Excel usan
    /// los mismos). El rango es continuo y puede cruzar años (mismo slider que PYG/Línea de Tiempo), pero
    /// a diferencia del PYG el balance NO es un flujo acumulable: el "corte" para KPIs/tabla/cuadre es
    /// siempre el extremo derecho del rango (AnioHasta/MesHasta) — el rango completo solo alimenta las
    /// tendencias mensuales.</summary>
    public class FiltrosBalance
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Contra qué se compara el saldo al corte — ver BalanceComparar. Por defecto el cierre
        /// del año anterior (31-dic), que es el comparativo estándar de un balance (NIC1/NIIF Pymes).</summary>
        public string Comparar { get; set; } = BalanceComparar.CierreAnterior;

        /// <summary>Segundo escenario a comparar contra el principal (columna extra). null = no comparar.</summary>
        public byte? IdEscenarioComparar { get; set; }

        /// <summary>"Mes" | "Trimestre" | "Anio" — agrupación de las gráficas de tendencia.</summary>
        public string Agrupar { get; set; } = "Mes";
    }

    /// <summary>Códigos de "Comparar contra" (la vista los traduce; el servicio no lee resx).</summary>
    public static class BalanceComparar
    {
        public const string CierreAnterior = "cierreAnterior";
        public const string MesAnterior = "mesAnterior";
        public const string MismoMesAnioAnterior = "mismoMesAnioAnterior";

        public static string Normalizar(string valor)
        {
            if (string.Equals(valor, MesAnterior, System.StringComparison.OrdinalIgnoreCase)) return MesAnterior;
            if (string.Equals(valor, MismoMesAnioAnterior, System.StringComparison.OrdinalIgnoreCase)) return MismoMesAnioAnterior;
            return CierreAnterior;
        }
    }

    /// <summary>Un mes con datos en dbo.ModeloBalance, para poblar el slider de rango.</summary>
    public class BalanceMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    /// <summary>Una fila cruda de dbo.ModeloBalance (resultado materializado de sp_ModeloBalance), ya con
    /// el flag de subtotal resuelto vía JOIN a dbo.REL_Balance. A diferencia de PygFilaModelo no tiene
    /// columnas de presupuesto/forecast: ModeloBalance solo guarda el saldo real por Año/Mes/Cuenta.</summary>
    public class BalanceFilaModelo
    {
        public byte IdEscenario { get; set; }
        public int Año { get; set; }
        public int Mes { get; set; }                 // 1-12
        public int Ord { get; set; }                 // = REL_Balance.Id, orden de despliegue
        public string CuentaPUC { get; set; }
        public string Descripcion { get; set; }
        public decimal Valor { get; set; }            // saldo del mes (foto, no flujo)
        public bool EsSubtotal { get; set; }          // REL_Balance.Tipo = "CALCULO"
    }

    /// <summary>Una fila de la tabla de Balance lista para la vista: saldo al mes de corte + análisis
    /// vertical (% del activo total) + comparativo elegido en los filtros + variación $/%.</summary>
    public class BalanceFilaReporte
    {
        public int Orden { get; set; }
        public string Descripcion { get; set; }
        public bool EsSubtotal { get; set; }
        /// <summary>true = línea de pasivo (CuentaPUC empieza en "2", o para subtotales sin cuenta propia,
        /// la descripción contiene "pasivo" y no "patrimonio") — controla el color rojo/verde de la
        /// variación: en pasivo, un saldo MAYOR al comparativo es más deuda (desfavorable); en activo o
        /// patrimonio, mayor saldo es favorable. Heurística simple y documentada, no NIC1 estricta.</summary>
        public bool EsPasivo { get; set; }

        public decimal SaldoCorte { get; set; }
        /// <summary>Valor / ActivoTotal del corte — convención de "common-size balance sheet": todas las
        /// líneas (incluidas pasivo/patrimonio) se expresan como % del activo total.</summary>
        public decimal? PctVertical { get; set; }

        public decimal SaldoComparativo { get; set; }
        public decimal VariacionAbsoluta => SaldoCorte - SaldoComparativo;
        public decimal? VariacionPorcentual { get; set; }

        /// <summary>Real del escenario de comparación (FiltrosBalance.IdEscenarioComparar) y la variación
        /// del escenario principal contra él — null cuando no se pidió comparar escenarios.</summary>
        public decimal? SaldoEscenario { get; set; }
        public decimal? VariacionEscenarioPorcentual { get; set; }
    }

    /// <summary>Un punto de la serie de tendencia (Capital de trabajo/Razón corriente/Endeudamiento),
    /// un valor de saldo por mes (foto de cada mes, nunca sumado).</summary>
    public class BalancePuntoTendencia
    {
        public int Año { get; set; }
        public int Mes { get; set; }                  // 1-12 (agrupado por mes), 1-4 (trimestre) o 0 (año)
        public string Etiqueta { get; set; }
        public decimal Valor { get; set; }
        /// <summary>Mismo indicador del escenario de comparación — null si no se pidió comparar escenarios.</summary>
        public decimal? ValorEscenario { get; set; }
        /// <summary>true si el punto (mes/trimestre/año) toca el rango consultado — la vista lo sombrea.</summary>
        public bool EnRango { get; set; }
    }

    /// <summary>KPI (Capital de trabajo/Razón corriente/Endeudamiento/Activo total) para las tarjetas
    /// superiores. A diferencia de PygKpiMargen no hay bloque Periodo/Acumulado — el balance es una foto.</summary>
    public class BalanceKpi
    {
        public string Codigo { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorComparativo { get; set; }
        /// <summary>null cuando el KPI no aplica (denominador en cero, p. ej. Pasivo corriente = 0).</summary>
        public bool NoAplica { get; set; }
        /// <summary>Variación relativa (Valor vs ValorComparativo) — null si NoAplica o sin base.</summary>
        public decimal? VariacionPorcentual { get; set; }
    }

    /// <summary>Resultado completo consumido por el controlador/vista del Informe de Balance General.</summary>
    public class BalanceReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }

        /// <summary>Rango elegido en el slider (ambos inclusive), puede cruzar años — alimenta solo las
        /// tendencias. El "corte" real para KPIs/tabla/cuadre es siempre AnioHasta/MesHasta.</summary>
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Código BalanceComparar efectivamente usado para el comparativo/variación.</summary>
        public string Comparar { get; set; }
        public string Agrupar { get; set; }
        public bool HayEscenarioComparar { get; set; }
        public byte? IdEscenarioComparar { get; set; }

        public List<BalanceFilaReporte> Filas { get; set; } = new List<BalanceFilaReporte>();
        public List<BalanceKpi> Kpis { get; set; } = new List<BalanceKpi>();

        /// <summary>Estructura del corte para las 2 barras apiladas (Activo | Pasivo+Patrimonio).</summary>
        public decimal ActivoCorriente { get; set; }
        public decimal ActivoNoCorriente { get; set; }
        public decimal PasivoCorriente { get; set; }
        public decimal PasivoNoCorriente { get; set; }
        public decimal Patrimonio { get; set; }

        public List<BalancePuntoTendencia> TendenciaCapitalTrabajo { get; set; } = new List<BalancePuntoTendencia>();
        public List<BalancePuntoTendencia> TendenciaRazonCorriente { get; set; } = new List<BalancePuntoTendencia>();
        public List<BalancePuntoTendencia> TendenciaEndeudamiento { get; set; } = new List<BalancePuntoTendencia>();

        /// <summary>Chequeo de cuadre: Activo Total vs Pasivo Total + Patrimonio, al mes de corte.</summary>
        public bool CuadraBalance { get; set; }
        public decimal DiferenciaCuadre { get; set; }

        /// <summary>Cruce opcional con dbo.ModeloPYG: utilidad neta acumulada del PYG a la fecha de corte
        /// vs. la línea de "Resultado del Ejercicio" del balance. Solo se calcula si se reconoce un
        /// literal de línea del balance para el resultado del ejercicio — ver InformeBalanceService.</summary>
        public bool HayComparacionPYG { get; set; }
        public bool CuadraConPYG { get; set; }
        public decimal DiferenciaConPYG { get; set; }

        /// <summary>true si el período elegido en "Comparar contra" (cierre anterior/mes anterior/mismo
        /// mes año anterior) tiene datos cargados — si no, KPIs/tabla muestran el saldo al corte sin
        /// variación ("—"), nunca una comparación falsa contra cero.</summary>
        public bool HayComparativo { get; set; }

        public bool SinDatos { get; set; }
        public string Mensaje { get; set; }
    }
}
