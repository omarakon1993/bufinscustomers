namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo para Ajuste 1
    /// Tabla detalle que almacena la lista de ajustes tipo 1
    /// </summary>
    public class ConfigAjuste1
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreAjuste { get; set; }
        public int Orden { get; set; }

        public ConfigAjuste1()
        {
            Orden = 0;
        }
    }
}
