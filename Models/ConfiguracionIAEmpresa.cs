using System;

namespace bufinscustomers.Models
{
    /// <summary>Override por empresa del presupuesto mensual de tokens de IA. Solo existe una fila
    /// cuando un Super Admin fija un valor explícito; sin fila, la empresa usa el valor global
    /// ConfiguracionSistema.IA_TokensMensualesPorEmpresa.</summary>
    public class ConfiguracionIAEmpresa
    {
        public int IdEmpresa { get; set; }
        public long PresupuestoTokensMensual { get; set; }
        public DateTime FechaActualizacion { get; set; }
        public int? IdUsuarioActualizacion { get; set; }
    }
}
