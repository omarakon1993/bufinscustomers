using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Models
{
    /// <summary>
    /// ViewModel para mostrar opciones de menú con estado de asignación para un usuario
    /// </summary>
    public class OpcionMenuUsuarioViewModel
    {
        public int IdMenuOpcion { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public string Categoria { get; set; }
        public string Icono { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public bool Asignado { get; set; }
        public string NombreGrupo { get; set; }
        public string IconoGrupo { get; set; }
        public string IconoCategoria { get; set; }
        public int OrdenCategoria { get; set; }
        public bool SoloSuperAdmin { get; set; }
        public bool SoloAdminEmpresa { get; set; }
    }

    /// <summary>
    /// ViewModel para la vista de gestión de opciones de menú de un usuario
    /// </summary>
    public class GestionOpcionesMenuViewModel
    {
        public Usuarios Usuario { get; set; }
        public List<IGrouping<string, OpcionMenuUsuarioViewModel>> OpcionesPorCategoria { get; set; }
    }
}
