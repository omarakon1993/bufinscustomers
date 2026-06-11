using System;

namespace bufinscustomers.Models
{
    public class CuentaCorreo
    {
        public int      Id                 { get; set; }
        public string   Nombre             { get; set; }
        public string   Host               { get; set; }
        public int      Puerto             { get; set; }
        public bool     Ssl                { get; set; }
        public bool     IgnorarCertificado { get; set; }
        public string   Usuario            { get; set; }
        public string   Contrasena         { get; set; }
        public string   Remitente          { get; set; }
        public string   NombreRemitente    { get; set; }
        public bool     Predeterminada     { get; set; }
        public bool     Activa             { get; set; }
        public DateTime FechaCreacion      { get; set; }
        public DateTime FechaModificacion  { get; set; }
    }
}
