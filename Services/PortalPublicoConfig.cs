using System;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Nombre VISIBLE del portal público en la auditoría (hoy «Observatorio»). Cuando el sitio cambie de nombre basta con cambiar
    /// la clave <c>PortalPublico_Nombre</c> en Configuraciones generales BUFINS: ningún nombre está escrito en el código.
    /// </summary>
    public static class PortalPublicoConfig
    {
        public const string Clave = "PortalPublico_Nombre";
        public const string PorDefecto = "Observatorio";

        private static string _nombre; private static DateTime _hora = DateTime.MinValue;

        public static string Nombre()
        {
            if (_nombre != null && DateTime.UtcNow - _hora < TimeSpan.FromMinutes(1)) return _nombre;
            string v = null;
            try { v = new ConfiguracionSistemaService().ObtenerValor(Clave); } catch (Exception) { }
            v = string.IsNullOrWhiteSpace(v) ? PorDefecto : v.Trim();
            if (v.Length > 40) v = v.Substring(0, 40);
            _nombre = v; _hora = DateTime.UtcNow;
            return _nombre;
        }
    }
}
