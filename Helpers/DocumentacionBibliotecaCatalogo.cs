using System;
using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Helpers
{
    /// <summary>Categoría con la que se agrupan los documentos de la biblioteca «Documentación BUFINS».</summary>
    public class CategoriaDocumento
    {
        /// <summary>Id corto; el nombre traducido está en el recurso <c>DocBuf_Cat_{Id}</c> de ambos .resx.</summary>
        public string Id;
        public string Icono;
    }

    /// <summary>Un documento de la biblioteca «Documentación BUFINS» (solo Super Admin).</summary>
    public class DocumentoBiblioteca
    {
        /// <summary>Clave pública corta que viaja en la URL. Solo se aceptan las del catálogo.</summary>
        public string Clave;
        /// <summary>Ruta virtual del HTML (en App_Data, nunca pública). Nunca viene del cliente.</summary>
        public string Ruta;
        public string Titulo;
        public string Descripcion;
        /// <summary>Id de <see cref="CategoriaDocumento"/>.</summary>
        public string Categoria;
        /// <summary>true = documenta una opción concreta del menú (<see cref="Menu"/>); false = documento general.</summary>
        public bool EsDeMenu;
        /// <summary>Opción(es) del menú a la que se refiere, p. ej. "Informes → Informe de indicadores". Vacío en los generales.</summary>
        public string Menu;
        /// <summary>Fecha de la última actualización del contenido (informativa).</summary>
        public DateTime Actualizado;
    }

    /// <summary>
    /// SOLO contiene la documentación oficial: la de las opciones que ya tienen documentación y la que el responsable pida
    /// agregar explícitamente (no se agrega nada por iniciativa propia). Catálogo ÚNICO de la biblioteca «Documentación BUFINS» (Configuración → Documentación BUFINS, solo Super Admin).
    /// Agregar un documento = guardar el HTML en <c>App_Data/Documentacion/Biblioteca/</c> (o reutilizar uno de
    /// <c>App_Data/Documentacion/</c>), registrarlo en el .csproj como Content y agregar UNA línea aquí. Lo sirve
    /// <c>DocumentacionBufinsController.Ver</c> con la misma protección que la documentación de los informes
    /// (marca de agua, sin caché, rechazo de apertura directa y auditoría de cada consulta).
    /// </summary>
    public static class DocumentacionBibliotecaCatalogo
    {
        private const string Biblioteca = "~/App_Data/Documentacion/Biblioteca/";
        private const string Informes = "~/App_Data/Documentacion/";

        public static readonly List<CategoriaDocumento> Categorias = new List<CategoriaDocumento>
        {
            new CategoriaDocumento { Id = "seguridad",     Icono = "fas fa-shield-alt" },
            new CategoriaDocumento { Id = "informes",      Icono = "fas fa-chart-line" },
            new CategoriaDocumento { Id = "datos",         Icono = "fas fa-file-upload" },
        };

        public static readonly List<DocumentoBiblioteca> Todos = new List<DocumentoBiblioteca>
        {
            new DocumentoBiblioteca { Clave = "seguridad-blindaje", Ruta = Biblioteca + "Seguridad_Blindaje_Inicio_Sesion.html", Categoria = "seguridad",
                Titulo = "Blindaje del inicio de sesión · Doble factor",
                Descripcion = "Cómo se protege el acceso: doble factor con app autenticadora y con passkeys, recuperación por solicitud aprobada por un Super Admin, administración y decisiones de diseño.",
                EsDeMenu = false, Menu = "", Actualizado = new DateTime(2026, 10, 15) },

            new DocumentoBiblioteca { Clave = "seguridad-ciberseguridad", Ruta = Biblioteca + "Ciberseguridad_BUFINS.html", Categoria = "seguridad",
                Titulo = "Documentación oficial de ciberseguridad",
                Descripcion = "Estado de la seguridad de BUFINS: qué controles están implementados, los hallazgos de la revisión con lo ya corregido y lo pendiente, y la hoja de ruta.",
                EsDeMenu = false, Menu = "", Actualizado = new DateTime(2026, 10, 16) },

            new DocumentoBiblioteca { Clave = "informe-indicadores", Ruta = Informes + "Informe_Indicadores.html", Categoria = "informes",
                Titulo = "Informe de indicadores",
                Descripcion = "Qué es, de dónde salen las cifras, cómo se calcula cada uno de los 27 indicadores y cómo leer el semáforo.",
                EsDeMenu = true, Menu = "Informes → Informe de indicadores", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "informe-hist-precios", Ruta = Informes + "Informe_HistPrecios_vs_Excel.html", Categoria = "informes",
                Titulo = "Informe histórico de precios",
                Descripcion = "Informe personalizado para Churido y Ucrania: de dónde salen los precios, cómo se compara con el Excel y cómo se calculan las variaciones.",
                EsDeMenu = true, Menu = "Informes → Histórico de precios (personalizado)", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "informe-lineas-negocio", Ruta = Informes + "Informe_LineasNegocio.html", Categoria = "informes",
                Titulo = "Rentabilidad por línea de negocio",
                Descripcion = "Qué es, de dónde salen las cifras por línea, cómo se calcula cada concepto y cómo se valida contra el Estado de resultados.",
                EsDeMenu = true, Menu = "Informes → Rentabilidad por línea de negocio", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "informe-gerencia-mensual", Ruta = Informes + "Informe_GerenciaMensual.html", Categoria = "informes",
                Titulo = "Informe mensual de gerencia",
                Descripcion = "El cierre del mes en un solo documento: de qué informe sale cada sección, cómo se lee y cómo se exporta.",
                EsDeMenu = true, Menu = "Informes → Informe mensual de gerencia", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "cargues-auditoria", Ruta = Biblioteca + "Auditoria_Cargues_Blindaje.html", Categoria = "datos",
                Titulo = "Historial de cargues y protección de los datos",
                Descripcion = "Guía de uso: cómo Bufins protege los datos al cargar un Excel, cómo leer las pestañas Versiones y Registro y cómo deshacer un cargue.",
                EsDeMenu = true, Menu = "Datos → Historial de cargues", Actualizado = new DateTime(2026, 10, 17) },

        };

        public static DocumentoBiblioteca Buscar(string clave)
        {
            return Todos.FirstOrDefault(d => string.Equals(d.Clave, clave, StringComparison.Ordinal));
        }
    }
}
