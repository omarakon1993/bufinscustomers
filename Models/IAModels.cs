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
        /// <summary>Código estable del error cuando el llamador debe traducirlo (p. ej. "SIN_CREDITO"); null en el resto.</summary>
        public string CodigoError { get; set; }
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

    /// <summary>Resumen de uso/configuración de IA de una empresa para la pantalla Configuración IA → Por Empresa.</summary>
    public class ResumenIAEmpresaViewModel
    {
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        /// <summary>Override explícito de esta empresa, o null si usa el valor global.</summary>
        public long? PresupuestoOverride { get; set; }
        public long PresupuestoGlobalDefault { get; set; }
        /// <summary>Override ?? valor global — el que realmente se aplica.</summary>
        public long PresupuestoEfectivo { get; set; }
        public bool Ilimitado { get; set; }
        public long ConsumidoMes { get; set; }
        public int PorcentajeConsumido { get; set; }

        // Control por empresa (Fase 2)
        public ConfigIAEmpresa Config { get; set; }
        /// <summary>Inicio del periodo de consumo en curso (según el día de corte), "dd/MM/yyyy".</summary>
        public string InicioPeriodo { get; set; }
        /// <summary>true si el consumo/presupuesto mostrado es el del pool del grupo.</summary>
        public bool EnPool { get; set; }
        public int EmpresasEnPool { get; set; }
        public string NombreGrupo { get; set; }
        /// <summary>false si la empresa no pertenece a un Grupo Empresarial (el pool no aplica).</summary>
        public bool TieneGrupo { get; set; }
        public List<UsuarioAccesoIAViewModel> Usuarios { get; set; } = new List<UsuarioAccesoIAViewModel>();
    }

    /// <summary>Acceso de un usuario a las consultas de IA, visto desde Configuración IA → Por Empresa.</summary>
    public class UsuarioAccesoIAViewModel
    {
        public int Id { get; set; }
        public string NombreCompleto { get; set; }
        public string Correo { get; set; }
        public byte? Admin { get; set; }
        /// <summary>Valor crudo de Usuarios.AccesoConsultasIA: null = usa el valor por defecto (permitido).</summary>
        public bool? AccesoExplicito { get; set; }
        /// <summary>AccesoExplicito ?? true — el que realmente se aplica.</summary>
        public bool AccesoEfectivo { get; set; }
    }
}
