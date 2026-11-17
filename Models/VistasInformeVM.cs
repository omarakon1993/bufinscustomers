namespace bufinscustomers.Models
{
    /// <summary>
    /// Selector de «vistas» de una familia de informes (p. ej. PYG: Análisis | Una hoja). Lo pinta el parcial _VistasInforme; al cambiar de
    /// vista se conservan la empresa y el escenario elegidos (ver Assets/js/vistas-informe.js).
    /// </summary>
    public class VistasInformeVM
    {
        /// <summary>"pyg" | "caja" | "gerencia".</summary>
        public string Familia { get; set; }
        /// <summary>Vista que se está mostrando: "analisis" | "hoja" | "flujo" | "simple" | "resumen" | "comite".</summary>
        public string Actual { get; set; }
        /// <summary>Selector CSS del combo de empresa de la página (se usa para conservar la elección).</summary>
        public string EmpresaSel { get; set; }
        /// <summary>Selector CSS del combo de escenario de la página.</summary>
        public string EscenarioSel { get; set; }
    }
}
