using System;
using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Helpers
{
    /// <summary>Un documento de la documentación protegida de un informe (ver DocumentacionController).</summary>
    public class DocumentoInforme
    {
        /// <summary>Clave pública corta que viaja en la URL (p. ej. "indicadores"). Solo se aceptan las del catálogo.</summary>
        public string Clave;
        /// <summary>Archivo dentro de App_Data/Documentacion (nunca viene del cliente).</summary>
        public string Archivo;
        /// <summary>Código de permiso del informe: solo quien lo tiene (o el Super Admin) puede abrir su documentación.</summary>
        public string Permiso;
        /// <summary>Nombre que se registra en la auditoría de consultas.</summary>
        public string Titulo;
    }

    /// <summary>
    /// Catálogo ÚNICO de la documentación de informes. Agregar la documentación de un informe nuevo = guardar el HTML en
    /// App_Data/Documentacion (convención <c>Informe_&lt;Nombre&gt;.html</c>) y agregar UNA línea aquí; el servidor (marca de agua,
    /// permiso, auditoría, sin caché) y el visor (cápsula "Documentación") ya son los compartidos. Ver CLAUDE.md,
    /// "Documentación de informes — ESTÁNDAR".
    /// </summary>
    public static class DocumentacionCatalogo
    {
        public const string Carpeta = "~/App_Data/Documentacion/";

        public static readonly List<DocumentoInforme> Todos = new List<DocumentoInforme>
        {
            new DocumentoInforme { Clave = "hist-precios", Archivo = "Informe_HistPrecios_vs_Excel.html", Permiso = "INFORMES_HIST_PRECIOS", Titulo = "Informe histórico de precios" },
            new DocumentoInforme { Clave = "indicadores", Archivo = "Informe_Indicadores.html", Permiso = "INFORMES_INDICADORES", Titulo = "Informe de indicadores" },
        };

        public static DocumentoInforme Buscar(string clave)
        {
            return Todos.FirstOrDefault(d => string.Equals(d.Clave, clave, StringComparison.Ordinal));
        }
    }
}
