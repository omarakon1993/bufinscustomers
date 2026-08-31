using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Una fila de la tabla única de auditoría (<c>Auditoria</c>). Toda auditoría del sistema
    /// vive aquí, diferenciada por <see cref="Tipo"/>. Ver <see cref="Services.AuditoriaService"/>.
    /// </summary>
    public class RegistroAuditoria
    {
        public long Id { get; set; }
        public DateTime Fecha { get; set; }

        /// <summary>Código de tipo — ver <see cref="AuditoriaTipo"/>. Es el discriminador de la tabla.</summary>
        public string Tipo { get; set; }

        /// <summary>Agrupación libre para el visor (por defecto, igual a <see cref="Tipo"/>).</summary>
        public string Categoria { get; set; }

        /// <summary>Verbo — ver <see cref="AuditoriaAccion"/>.</summary>
        public string Accion { get; set; }

        /// <summary>Nombre lógico de la entidad afectada: "Empresas", "MenuOpciones", "ConfiguracionEmpresa"…</summary>
        public string Entidad { get; set; }

        /// <summary>Id de la fila afectada, como texto (puede ser null).</summary>
        public string EntidadId { get; set; }

        public string Descripcion { get; set; }

        /// <summary>JSON del estado previo (opcional).</summary>
        public string ValorAnterior { get; set; }

        /// <summary>JSON del estado nuevo (opcional).</summary>
        public string ValorNuevo { get; set; }

        public int? IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public int? IdEmpresa { get; set; }

        /// <summary>Resuelto en el SELECT (LEFT JOIN Empresas); no se almacena.</summary>
        public string NombreEmpresa { get; set; }

        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
    }

    /// <summary>Códigos de <see cref="RegistroAuditoria.Tipo"/>. Agregar aquí cualquier auditoría nueva.</summary>
    public static class AuditoriaTipo
    {
        public const string Configuracion = "CONFIGURACION";
        public const string Seguridad     = "SEGURIDAD";
        public const string Usuarios      = "USUARIOS";
        public const string Empresas      = "EMPRESAS";
        public const string Permisos      = "PERMISOS";
        public const string Modelos       = "MODELOS";
        public const string Grupos        = "GRUPOS_EMPRESARIALES";
        public const string Menu          = "MENU";
    }

    public static class AuditoriaAccion
    {
        public const string Crear        = "Crear";
        public const string Editar       = "Editar";
        public const string Eliminar     = "Eliminar";
        public const string Asignar      = "Asignar";
        public const string Login        = "Login";
        public const string LoginFallido = "LoginFallido";
        public const string Bloqueo      = "Bloqueo";
    }

    /// <summary>Filtros del visor de auditoría. Paginación server-side.</summary>
    public class AuditoriaFiltro
    {
        public string Tipo { get; set; }
        public string Accion { get; set; }
        public int? IdUsuario { get; set; }
        public int? IdEmpresa { get; set; }
        public string Entidad { get; set; }
        public string Texto { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 25;

        /// <summary>
        /// Restricción de alcance por empresa (usuarios que no son Super Admin):
        /// <c>null</c> = sin restricción; lista vacía = no ve nada;
        /// con valores = solo filas cuya <c>IdEmpresa</c> esté en la lista.
        /// </summary>
        public List<int> IdsEmpresaPermitidas { get; set; }
    }

    public class AuditoriaResultado
    {
        public List<RegistroAuditoria> Items { get; set; } = new List<RegistroAuditoria>();
        public int Total { get; set; }
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalPaginas => TamanoPagina > 0 ? (int)Math.Ceiling(Total / (double)TamanoPagina) : 0;
    }
}
