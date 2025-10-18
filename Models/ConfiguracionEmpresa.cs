using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>
    /// Modelo principal de configuraciones empresariales
    /// Representa la configuración básica de una empresa
    /// </summary>
    public class ConfiguracionEmpresa
    {
        public int Id { get; set; }
        public int IdEmpresa { get; set; }
        
        // Configuraciones básicas (selección única)
        public int AnioEjecucion { get; set; }
        public string SignoCreditos { get; set; }
        public string Moneda { get; set; }
        public string Unidad { get; set; }
        
        // Auditoría
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public int? UsuarioModificacion { get; set; }
        
        // Propiedades de navegación (opcional, para mostrar datos relacionados)
        public string NombreEmpresa { get; set; }
        public string NombreUsuarioModificacion { get; set; }
        
        // Listas de configuraciones detalle (se cargarán desde el service)
        public List<ConfigEmpresaConsolidar> EmpresasConsolidar { get; set; }
        public List<ConfigPais> Paises { get; set; }
        public List<ConfigCategoria> Categorias { get; set; }
        public List<ConfigTipo> Tipos { get; set; }
        public List<ConfigLineaNegocio> LineasNegocio { get; set; }
        
        public ConfiguracionEmpresa()
        {
            // Inicializar listas vacías
            EmpresasConsolidar = new List<ConfigEmpresaConsolidar>();
            Paises = new List<ConfigPais>();
            Categorias = new List<ConfigCategoria>();
            Tipos = new List<ConfigTipo>();
            LineasNegocio = new List<ConfigLineaNegocio>();
            
            // Valores por defecto
            AnioEjecucion = DateTime.Now.Year;
            SignoCreditos = string.Empty;
            Moneda = "CO$";
            Unidad = "MILES";
        }
    }
}
