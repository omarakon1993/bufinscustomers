namespace bufinscustomers.Models
{
    public class ConfigLineaNegocio
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreLinea { get; set; }
        public int Orden { get; set; }
        
        public ConfigLineaNegocio()
        {
            Orden = 0;
        }
    }
}