using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros del Informe de indicadores (consulta, insights IA, Excel y correo usan los mismos). El extremo derecho
    /// del rango es el MES DE CORTE (los flujos se miden en los 12 meses que terminan ahí y los saldos son los de ese mes); el
    /// rango completo solo alimenta las tendencias.</summary>
    public class FiltrosIndicadores
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }

        /// <summary>Contra qué mes se compara el corte — ver IndicadoresComparar.</summary>
        public string Comparar { get; set; } = IndicadoresComparar.CierreAnterior;
    }

    public static class IndicadoresComparar
    {
        public const string CierreAnterior = "cierreAnterior";            // diciembre del año anterior (por defecto)
        public const string MesAnterior = "mesAnterior";
        public const string MismoMesAnioAnterior = "mismoMesAnioAnterior";

        public static string Normalizar(string valor)
        {
            if (string.Equals(valor, MesAnterior, System.StringComparison.OrdinalIgnoreCase)) return MesAnterior;
            if (string.Equals(valor, MismoMesAnioAnterior, System.StringComparison.OrdinalIgnoreCase)) return MismoMesAnioAnterior;
            return CierreAnterior;
        }
    }

    /// <summary>Niveles del semáforo. "gris" = sin dato / parcial / no aplica.</summary>
    public static class IndicadorNivel
    {
        public const string Verde = "verde";
        public const string Amarillo = "amarillo";
        public const string Naranja = "naranja";
        public const string Rojo = "rojo";
        public const string Gris = "gris";
    }

    /// <summary>Un mes con datos (PYG o Balance), para poblar el slider de rango.</summary>
    public class IndicadorMesDisponible
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Etiqueta { get; set; }
    }

    /// <summary>Un punto de la mini-tendencia de un indicador (valor del indicador evaluado en ese mes).</summary>
    public class IndicadorPunto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public decimal? Valor { get; set; }
        public string Nivel { get; set; }
    }

    /// <summary>Un indicador evaluado: valor al corte, valor en el mes de comparación, variación, semáforo y tendencia. Incluye
    /// los cortes del semáforo para que la ficha muestre los rangos sin duplicarlos en la vista.</summary>
    public class IndicadorValor
    {
        public string Codigo { get; set; }
        public string Familia { get; set; }

        /// <summary>"pct" (fracción: 0,25 = 25 %), "x" (veces), "dias" o "moneda".</summary>
        public string Formato { get; set; }

        /// <summary>true = más alto es mejor · false = más bajo es mejor · null = sin dirección (sin semáforo por rango).</summary>
        public bool? MasEsMejor { get; set; }

        /// <summary>Tres cortes del semáforo, del más exigente al menos exigente (verde / amarillo / naranja; por debajo = rojo).</summary>
        public decimal[] Cortes { get; set; }

        public decimal? Valor { get; set; }
        public decimal? ValorComp { get; set; }

        /// <summary>Valor − ValorComp en la unidad natural (en "pct" son puntos porcentuales como fracción).</summary>
        public decimal? Variacion { get; set; }

        /// <summary>true = el movimiento es favorable · false = desfavorable · null = sin cambio o sin dirección.</summary>
        public bool? Favorable { get; set; }

        public string Nivel { get; set; }
        public string NivelComp { get; set; }

        public bool NoAplica { get; set; }
        public bool Parcial { get; set; }

        /// <summary>Por qué no hay valor: sin_dato · ltm_parcial · divisor_cero · ebitda_no_positivo · sin_intereses · sin_comparativo.</summary>
        public string Motivo { get; set; }

        /// <summary>Meses con datos dentro de la ventana de 12 meses del corte (12 = completa).</summary>
        public int MesesLtm { get; set; }

        public List<IndicadorPunto> Tendencia { get; set; } = new List<IndicadorPunto>();
    }

    public class IndicadoresReporteViewModel
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; }
        public int AnioDesde { get; set; }
        public int MesDesde { get; set; }
        public int AnioHasta { get; set; }
        public int MesHasta { get; set; }
        public string Comparar { get; set; }
        public int AnioComp { get; set; }
        public int MesComp { get; set; }

        public bool SinDatos { get; set; }
        public bool HayComparativo { get; set; }
        public bool HayBalance { get; set; }
        public bool HayFlujoCaja { get; set; }

        /// <summary>Meses con datos del PYG dentro de los 12 que terminan en el corte (12 = indicadores de flujo completos).</summary>
        public int MesesLtm { get; set; }

        public int TotalVerde { get; set; }
        public int TotalAmarillo { get; set; }
        public int TotalNaranja { get; set; }
        public int TotalRojo { get; set; }
        public int TotalGris { get; set; }

        /// <summary>Códigos de los indicadores que más requieren atención (rojos y naranjas, los más graves primero).</summary>
        public List<string> Atencion { get; set; } = new List<string>();

        public List<IndicadorValor> Indicadores { get; set; } = new List<IndicadorValor>();
    }
}
