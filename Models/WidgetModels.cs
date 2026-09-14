using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class WidgetTarjeta
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string NombreEn { get; set; }     // opcional: versión en inglés de Nombre
        public byte Tipo { get; set; }           // 1 = KPI/Valor, 2 = Gráfico, 3 = Advertencia
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
        // Tipo 3: severidad fija del widget completo — "success" / "warning" / "error"
        public string Severidad { get; set; }
        // Tipo 3: subtipo del widget — "plantilla" / "modelo" (uso futuro)
        public string Subtipo { get; set; }
        // Fuente de datos: 1 = Consulta SQL (ConsultaSQL), 2 = Procedimiento almacenado (NombreSP)
        public byte TipoFuente { get; set; }
        public string NombreSP { get; set; }
    }

    public class WidgetKpiResultado
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Valor { get; set; }
        public string Etiqueta { get; set; }
        public string EtiquetaEn { get; set; } // opcional: versión en inglés de Etiqueta
    }

    public class WidgetGraficoResultado
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Etiqueta { get; set; }
        public string EtiquetaEn { get; set; } // opcional: versión en inglés de Etiqueta
        public string Serie { get; set; }
        public string SerieEn { get; set; }    // opcional: versión en inglés de Serie
        public decimal Valor { get; set; }
    }

    // Severidad ("success"/"warning"/"error") se copia del widget (WidgetTarjeta.Severidad), no viene del SQL/SP
    public class WidgetAdvertenciaResultado
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Severidad { get; set; }
        public string Titulo { get; set; }
        public string TituloEn { get; set; }   // opcional: versión en inglés de Titulo
        public string Mensaje { get; set; }
        public string MensajeEn { get; set; }  // opcional: versión en inglés de Mensaje
    }

    public class WidgetTarjetaViewModel
    {
        public WidgetTarjeta Config { get; set; }
        public List<WidgetKpiResultado> KpiResultados { get; set; } = new List<WidgetKpiResultado>();
        public List<WidgetGraficoResultado> GraficoResultados { get; set; } = new List<WidgetGraficoResultado>();
        public List<WidgetAdvertenciaResultado> AdvertenciaResultados { get; set; } = new List<WidgetAdvertenciaResultado>();
    }
}
