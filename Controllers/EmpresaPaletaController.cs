using System;
using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Colores corporativos de la empresa (hasta 2 paletas): alta/edición/baja desde el modal «Colores corporativos» del
    /// gestor de Empresas y de Configuración de empresas. Acceso siempre por <see cref="EmpresaAccesoHelper"/> (empresa propia +
    /// las de su grupo; Super Admin todas). Ver CLAUDE.md, «Tema de colores de los informes».
    /// </summary>
    [ValidarSesion]
    public class EmpresaPaletaController : BaseController
    {
        private readonly EmpresaPaletaService _servicio = new EmpresaPaletaService();

        [HttpGet]
        [RequierePermiso("ADMIN_EMPRESAS_GESTOR,ADMIN_CONFIG_EMPRESAS")]
        public ActionResult Obtener(int idEmpresa)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            if (!EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, idEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") }, JsonRequestBehavior.AllowGet);

            var emp = EmpresaCacheHelper.ObtenerEmpresasCacheadas().FirstOrDefault(e => e.Id == idEmpresa);
            return Json(new
            {
                exito = true,
                disponible = _servicio.Disponible(),
                empresa = emp != null ? emp.Nombre : "",
                paletas = _servicio.Obtener(idEmpresa).Select(p => new { slot = p.Slot, primario = p.Primario, secundario = p.Secundario, acento = p.Acento })
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_EMPRESAS_GESTOR,ADMIN_CONFIG_EMPRESAS")]
        public ActionResult Guardar(int idEmpresa, byte slot, string primario, string secundario, string acento)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (slot != 1 && slot != 2)
                return Json(new { exito = false, mensaje = R("PaletaCorp_Err_Slot") });

            string aviso;
            string error = TemaInforme.Validar(primario, secundario, acento, out aviso);
            if (error != null)
                return Json(new { exito = false, mensaje = R(error) });

            try
            {
                if (!_servicio.Disponible())
                    return Json(new { exito = false, mensaje = R("PaletaCorp_SinTabla") });

                string p, s, a = null;
                TemaInforme.TryHex(primario, out p);
                TemaInforme.TryHex(secundario, out s);
                if (!string.IsNullOrWhiteSpace(acento)) TemaInforme.TryHex(acento, out a);

                var anterior = _servicio.Obtener(idEmpresa, slot);
                _servicio.Guardar(new EmpresaPaleta { IdEmpresa = idEmpresa, Slot = slot, Nombre = null, Primario = p, Secundario = s, Acento = a }, usuario?.Id);

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "EmpresaPaletas", idEmpresa + ":" + slot,
                    (anterior == null ? "Colores corporativos agregados" : "Colores corporativos modificados") + $" (empresa {idEmpresa}, paleta {slot})",
                    anterior == null ? null : new { anterior.Primario, anterior.Secundario, anterior.Acento },
                    new { Primario = p, Secundario = s, Acento = a }, idEmpresa: idEmpresa);

                return Json(new { exito = true, mensaje = R("PaletaCorp_Guardada"), aviso = aviso == null ? null : R(aviso) });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequierePermiso("ADMIN_EMPRESAS_GESTOR,ADMIN_CONFIG_EMPRESAS")]
        public ActionResult Eliminar(int idEmpresa, byte slot)
        {
            if (!EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, idEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            try
            {
                var anterior = _servicio.Obtener(idEmpresa, slot);
                if (!_servicio.Eliminar(idEmpresa, slot))
                    return Json(new { exito = false, mensaje = R("PaletaCorp_NoHay") });

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "EmpresaPaletas", idEmpresa + ":" + slot, $"Colores corporativos eliminados (empresa {idEmpresa}, paleta {slot})",
                    anterior == null ? null : new { anterior.Primario, anterior.Secundario, anterior.Acento }, null, idEmpresa: idEmpresa);

                return Json(new { exito = true, mensaje = R("PaletaCorp_Eliminada") });
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = MensajeError(ex) });
            }
        }
    }
}
