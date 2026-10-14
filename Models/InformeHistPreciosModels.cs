using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Filtros del Informe histórico de precios (Churido &amp; Ucrania). Lee de dbo.Ini_HistPrecios_Churido
    /// (hoja de cargue personalizada Z_HistPrecios_Churido): una fila por empresa/tipo de fruta/año/mes.
    /// </summary>
    public class FiltrosHistPrecios
    {
        public int IdEmpresa { get; set; }
        public byte IdEscenario { get; set; } = 1;
        /// <summary>Rango continuo de SEMESTRES (S1 = ene-jun, S2 = jul-dic): desde AnioDesde/SemDesde hasta AnioHasta/SemHasta, ambos inclusive.</summary>
        public int AnioDesde { get; set; }
        public int SemDesde { get; set; } = 1;
        public int AnioHasta { get; set; }
        public int SemHasta { get; set; } = 2;
        /// <summary>"Anio" (default) | "Semestre".</summary>
        public string Agrupar { get; set; } = "Anio";
        /// <summary>Tipos de fruta a incluir (null/vacío = todos).</summary>
        public List<string> Tipos { get; set; }
    }

    /// <summary>Un semestre con datos (eje del slider de rango).</summary>
    public class HpSemestre
    {
        public int Anio { get; set; }
        public int Sem { get; set; }
    }

    /// <summary>Semestres y tipos de fruta con datos para una empresa/escenario (alimenta los filtros).</summary>
    public class HpOpciones
    {
        public List<HpSemestre> Semestres { get; set; } = new List<HpSemestre>();
        public List<string> Tipos { get; set; } = new List<string>();
        public bool HayDatos { get; set; }
    }

    /// <summary>
    /// Un periodo del eje X. <c>Sub</c> es 0 para "Anio" y 1-2 para Semestre;
    /// la etiqueta la arma quien la muestra (vista / Excel) para respetar el idioma.
    /// </summary>
    public class HpPeriodo
    {
        public int Anio { get; set; }
        public int Sub { get; set; }
    }

    /// <summary>Serie de precios de un tipo de fruta (o el total general), alineada con <see cref="HpReporte.Periodos"/>.</summary>
    public class HpSerie
    {
        public string Tipo { get; set; }
        /// <summary>Precio promedio del periodo (null = sin dato).</summary>
        public decimal?[] Valores { get; set; }
        /// <summary>Variación vs. el mismo periodo del año anterior (fracción: 0,05 = 5 %); null si no hay comparable.</summary>
        public decimal?[] Variaciones { get; set; }
        /// <summary>Promedio de todos los registros del rango (columna "Total general" del Excel original).</summary>
        public decimal? Promedio { get; set; }
        public decimal? Minimo { get; set; }
        public decimal? Maximo { get; set; }
    }

    /// <summary>Tipo de fruta destacado en un KPI (nombre + valor).</summary>
    public class HpDestacado
    {
        public string Tipo { get; set; }
        public decimal Valor { get; set; }
    }

    public class HpReporte
    {
        public bool SinDatos { get; set; }
        public string Agrupar { get; set; }
        public int AnioDesde { get; set; }
        public int SemDesde { get; set; }
        public int AnioHasta { get; set; }
        public int SemHasta { get; set; }
        public List<HpPeriodo> Periodos { get; set; } = new List<HpPeriodo>();
        public List<HpSerie> Series { get; set; } = new List<HpSerie>();
        /// <summary>Promedio de todos los registros de los tipos seleccionados en cada periodo ("Total general").</summary>
        public HpSerie Total { get; set; }

        /// <summary>Índice (en Periodos) del último periodo con dato del total; -1 si no hay.</summary>
        public int IdxUltimo { get; set; } = -1;
        public HpDestacado MasCaro { get; set; }
        /// <summary>Tipo con mayor variación (alza) en el último periodo; Valor = fracción.</summary>
        public HpDestacado MayorAlza { get; set; }
        /// <summary>Tipo con menor variación (la caída más fuerte, o la menor alza) en el último periodo.</summary>
        public HpDestacado MenorVariacion { get; set; }

        /// <summary>Crecimiento anual compuesto del promedio general entre el primer y el último año con datos del rango.</summary>
        public decimal? Cagr { get; set; }
        public int CagrAnioInicial { get; set; }
        public int CagrAnioFinal { get; set; }
        /// <summary>Semestres con dato del último año del rango (&lt; 2 = año parcial, el promedio es provisional).</summary>
        public int SemestresUltimoAnio { get; set; }
        /// <summary>Hay al menos un periodo con comparable del año anterior (si no, las variaciones salen "—").</summary>
        public bool HayComparable { get; set; }
    }
}
