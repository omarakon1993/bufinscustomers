namespace bufinscustomers.Models
{
    public class ConfiguracionSistemaItem
    {
        public string Clave       { get; set; }
        public string Valor       { get; set; }
        public string Descripcion { get; set; }

        /// <summary>
        /// True si la clave contiene un secreto (p. ej. la API key): se cifra en reposo y la lista
        /// solo expone su versión enmascarada. Lo marca <c>ConfiguracionSistemaService.ObtenerTodos()</c>.
        /// </summary>
        public bool EsSecreta { get; set; }
    }
}
