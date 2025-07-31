using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace bufinscustomers.Models
{
    public class Reportes
    {
        public int Id { get; set; }
        public int IdEmpresa { get; set; }
        public string Nombre { get; set; }
        public string AñoInicial { get; set; }
        public string AñoFinal { get; set; }
        public string Descripcion { get; set; }
        public string EnlaceHTML { get; set; }
        public string NombreEmpresa { get; set; } // Para mostrar el nombre de la empresa en la vista
    }
}
