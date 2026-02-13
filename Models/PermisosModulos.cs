namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo que representa las opciones del menú disponibles en el sistema
    /// </summary>
    public class MenuOpciones
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public string Categoria { get; set; }
        public string Icono { get; set; }
        public int Orden { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public bool Activo { get; set; }

        // Propiedades para renderizado del sidebar dinámico
        public string NombreGrupo { get; set; }
        public string IconoGrupo { get; set; }
        public string IconoCategoria { get; set; }
        public int OrdenCategoria { get; set; }
        public bool SoloSuperAdmin { get; set; }
        public bool SoloAdminEmpresa { get; set; }
    }
}
