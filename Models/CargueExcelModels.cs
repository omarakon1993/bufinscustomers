using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    [Serializable]
    public class ResultadoCargaExcel
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; }
        public List<DetalleCargaHojaExcel> DetalleHojas { get; set; } = new List<DetalleCargaHojaExcel>();
        public int TotalFilasInsertadas { get; set; }
        public int TotalHojasProcesadas { get; set; }
        public bool MostrarDescargaLog { get; set; }

        // Datos estructurados del lote confirmado (solo se llenan en DatosController.ConfirmarCargue),
        // para el modal-resumen de resultado en CargueExcel.cshtml — evita tener que parsear Mensaje.
        public string NombreEmpresa { get; set; }
        public int? Anio { get; set; }
        public string ModoTexto { get; set; }
        public int? IdEscenario { get; set; }
        /// <summary>Nota corta adicional (ej. el mensaje de SP_ValidarPlantillaInicial) — Mensaje sigue llevando la frase completa para el banner/notificación.</summary>
        public string NotaExtra { get; set; }
    }

    [Serializable]
    public class DetalleCargaHojaExcel
    {
        public string NombreHoja { get; set; }
        public string NombreTabla { get; set; }
        public int FilasInsertadas { get; set; }
        public string Estado { get; set; }
        public string MensajeError { get; set; }
    }

}
