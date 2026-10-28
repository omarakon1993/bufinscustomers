using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros del informe de rentabilidad por línea de negocio (dbo.ModeloLineasNegocio).</summary>
    public class FiltrosLineasNegocio
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }
        // Tema de colores (ver TemaInforme): el servidor lo re-resuelve para Excel y correo.
        public string Tema { get; set; }
        public string TemaP { get; set; }
        public string TemaS { get; set; }
        public string TemaA { get; set; }
    }

    public class LnMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    /// <summary>Fila de dbo.ModeloLineasNegocio ya interpretada: la línea sale del sufijo de Descripcion.</summary>
    public class LnFilaModelo
    {
        public int Año { get; set; }
        public int Mes { get; set; }
        public string CuentaPUC { get; set; }
        public string Linea { get; set; }
        public decimal Valor { get; set; }
    }

    /// <summary>Cifras de una línea (o del total) para el rango consultado. Los gastos y costos son magnitudes positivas.</summary>
    public class LnLineaResumen
    {
        public string Nombre { get; set; }
        public decimal Ingresos { get; set; }
        public decimal? PctIngresos { get; set; }
        public decimal CostoVentas { get; set; }
        public decimal UtilidadBruta { get; set; }
        public decimal GastosOperacionales { get; set; }
        public decimal UtilidadOperacional { get; set; }
        public decimal Ebitda { get; set; }
        public decimal ResultadoNoOperacional { get; set; }
        public decimal UtilidadAntesImpuestos { get; set; }
        public decimal Impuesto { get; set; }
        public decimal UtilidadNeta { get; set; }
        public decimal? MargenBruto { get; set; }
        public decimal? MargenOperacional { get; set; }
        public decimal? MargenEbitda { get; set; }
        public decimal? MargenNeto { get; set; }
        /// <summary>Participación de la línea en la utilidad operacional total (null si el total es 0).</summary>
        public decimal? PctUtilidadOperacional { get; set; }
        // Mismo rango del año anterior (null = sin datos del año anterior).
        public decimal? IngresosAnterior { get; set; }
        public decimal? VariacionIngresos { get; set; }
        public decimal? UtilidadOperacionalAnterior { get; set; }
        public decimal? VariacionUtilidadOperacional { get; set; }
    }

    /// <summary>Una fila del estado de resultados por línea: un valor por cada línea (mismo orden que Lineas) y el total.</summary>
    public class LnFilaEstado
    {
        /// <summary>Código fijo que la vista traduce (Ingresos, CostoVentas, UtilidadBruta, ...).</summary>
        public string Codigo { get; set; }
        public bool EsSubtotal { get; set; }
        public List<decimal> Valores { get; set; } = new List<decimal>();
        public decimal Total { get; set; }
    }

    public class LnSerieMensual
    {
        public string Linea { get; set; }
        public List<decimal> Ingresos { get; set; } = new List<decimal>();
    }

    public class LineasNegocioReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }
        public bool SinDatos { get; set; }

        /// <summary>Nombres de las líneas con movimiento en el rango, de mayor a menor ingreso.</summary>
        public List<string> Lineas { get; set; } = new List<string>();
        public List<LnLineaResumen> Resumen { get; set; } = new List<LnLineaResumen>();
        public LnLineaResumen Total { get; set; }
        public List<LnFilaEstado> Filas { get; set; } = new List<LnFilaEstado>();
        public List<string> Etiquetas { get; set; } = new List<string>();
        public List<LnSerieMensual> Series { get; set; } = new List<LnSerieMensual>();

        public bool HayAnioAnterior { get; set; }

        // Cuadre contra dbo.ModeloPYG (mismo rango): solo se muestra si el PYG tiene datos.
        public bool HayComparacionPYG { get; set; }
        public decimal DiferenciaIngresosPYG { get; set; }
        public bool CuadraIngresosPYG { get; set; }
        public decimal DiferenciaUtilidadPYG { get; set; }
        public bool CuadraUtilidadPYG { get; set; }
    }
}
