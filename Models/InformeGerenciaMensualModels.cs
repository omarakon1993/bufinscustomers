using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros del Informe mensual de gerencia: un solo mes de cierre (Anio/Mes).</summary>
    public class FiltrosGerenciaMensual
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Tema { get; set; }
        public string TemaP { get; set; }
        public string TemaS { get; set; }
        public string TemaA { get; set; }
    }

    public class GmMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    /// <summary>Cifra de resultados del mes (PYG): Real del mes vs presupuesto y año anterior, y acumulado del año.</summary>
    public class GmCifraResultado
    {
        /// <summary>Código fijo: Ingresos | Ebitda | UtilidadNeta (la vista traduce).</summary>
        public string Codigo { get; set; }
        public decimal Real { get; set; }
        public decimal Presupuesto { get; set; }
        public decimal? VarPresupuesto { get; set; }
        public decimal? VarAnioAnterior { get; set; }
        public decimal AcumReal { get; set; }
        public decimal AcumPresupuesto { get; set; }
        public decimal? AcumVarPresupuesto { get; set; }
    }

    public class GmKpiBalance
    {
        /// <summary>ActivoTotal | CapitalTrabajo | RazonCorriente | Endeudamiento</summary>
        public string Codigo { get; set; }
        public decimal Valor { get; set; }
        public bool NoAplica { get; set; }
        public decimal? Variacion { get; set; }
    }

    public class GmIndicadorAtencion
    {
        public string Codigo { get; set; }
        public string Formato { get; set; }
        public decimal? Valor { get; set; }
        public string Nivel { get; set; }
    }

    public class GerenciaMensualReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool SinDatos { get; set; }

        // Resultados del mes
        public bool HayResultados { get; set; }
        public List<GmCifraResultado> Resultados { get; set; } = new List<GmCifraResultado>();
        public bool HayAnioAnterior { get; set; }

        // Balance al cierre
        public bool HayBalance { get; set; }
        public bool CuadraBalance { get; set; }
        public List<GmKpiBalance> Balance { get; set; } = new List<GmKpiBalance>();

        // Caja del mes
        public bool HayCaja { get; set; }
        public bool HayCajaInicial { get; set; }
        public decimal CajaInicial { get; set; }
        public decimal FlujoNeto { get; set; }
        public decimal FlujoNetoPresupuesto { get; set; }
        public decimal FclOperativo { get; set; }
        public bool HayCajaFinal { get; set; }
        public decimal CajaFinal { get; set; }
        public bool CuadraCaja { get; set; }

        // Indicadores con semáforo
        public bool HayIndicadores { get; set; }
        public int TotalVerde { get; set; }
        public int TotalAmarillo { get; set; }
        public int TotalNaranja { get; set; }
        public int TotalRojo { get; set; }
        public List<GmIndicadorAtencion> Atencion { get; set; } = new List<GmIndicadorAtencion>();

        // Líneas de negocio (mes)
        public bool HayLineas { get; set; }
        public List<LnLineaResumen> Lineas { get; set; } = new List<LnLineaResumen>();
    }
}
