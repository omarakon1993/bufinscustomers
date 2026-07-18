using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class GrupoEmpresarial
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }

        // Solo lectura, poblado via COUNT para el listado del gestor
        public int TotalEmpresas { get; set; }
    }

    // ViewModel para la pantalla "Gestionar empresas" de un grupo (checkboxes + AJAX)
    public class GestionarGrupoEmpresasViewModel
    {
        public GrupoEmpresarial Grupo { get; set; }
        public List<EmpresaSeleccionableViewModel> Empresas { get; set; }
    }

    public class EmpresaSeleccionableViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Abreviatura { get; set; }
        public bool Seleccionada { get; set; }

        // No nulo si la empresa ya pertenece a OTRO grupo distinto al que se está gestionando
        public string NombreOtroGrupo { get; set; }
    }
}
