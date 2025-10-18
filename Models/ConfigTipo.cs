namespace bufinscustomers.Models
{
    public class ConfigTipo
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreTipo { get; set; }
        public int Orden { get; set; }
        
        public ConfigTipo()
        {
            Orden = 0;
        }
    }
}