using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Una visita a una página del sistema (tabla <c>AuditoriaNavegacion</c>). Se registra una
    /// fila por cada carga de página real (GET que devuelve una vista) de un usuario autenticado,
    /// con deduplicación de repeticiones inmediatas. Ver <see cref="Services.AuditoriaNavegacionService"/>
    /// y <see cref="Filters.RegistroNavegacionFilter"/>.
    /// </summary>
    public class RegistroNavegacion
    {
        public long Id { get; set; }
        public DateTime Fecha { get; set; }
        public int? IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public int? IdEmpresa { get; set; }
        /// <summary>Valor de <c>Usuarios.Admin</c> en el momento de la visita (0/1/2).</summary>
        public byte? RolUsuario { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        /// <summary>Código de la opción de <c>MenuOpciones</c> cuya ruta coincide, si existe.</summary>
        public string CodigoMenu { get; set; }
        /// <summary>Nombre legible de la página (instantánea del nombre del menú en ES).</summary>
        public string TituloPagina { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }

        // Resuelto en el SELECT (LEFT JOIN Empresas); no se almacena.
        public string NombreEmpresa { get; set; }
    }

    /// <summary>Filtros del informe de navegación. Paginación server-side en el modo detalle.</summary>
    public class NavegacionFiltro
    {
        public int? IdUsuario { get; set; }
        public int? IdEmpresa { get; set; }
        public string CodigoMenu { get; set; }
        public byte? Rol { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 25;

        /// <summary>
        /// Recorte por empresa para usuarios que no son Super Admin: <c>null</c> = sin recorte;
        /// lista vacía = no ve nada; con valores = solo esas empresas.
        /// </summary>
        public List<int> IdsEmpresaPermitidas { get; set; }
    }

    public class NavegacionResultado
    {
        public List<RegistroNavegacion> Items { get; set; } = new List<RegistroNavegacion>();
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalPaginas => TamanoPagina > 0 ? (int)Math.Ceiling(Total / (double)TamanoPagina) : 0;
    }

    /// <summary>Fila del resumen "por usuario".</summary>
    public class NavegacionResumenUsuario
    {
        public int? IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public string NombreEmpresa { get; set; }
        public int Visitas { get; set; }
        public int PaginasDistintas { get; set; }
        public DateTime Primera { get; set; }
        public DateTime Ultima { get; set; }
    }

    /// <summary>Fila del resumen "por página".</summary>
    public class NavegacionResumenPagina
    {
        public string Clave { get; set; }        // CodigoMenu o "Controller/Action"
        public string Titulo { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public int Visitas { get; set; }
        public int UsuariosDistintos { get; set; }
        public DateTime Ultima { get; set; }
    }

    /// <summary>Fila del resumen cruzado "por usuario y por página" (una fila por combinación usuario × página).</summary>
    public class NavegacionResumenUsuarioPagina
    {
        public int? IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public string NombreEmpresa { get; set; }
        public string Clave { get; set; }        // CodigoMenu o "Controller/Action"
        public string Titulo { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public int Visitas { get; set; }
        public DateTime Primera { get; set; }
        public DateTime Ultima { get; set; }
    }
}
