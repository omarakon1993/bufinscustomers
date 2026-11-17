using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros de los informes ejecutivos (PYG en una hoja, Caja disponible, Informe para comité): un mes de corte.</summary>
    public class FiltrosEjecutivo
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int Anio { get; set; }
        public int Mes { get; set; }

        // Tema de colores (ver TemaInforme): el servidor lo re-resuelve para Excel y correo.
        public string Tema { get; set; }
        public string TemaP { get; set; }
        public string TemaS { get; set; }
        public string TemaA { get; set; }
    }

    public class EjMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    // ===== PYG en una hoja =====

    public class PygHojaFila
    {
        public int Ord { get; set; }
        public string Descripcion { get; set; }
        public bool EsSubtotal { get; set; }
        public bool EsGastoOCosto { get; set; }
        /// <summary>12 posiciones (ene-dic); null = mes sin cifras reales (posterior al corte).</summary>
        public List<decimal?> Meses { get; set; } = new List<decimal?>();
        public decimal Acumulado { get; set; }
        /// <summary>Participación vertical: acumulado de la línea / acumulado de ingresos (null si no hay ingresos).</summary>
        public decimal? Av { get; set; }
        public decimal AcumPresupuesto { get; set; }
        public decimal? VarPresupuesto { get; set; }
        public decimal? AcumAnioAnterior { get; set; }
        public decimal? VarAnioAnterior { get; set; }
    }

    public class PygHojaReporte
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int Anio { get; set; }
        public int MesCorte { get; set; }
        public bool HayPresupuesto { get; set; }
        public bool HayAnioAnterior { get; set; }
        public bool SinDatos { get; set; }
        public List<PygHojaFila> Filas { get; set; } = new List<PygHojaFila>();
    }

    // ===== ¿Cuánta plata me queda? =====

    public class CdTramo
    {
        public string Codigo { get; set; }
        public string Tipo { get; set; }      // "fuente" | "uso"
        public decimal Valor { get; set; }
    }

    public class CdMes
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool Proyectado { get; set; }
        public decimal Flujo { get; set; }
        public decimal CajaFin { get; set; }
    }

    public class CajaDisponibleReporte
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int Anio { get; set; }
        public int MesCorte { get; set; }
        public bool SinDatos { get; set; }

        public bool HayCajaInicial { get; set; }
        public decimal CajaInicial { get; set; }          // cierre del año anterior (balance)
        public bool HayCajaCorte { get; set; }
        public decimal CajaCorte { get; set; }            // caja al mes de corte (balance si existe; si no, calculada)
        public bool CajaCorteDeBalance { get; set; }
        public bool Cuadra { get; set; }                  // la caja calculada coincide con la del balance

        public decimal TotalFuentes { get; set; }         // lo que entró en el año hasta el corte
        public decimal TotalUsos { get; set; }            // lo que salió (negativo)
        public decimal FlujoNetoAnio { get; set; }
        public List<CdTramo> Tramos { get; set; } = new List<CdTramo>();

        public bool HayProyeccion { get; set; }
        public decimal FlujoProyectado { get; set; }      // flujo neto de los meses posteriores al corte
        public decimal CajaProyectada { get; set; }       // a diciembre
        public bool HayMinimo { get; set; }
        public decimal CajaMinima { get; set; }
        public int MesMinimo { get; set; }
        public int MesesEnDeficit { get; set; }
        public List<CdMes> Meses { get; set; } = new List<CdMes>();
    }

    // ===== Informe para comité =====

    public class ComiteReporte
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int Anio { get; set; }
        public int Mes { get; set; }
        public bool SinDatos { get; set; }
        public GerenciaMensualReporteViewModel Gerencia { get; set; }
        public PygHojaReporte Pyg { get; set; }
        public CajaDisponibleReporte Caja { get; set; }
        public List<bufinscustomers.Services.ComentarioGestion> Comentarios { get; set; } = new List<bufinscustomers.Services.ComentarioGestion>();
    }
}
