using Microsoft.AspNetCore.Http;

namespace bufinscustomers
{
    /// <summary>Atajos que las vistas y controladores usaban de HttpRequest (Request.Url.*) en .NET Framework.</summary>
    public static class RequestExtensions
    {
        /// <summary>/ruta?consulta (equivale a Request.Url.PathAndQuery).</summary>
        public static string PathAndQuery(this HttpRequest r) { return r.Path.ToUriComponent() + r.QueryString.ToUriComponent(); }

        /// <summary>IP del cliente (IPv4 aunque llegue mapeada en IPv6).</summary>
        public static string Ip(this HttpContext c)
        {
            var ip = c.Connection.RemoteIpAddress;
            if (ip == null) return "desconocida";
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return ip.ToString();
        }
    }
}
