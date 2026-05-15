namespace bufinscustomers.Models
{
    public class ConfigAnoHistorico
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreAno { get; set; }
        public int Orden { get; set; }

        public ConfigAnoHistorico()
        {
            Orden = 0;
        }
    }
}
