using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class WidgetTarjeta
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public byte Tipo { get; set; }           // 1 = KPI/Valor, 2 = Gráfico
        public string Icono { get; set; }
        public string ColorIcono { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public string ConsultaSQL { get; set; }
        // Tipo 1
        public string UnidadValor { get; set; }
        // Tipo 2
        public string TipoGrafico { get; set; }  // bar, line, area, doughnut, pie
        public bool FondoOscuro { get; set; }
    }

    public class WidgetKpiResultado
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Valor { get; set; }
        public string Etiqueta { get; set; }
    }

    public class WidgetGraficoResultado
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Etiqueta { get; set; }
        public string Serie { get; set; }
        public decimal Valor { get; set; }
    }

    public class WidgetTarjetaViewModel
    {
        public WidgetTarjeta Config { get; set; }
        public List<WidgetKpiResultado> KpiResultados { get; set; } = new List<WidgetKpiResultado>();
        public List<WidgetGraficoResultado> GraficoResultados { get; set; } = new List<WidgetGraficoResultado>();
    }
}
