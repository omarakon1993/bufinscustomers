namespace bufinscustomers.Models
{
    /// <summary>
    /// Escenario de datos: copia paralela completa de los datos cargados para una empresa
    /// (mismos años/configuración, otros datos). Id = 1 es siempre el escenario principal
    /// (además es el DEFAULT de la columna IdEscenario en las tablas Ini_*). Catálogo
    /// parametrizable: agregar un escenario nuevo es un registro más en esta tabla, sin
    /// cambios de esquema ni de código (ver Sql/001_Escenarios_CreateTable.sql).
    /// </summary>
    public class Escenario
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
    }
}
