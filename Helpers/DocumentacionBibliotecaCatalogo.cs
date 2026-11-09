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
        /// <summary>Título, descripción y menú en inglés (si faltan se muestra el español).</summary>
        public string TituloEn;
        public string DescripcionEn;
        public string MenuEn;
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
    /// Agregar un documento = guardar el HTML en la subcarpeta de su categoría de <c>App_Data/Documentacion/</c> (Seguridad, Informes, Cargues…), registrarlo en el .csproj como Content y agregar UNA línea aquí. Lo sirve
    /// <c>DocumentacionBufinsController.Ver</c> con la misma protección que la documentación de los informes
    /// (marca de agua, sin caché, rechazo de apertura directa y auditoría de cada consulta).
    /// </summary>
    public static class DocumentacionBibliotecaCatalogo
    {
        private const string Seguridad = "~/App_Data/Documentacion/Seguridad/";
        private const string Informes = "~/App_Data/Documentacion/Informes/";
        private const string Cargues = "~/App_Data/Documentacion/Cargues/";

        public static readonly List<CategoriaDocumento> Categorias = new List<CategoriaDocumento>
        {
            new CategoriaDocumento { Id = "seguridad",     Icono = "fas fa-shield-alt" },
            new CategoriaDocumento { Id = "informes",      Icono = "fas fa-chart-line" },
            new CategoriaDocumento { Id = "datos",         Icono = "fas fa-file-upload" },
        };

        public static readonly List<DocumentoBiblioteca> Todos = new List<DocumentoBiblioteca>
        {
            new DocumentoBiblioteca { Clave = "seguridad-blindaje", Ruta = Seguridad + "Seguridad_Blindaje_Inicio_Sesion.html", Categoria = "seguridad",
                Titulo = "Blindaje del inicio de sesión · Doble factor",
                Descripcion = "Cómo se protege el acceso: doble factor con app autenticadora y con passkeys, recuperación por solicitud aprobada por un Super Admin, administración y decisiones de diseño.",
                TituloEn = "Login hardening · Two-factor authentication", DescripcionEn = "How access is protected: two-factor with an authenticator app and passkeys, recovery through a request approved by a Super Admin, administration and design decisions.", MenuEn = "",
                EsDeMenu = false, Menu = "", Actualizado = new DateTime(2026, 10, 15) },

            new DocumentoBiblioteca { Clave = "seguridad-ciberseguridad", Ruta = Seguridad + "Ciberseguridad_BUFINS.html", Categoria = "seguridad",
                Titulo = "Documentación oficial de ciberseguridad",
                Descripcion = "Estado de la seguridad de BUFINS: qué controles están implementados, los hallazgos de la revisión con lo ya corregido y lo pendiente, y la hoja de ruta.",
                TituloEn = "Official cybersecurity documentation", DescripcionEn = "The state of BUFINS security: which controls are in place, the findings of the review with what is already fixed and what is pending, and the roadmap.", MenuEn = "",
                EsDeMenu = false, Menu = "", Actualizado = new DateTime(2026, 10, 16) },

            new DocumentoBiblioteca { Clave = "informe-indicadores", Ruta = Informes + "Informe_Indicadores.html", Categoria = "informes",
                Titulo = "Informe de indicadores",
                Descripcion = "Qué es, de dónde salen las cifras, cómo se calcula cada uno de los 27 indicadores y cómo leer el semáforo.",
                TituloEn = "Indicators report", DescripcionEn = "What it is, where the figures come from, how each of the 27 indicators is calculated and how to read the traffic light.", MenuEn = "Reports → Indicators report",
                EsDeMenu = true, Menu = "Informes → Informe de indicadores", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "informe-hist-precios", Ruta = Informes + "Informe_HistPrecios_vs_Excel.html", Categoria = "informes",
                Titulo = "Informe histórico de precios",
                Descripcion = "Informe personalizado para Churido y Ucrania: de dónde salen los precios, cómo se compara con el Excel y cómo se calculan las variaciones.",
                TituloEn = "Price history report", DescripcionEn = "Custom report for Churido and Ucrania: where the prices come from, how it compares with the Excel file and how the variations are calculated.", MenuEn = "Reports → Price history (custom)",
                EsDeMenu = true, Menu = "Informes → Histórico de precios (personalizado)", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "informe-lineas-negocio", Ruta = Informes + "Informe_LineasNegocio.html", Categoria = "informes",
                Titulo = "Rentabilidad por línea de negocio",
                Descripcion = "Qué es, de dónde salen las cifras por línea, cómo se calcula cada concepto y cómo se valida contra el Estado de resultados.",
                TituloEn = "Business line profitability", DescripcionEn = "What it is, where the figures by line come from, how each item is calculated and how it is validated against the Income statement.", MenuEn = "Reports → Business line profitability",
                EsDeMenu = true, Menu = "Informes → Rentabilidad por línea de negocio", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "informe-gerencia-mensual", Ruta = Informes + "Informe_GerenciaMensual.html", Categoria = "informes",
                Titulo = "Informe mensual de gerencia",
                Descripcion = "El cierre del mes en un solo documento: de qué informe sale cada sección, cómo se lee y cómo se exporta.",
                EsDeMenu = true, Menu = "Informes → Informe mensual de gerencia", Actualizado = new DateTime(2026, 10, 27) },

            new DocumentoBiblioteca { Clave = "cargues-auditoria", Ruta = Cargues + "Auditoria_Cargues_Blindaje.html", Categoria = "datos",
                Titulo = "Historial de cargues y protección de los datos",
                Descripcion = "Guía de uso: cómo Bufins protege los datos al cargar un Excel, cómo leer las pestañas Versiones y Registro y cómo deshacer un cargue.",
                EsDeMenu = true, Menu = "Datos → Historial de cargues", Actualizado = new DateTime(2026, 10, 17) },


            new DocumentoBiblioteca { Clave = "informe-pyg", Ruta = Informes + "Informe_PYG.html", Categoria = "informes",
                Titulo = "Estado de resultados (PYG)", TituloEn = "Income statement (P&L)",
                Descripcion = "Qué es, de dónde salen las cifras, cómo se calculan los márgenes y el EBITDA, cómo se compara contra el presupuesto y cómo leer la cascada.",
                DescripcionEn = "What it is, where the figures come from, how margins and EBITDA are calculated, how it compares with the budget and how to read the waterfall.",
                EsDeMenu = true, Menu = "Informes → Estado de resultados (PYG)", MenuEn = "Reports → Income statement (P&L)", Actualizado = new DateTime(2026, 11, 8) },

            new DocumentoBiblioteca { Clave = "informe-balance", Ruta = Informes + "Informe_Balance.html", Categoria = "informes",
                Titulo = "Balance general", TituloEn = "Balance sheet",
                Descripcion = "Qué es, por qué es una foto al mes de corte, de dónde salen las cifras, cómo se calculan los indicadores y cómo se comprueba que el balance cuadra.",
                DescripcionEn = "What it is, why it is a snapshot at the cut-off month, where the figures come from, how the indicators are calculated and how the balance check works.",
                EsDeMenu = true, Menu = "Informes → Balance general", MenuEn = "Reports → Balance sheet", Actualizado = new DateTime(2026, 11, 8) },

            new DocumentoBiblioteca { Clave = "informe-tesoreria", Ruta = Informes + "Informe_Tesoreria.html", Categoria = "informes",
                Titulo = "Tesorería del presupuesto", TituloEn = "Budget treasury",
                Descripcion = "Si la caja del presupuesto alcanza, en qué meses falta, cuánta financiación se necesita y qué rubros lo explican.",
                DescripcionEn = "Whether the budget cash is enough, in which months it falls short, how much funding is needed and which items explain it.",
                EsDeMenu = true, Menu = "Informes → Tesorería del presupuesto", MenuEn = "Reports → Budget treasury", Actualizado = new DateTime(2026, 11, 8) },

            new DocumentoBiblioteca { Clave = "informe-flujo-caja", Ruta = Informes + "Informe_FlujoCaja.html", Categoria = "informes",
                Titulo = "Flujo de caja", TituloEn = "Cash flow",
                Descripcion = "La vista gerencial de la caja: de la utilidad operacional al flujo neto, la conversión a caja y la validación contra el balance.",
                DescripcionEn = "The management view of cash: from operating profit to net flow, cash conversion and validation against the balance sheet.",
                EsDeMenu = true, Menu = "Informes → Flujo de caja", MenuEn = "Reports → Cash flow", Actualizado = new DateTime(2026, 11, 8) },

            new DocumentoBiblioteca { Clave = "informe-flujo-efectivo", Ruta = Informes + "Informe_FlujoEfectivo.html", Categoria = "informes",
                Titulo = "Flujo de efectivo", TituloEn = "Statement of cash flows",
                Descripcion = "El estado de flujo de efectivo: operación, inversión y financiación, y cómo concilia con la caja del balance.",
                DescripcionEn = "The cash flow statement: operating, investing and financing activities, and how it reconciles with the balance sheet cash.",
                EsDeMenu = true, Menu = "Informes → Flujo de efectivo", MenuEn = "Reports → Statement of cash flows", Actualizado = new DateTime(2026, 11, 8) },

            new DocumentoBiblioteca { Clave = "informe-linea-tiempo", Ruta = Informes + "Informe_LineaTiempo.html", Categoria = "informes",
                Titulo = "Línea de tiempo dinámica", TituloEn = "Dynamic timeline",
                Descripcion = "Cómo elegir cualquier variable de los modelos, ver su evolución mes a mes, compararla entre escenarios y exportar el resultado.",
                DescripcionEn = "How to pick any variable from the models, see its month-by-month evolution, compare it across scenarios and export the result.",
                EsDeMenu = true, Menu = "Informes → Línea de tiempo", MenuEn = "Reports → Timeline", Actualizado = new DateTime(2026, 11, 8) },

            new DocumentoBiblioteca { Clave = "informe-modelos", Ruta = Informes + "Informe_Modelos.html", Categoria = "informes",
                Titulo = "Informe de modelos", TituloEn = "Models report",
                Descripcion = "Cómo consultar, filtrar y exportar las tablas que genera la ejecución de modelos.",
                DescripcionEn = "How to query, filter and export the tables produced by running the models.",
                EsDeMenu = true, Menu = "Informes → Informe de modelos", MenuEn = "Reports → Models report", Actualizado = new DateTime(2026, 11, 8) },
        };

        public static DocumentoBiblioteca Buscar(string clave)
        {
            return Todos.FirstOrDefault(d => string.Equals(d.Clave, clave, StringComparison.Ordinal));
        }
    }
}
