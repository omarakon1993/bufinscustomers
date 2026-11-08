using System;
using Microsoft.Extensions.Configuration;

namespace bufinscustomers.Compat
{
    /// <summary>
    /// Reemplazo de System.Configuration.ConfigurationManager con la misma forma de uso
    /// (<c>ConfigurationManager.AppSettings["Clave"]</c> y <c>ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString</c>),
    /// leyendo de IConfiguration: variables de entorno > secretsSettings.json > secretos de usuario (desarrollo) > appsettings.json.
    /// </summary>
    public static class ConfigurationManager
    {
        private static IConfiguration _config;
        public static void Iniciar(IConfiguration config) { _config = config; }

        public static readonly AppSettingsCompat AppSettings = new AppSettingsCompat();
        public static readonly ConnectionStringsCompat ConnectionStrings = new ConnectionStringsCompat();

        public sealed class AppSettingsCompat
        {
            /// <summary>Valor de la clave o null si no existe (igual que NameValueCollection).</summary>
            public string this[string clave] { get { return _config == null ? null : _config[clave]; } }
        }

        public sealed class ConnectionStringsCompat
        {
            public ConnectionStringSettings this[string nombre]
            {
                get
                {
                    var cs = _config == null ? null : _config.GetConnectionString(nombre);
                    return cs == null ? null : new ConnectionStringSettings { Name = nombre, ConnectionString = cs };
                }
            }
        }

        public sealed class ConnectionStringSettings
        {
            public string Name { get; set; }
            public string ConnectionString { get; set; }
        }
    }
}
