namespace bufinscustomers.Models
{
    public class CategoriaMenu
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string NombreEN { get; set; }
        public string Icono { get; set; }
        public int Orden { get; set; }
    }

    public class GrupoMenu
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string NombreEN { get; set; }
        public string Icono { get; set; }
        public int IdCategoria { get; set; }
        public string NombreCategoria { get; set; } // solo lectura, cargado via JOIN
        public int Orden { get; set; }
    }
}
