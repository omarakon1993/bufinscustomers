using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Filtros para consultas de informes de relacionamientos BUFINS
    /// </summary>
    public class FiltrosInformeRelacionamientos
    {
        public string NombreTabla { get; set; }
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
        public string CuentaPucCalc { get; set; }
    }

    /// <summary>
    /// Resultado de consulta de informe de relacionamientos
    /// </summary>
    public class ResultadoInformeRelacionamientos
    {
        public List<Dictionary<string, object>> Filas { get; set; }
        public List<string> Columnas { get; set; }
        public Dictionary<string, Type> TiposColumnas { get; set; }
        public int TotalRegistros { get; set; }

        public ResultadoInformeRelacionamientos()
        {
            Filas = new List<Dictionary<string, object>>();
            Columnas = new List<string>();
            TiposColumnas = new Dictionary<string, Type>();
            TotalRegistros = 0;
        }
    }

    /// <summary>
    /// Informacion de una tabla de relacionamiento disponible
    /// </summary>
    public class TablaRelacionamiento
    {
        public string NombreTabla { get; set; }
        public string NombreAmigable { get; set; }
        public string Descripcion { get; set; }
    }
}
