using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class WorkflowTarjeta
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public byte Tipo { get; set; }           // 1 = KPI/Valor, 2 = Info
        public string Icono { get; set; }
        public string ColorIcono { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
        // Tipo 1
        public string ConsultaSQL { get; set; }
        public string UnidadValor { get; set; }
        // Tipo 2
        public string InfoTitulo { get; set; }
        public string InfoSubtitulo { get; set; }
        public string InfoCuerpo { get; set; }
        public string InfoUrlAccion { get; set; }
        public string InfoTextoAccion { get; set; }
    }

    public class WorkflowKpiResultado
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Valor { get; set; }
        public string Etiqueta { get; set; }
    }

    public class WorkflowTarjetaViewModel
    {
        public WorkflowTarjeta Config { get; set; }
        public List<WorkflowKpiResultado> KpiResultados { get; set; } = new List<WorkflowKpiResultado>();
    }
}
