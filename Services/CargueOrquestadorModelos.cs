using bufinscustomers.Models;
using System.Collections.Generic;
using System.IO;

namespace bufinscustomers.Services
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Modelos de entrada / salida del orquestador. Sin dependencias de HTTP ni de la
    // sesión: los usa por igual la pantalla de Cargue de Excel y, más adelante, la API.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Todo lo necesario para iniciar un cargue desde un archivo Excel.</summary>
    public class SolicitudCargue
    {
        public Usuarios Usuario { get; set; }
        public int IdEmpresa { get; set; }
        public int Anio { get; set; }
        /// <summary>"ejecucion" o "historico".</summary>
        public string Modo { get; set; }
        public int IdEscenario { get; set; } = 1;
        /// <summary>Nombre original del archivo (con o sin ruta).</summary>
        public string NombreArchivo { get; set; }
        public long LongitudArchivo { get; set; }
        public Stream Archivo { get; set; }
    }

    /// <summary>Resultado simple (válido / mensaje) de una validación previa.</summary>
    public class ResultadoCargueSimple
    {
        public bool Ok { get; set; }
        public string Mensaje { get; set; }
        public static ResultadoCargueSimple Correcto() => new ResultadoCargueSimple { Ok = true };
        public static ResultadoCargueSimple Error(string mensaje) => new ResultadoCargueSimple { Ok = false, Mensaje = mensaje };
    }

    public enum ProcesoCargueEstado
    {
        /// <summary>El archivo quedó en staging, validado y listo para revisar/confirmar.</summary>
        EnRevision,
        /// <summary>Ya había un cargue activo para la misma llave: se devuelve su <c>IdLote</c>.</summary>
        LoteActivoExistente,
        PlantillaIncompleta,
        ErrorEnHoja,
        SinFilasDelAnio,
        ErrorValidacion,
        ErrorInesperado
    }

    public class ResultadoProcesoCargue
    {
        public ProcesoCargueEstado Estado { get; set; }
        public long IdLote { get; set; }
        /// <summary>Mensaje listo para el usuario (ya traducido cuando aplica).</summary>
        public string Mensaje { get; set; }
        /// <summary>Detalle por hoja y totales (null cuando no hubo lectura del libro).</summary>
        public ResultadoCargaExcel Resultado { get; set; }
        public string NombreArchivo { get; set; }
        /// <summary>Bitácora de la importación (vacía si no se llegó a leer el libro).</summary>
        public string LogImportacion { get; set; }
    }

    public enum ConfirmacionCargueEstado
    {
        LoteNoExiste,
        SinPermisoPlantilla,
        NoConfirmable,
        ErrorImpacto,
        CargaVacia,
        Bloqueado,
        FaltaConfirmarReemplazo,
        LoteNoDisponible,
        Error,
        Confirmado,
        /// <summary>Entorno de pruebas (sandbox): todas las comprobaciones pasaron y se habría confirmado, pero NO se tocó ningún dato.</summary>
        Simulado
    }

    public class ResultadoConfirmacionCargue
    {
        public ConfirmacionCargueEstado Estado { get; set; }
        public string Mensaje { get; set; }
        public ResultadoCargaExcel Resultado { get; set; }
        public CargueLote Lote { get; set; }
        public bool Confirmado => Estado == ConfirmacionCargueEstado.Confirmado;
    }

    public enum HojaApiEstado
    {
        Ok,
        /// <summary>El lote no existe, no es accesible o ya está confirmado/descartado.</summary>
        LoteNoDisponible,
        HojaNoSoportada,
        ColumnaDesconocida,
        DemasiadasFilas,
        /// <summary>Un valor no se pudo convertir (número, fecha, Sí/No) o la tabla no existe.</summary>
        DatosInvalidos
    }

    public class ResultadoHojaApi
    {
        public HojaApiEstado Estado { get; set; }
        public string Mensaje { get; set; }
        public string Hoja { get; set; }
        public string Tabla { get; set; }
        public int FilasRecibidas { get; set; }
        public int FilasGuardadas { get; set; }
        public int FilasOmitidasPorAnio { get; set; }
        /// <summary>Filas que tiene la hoja en el lote después de esta llamada.</summary>
        public int FilasEnHoja { get; set; }
    }

    public enum ValidacionApiEstado { Ok, LoteNoDisponible, HojasFaltantes, SinFilasDelAnio, ErrorValidacion }

    public class ResultadoValidacionApi
    {
        public ValidacionApiEstado Estado { get; set; }
        public string Mensaje { get; set; }
        public List<string> HojasFaltantes { get; set; } = new List<string>();
        public CargueLote Lote { get; set; }
    }
}
