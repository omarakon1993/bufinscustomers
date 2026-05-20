using System;
using System.Collections.Generic;

namespace bufinscustomers.Models
{
    public class TRMViewModel
    {
        public decimal Valor      { get; set; }
        public string  Fecha      { get; set; }
        public bool    Disponible { get; set; }
    }

    public class NoticiaViewModel
    {
        public string Titulo     { get; set; }
        public string Enlace     { get; set; }
        public string Fuente     { get; set; }
        public string FechaTexto { get; set; }
    }

    public class FeedNoticiaViewModel
    {
        public string                 Fuente   { get; set; }
        public List<NoticiaViewModel> Noticias { get; set; }
    }

    public class IndicadoresFinancierosViewModel
    {
        public TRMViewModel               TRM            { get; set; }
        public List<NoticiaViewModel>     Noticias       { get; set; }
        public List<FeedNoticiaViewModel> Feeds          { get; set; }
        public bool                       RSSConfigurado { get; set; }
    }
}
