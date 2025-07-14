using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace bufinscustomers.Models
{
    public class Reporte
    {
        public int Id { get; set; }
        public int IdEmpresa { get; set; }
        public string Nombre { get; set; }
        public int AñoInicial { get; set; }
        public int AñoFinal { get; set; }
        public string Descripcion { get; set; }
        public string EnlaceHTML { get; set; }
        public string NombreEmpresa { get; set; }
    }
}
