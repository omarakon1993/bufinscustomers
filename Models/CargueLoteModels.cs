using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Estados de <see cref="CargueLote"/>. El cargue vive en staging (Ini_* real intacto)
    /// desde "EnValidacion" hasta "Confirmado"/"Descartado".
    /// </summary>
    public static class CargueLoteEstado
    {
        public const string EnValidacion = "EnValidacion";
        public const string ValidadoOk = "ValidadoOk";
        public const string ConAdvertencias = "ConAdvertencias";
        public const string ConErrores = "ConErrores";
        public const string Confirmado = "Confirmado";
        public const string Descartado = "Descartado";
    }

    public static class CargueLoteSeveridad
    {
        public const string Error = "Error";
        public const string Advertencia = "Advertencia";
    }

    /// <summary>Cabecera de un lote de cargue en revisión (tabla dbo.CarguesLotes).</summary>
    [Serializable]
    public class CargueLote
    {
        public long IdLote { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public int Anio { get; set; }
        public byte Modo { get; set; }
        public int IdEscenario { get; set; }
        public int IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public string NombreArchivo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaValidacion { get; set; }
        public DateTime? FechaConfirmacion { get; set; }
        public string Estado { get; set; }
        public int TotalFilas { get; set; }
        public int TotalErrores { get; set; }
        public int TotalAdvertencias { get; set; }
        public int? IdHistorialGenerado { get; set; }

        public bool PuedeConfirmar =>
            Estado == CargueLoteEstado.ValidadoOk || Estado == CargueLoteEstado.ConAdvertencias;
    }

    /// <summary>Un hallazgo de validación (tabla dbo.CarguesLotesErrores).</summary>
    [Serializable]
    public class CargueLoteHallazgo
    {
        public long Id { get; set; }
        public long IdLote { get; set; }
        public string NombreHoja { get; set; }
        public string NombreTabla { get; set; }
        public int? NumeroFilaExcel { get; set; }
        public string Columna { get; set; }
        public string Severidad { get; set; }
        public string CodigoRegla { get; set; }
        public string Mensaje { get; set; }
        public string MensajeEn { get; set; }
    }

    /// <summary>ViewModel de la pantalla "Revisar cargue" (informe de validación antes de confirmar).</summary>
    [Serializable]
    public class RevisarCargueViewModel
    {
        public CargueLote Lote { get; set; }
        public List<CargueLoteHallazgo> Hallazgos { get; set; } = new List<CargueLoteHallazgo>();
        public string NombreArchivo { get; set; }
    }
}
