using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class MensajeChatIA
    {
        public string Rol { get; set; }
        public string Contenido { get; set; }
    }

    public class IAConsultaRequest
    {
        public string Pregunta { get; set; }
        /// <summary>Datos serializados que se envían al modelo. El formato lo indica <see cref="FormatoDatos"/>.</summary>
        public string DatosJson { get; set; }
        /// <summary>"csv" o "json" (por defecto se asume JSON). El CSV usa ~40% menos tokens.</summary>
        public string FormatoDatos { get; set; }
        public string NombreTabla { get; set; }
        public string FiltrosDescripcion { get; set; }
        public List<MensajeChatIA> Historial { get; set; }
    }

    public class IAConsultaResponse
    {
        public bool Exitoso { get; set; }
        public string Respuesta { get; set; }
        public string Error { get; set; }
        public int FilasEnviadas { get; set; }
        public int TotalFilas { get; set; }
        public bool DesdeCache { get; set; }
        public string PromptContextoInicial { get; set; }

        // Consumo de tokens del bloque "usage" de la respuesta de OpenAI (0 si no vino o desde caché).
        public int TokensPrompt { get; set; }
        public int TokensRespuesta { get; set; }
        public int TokensTotal { get; set; }

        // Metadatos para la UI (N14): modelo usado, coste estimado y fila de auditoría (para valorar).
        public string Modelo { get; set; }
        public decimal CostoEstimadoUSD { get; set; }
        public int IdAuditoria { get; set; }
    }
}
