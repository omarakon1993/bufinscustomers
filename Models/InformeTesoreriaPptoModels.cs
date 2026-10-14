using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros del Informe de tesorería del presupuesto (consulta, insights IA, Excel y correo usan los
    /// mismos). El rango es continuo y puede cruzar años (mismo slider que PYG/Balance), aunque
    /// dbo.ModeloTesoreriaPpto solo guarda el año de ejecución de cada empresa.</summary>
    public class FiltrosTesoreria
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Qué se muestra como cifra principal — ver TesoreriaMedida.</summary>
        public string Medida { get; set; } = TesoreriaMedida.Presupuesto;

        /// <summary>"medida" = comparar contra la otra medida (presupuesto ↔ proyección); "ninguno" = sin comparar.</summary>
        public string Comparar { get; set; } = TesoreriaComparar.Medida;

        /// <summary>Segundo escenario a comparar contra el principal (misma medida). null = no comparar.</summary>
        public byte? IdEscenarioComparar { get; set; }
    }

    /// <summary>Medidas de dbo.ModeloTesoreriaPpto: presupuesto (Valor) o proyección (ValorFuturo: lo ejecutado más
    /// lo presupuestado restante, según el modelo).</summary>
    public static class TesoreriaMedida
    {
        public const string Presupuesto = "ppto";
        public const string Proyeccion = "proy";

        public static string Normalizar(string valor)
        {
            return string.Equals(valor, Proyeccion, System.StringComparison.OrdinalIgnoreCase) ? Proyeccion : Presupuesto;
        }

        public static string Otra(string medida) { return medida == Proyeccion ? Presupuesto : Proyeccion; }
    }

    public static class TesoreriaComparar
    {
        public const string Medida = "medida";
        public const string Ninguno = "ninguno";

        public static string Normalizar(string valor)
        {
            return string.Equals(valor, Ninguno, System.StringComparison.OrdinalIgnoreCase) ? Ninguno : Medida;
        }
    }

    /// <summary>Un mes con datos en dbo.ModeloTesoreriaPpto, para poblar el slider de rango.</summary>
    public class TesoreriaMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    /// <summary>Una fila cruda de dbo.ModeloTesoreriaPpto (resultado materializado de sp_ModeloTesoreriaPpto).
    /// Valor = presupuesto del mes; ValorFuturo = proyección del mes. Mes es entero 1-12.</summary>
    public class TesoreriaFilaModelo
    {
        public int Año { get; set; }
        public int Mes { get; set; }
        public int Ord { get; set; }
        public string Descripcion { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorFuturo { get; set; }
    }

    public class TesoreriaMesRef
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
    }

    /// <summary>Un punto mensual para el gráfico principal y el mapa de calor (todos los meses del año de
    /// ejecución; EnRango marca los del rango consultado). Los acumulados reinician cada año.</summary>
    public class TesoreriaPuntoMes
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool EnRango { get; set; }
        public decimal CajaNeta { get; set; }
        public decimal CajaNetaAcum { get; set; }
        public decimal CajaNetaComp { get; set; }        // otra medida (presupuesto ↔ proyección)
        public decimal CajaNetaAcumComp { get; set; }
        public decimal CajaNetaEsc { get; set; }         // escenario de comparación (misma medida)
        public decimal CajaNetaAcumEsc { get; set; }
    }

    /// <summary>Una línea de la tesorería con sus valores mensuales del rango (Valores/ValoresComp/ValoresEsc
    /// van alineados con TesoreriaReporteViewModel.MesesRango).</summary>
    public class TesoreriaFilaReporte
    {
        public int Ord { get; set; }
        public string Descripcion { get; set; }
        public bool EsSubtotal { get; set; }

        /// <summary>Línea que ya es un saldo corrido (caja neta acumulada, excedentes, deuda nueva): su "Total" es el
        /// valor del último mes del rango, no la suma.</summary>
        public bool EsSaldo { get; set; }

        /// <summary>true si subir esta línea es desfavorable (costos, gastos, impuestos, deuda nueva).</summary>
        public bool EsGastoOCosto { get; set; }

        public List<decimal> Valores { get; set; } = new List<decimal>();
        public List<decimal> ValoresComp { get; set; } = new List<decimal>();
        public List<decimal> ValoresEsc { get; set; } = new List<decimal>();

        public decimal Total { get; set; }
        public decimal TotalComp { get; set; }
        public decimal TotalEsc { get; set; }
        public decimal VariacionAbsoluta { get; set; }        // Total − TotalComp
        public decimal? VariacionPorcentual { get; set; }     // null si la base es 0
        public decimal? VariacionEscenarioPorcentual { get; set; }
    }

    /// <summary>KPI de caja. Codigo fijo (la vista traduce): CajaNetaPeriodo, CajaAcumFinal, SaldoMinimo,
    /// MesesDeficit, DeudaNueva.</summary>
    public class TesoreriaKpi
    {
        public string Codigo { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorComp { get; set; }
        public decimal ValorEsc { get; set; }
        public decimal? VariacionPorcentual { get; set; }
        public int Anio { get; set; }          // SaldoMinimo: mes/año en que ocurre
        public int Mes { get; set; }
        public int Total { get; set; }         // MesesDeficit: de cuántos meses del rango
    }

    /// <summary>Barra del puente de caja. Etiqueta es un código fijo (la vista traduce). Tipo: "total" | "positivo" |
    /// "negativo". En "negativo", Desde es el nivel final (más bajo) y Hasta el inicial; en "positivo", al revés.</summary>
    public class TesoreriaCascadaBarra
    {
        public string Etiqueta { get; set; }
        public string Tipo { get; set; }
        public decimal Desde { get; set; }
        public decimal Hasta { get; set; }
        public decimal Delta { get; set; }
    }

    public class TesoreriaReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }
        public string Medida { get; set; }
        public string Comparar { get; set; }
        public byte? IdEscenarioComparar { get; set; }

        public bool SinDatos { get; set; }
        public bool HayProyeccion { get; set; }        // la proyección tiene datos distintos de cero
        public bool HayComparativo { get; set; }       // se compara contra la otra medida
        public bool HayEscenarioComparar { get; set; }

        public List<TesoreriaMesRef> MesesRango { get; set; } = new List<TesoreriaMesRef>();
        public List<TesoreriaPuntoMes> Meses { get; set; } = new List<TesoreriaPuntoMes>();
        public List<TesoreriaFilaReporte> Filas { get; set; } = new List<TesoreriaFilaReporte>();
        public List<TesoreriaKpi> Kpis { get; set; } = new List<TesoreriaKpi>();
        public List<TesoreriaCascadaBarra> Cascada { get; set; } = new List<TesoreriaCascadaBarra>();
    }
}
