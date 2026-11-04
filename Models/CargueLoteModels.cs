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
        /// <summary>Título corto de la regla (lo escribe sp_ValidarCargueStaging); null si el hallazgo no tiene título.</summary>
        public string Titulo { get; set; }
        public string TituloEn { get; set; }

        // ── Datos estructurados por caso (Sql/043; null si el script no se ha ejecutado o la regla aún no los llena) ──
        /// <summary>Periodo del caso en formato 'AAAA-MM'.</summary>
        public string Periodo { get; set; }
        /// <summary>Cuenta / nivel afectado. '@N4' significa «Nivel 4 dígitos» (lo traduce la pantalla).</summary>
        public string Referencia { get; set; }
        public string Detalle { get; set; }
        public string DetalleEn { get; set; }
        public decimal? Diferencia { get; set; }
        /// <summary>Descripción de la regla (igual en todos los casos de la misma validación).</summary>
        public string Regla { get; set; }
        public string ReglaEn { get; set; }
        public string Sugerencia { get; set; }
        public string SugerenciaEn { get; set; }
    }

    /// <summary>ViewModel de la pantalla "Revisar cargue" (informe de validación antes de confirmar).</summary>
    [Serializable]
    public class RevisarCargueViewModel
    {
        public CargueLote Lote { get; set; }
        public List<CargueLoteHallazgo> Hallazgos { get; set; } = new List<CargueLoteHallazgo>();
        /// <summary>Total real de casos por validación (CodigoRegla); <see cref="Hallazgos"/> trae solo los primeros N de cada una.</summary>
        public Dictionary<string, int> TotalesPorRegla { get; set; } = new Dictionary<string, int>();
        public string NombreArchivo { get; set; }
        /// <summary>Qué le haría este cargue a los datos actuales (null si no se pudo calcular).</summary>
        public ImpactoCargue Impacto { get; set; }
    }
}
