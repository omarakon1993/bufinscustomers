namespace bufinscustomers.Models
{
    public class Empresas
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Nit { get; set; }
        public string Direccion { get; set; }
        public string Telefono { get; set; }
        public string Correo { get; set; }
        public string Abreviatura { get; set; }

        public int? IdGrupoEmpresarial { get; set; }

        // Solo lectura, poblado via JOIN para mostrar el grupo en el gestor de Empresas
        public string NombreGrupoEmpresarial { get; set; }
    }
}
