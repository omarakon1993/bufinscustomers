using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    /// <summary>Filtros de la auditoría del portal público. Todos opcionales salvo el rango de fechas.</summary>
    public class PortalFiltro
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        /// <summary>m (móvil) | t (tablet) | d (escritorio)</summary>
        public string Dispositivo { get; set; }
        /// <summary>directo | buscador | red-social | correo | otro-sitio | interno</summary>
        public string Origen { get; set; }
        public string Idioma { get; set; }
        /// <summary>Texto libre: se busca en empresa, NIT, sector y detalle del evento.</summary>
        public string Texto { get; set; }
    }

    /// <summary>Una fila genérica de ranking: clave técnica, nombre legible, y uno o dos conteos.</summary>
    public class PortalItem
    {
        public string Clave { get; set; }
        public string Nombre { get; set; }
        public int N { get; set; }
        /// <summary>Segundo conteo (p. ej. visitantes distintos).</summary>
        public int N2 { get; set; }
        public string Extra { get; set; }
    }

    public class PortalKpis
    {
        public int Visitantes { get; set; }
        public double VisitantesPorDia { get; set; }
        public int Visitas { get; set; }
        public int Vistas { get; set; }
        public double PaginasPorVisita { get; set; }
        public int DuracionSeg { get; set; }
        public double RebotePct { get; set; }
        public int Descargas { get; set; }
        public int Contactos { get; set; }
        public int Busquedas { get; set; }
    }

    public class PortalDia
    {
        public string Dia { get; set; }
        public int Vistas { get; set; }
        public int Visitantes { get; set; }
        public int Visitas { get; set; }
    }

    public class PortalTransicion
    {
        public string De { get; set; }
        public string A { get; set; }
        public int N { get; set; }
    }

    public class PortalLectura
    {
        public string Pagina { get; set; }
        public int PermanenciaSeg { get; set; }
        public int ScrollPct { get; set; }
        public int N { get; set; }
    }

    public class PortalBot
    {
        public string Familia { get; set; }
        public int N { get; set; }
        public List<PortalItem> Paginas { get; set; } = new List<PortalItem>();
    }

    public class PortalResultado
    {
        public bool Disponible { get; set; }
        /// <summary>Motivo si no hay datos: SinCarpeta | SinEventos</summary>
        public string Motivo { get; set; }
        public string Desde { get; set; }
        public string Hasta { get; set; }
        public int Dias { get; set; }
        public int Eventos { get; set; }
        public string PrimerEvento { get; set; }
        public PortalKpis Kpis { get; set; } = new PortalKpis();
        public List<PortalDia> Serie { get; set; } = new List<PortalDia>();
        /// <summary>Páginas vistas por día de la semana (lunes=0) y hora (0-23).</summary>
        public int[][] Mapa { get; set; }
        public int VisitantesAhora { get; set; }
        public int VistasAhora { get; set; }

        public List<PortalItem> Empresas { get; set; } = new List<PortalItem>();
        public List<PortalItem> Sectores { get; set; } = new List<PortalItem>();
        public List<PortalItem> Rankings { get; set; } = new List<PortalItem>();
        public List<PortalItem> RankingsSegmento { get; set; } = new List<PortalItem>();
        public List<PortalItem> Paginas { get; set; } = new List<PortalItem>();
        public List<PortalItem> Busquedas { get; set; } = new List<PortalItem>();
        public List<PortalItem> Comparaciones { get; set; } = new List<PortalItem>();
        public List<PortalItem> FiltrosSector { get; set; } = new List<PortalItem>();

        public List<PortalItem> Cta { get; set; } = new List<PortalItem>();
        public List<PortalItem> Exportaciones { get; set; } = new List<PortalItem>();
        public List<PortalItem> OtrasAcciones { get; set; } = new List<PortalItem>();
        public List<PortalItem> Salidas { get; set; } = new List<PortalItem>();

        public List<PortalItem> Origen { get; set; } = new List<PortalItem>();
        public List<PortalItem> Sitios { get; set; } = new List<PortalItem>();
        public List<PortalItem> Campanas { get; set; } = new List<PortalItem>();

        public List<PortalItem> Dispositivos { get; set; } = new List<PortalItem>();
        public List<PortalItem> Navegadores { get; set; } = new List<PortalItem>();
        public List<PortalItem> Sistemas { get; set; } = new List<PortalItem>();
        public List<PortalItem> Idiomas { get; set; } = new List<PortalItem>();
        public List<PortalItem> Zonas { get; set; } = new List<PortalItem>();

        public List<PortalItem> Entradas { get; set; } = new List<PortalItem>();
        public List<PortalItem> SalidasPagina { get; set; } = new List<PortalItem>();
        public List<PortalTransicion> Transiciones { get; set; } = new List<PortalTransicion>();
        public List<PortalItem> Profundidad { get; set; } = new List<PortalItem>();
        public List<PortalItem> Embudo { get; set; } = new List<PortalItem>();
        public List<PortalLectura> Lectura { get; set; } = new List<PortalLectura>();

        public List<PortalBot> Bots { get; set; } = new List<PortalBot>();
        public List<PortalItem> Errores404 { get; set; } = new List<PortalItem>();
        public List<PortalItem> Limites { get; set; } = new List<PortalItem>();
    }
}
