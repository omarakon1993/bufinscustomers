using System;

namespace bufinscustomers.Models
{
    public class AuditoriaAnalisisIA
    {
        public int Id { get; set; }
        public int IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string NombreTabla { get; set; }
        public string Filtros { get; set; }
        public string Pregunta { get; set; }
        public string Respuesta { get; set; }
        public DateTime FechaPregunta { get; set; }
        public int FilasAnalizadas { get; set; }
        /// <summary>Tokens consumidos por la llamada (bloque <c>usage.total_tokens</c>). 0 si no vino / desde caché.</summary>
        public int TokensTotal { get; set; }
        /// <summary>Valoración del usuario (N02): 1 = útil, 0 = no útil, null = sin valorar.</summary>
        public int? Valoracion { get; set; }
    }

    public class UsuarioAuditoriaDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
    }
}
