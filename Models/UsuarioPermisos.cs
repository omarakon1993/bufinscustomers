using System;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo que representa la relación entre usuarios y opciones de menú asignadas
    /// </summary>
    public class UsuarioMenuPermisos
    {
        public int Id { get; set; }
        public int IdUsuario { get; set; }
        public int IdMenuOpcion { get; set; }
        public DateTime FechaAsignacion { get; set; }
        public int? UsuarioAsigno { get; set; }
    }
}
