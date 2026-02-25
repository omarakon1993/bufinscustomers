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
        public int TotalHojasIgnoradas { get; set; }
        public bool MostrarDescargaLog { get; set; }
    }

    [Serializable]
    public class DetalleCargaHojaExcel
    {
        public string NombreHoja { get; set; }
        public string NombreTabla { get; set; }
        public int FilasInsertadas { get; set; }
        public int TotalColumnas { get; set; }
        public string Estado { get; set; }
        public string MensajeError { get; set; }
    }
}
