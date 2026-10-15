namespace bufinscustomers.Models
{
    public class ConfiguracionSistemaItem
    {
        public string Clave       { get; set; }
        public string Valor       { get; set; }
        public string Descripcion { get; set; }

        /// <summary>Tipo (categoría) del parámetro: ConfiguracionSistema.IdTipo (script Sql/028). Null = sin tipo ("Otros").</summary>
        public int? IdTipo { get; set; }

        /// <summary>
        /// True si la clave contiene un secreto (p. ej. la API key): se cifra en reposo y la lista
        /// solo expone su versión enmascarada. Lo marca <c>ConfiguracionSistemaService.ObtenerTodos()</c>.
        /// </summary>
        public bool EsSecreta { get; set; }
    }

    /// <summary>Tipo/categoría para agrupar los parámetros globales (tabla ConfiguracionSistemaTipos).</summary>
    public class ConfiguracionSistemaTipo
    {
        public int    Id       { get; set; }
        public string Nombre   { get; set; }
        public string NombreEN { get; set; }
        public string Icono    { get; set; }
        public int    Orden    { get; set; }
        public bool   Activo   { get; set; }
        /// <summary>Cantidad de parámetros asignados (solo informativo).</summary>
        public int    Cantidad { get; set; }
    }
}
