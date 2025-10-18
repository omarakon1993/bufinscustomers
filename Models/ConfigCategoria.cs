namespace bufinscustomers.Models
{
    public class ConfigCategoria
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreCategoria { get; set; }
        public int Orden { get; set; }

        public ConfigCategoria()
        {
            Orden = 0;
        }
    }
}