using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class HistorialVersiones
    {
        public int Id { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public int Anio { get; set; }
        public byte Modo { get; set; }
        public string ModoTexto => Modo == 0 ? "Ejecución" : "Histórico";
        public int IdEscenario { get; set; } = 1;
        public DateTime FechaCargue { get; set; }
        public int IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public string NombreArchivo { get; set; }
        public int TotalFilas { get; set; }
        public bool EsVersionActual { get; set; }
        public DateTime? FechaReversion { get; set; }
        public int? IdUsuarioReversion { get; set; }
        public string NombreUsuarioReversion { get; set; }
        public string EstadoDisplay { get; set; }
        public int NumeroVersionMes { get; set; }
        public string VersionDisplay => $"{Anio}.{FechaCargue.Month:D2}.{NumeroVersionMes:D3}";
    }

    /// <summary>
    /// Copia automática de los datos que una restauración iba a reemplazar (dbo.RespaldosRestauracion, Sql/034).
    /// Permite recuperarlos si la restauración fue un error. No incluye el contenido (solo metadatos).
    /// </summary>
    public class RespaldoRestauracion
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int IdEmpresa { get; set; }
        public string NombreEmpresa { get; set; }
        public int Anio { get; set; }
        public byte Modo { get; set; }
        public int IdEscenario { get; set; } = 1;
        public int? IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        /// <summary>Versión que se restauró; null si el respaldo se tomó al recuperar otro respaldo.</summary>
        public int? IdHistorialRestaurado { get; set; }
        public int TotalFilas { get; set; }
    }

    public class HistorialVersionesPageViewModel
    {
        public List<HistorialVersiones> Versiones { get; set; } = new List<HistorialVersiones>();
        public List<RespaldoRestauracion> Respaldos { get; set; } = new List<RespaldoRestauracion>();
        public List<Empresas> Empresas { get; set; } = new List<Empresas>();
        public int? IdEmpresaFiltro { get; set; }
        public int? AnioFiltro { get; set; }
        public byte? ModoFiltro { get; set; }
        public int? EscenarioFiltro { get; set; }
        public int MaxVersiones { get; set; } = 3;
    }
}
