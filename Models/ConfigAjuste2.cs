namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo para Ajuste 2
    /// Tabla detalle que almacena la lista de ajustes tipo 2
    /// </summary>
    public class ConfigAjuste2
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreAjuste { get; set; }
        public int Orden { get; set; }

        public ConfigAjuste2()
        {
            Orden = 0;
        }
    }
}
