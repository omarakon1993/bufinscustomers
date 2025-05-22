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
        public byte?  Admin    {get; set;}
        public string Nombre     {get; set;}
        public string Apellidos  {get; set;}
        public int?   Telefono   {get; set;} 
        public int?   IdEmpresa  {get; set;}
    }
}