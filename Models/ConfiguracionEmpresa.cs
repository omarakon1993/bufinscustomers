using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo principal de configuraciones empresariales
    /// Representa la configuraci�n b�sica de una empresa
    /// </summary>
    public class ConfiguracionEmpresa
    {
        public int Id { get; set; }
        public int IdEmpresa { get; set; }
        
        // Configuraciones b�sicas (selecci�n �nica)
        public int AnioEjecucion { get; set; }
        public string SignoCreditos { get; set; }
        public string Moneda { get; set; }
        public string Unidad { get; set; }
        
        // Auditor�a
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public int? UsuarioModificacion { get; set; }
        
        // Propiedades de navegaci�n (opcional, para mostrar datos relacionados)
        public string NombreEmpresa { get; set; }
        public string NombreUsuarioModificacion { get; set; }
        
        // Listas de configuraciones detalle (se cargar�n desde el service)
        public List<ConfigEmpresaConsolidar> EmpresasConsolidar { get; set; }
        public List<ConfigPais> Paises { get; set; }
        public List<ConfigCategoria> Categorias { get; set; }
        public List<ConfigTipo> Tipos { get; set; }
        public List<ConfigLineaNegocio> LineasNegocio { get; set; }
        public List<ConfigAjuste1> Ajuste1 { get; set; }
        public List<ConfigAjuste2> Ajuste2 { get; set; }

        public ConfiguracionEmpresa()
        {
            // Inicializar listas vac�as
            EmpresasConsolidar = new List<ConfigEmpresaConsolidar>();
            Paises = new List<ConfigPais>();
            Categorias = new List<ConfigCategoria>();
            Tipos = new List<ConfigTipo>();
            LineasNegocio = new List<ConfigLineaNegocio>();
            Ajuste1 = new List<ConfigAjuste1>();
            Ajuste2 = new List<ConfigAjuste2>();

            // Valores por defecto
            AnioEjecucion = DateTime.Now.Year;
            SignoCreditos = string.Empty;
            Moneda = "CO$";
            Unidad = "MILES";
        }
    }
}
