using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Datos del panel izquierdo de las pantallas de acceso (Views/Acceso/_AccesoPanel.cshtml).</summary>
    public class AccesoPanelVM
    {
        public string Eyebrow { get; set; }
        public List<AccesoPasoVM> Pasos { get; set; } = new List<AccesoPasoVM>();
        /// <summary>true = pie "Ingresando como" (hay sesiÃ³n parcial); false = pie del sitio (volver a bufins.com).</summary>
        public bool PieUsuario { get; set; }
        public string CorreoEnmascarado { get; set; }
        /// <summary>Destino de «¿No eres tú? Cambiar cuenta» (Login o CerrarSesion).</summary>
        public string UrlCambiarCuenta { get; set; }
    }

    public class AccesoPasoVM
    {
        public string Titulo { get; set; }
        /// <summary>Si es null se usa el texto por defecto del estado (Completado / En curso / nada).</summary>
        public string Subtitulo { get; set; }
        /// <summary>"done" | "active" | "todo" | "error"</summary>
        public string Estado { get; set; }

        public AccesoPasoVM() { }
        public AccesoPasoVM(string titulo, string estado, string subtitulo = null)
        {
            Titulo = titulo; Estado = estado; Subtitulo = subtitulo;
        }
    }
}
