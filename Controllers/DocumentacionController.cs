using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Sirve la documentación de los informes (visor de solo lectura de cada página): UNA sola acción para todas. La clave debe estar
    /// en DocumentacionCatalogo (el archivo nunca sale de la petición); exige sesión y el permiso del informe; el contenido lo entrega
    /// DocumentacionProtegidaHelper con marca de agua, sin caché, rechazando la apertura directa y auditando cada consulta.
    /// </summary>
    [ValidarSesion]
    public class DocumentacionController : BaseController
    {
        [HttpGet]
        public ActionResult Ver(string clave)
        {
            var doc = DocumentacionCatalogo.Buscar(clave);
            if (doc == null) return HttpNotFound();

            // Super Admin pasa (TienePermiso); además aplica la restricción por empresa de los menús personalizados.
            if (!string.IsNullOrEmpty(doc.Permiso) && !UsuarioSesionHelper.TienePermiso(doc.Permiso))
                return new HttpStatusCodeResult(403);

            return DocumentacionProtegidaHelper.Servir(this, DocumentacionCatalogo.Carpeta + doc.Archivo,
                R("Doc_MarcaLeyenda"), R("Doc_Aviso"), "Documentacion." + doc.Clave,
                "Documentación: " + doc.Titulo, "Consultó la documentación: " + doc.Titulo + ".");
        }
    }
}
