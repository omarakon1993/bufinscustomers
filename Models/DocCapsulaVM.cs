namespace bufinscustomers.Models
{
    /// <summary>Datos de la cápsula «Documentación» del título de un informe (ver Views/Shared/_DocumentacionCapsula.cshtml).</summary>
    public class DocCapsulaVM
    {
        /// <summary>Id del botón (el JS del informe lo liga con DocVisor.init).</summary>
        public string Id { get; set; }
        public string Tooltip { get; set; }
        /// <summary>Clave del documento en DocumentacionCatalogo (la misma de la URL del visor).</summary>
        public string Clave { get; set; }
    }
}
