namespace bufinscustomers.Models
{
    /// <summary>
    /// Parámetros del botón "Exportar" desplegable (parcial <c>Views/Shared/_ExportMenu.cshtml</c>). Todo es opcional
    /// salvo <see cref="Id"/>: lo que no se indique usa los textos genéricos <c>Common_Exp_*</c>. Los textos llegan ya
    /// traducidos (el modelo no lee recursos).
    /// </summary>
    public class ExportMenuVM
    {
        /// <summary>Id del contenedor (el mismo que se pasa a <c>ExportMenu.init</c>).</summary>
        public string Id { get; set; }

        /// <summary>Opciones visibles, separadas por coma: excel, pdf, imagen, correo (por defecto las cuatro).</summary>
        public string Opciones { get; set; }

        public string BtnTexto { get; set; }
        public string BtnTooltip { get; set; }
        public string MenuTitulo { get; set; }

        public string ExcelDesc { get; set; }
        public string PdfDesc { get; set; }
        public string ImagenDesc { get; set; }
        public string CorreoDesc { get; set; }
    }
}

namespace bufinscustomers.Models
{
    /// <summary>Textos opcionales del modal de correo compartido (<c>Views/Shared/_ExportCorreoModal.cshtml</c>); lo no indicado usa los textos genéricos.</summary>
    public class ExportCorreoVM
    {
        /// <summary>Título del modal (por defecto "Enviar informe por correo").</summary>
        public string Titulo { get; set; }
        /// <summary>Nota de adjuntos (por defecto "Se adjuntan el Excel y el PDF del informe.").</summary>
        public string Adjuntos { get; set; }
        /// <summary>true = el informe no tiene PDF (solo se adjunta el Excel): cambia la nota y oculta el icono de PDF.</summary>
        public bool SoloExcel { get; set; }
    }
}
