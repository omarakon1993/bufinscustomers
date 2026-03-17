using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Registro de la tabla OrdenVariables
    /// </summary>
    public class OrdenVariable
    {
        public int Id { get; set; }
        public string NombreTabla { get; set; }
        public string Variable { get; set; }
        public int OrdenVariable_ { get; set; }
        public bool SubtotalVariable { get; set; }
        public string VariablePadre { get; set; }
        public string VariableIndicador { get; set; }
        public string ClaseVariable { get; set; }
        public string AgrupacionKEY { get; set; }
        public string AgrupacionKEYABR { get; set; }
        public int? AgrupacionKEYOrden { get; set; }
        public string VariablePadreReal { get; set; }
        public string VariablePadreAbr { get; set; }
        public int? VariablePadreRealOrden { get; set; }
        public string UsuarioCargo { get; set; }
        public DateTime? FechaCarga { get; set; }
    }

    /// <summary>
    /// Filtros para consulta de Variables PBI
    /// </summary>
    public class FiltrosVariablesPBI
    {
        public string NombreTabla { get; set; }
        public string Variable { get; set; }
        public bool? SoloSubtotales { get; set; }
    }

    /// <summary>
    /// Resultado de la consulta de Variables PBI
    /// </summary>
    public class ResultadoVariablesPBI
    {
        public List<OrdenVariable> Filas { get; set; } = new List<OrdenVariable>();
        public int TotalRegistros { get; set; }
        public string UsuarioCargo { get; set; }
        public DateTime? FechaCarga { get; set; }
    }

    /// <summary>
    /// Estado actual de la tabla OrdenVariables en BD
    /// </summary>
    public class EstadoVariablesPBI
    {
        public int TotalRegistros { get; set; }
        public string UsuarioCargo { get; set; }
        public DateTime? FechaCarga { get; set; }
        public int TotalTablas { get; set; }
    }

    /// <summary>
    /// Resultado completo de la carga del Excel de Variables PBI
    /// </summary>
    public class ResultadoCargaVariablesPBI
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; }
        public int FilasInsertadas { get; set; }
        public int FilasEliminadasPrevias { get; set; }
        public string NombreArchivo { get; set; }
        public ComparacionVariablesPBI Comparacion { get; set; }
    }

    /// <summary>
    /// Comparacion entre datos existentes en BD y nuevos datos del Excel
    /// </summary>
    public class ComparacionVariablesPBI
    {
        public List<RegistroOrdenVariableResumen> Agregados { get; set; } = new List<RegistroOrdenVariableResumen>();
        public List<RegistroOrdenVariableResumen> Eliminados { get; set; } = new List<RegistroOrdenVariableResumen>();
        public List<ModificacionOrdenVariable> Modificados { get; set; } = new List<ModificacionOrdenVariable>();

        public int TotalAgregados => Agregados.Count;
        public int TotalEliminados => Eliminados.Count;
        public int TotalModificados => Modificados.Count;
        public bool TieneCambios => TotalAgregados > 0 || TotalEliminados > 0 || TotalModificados > 0;
    }

    /// <summary>
    /// Resumen de un registro para el diff (sin datos de auditoria)
    /// </summary>
    public class RegistroOrdenVariableResumen
    {
        public string NombreTabla { get; set; }
        public string Variable { get; set; }
        public int Orden { get; set; }
        public bool EsSubtotal { get; set; }
        public string VariablePadre { get; set; }
    }

    /// <summary>
    /// Descripcion de un registro modificado en el diff
    /// </summary>
    public class ModificacionOrdenVariable
    {
        public string NombreTabla { get; set; }
        public string Variable { get; set; }
        /// <summary>
        /// Resumen de campos modificados: "Orden: 1→2 | VariablePadre: A→B"
        /// </summary>
        public string CambioResumen { get; set; }
    }
}
