namespace bufinscustomers.Models
{
    public class IAConsultaRequest
    {
        public string Pregunta { get; set; }
        public string DatosJson { get; set; }
        public string NombreTabla { get; set; }
        public string FiltrosDescripcion { get; set; }
    }

    public class IAConsultaResponse
    {
        public bool Exitoso { get; set; }
        public string Respuesta { get; set; }
        public string Error { get; set; }
        public int FilasEnviadas { get; set; }
        public int TotalFilas { get; set; }
    }
}
