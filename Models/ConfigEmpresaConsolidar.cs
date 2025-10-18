namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo para empresas a consolidar
    /// Tabla detalle que almacena la lista de empresas que se consolidarán
    /// </summary>
    public class ConfigEmpresaConsolidar
    {
        public int Id { get; set; }
        public int IdConfiguracion { get; set; }
        public string NombreEmpresa { get; set; }
        public int Orden { get; set; }
        
        public ConfigEmpresaConsolidar()
        {
            Orden = 0;
        }
    }
}
