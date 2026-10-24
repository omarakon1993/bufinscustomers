using System;
using System.IO;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Logo de la empresa: alta/baja desde el gestor de Empresas y entrega a los informes y sus exportaciones.
    /// Ver "Logo de la empresa en los informes" en CLAUDE.md. Acceso siempre por <see cref="EmpresaAccesoHelper"/>
    /// (empresa propia + las de su grupo empresarial; Super Admin todas).
    /// </summary>
    [ValidarSesion]
    public class EmpresaLogoController : BaseController
    {
        private readonly EmpresaLogoService _servicio = new EmpresaLogoService();

        /// <summary>Imagen del logo (PNG). Se usa en el gestor de Empresas; el parámetro <c>v</c> solo rompe el caché del navegador.</summary>
        [HttpGet]
        public ActionResult Ver(int id)
        {
            if (!EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, id))
                return new HttpStatusCodeResult(403);

            var logo = _servicio.Obtener(id);
            if (logo == null) return new HttpStatusCodeResult(404);

            Response.Cache.SetCacheability(HttpCacheability.Private);
            Response.Cache.SetMaxAge(TimeSpan.FromHours(12));
            Response.Cache.SetETag("\"" + logo.Version + "\"");
            return File(logo.Imagen, logo.TipoImagen);
        }

        /// <summary>
        /// Logo para los informes: JSON con la imagen en data URL (así se pinta sin una segunda petición y sirve tal cual para
        /// html2canvas y jsPDF). Sin logo o sin acceso → <c>{ tiene:false }</c> (nunca un 404 que ensucie la consola).
        /// </summary>
        [HttpGet]
        public ActionResult Datos(int id)
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            if (!EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, id))
                return Json(new { tiene = false }, JsonRequestBehavior.AllowGet);

            var logo = _servicio.Obtener(id);
            if (logo == null) return Json(new { tiene = false }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                tiene = true,
                dataUrl = "data:" + logo.TipoImagen + ";base64," + Convert.ToBase64String(logo.Imagen),
                ancho = logo.Ancho,
                alto = logo.Alto,
                version = logo.Version
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Sube (o reemplaza) el logo. Se valida el contenido real del archivo y se guarda normalizado (ver <see cref="EmpresaLogoHelper"/>).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_EMPRESAS_GESTOR")]
        public ActionResult Subir(int idEmpresa, HttpPostedFileBase archivo)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { exito = false, mensaje = R("Emp_SinPermisoEditar") });

            if (archivo == null || archivo.ContentLength <= 0)
                return Json(new { exito = false, mensaje = R("EmpLogo_Err_Vacio") });
            if (archivo.ContentLength > EmpresaLogoHelper.MaxBytesArchivo)
                return Json(new { exito = false, mensaje = string.Format(R("EmpLogo_Err_Peso"), EmpresaLogoHelper.MaxBytesArchivo / (1024 * 1024)) });

            try
            {
                if (!_servicio.Disponible())
                    return Json(new { exito = false, mensaje = R("EmpLogo_SinTabla") });

                byte[] datos;
                using (var ms = new MemoryStream())
                {
                    archivo.InputStream.CopyTo(ms);
                    datos = ms.ToArray();
                }

                var r = EmpresaLogoHelper.Procesar(datos);
                if (!r.Ok)
                    return Json(new { exito = false, mensaje = string.Format(R(r.ErrorKey), r.ErrorArgs ?? new object[0]) });

                bool habiaLogo = _servicio.Obtener(idEmpresa) != null;
                _servicio.Guardar(idEmpresa, r.Png, r.Ancho, r.Alto, r.Version, Path.GetFileName(archivo.FileName), usuario?.Id);
                EmpresasViewBagFilter.Invalidar();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "Empresas", idEmpresa.ToString(),
                    (habiaLogo ? "Logo de la empresa reemplazado" : "Logo de la empresa agregado") + $" (Id {idEmpresa})",
                    null, new { r.Ancho, r.Alto, Bytes = r.Png.Length, r.Version }, idEmpresa: idEmpresa);

                return Json(new
                {
                    exito = true,
                    mensaje = R("EmpLogo_Guardado"),
                    aviso = r.AvisoKey == null ? null : R(r.AvisoKey),
                    version = r.Version,
                    url = Url.Action("Ver", "EmpresaLogo", new { id = idEmpresa, v = r.Version })
                });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = MensajeError(ex) });
            }
        }

        /// <summary>Quita el logo de la empresa.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_EMPRESAS_GESTOR")]
        public ActionResult Eliminar(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { exito = false, mensaje = R("Emp_SinPermisoEditar") });

            try
            {
                if (!_servicio.Eliminar(idEmpresa))
                    return Json(new { exito = false, mensaje = R("EmpLogo_NoHay") });

                EmpresasViewBagFilter.Invalidar();
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "Empresas", idEmpresa.ToString(), $"Logo de la empresa eliminado (Id {idEmpresa})", null, null, idEmpresa: idEmpresa);

                return Json(new { exito = true, mensaje = R("EmpLogo_Eliminado") });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = MensajeError(ex) });
            }
        }
    }
}
