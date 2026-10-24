using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Qué modelo lee el informe: el flujo de caja gerencial (dbo.ModeloFlujoCaja) o el estado de flujos de
    /// efectivo por el método indirecto (dbo.ModeloFlujoEfectivo). El tipo lo fija el controlador, nunca el cliente.</summary>
    public enum FlujoTipo { Caja, Efectivo }

    /// <summary>Filtros de los informes de Flujo de caja / Flujo de efectivo (consulta, insights IA, Excel y correo usan
    /// los mismos). El rango es continuo y puede cruzar años (mismo slider que PYG/Balance).</summary>
    public class FiltrosFlujo
    {
        // Paleta del informe (ver TemaInforme): el servidor re-resuelve y valida; vacío = Bufins.
        public string Tema { get; set; }
        public string TemaP { get; set; }
        public string TemaS { get; set; }
        public string TemaA { get; set; }

        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Cifras principales — ver FlujoMedida.</summary>
        public string Medida { get; set; } = FlujoMedida.Real;

        /// <summary>Segundo escenario a comparar contra el principal (misma medida). null = no comparar.</summary>
        public byte? IdEscenarioComparar { get; set; }
    }

    /// <summary>Medidas: "real" = Valor del modelo (solo meses ejecutados); "proy" = cierre proyectado (real hasta el
    /// último mes ejecutado del año y, de ahí en adelante, la proyección ValorFuturo del modelo).</summary>
    public static class FlujoMedida
    {
        public const string Real = "real";
        public const string Proyectado = "proy";

        public static string Normalizar(string valor)
        {
            return string.Equals(valor, Proyectado, System.StringComparison.OrdinalIgnoreCase) ? Proyectado : Real;
        }
    }

    /// <summary>Un mes con datos del modelo, para poblar el slider de rango.</summary>
    public class FlujoMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    /// <summary>Una fila cruda del modelo (Valor = real, ValorFuturo = proyección, ValorPresupuesto = presupuesto; Mes
    /// es entero 1-12), con el flag de subtotal resuelto vía JOIN a la tabla Rel_ del modelo.</summary>
    public class FlujoFilaModelo
    {
        public int Año { get; set; }
        public int Mes { get; set; }
        public int Ord { get; set; }
        public string Descripcion { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorFuturo { get; set; }
        public decimal ValorPresupuesto { get; set; }
        public bool EsSubtotal { get; set; }
    }

    public class FlujoMesRef
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool Proyectado { get; set; }   // mes cuyas cifras son proyección (posterior al último mes ejecutado)
    }

    /// <summary>Un punto mensual del gráfico principal (flujo neto del mes y acumulado del rango; presupuesto, año anterior
    /// y escenario de comparación alineados con el mismo mes).</summary>
    public class FlujoPuntoMes
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool Proyectado { get; set; }
        public decimal Neto { get; set; }
        public decimal NetoAcum { get; set; }
        public decimal NetoPpto { get; set; }
        public decimal NetoPptoAcum { get; set; }
        public decimal NetoAnt { get; set; }
        public decimal NetoAntAcum { get; set; }
        public decimal NetoEsc { get; set; }
        public decimal NetoEscAcum { get; set; }
    }

    /// <summary>Una línea del modelo con sus valores mensuales del rango (Valores/ValoresPpto/ValoresAnt/ValoresEsc van
    /// alineados con FlujoReporteViewModel.MesesRango).</summary>
    public class FlujoFilaReporte
    {
        public int Ord { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Código fijo de una línea que el informe agrega (no existe en el modelo): "FlujoNeto". La vista la traduce.</summary>
        public string Codigo { get; set; }
        public bool EsSubtotal { get; set; }
        public bool EsTotal { get; set; }

        /// <summary>true si subir esta línea es desfavorable para la caja (impuestos, gastos financieros…).</summary>
        public bool EsGastoOCosto { get; set; }

        public List<decimal> Valores { get; set; } = new List<decimal>();
        public List<decimal> ValoresPpto { get; set; } = new List<decimal>();
        public List<decimal> ValoresAnt { get; set; } = new List<decimal>();
        public List<decimal> ValoresEsc { get; set; } = new List<decimal>();

        public decimal Total { get; set; }
        public decimal TotalPpto { get; set; }
        public decimal TotalAnt { get; set; }
        public decimal TotalEsc { get; set; }
        public decimal VariacionAbsoluta { get; set; }          // Total − TotalPpto
        public decimal? VariacionPorcentual { get; set; }       // contra presupuesto; null si la base es 0
        public decimal? VariacionAnioPorcentual { get; set; }   // contra el mismo rango del año anterior
        public decimal? VariacionEscenarioPorcentual { get; set; }
    }

    /// <summary>KPI del informe. Codigo fijo (la vista traduce). Formato: "moneda" | "pct". Neutral = ni subir ni bajar es
    /// bueno o malo por sí mismo (p. ej. la inversión o el cambio en deuda).</summary>
    public class FlujoKpi
    {
        public string Codigo { get; set; }
        public string Formato { get; set; }
        public bool Neutral { get; set; }
        public bool NoAplica { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorPpto { get; set; }
        public decimal ValorAnt { get; set; }
        public decimal ValorEsc { get; set; }
        public decimal? VariacionPptoPorcentual { get; set; }
        public decimal? VariacionAnioPorcentual { get; set; }
        public decimal? VariacionEscenarioPorcentual { get; set; }
    }

    /// <summary>Barra del puente de caja. Etiqueta es un código fijo (la vista traduce). Tipo: "total" | "positivo" |
    /// "negativo"; en "negativo" Desde es el nivel final (más bajo) y Hasta el inicial; en "positivo", al revés.</summary>
    public class FlujoCascadaBarra
    {
        public string Etiqueta { get; set; }
        public string Tipo { get; set; }
        public decimal Desde { get; set; }
        public decimal Hasta { get; set; }
        public decimal Delta { get; set; }
    }

    public class FlujoReporteViewModel
    {
        public string Tipo { get; set; }          // "Caja" | "Efectivo"
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }
        public string Medida { get; set; }
        public byte? IdEscenarioComparar { get; set; }

        public bool SinDatos { get; set; }
        public bool HayPresupuesto { get; set; }        // el presupuesto tiene cifras en el rango
        public bool HayAnioAnterior { get; set; }       // el mismo rango del año anterior tiene cifras
        public bool HayEscenarioComparar { get; set; }
        public bool HayMesesProyectados { get; set; }   // el rango incluye meses proyectados (solo con medida "proy")
        public int UltimoMesReal { get; set; }          // último mes ejecutado del año final del rango (0 = ninguno)

        public List<FlujoMesRef> MesesRango { get; set; } = new List<FlujoMesRef>();
        public List<FlujoPuntoMes> Meses { get; set; } = new List<FlujoPuntoMes>();
        public List<FlujoFilaReporte> Filas { get; set; } = new List<FlujoFilaReporte>();
        public List<FlujoKpi> Kpis { get; set; } = new List<FlujoKpi>();
        public List<FlujoCascadaBarra> Cascada { get; set; } = new List<FlujoCascadaBarra>();
    }
}
