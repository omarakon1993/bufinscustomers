using System.Collections.Generic;
using System.Web.Mvc;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Un selector del panel de filtros uniforme de Auditoría (<c>Views/Shared/_AudFiltros.cshtml</c>).
    /// Las 4 pestañas (Cambios / Navegación / Cargues / Consultas IA) comparten el mismo orden:
    /// Empresa · Usuario · campos propios de la pestaña; lo avanzado va en "Más filtros".
    /// </summary>
    public class AudCampo
    {
        /// <summary>id del &lt;select&gt; (p. ej. "fEmpresa"). El JS de cada pestaña lo lee por este id.</summary>
        public string Id { get; set; }
        public string Etiqueta { get; set; }
        /// <summary>Clase FontAwesome del ícono de la etiqueta (p. ej. "fa-building").</summary>
        public string Icono { get; set; }
        /// <summary>Texto de la primera opción (valor vacío = sin filtrar).</summary>
        public string TextoTodos { get; set; }
        /// <summary>Opciones fijas; vacío si el JS de la pestaña las puebla (p. ej. "Qué ocurrió").</summary>
        public List<SelectListItem> Opciones { get; set; } = new List<SelectListItem>();
        /// <summary>true = campo propio de la pestaña (se resalta levemente frente a los comunes).</summary>
        public bool Propio { get; set; }
        /// <summary>id del contenedor, por si el JS necesita mostrarlo u ocultarlo.</summary>
        public string WrapId { get; set; }
    }

    /// <summary>Configuración del panel de filtros uniforme de Auditoría.</summary>
    public class AudFiltrosVM
    {
        public string PlaceholderBusqueda { get; set; }
        /// <summary>Chip de período activo al cargar: "hoy" | "7" | "30" | "mes".</summary>
        public string PeriodoDefecto { get; set; } = "30";
        public List<AudCampo> Campos { get; set; } = new List<AudCampo>();
        public List<AudCampo> Avanzados { get; set; } = new List<AudCampo>();
    }
}
