using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace bufinscustomers.Models
{
    public class Usuarios
    {
        public int Id { get; set; }
        public string Usuario { get; set; }       
        public string Clave { get; set; }
        public string ConfirmarClave { get; set; }      
        public string Nombre     {get; set;}
        public string Apellidos  {get; set;}
        public string Correo { get; set; }
        public string Telefono   {get; set;}
        public byte? Admin { get; set; }
        public int?   IdEmpresa  {get; set;}
        public ImagenUsuario Imagen { get; set; }
        /// <summary>Acceso a consultas de IA: null = permitido por defecto, false = sin acceso, true = acceso explícito.</summary>
        public bool? AccesoConsultasIA { get; set; }
        /// <summary>true = debe cambiar la contraseña antes de usar la app (Usuarios.DebeCambiarClave; no se persiste desde el formulario).</summary>
        public bool DebeCambiarClave { get; set; }

    }
}