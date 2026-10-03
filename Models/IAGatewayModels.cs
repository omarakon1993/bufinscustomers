namespace bufinscustomers.Models
{
    /// <summary>Funciones del producto que consumen IA. Se guardan tal cual en dbo.IAUsoLog.Funcion.</summary>
    public static class IAFuncion
    {
        public const string Chat = "Chat";                     // Análisis IA (preguntas sobre una tabla Modelo*)
        public const string ResumenHome = "ResumenHome";       // Resumen ejecutivo del dashboard
        public const string InsightsPYG = "InsightsPYG";       // Insights del Estado de Resultados gerencial
        public const string InsightsBalance = "InsightsBalance"; // Insights del Balance General gerencial
    }

    /// <summary>Qué hace el sistema cuando una empresa agota su presupuesto mensual de tokens.</summary>
    public static class IAPoliticaAgotado
    {
        public const string Bloquear = "Bloquear";         // rechaza la consulta (por defecto)
        public const string Degradar = "Degradar";         // sigue atendiendo con un modelo económico y respuestas más cortas
        public const string Sobreconsumo = "Sobreconsumo"; // sigue atendiendo normal; el exceso queda marcado en IAUsoLog
    }

    /// <summary>
    /// Configuración de IA de una empresa (dbo.ConfiguracionIAEmpresa). Sin fila, la empresa usa los
    /// valores por defecto de esta clase y el presupuesto global.
    /// </summary>
    public class ConfigIAEmpresa
    {
        public int IdEmpresa { get; set; }
        /// <summary>Tope mensual de tokens propio; null = usa IA_TokensMensualesPorEmpresa; 0 = ilimitado.</summary>
        public long? PresupuestoTokensMensual { get; set; }
        public bool IaHabilitada { get; set; } = true;
        public string PoliticaAgotado { get; set; } = IAPoliticaAgotado.Bloquear;
        public bool PoolGrupo { get; set; }
        /// <summary>Día del mes (1-28) en que reinicia el periodo; null = mes calendario.</summary>
        public int? DiaCorte { get; set; }
        /// <summary>Modelo fijo para esta empresa; null = el global.</summary>
        public string ModeloPermitido { get; set; }
        /// <summary>Funciones (<see cref="IAFuncion"/>) habilitadas; null = todas.</summary>
        public System.Collections.Generic.List<string> FuncionesPermitidas { get; set; }
        /// <summary>Tokens por usuario y día; null = sin tope.</summary>
        public long? TopeDiarioUsuario { get; set; }
        /// <summary>Contexto de negocio propio de la empresa (se suma al global); null = sin contexto propio.</summary>
        public string ContextoNegocio { get; set; }
    }

    /// <summary>
    /// Todo lo que <see cref="Services.IAGateway"/> necesita para atender una consulta de IA. El
    /// controlador arma los datos (<see cref="Request"/>) y el gateway se encarga del resto:
    /// configuración, prompts, idioma, llamada al modelo, costo, registro de uso y auditoría.
    /// </summary>
    public class IASolicitud
    {
        /// <summary>Uno de <see cref="IAFuncion"/>.</summary>
        public string Funcion { get; set; }
        public Usuarios Usuario { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }

        /// <summary>Datos/pregunta/historial que se envían al modelo.</summary>
        public IAConsultaRequest Request { get; set; }

        /// <summary>Código en GestorPrompts de las instrucciones de esta función (p. ej. "RESUMEN_PYG_GERENCIAL").</summary>
        public string CodigoPrompt { get; set; }
        /// <summary>Texto de reserva si el prompt no existe o está inactivo (null = usa el genérico de IAService).</summary>
        public string PromptPorDefecto { get; set; }
        /// <summary>Tope de tokens del resumen inicial (sin pregunta); se acota por el máximo configurado. 0 = sin tope propio.</summary>
        public int MaxTokensResumen { get; set; }

        /// <summary>"es" | "en". Idioma en el que debe responder el modelo.</summary>
        public string Idioma { get; set; }
        /// <summary>
        /// Instrucción adicional que se agrega al prompt solo cuando <see cref="Idioma"/> es "en".
        /// Sirve para que el modelo conserve las etiquetas fijas que la vista usa para separar la
        /// respuesta en tarjetas (los prompts están escritos en español, con etiquetas en español).
        /// </summary>
        public string InstruccionEnIngles { get; set; }

        /// <summary>Resolutor de textos traducidos (el controlador pasa su <c>R</c>): el gateway no lee recursos de idioma.</summary>
        public System.Func<string, string> Traducir { get; set; }

        // Para la fila de AuditoriaAnalisisIA (visor de auditoría) y para completar la respuesta.
        public string NombreTablaAuditoria { get; set; }
        public string FiltrosDescripcion { get; set; }
        public int FilasAnalizadas { get; set; }
        /// <summary>Total de filas disponibles (si difiere de las enviadas). 0 = igual a FilasAnalizadas.</summary>
        public int TotalFilas { get; set; }
    }
}
