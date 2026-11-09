using System.Linq;
using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using bufinscustomers.Models;

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
    /// Entra el Super Admin y, si se les asigna la opción de menú (ver Sql/044), quienes tengan el permiso ADMIN_DOCUMENTACION_BUFINS: ese
    /// usuario solo ve los documentos que el Super Admin NO marcó como «solo Super Admin». El Super Admin ve todo y decide la marca desde aquí
    /// (<see cref="GuardarAcceso"/>). Cada documento se abre en el visor compartido (mismo modal que los informes) y lo sirve
    /// <see cref="DocumentacionProtegidaHelper"/> (marca de agua, sin caché, sin apertura directa, consulta auditada).
    /// </summary>
    [ValidarSesion]
    [RequierePermiso("ADMIN_DOCUMENTACION_BUFINS")]
    public class DocumentacionBufinsController : BaseController
    {
        public ActionResult Index()
        {
            ViewBag.Categorias = DocumentacionBibliotecaCatalogo.Categorias
                .Select(c => new CategoriaDocumentoVM { Id = c.Id, Icono = c.Icono, Nombre = R("DocBuf_Cat_" + c.Id) }).ToList();
            var usuario = UsuarioSesionHelper.UsuarioActual;
            bool esSuper = UsuarioSesionHelper.EsSuperAdmin();
            var visibles = DocumentacionBibliotecaCatalogo.Todos.Where(d => DocumentacionAccesoService.PuedeVer(d.Clave, usuario)).ToList();
            ViewBag.Disponibles = visibles
                .Where(d => System.IO.File.Exists(Server.MapPath(d.Ruta))).Select(d => d.Clave).ToList();
            ViewBag.EsSuperAdmin = esSuper;
            ViewBag.Restringidos = DocumentacionAccesoService.Restringidos().ToList();
            ViewBag.AccesoDisponible = DocumentacionAccesoService.TablaDisponible;
            return View("~/Views/Configuracion/DocumentacionBufins.cshtml", visibles);
        }

        [HttpGet]
        public ActionResult Ver(string clave)
        {
            var doc = DocumentacionBibliotecaCatalogo.Buscar(clave);
            if (doc == null) return HttpNotFound();
            if (!DocumentacionAccesoService.PuedeVer(doc.Clave, UsuarioSesionHelper.UsuarioActual)) return new HttpStatusCodeResult(403);

            return DocumentacionProtegidaHelper.Servir(this, doc.Ruta,
                R("Doc_MarcaLeyenda"), R("Doc_Aviso"), "DocumentacionBufins." + doc.Clave,
                "Documentación BUFINS: " + doc.Titulo, "Consultó la documentación: " + doc.Titulo + " (biblioteca Documentación BUFINS).");
        }

        /// <summary>Marca o desmarca un documento como «solo Super Admin». Solo el Super Admin; queda en la auditoría.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardarAcceso(string clave, bool soloSuperAdmin)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin()) return new HttpStatusCodeResult(403);
            var doc = DocumentacionBibliotecaCatalogo.Buscar(clave);
            if (doc == null) return Json(new { exito = false, mensaje = R("DocBuf_AccesoDocInvalido") });

            bool antes = DocumentacionAccesoService.EsSoloSuperAdmin(clave);
            var usuario = UsuarioSesionHelper.UsuarioActual;
            string errorKey;
            if (!new DocumentacionAccesoService().Guardar(clave, soloSuperAdmin, usuario?.Id ?? 0, out errorKey))
                return Json(new { exito = false, mensaje = R(errorKey) });

            if (antes != soloSuperAdmin)
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Permisos, AuditoriaAccion.Editar, "Documentacion." + clave, clave,
                    (soloSuperAdmin ? "Restringió a solo Super Admin" : "Abrió a los usuarios con acceso") + " la documentación: " + doc.Titulo + ".",
                    antes ? "Solo Super Admin" : "Con acceso", soloSuperAdmin ? "Solo Super Admin" : "Con acceso", usuario?.IdEmpresa, entidadNombre: doc.Titulo);

            return Json(new { exito = true, mensaje = R(soloSuperAdmin ? "DocBuf_AccesoRestringido" : "DocBuf_AccesoAbierto"), soloSuperAdmin });
        }
    }
}
