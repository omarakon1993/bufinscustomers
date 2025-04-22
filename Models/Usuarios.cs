using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace bufinscustomers.Models
{
    public class Usuarios
    {
        public int Id { get; set; }
        public string Correo { get; set; }
        public string Clave { get; set; }
        public string ConfirmarClave { get; set; }
    }
}