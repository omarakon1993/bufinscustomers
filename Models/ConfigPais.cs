namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo para países
    /// Tabla detalle que almacena la lista de países
    /// </summary>
    public class ConfigPais
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombrePais { get; set; }
        public int Orden { get; set; }
        
        public ConfigPais()
        {
            Orden = 0;
        }
    }
}
