using System.Configuration;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Clase base para todos los servicios que proporciona funcionalidades comunes
    /// </summary>
    public abstract class BaseService
    {
        /// <summary>
        /// Cadena de conexión centralizada obtenida del Web.config
        /// </summary>
        protected static readonly string CadenaConexion = 
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
    }
}