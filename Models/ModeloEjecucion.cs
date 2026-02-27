using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class ModeloEjecucion
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string NombreSP { get; set; }
        public string Descripcion { get; set; }
        public string Icono { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
    }

    public class ModeloPageViewModel
    {
        public List<Empresas> Empresas { get; set; }
        public List<ModeloEjecucion> Modelos { get; set; }
    }
}
