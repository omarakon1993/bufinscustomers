namespace bufinscustomers.Models
{
    /// <summary>
    /// Un hallazgo devuelto por <c>sp_ObtenerAlertasHome</c> (mismas columnas que <c>CarguesLotesErrores</c>
    /// más la empresa). <see cref="Severidad"/>: "Error" / "Advertencia" / "Info".
    /// </summary>
    public class AlertaHome
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Severidad { get; set; }
        public string CodigoRegla { get; set; }
        public string Titulo { get; set; }
        public string TituloEn { get; set; }
        public string Mensaje { get; set; }
        public string MensajeEn { get; set; }
    }
}
