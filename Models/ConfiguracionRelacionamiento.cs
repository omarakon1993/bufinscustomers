using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class ResultadoCargaRelacionamiento
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; }
        public List<DetalleCargaHoja> DetalleHojas { get; set; }
        public int TotalFilasInsertadas { get; set; }
        public int TotalHojasProcesadas { get; set; }
        public int TotalHojasIgnoradas { get; set; }

        public ResultadoCargaRelacionamiento()
        {
            DetalleHojas = new List<DetalleCargaHoja>();
        }
    }

    public class DetalleCargaHoja
    {
        public string NombreHoja { get; set; }
        public string NombreTabla { get; set; }
        public int FilasInsertadas { get; set; }
        public int TotalColumnas { get; set; }
        public string Estado { get; set; } // Exitoso, Error, Ignorada
        public string MensajeError { get; set; }
    }

    public class TablaRelInfo
    {
        public string NombreTabla { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalColumnas { get; set; }
    }
}
