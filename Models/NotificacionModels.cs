using System;

namespace bufinscustomers.Models
{
    public class Notificacion
    {
        public int      Id            { get; set; }
        public int      IdUsuario     { get; set; }
        public string   Titulo        { get; set; }
        public string   Mensaje       { get; set; }
        public string   Tipo          { get; set; }   // success | info | warning | error
        public bool     Leida         { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string   Url           { get; set; }   // D4: destino al hacer clic (opcional)
    }
}
