using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Una hoja del cargue de Excel (dbo.CargueHojas): hoja <c>Z_X</c> → tabla <c>Ini_X</c>
    /// (+ su espejo <c>Staging_Ini_X</c>). Administrada por el Gestor de hojas de cargue.
    /// </summary>
    public class HojaCargue
    {
        public const string AlcanceEstandar = "ESTANDAR";
        public const string AlcancePersonalizada = "PERSONALIZADA";
        public const string OrigenExistente = "EXISTENTE";
        public const string OrigenGestor = "GESTOR";

        public int IdHoja { get; set; }
        public string NombreHoja { get; set; }
        public string TablaIni { get; set; }
        public string Descripcion { get; set; }
        public string Alcance { get; set; } = AlcanceEstandar;
        public bool Activo { get; set; } = true;
        public int Orden { get; set; }
        public string Origen { get; set; } = OrigenGestor;
        public DateTime FechaCreacion { get; set; }
        /// <summary>Desde cuándo aplica con su alcance actual (base de la regla de rollback).</summary>
        public DateTime VigenteDesde { get; set; }
        public DateTime? FechaModificacion { get; set; }

        /// <summary>Empresas asignadas (activas) → fecha de asignación. Solo aplica a las personalizadas.</summary>
        public Dictionary<int, DateTime> Empresas { get; set; } = new Dictionary<int, DateTime>();

        public bool EsEstandar => string.Equals(Alcance, AlcanceEstandar, StringComparison.OrdinalIgnoreCase);
        public bool EsPersonalizada => !EsEstandar;
        public string TablaStaging => "Staging_" + TablaIni;

        /// <summary>True si la hoja se exige y se procesa en el cargue de la empresa.</summary>
        public bool AplicaA(int idEmpresa) => Activo && (EsEstandar || Empresas.ContainsKey(idEmpresa));

        /// <summary>
        /// Desde cuándo la hoja aplica a la empresa (la más reciente entre su vigencia y la asignación).
        /// El rollback exige el snapshot de su tabla solo en versiones cargadas después de esta fecha.
        /// </summary>
        public DateTime VigenteDesdePara(int idEmpresa)
        {
            if (EsEstandar || !Empresas.TryGetValue(idEmpresa, out var asignada)) return VigenteDesde;
            return asignada > VigenteDesde ? asignada : VigenteDesde;
        }    }

    /// <summary>
    /// Columna de una tabla Ini_ (leída del esquema físico). Las de negocio son los encabezados del
    /// Excel; las de sistema (<see cref="EsSistema"/>) las llena el cargue y no se editan en el gestor.
    /// </summary>
    public class ColumnaHoja
    {
        public string Nombre { get; set; }
        /// <summary>Tipo SQL en minúsculas: varchar, nvarchar, int, bigint, decimal, money, date, datetime, bit…</summary>
        public string Tipo { get; set; }
        /// <summary>Longitud para (n)varchar; -1 = MAX.</summary>
        public int? Longitud { get; set; }
        public int? Precision { get; set; }
        public int? Escala { get; set; }
        public bool PermiteNulos { get; set; } = true;
        /// <summary>Valor por defecto tal como lo escribe el usuario (sin paréntesis ni comillas de T-SQL).</summary>
        public string ValorDefecto { get; set; }
        public int Orden { get; set; }
        public bool EsSistema { get; set; }

        /// <summary>Tipo legible: VARCHAR(100), DECIMAL(18,2), NVARCHAR(MAX)…</summary>
        public string TipoCompleto
        {
            get
            {
                string t = (Tipo ?? "").ToUpperInvariant();
                if ((t == "VARCHAR" || t == "NVARCHAR") && Longitud.HasValue)
                    return $"{t}({(Longitud.Value < 0 ? "MAX" : Longitud.Value.ToString())})";
                if ((t == "DECIMAL" || t == "NUMERIC") && Precision.HasValue)
                    return $"{t}({Precision},{Escala ?? 0})";
                return t;
            }
        }
    }

    /// <summary>Detalle completo de una hoja para el panel del gestor.</summary>
    public class HojaCargueDetalle
    {
        public HojaCargue Hoja { get; set; }
        public List<ColumnaHoja> Columnas { get; set; } = new List<ColumnaHoja>();
        public List<ColumnaHoja> ColumnasSistema { get; set; } = new List<ColumnaHoja>();
        public bool ExisteIni { get; set; }
        public bool ExisteStaging { get; set; }
        public int FilasIni { get; set; }
        public int FilasStaging { get; set; }
        /// <summary>Filas de la tabla Ini_ por empresa (IdEmpresa_Log → filas).</summary>
        public Dictionary<int, int> FilasPorEmpresa { get; set; } = new Dictionary<int, int>();
        /// <summary>Procedimientos/vistas/funciones de la BD que mencionan la tabla.</summary>
        public List<string> UsadaPor { get; set; } = new List<string>();
        public List<HallazgoDiagnostico> Diagnostico { get; set; } = new List<HallazgoDiagnostico>();
        /// <summary>
        /// Columnas existentes modificables/eliminables: tablas vacías y sin módulos SQL que las usen
        /// (p. ej. sp_ValidarCargueStaging nombra columnas de las Staging_Ini_ estándar). Agregar
        /// columnas siempre se permite.
        /// </summary>
        public bool EstructuraEditable => FilasIni == 0 && FilasStaging == 0 && UsadaPor.Count == 0;
    }

    /// <summary>Resultado de "Verificar estructura" de una hoja.</summary>
    public class HallazgoDiagnostico
    {
        /// <summary>"ok" | "warn" | "error".</summary>
        public string Nivel { get; set; }
        /// <summary>Código estable (se traduce en la vista con la clave HojasCargue_Diag_{Codigo}).</summary>
        public string Codigo { get; set; }
        /// <summary>Dato variable del mensaje (columna, tabla…).</summary>
        public string Detalle { get; set; }
    }

    /// <summary>Datos que envía el gestor al crear una hoja.</summary>
    public class HojaCargueNueva
    {
        public string NombreHoja { get; set; }
        public string TablaIni { get; set; }
        public string Descripcion { get; set; }
        public string Alcance { get; set; }
        public int Orden { get; set; }
        public List<int> Empresas { get; set; } = new List<int>();
        public List<ColumnaHoja> Columnas { get; set; } = new List<ColumnaHoja>();
    }
}
