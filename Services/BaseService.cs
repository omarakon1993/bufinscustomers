namespace bufinscustomers.Services
{
    /// <summary>
    /// Clase base para todos los servicios que proporciona funcionalidades comunes
    /// </summary>
    public abstract class BaseService
    {
        /// <summary>
        /// Cadena de conexión centralizada (se lee de ConnectionStrings:DefaultConnection en secretsSettings.json / appsettings.json)
        /// </summary>
        protected static readonly string CadenaConexion = 
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
    }
}