using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Una fila de dbo.Rel_BalancePpto (supuesto de una línea para un mes de una empresa).</summary>
    public class SupuestoBalancePpto
    {
        public int Id { get; set; }
        public string Descripcion { get; set; }
        public string DescripcionBase { get; set; }
        public int? MesBase { get; set; }
        public int Mes { get; set; }
        public string Formula { get; set; }
        public decimal? Valor { get; set; }
        public string TipoUnidad { get; set; }
        /// <summary>TipoUnidad = CALCULO: se muestra pero no se edita.</summary>
        public bool EsCalculo { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>Hallazgo de la validación de un Excel de supuestos (Error bloquea la carga; Advertencia no).</summary>
    public class HallazgoSupuesto
    {
        public int Fila { get; set; }
        public string Columna { get; set; }
        public string Severidad { get; set; }   // "Error" | "Advertencia"
        public string Codigo { get; set; }      // clave SBP_Hall_{Codigo} en los .resx
        public string Dato { get; set; }
        public string Mensaje { get; set; }     // lo llena el controlador (traducido)
    }

    public class ResultadoCargaSupuestos
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; }
        public int FilasLeidas { get; set; }
        public int FilasActualizadas { get; set; }
        public List<HallazgoSupuesto> Hallazgos { get; } = new List<HallazgoSupuesto>();
    }

    /// <summary>Completitud de los supuestos de una empresa (solo cuentan las filas que no son CALCULO).</summary>
    public class ResumenSupuestos
    {
        public bool Disponible { get; set; }
        public bool TieneFilas { get; set; }
        public int Total { get; set; }
        public int Diligenciados { get; set; }
        public int Pendientes { get; set; }
        public int Calculo { get; set; }
        public int LineasConPendientes { get; set; }
        public DateTime? UltimaCarga { get; set; }
        public DateTime? UltimaModificacion { get; set; }
        public string UltimoUsuario { get; set; }
        public List<LineaPendienteSupuesto> Lineas { get; } = new List<LineaPendienteSupuesto>();
        public int Porcentaje => Total == 0 ? 0 : (int)Math.Round(100.0 * Diligenciados / Total);
    }

    public class LineaPendienteSupuesto
    {
        public int Id { get; set; }
        public string Descripcion { get; set; }
        public int Pendientes { get; set; }
        public int Total { get; set; }
        public List<int> MesesPendientes { get; } = new List<int>();
    }
}
