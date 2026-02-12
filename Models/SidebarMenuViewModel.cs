using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class SidebarCategoriaViewModel
    {
        public string Nombre { get; set; }
        public string Icono { get; set; }
        public int OrdenCategoria { get; set; }
        public List<SidebarGrupoViewModel> Grupos { get; set; } = new List<SidebarGrupoViewModel>();
    }

    public class SidebarGrupoViewModel
    {
        public string Nombre { get; set; }
        public string Icono { get; set; }
        public List<SidebarItemViewModel> Items { get; set; } = new List<SidebarItemViewModel>();
    }

    public class SidebarItemViewModel
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public string Icono { get; set; }
    }
}
