using System;
using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Models
{
    /// <summary>Una tabla Ini_ afectada por un cargue: filas que hay hoy vs. filas que trae el archivo.</summary>
    [Serializable]
    public class ImpactoTabla
    {
        public string Tabla { get; set; }
        public int Existentes { get; set; }
        public int Nuevas { get; set; }

        /// <summary>
        /// Tablas que es normal que no traigan datos de un año a otro (presupuesto y PCH): quedan fuera de la regla de
        /// reducción/bloqueo. Siguen apareciendo en el detalle con sus conteos.
        /// </summary>
        private static readonly HashSet<string> TablasExentas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ini_PptoPYG", "Ini_PptoPYGConAjuste", "Ini_PCH", "Ini_EjecPCH"
        };

        public bool Exenta => Tabla != null && TablasExentas.Contains(Tabla);

        /// <summary>Tenía datos y el archivo no trae ninguno: la tabla quedaría vacía (no aplica a las tablas exentas).</summary>
        public bool QuedaVacia => !Exenta && Existentes > 0 && Nuevas == 0;
    }

    /// <summary>
    /// Qué le haría un cargue a los datos actuales de su llave (empresa, año, modo, escenario).
    /// Se calcula siempre en el servidor, nunca con cifras del navegador.
    /// </summary>
    [Serializable]
    public class ImpactoCargue
    {
        public List<ImpactoTabla> Tablas { get; set; } = new List<ImpactoTabla>();
        /// <summary>Porcentaje (1-100) bajo el cual una tabla se considera «reducción fuerte» (exige confirmación reforzada).</summary>
        public int UmbralPct { get; set; } = 50;
        /// <summary>Porcentaje bajo el cual una tabla queda «no permitida»: NADIE puede confirmar la operación (ni siquiera un Super Admin).</summary>
        public int UmbralBloqueoPct { get; set; } = 10;

        public int TotalExistentes => Tablas.Sum(t => t.Existentes);
        public int TotalNuevas => Tablas.Sum(t => t.Nuevas);

        /// <summary>El archivo no trae ninguna fila para este año: confirmarlo solo borraría datos.</summary>
        public bool CargaVacia => TotalNuevas == 0;

        public bool EsReduccionFuerte(ImpactoTabla t) =>
            !t.Exenta && t.Existentes > 0 && (long)t.Nuevas * 100 < (long)t.Existentes * UmbralPct;

        /// <summary>Tablas que perderían mucho (o todo) frente a lo que hay hoy.</summary>
        public List<ImpactoTabla> TablasEnRiesgo => Tablas.Where(EsReduccionFuerte).ToList();

        /// <summary>La tabla tenía datos y quedaría con menos del umbral de bloqueo (incluye quedar vacía): no se permite.</summary>
        public bool EsBloqueada(ImpactoTabla t) =>
            !t.Exenta && t.Existentes > 0 && (long)t.Nuevas * 100 < (long)t.Existentes * UmbralBloqueoPct;

        public List<ImpactoTabla> TablasBloqueadas => Tablas.Where(EsBloqueada).ToList();

        /// <summary>
        /// La operación NO se puede confirmar: dejaría el año sin ninguna fila, o dejaría alguna tabla con menos del umbral de
        /// bloqueo de lo que tiene hoy. No hay forma de forzarla desde la aplicación.
        /// </summary>
        public bool Bloqueado => CargaVacia || TablasBloqueadas.Count > 0;

        /// <summary>Hay datos que proteger y la operación los reduce más de lo permitido sin bloquearla: exige confirmación reforzada.</summary>
        public bool RequiereConfirmacion => !Bloqueado && TablasEnRiesgo.Count > 0;

        /// <summary>Porcentaje de lo actual que conserva el cargue (100 si no había nada).</summary>
        public int PorcentajeConservado => TotalExistentes == 0 ? 100 : (int)Math.Min(999, (long)TotalNuevas * 100 / TotalExistentes);
    }

    /// <summary>Una fila del detalle de auditoría (dbo.CargueDetalleAuditoria).</summary>
    [Serializable]
    public class CargueDetalleFila
    {
        public long Id { get; set; }
        public int? IdAuditoriaCargue { get; set; }
        public string Origen { get; set; }
        public DateTime Fecha { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public int? Anio { get; set; }
        public byte? Modo { get; set; }
        public int? IdEscenario { get; set; }
        public int? IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public string NombreArchivo { get; set; }
        public long? IdLote { get; set; }
        public int? IdHistorial { get; set; }
        public string NombreHoja { get; set; }
        public string NombreTabla { get; set; }
        public int FilasAntes { get; set; }
        public int FilasNuevas { get; set; }
        public string Nota { get; set; }
    }

    public static class CargueOrigen
    {
        public const string Cargue = "Cargue";
        public const string CierreAnio = "CierreAnio";
        public const string Rollback = "Rollback";
    }

    /// <summary>Metadatos de un archivo Excel conservado (sin su contenido).</summary>
    [Serializable]
    public class CargueArchivoInfo
    {
        public long Id { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreArchivo { get; set; }
        public DateTime FechaCarga { get; set; }
        public int TamanoBytes { get; set; }
        public string Sha256 { get; set; }
    }
}
