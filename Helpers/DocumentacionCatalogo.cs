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
        /// <summary>Clave del mismo documento en la biblioteca «Documentación BUFINS» (ahí se decide si es solo para Super Admin).</summary>
        public string BibliotecaClave;
        /// <summary>Archivo dentro de App_Data/Documentacion, con su subcarpeta de categoría (p. ej. "Informes/Informe_X.html"; nunca viene del cliente).</summary>
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
            new DocumentoInforme { Clave = "hist-precios", BibliotecaClave = "informe-hist-precios", Archivo = "Informes/Informe_HistPrecios_vs_Excel.html", Permiso = "INFORMES_HIST_PRECIOS", Titulo = "Informe histórico de precios" },
            new DocumentoInforme { Clave = "indicadores", BibliotecaClave = "informe-indicadores", Archivo = "Informes/Informe_Indicadores.html", Permiso = "INFORMES_INDICADORES", Titulo = "Informe de indicadores" },
            new DocumentoInforme { Clave = "lineas-negocio", BibliotecaClave = "informe-lineas-negocio", Archivo = "Informes/Informe_LineasNegocio.html", Permiso = "INFORMES_LINEAS_NEGOCIO", Titulo = "Rentabilidad por línea de negocio" },
            new DocumentoInforme { Clave = "pyg", BibliotecaClave = "informe-pyg", Archivo = "Informes/Informe_PYG.html", Permiso = "", Titulo = "Estado de resultados (PYG)" },
            new DocumentoInforme { Clave = "balance", BibliotecaClave = "informe-balance", Archivo = "Informes/Informe_Balance.html", Permiso = "", Titulo = "Balance general" },
            new DocumentoInforme { Clave = "tesoreria", BibliotecaClave = "informe-tesoreria", Archivo = "Informes/Informe_Tesoreria.html", Permiso = "INFORMES_TESORERIA_PPTO", Titulo = "Tesorería del presupuesto" },
            new DocumentoInforme { Clave = "flujo-caja", BibliotecaClave = "informe-flujo-caja", Archivo = "Informes/Informe_FlujoCaja.html", Permiso = "INFORMES_FLUJO_CAJA", Titulo = "Flujo de caja" },
            new DocumentoInforme { Clave = "flujo-efectivo", BibliotecaClave = "informe-flujo-efectivo", Archivo = "Informes/Informe_FlujoEfectivo.html", Permiso = "INFORMES_FLUJO_EFECTIVO", Titulo = "Flujo de efectivo" },
            new DocumentoInforme { Clave = "linea-tiempo", BibliotecaClave = "informe-linea-tiempo", Archivo = "Informes/Informe_LineaTiempo.html", Permiso = "", Titulo = "Línea de tiempo dinámica" },
            new DocumentoInforme { Clave = "modelos", BibliotecaClave = "informe-modelos", Archivo = "Informes/Informe_Modelos.html", Permiso = "", Titulo = "Informe de modelos" },
            new DocumentoInforme { Clave = "gerencia-mensual", BibliotecaClave = "informe-gerencia-mensual", Archivo = "Informes/Informe_GerenciaMensual.html", Permiso = "INFORMES_GERENCIA_MENSUAL", Titulo = "Informe mensual de gerencia" },
        };

        /// <summary>true si el usuario puede ver la documentación del informe: no está marcada como solo Super Admin en la biblioteca (o es Super Admin).</summary>
        public static bool PuedeVer(string claveInforme, bufinscustomers.Models.Usuarios usuario)
        {
            var d = Buscar(claveInforme);
            return d == null || bufinscustomers.Services.DocumentacionAccesoService.PuedeVer(d.BibliotecaClave, usuario);
        }

        public static DocumentoInforme Buscar(string clave)
        {
            return Todos.FirstOrDefault(d => string.Equals(d.Clave, clave, StringComparison.Ordinal));
        }
    }
}
