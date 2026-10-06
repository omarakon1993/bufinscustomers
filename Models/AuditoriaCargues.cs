using System;

namespace bufinscustomers.Models
{
    public class AuditoriaCargues
    {
        public int Id { get; set; }
        public DateTime FechaCargue { get; set; }
        public int IdUsuario { get; set; }
        public string Usuario { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string NombreArchivo { get; set; }
        /// <summary>Escenario del cargue (Sql/004). null si la BD aún no tiene la columna o la fila es anterior.</summary>
        public int? IdEscenario { get; set; }
    }
}
