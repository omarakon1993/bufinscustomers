using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Permisos;

namespace bufinscustomers.Controllers
{
    /// <summary>Categoría ya traducida para la vista.</summary>
    public class CategoriaDocumentoVM
    {
        public string Id { get; set; }
        public string Icono { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>
    /// Biblioteca «Documentación BUFINS» (Configuración): reúne la documentación del sistema predefinida en el catálogo
    /// (<see cref="DocumentacionBibliotecaCatalogo"/>), organizada por categoría e indicando si es general o de una opción del menú.
    /// Solo Super Admin. Cada documento se abre en el visor compartido (mismo modal que los informes) y lo sirve
    /// <see cref="DocumentacionProtegidaHelper"/> (marca de agua, sin caché, sin apertura directa, consulta auditada).
    /// </summary>
    [ValidarSesion]
    [SoloSuperAdmin]
    public class DocumentacionBufinsController : BaseController
    {
        public ActionResult Index()
        {
            ViewBag.Categorias = DocumentacionBibliotecaCatalogo.Categorias
                .Select(c => new CategoriaDocumentoVM { Id = c.Id, Icono = c.Icono, Nombre = R("DocBuf_Cat_" + c.Id) }).ToList();
            ViewBag.Disponibles = DocumentacionBibliotecaCatalogo.Todos
                .Where(d => System.IO.File.Exists(Server.MapPath(d.Ruta))).Select(d => d.Clave).ToList();
            return View("~/Views/Configuracion/DocumentacionBufins.cshtml", DocumentacionBibliotecaCatalogo.Todos);
        }

        [HttpGet]
        public ActionResult Ver(string clave)
        {
            var doc = DocumentacionBibliotecaCatalogo.Buscar(clave);
            if (doc == null) return HttpNotFound();

            return DocumentacionProtegidaHelper.Servir(this, doc.Ruta,
                R("Doc_MarcaLeyenda"), R("Doc_Aviso"), "DocumentacionBufins." + doc.Clave,
                "Documentación BUFINS: " + doc.Titulo, "Consultó la documentación: " + doc.Titulo + " (biblioteca Documentación BUFINS).");
        }
    }
}
