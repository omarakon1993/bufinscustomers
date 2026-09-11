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

        /// <summary>Nombre legible de la entidad afectada, instantánea en el momento del cambio
        /// ("ACME S.A.S." en vez de "Empresas #42"). Se autocompleta desde el JSON si no se pasa.</summary>
        public string EntidadNombre { get; set; }

        /// <summary>Nivel para resaltar en el visor — ver <see cref="AuditoriaSeveridad"/>.
        /// Se deriva de la acción/tipo si no se pasa.</summary>
        public string Severidad { get; set; }

        /// <summary>Identificador de la operación (petición HTTP) que agrupa los cambios escritos
        /// juntos. Se autocompleta con un GUID por petición.</summary>
        public Guid? OperacionId { get; set; }

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
        public const string Cargues       = "CARGUES";
        public const string Escenarios    = "ESCENARIOS";
    }

    public static class AuditoriaAccion
    {
        public const string Crear        = "Crear";
        public const string Editar       = "Editar";
        public const string Eliminar     = "Eliminar";
        public const string Asignar      = "Asignar";
        public const string Enviar       = "Enviar";
        public const string Ejecutar     = "Ejecutar";
        public const string Exportar     = "Exportar";
        public const string Login        = "Login";
        public const string LoginFallido = "LoginFallido";
        public const string Bloqueo      = "Bloqueo";
    }

    /// <summary>Nivel de una fila de auditoría, para resaltar en el visor.</summary>
    public static class AuditoriaSeveridad
    {
        public const string Info        = "Info";
        public const string Advertencia = "Advertencia";
        public const string Critico     = "Critico";

        /// <summary>Deriva la severidad de la acción/tipo cuando no se especifica una explícita.</summary>
        public static string Derivar(string tipo, string accion)
        {
            switch (accion)
            {
                case AuditoriaAccion.Eliminar:
                case AuditoriaAccion.Bloqueo:
                    return Critico;
                case AuditoriaAccion.Crear:
                case AuditoriaAccion.Asignar:
                case AuditoriaAccion.LoginFallido:
                    return Advertencia;
                default:
                    // Cualquier cambio en permisos merece atención aunque sea "Editar".
                    return string.Equals(tipo, AuditoriaTipo.Permisos, StringComparison.OrdinalIgnoreCase)
                        ? Advertencia : Info;
            }
        }
    }

    /// <summary>Filtros del visor de auditoría. Paginación server-side.</summary>
    public class AuditoriaFiltro
    {
        public string Tipo { get; set; }
        /// <summary>Excluye un tipo del resultado (p. ej. ocultar SEGURIDAD en la vista de "cambios del sistema").</summary>
        public string ExcluirTipo { get; set; }
        public string Accion { get; set; }
        public int? IdUsuario { get; set; }
        public int? IdEmpresa { get; set; }
        public string Entidad { get; set; }
        /// <summary>Historial de un registro concreto (se usa junto con <see cref="Entidad"/>).</summary>
        public string EntidadId { get; set; }
        public string Severidad { get; set; }
        /// <summary>Todos los cambios escritos en la misma operación (petición).</summary>
        public Guid? OperacionId { get; set; }
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
