using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace bufinscustomers.Models
{
    public class ImagenUsuario
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string ImagenBase64 { get; set; }
        public string TipoImagen { get; set; }
    }
}