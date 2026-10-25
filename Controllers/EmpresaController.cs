using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class EmpresaController : BaseController
    {
        private EmpresaService _empresaService = new EmpresaService();

        // Listar empresas
        public ActionResult Empresas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = _empresaService.ObtenerEmpresas();

            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                // Empresa propia y las de su mismo grupo empresarial
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new System.Collections.Generic.List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            return View("~/Views/Configuracion/Empresas.cshtml", empresas);
        }


        // POST: Crear empresa (solo Super Admin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CrearEmpresa(Empresas empresa, System.Web.HttpPostedFileBase logo)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage(R("Emp_SinPermisoCrear"));
                return RedirectToAction("Empresas");
            }

            // Página web: opcional, pero si viene debe ser una URL válida (se normaliza: sin esquema → https://).
            if (!UrlWebHelper.TryNormalizar(empresa.PaginaWeb, out string webNormalizada))
            {
                SetErrorMessage(R("Emp_ErrorPaginaWeb"));
                return RedirectToAction("Empresas");
            }
            empresa.PaginaWeb = webNormalizada;

            // Logo (opcional): se valida ANTES de crear, para no dejar la empresa creada con un logo rechazado.
            ResultadoLogo logoProcesado = null;
            if (logo != null && logo.ContentLength > 0)
            {
                if (logo.ContentLength > EmpresaLogoHelper.MaxBytesArchivo)
                {
                    SetErrorMessage(string.Format(R("EmpLogo_Err_Peso"), EmpresaLogoHelper.MaxBytesArchivo / (1024 * 1024)));
                    return RedirectToAction("Empresas");
                }
                byte[] datosLogo;
                using (var ms = new System.IO.MemoryStream())
                {
                    logo.InputStream.CopyTo(ms);
                    datosLogo = ms.ToArray();
                }
                logoProcesado = EmpresaLogoHelper.Procesar(datosLogo);
                if (!logoProcesado.Ok)
                {
                    SetErrorMessage(string.Format(R(logoProcesado.ErrorKey), logoProcesado.ErrorArgs ?? new object[0]));
                    return RedirectToAction("Empresas");
                }
            }

            string mensaje, advertencia;
            bool registrado = _empresaService.CrearEmpresa(empresa, out mensaje, out advertencia);

            if (registrado)
            {
                if (logoProcesado != null) GuardarLogoRecienCreada(empresa, logoProcesado, logo.FileName);
                EmpresasViewBagFilter.Invalidar();
                InicializarSupuestosBalancePpto(empresa);
                if (advertencia == "WEB_SIN_COLUMNA") SetInfoMessage(R("Emp_PaginaWebSinColumna"));
                SetSuccessMessage(R("Emp_Creada"));
                new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_EmpresaCreada"), empresa.Nombre, "success", "/Empresa/Empresas");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Crear,
                    "Empresas", empresa.Id.ToString(), $"Empresa creada: {empresa.Nombre}", null, empresa, idEmpresa: empresa.Id);
            }
            else
                SetErrorMessage(mensaje);

            return RedirectToAction("Empresas");
        }


        /// <summary>
        /// Toda empresa nueva nace con sus «Supuestos del balance del ppto» (estructura con Valor vacío).
        /// Si el módulo aún no está instalado (script 038) no hace nada; un fallo no deshace la creación de la empresa.
        /// </summary>
        private void InicializarSupuestosBalancePpto(Empresas empresa)
        {
            try
            {
                var svc = new SupuestosBalancePptoService();
                if (empresa.Id <= 0 || !svc.Disponible()) return;
                int filas = svc.Inicializar(empresa.Id, UsuarioSesionHelper.UsuarioActual?.Id);
                if (filas <= 0) return;
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Crear,
                    "SupuestosBalancePpto", empresa.Id.ToString(),
                    $"Supuestos del balance del ppto creados (valores vacíos) al crear la empresa {empresa.Nombre}: {filas} fila(s)",
                    null, new { filas }, idEmpresa: empresa.Id);
                TempData["SupuestosCreados"] = empresa.Id;
            }
            catch (System.Exception ex)
            {
                AppLogger.Error(ex, "EmpresaController.InicializarSupuestosBalancePpto");
            }
        }

        /// <summary>Guarda el logo elegido al crear la empresa. Si no se pudo, la empresa queda creada y se avisa (se agrega luego al editar).</summary>
        private void GuardarLogoRecienCreada(Empresas empresa, ResultadoLogo r, string nombreArchivo)
        {
            try
            {
                var servicio = new EmpresaLogoService();
                if (empresa.Id <= 0 || !servicio.Disponible())
                {
                    SetInfoMessage(R("EmpLogo_SinTabla"));
                    return;
                }
                servicio.Guardar(empresa.Id, r.Png, r.Ancho, r.Alto, r.Version, System.IO.Path.GetFileName(nombreArchivo), UsuarioSesionHelper.UsuarioActual?.Id);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "Empresas", empresa.Id.ToString(), $"Logo de la empresa agregado al crearla (Id {empresa.Id})",
                    null, new { r.Ancho, r.Alto, Bytes = r.Png.Length, r.Version }, idEmpresa: empresa.Id);
                if (r.AvisoKey != null) SetInfoMessage(R(r.AvisoKey));
            }
            catch (System.Exception ex)
            {
                SetInfoMessage(MensajeError(ex));
            }
        }

        // POST: Editar empresa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarEmpresa(Empresas empresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, empresa.Id))
            {
                SetErrorMessage(R("Emp_SinPermisoEditar"));
                return RedirectToAction("Empresas");
            }

            if (!UrlWebHelper.TryNormalizar(empresa.PaginaWeb, out string webNormalizada))
            {
                SetErrorMessage(R("Emp_ErrorPaginaWeb"));
                return RedirectToAction("Empresas");
            }
            empresa.PaginaWeb = webNormalizada;

            string mensaje, advertencia;
            bool actualizado = _empresaService.EditarEmpresa(empresa, out mensaje, out advertencia);

            if (actualizado)
            {
                EmpresasViewBagFilter.Invalidar();
                if (advertencia == "WEB_SIN_COLUMNA") SetInfoMessage(R("Emp_PaginaWebSinColumna"));
                SetSuccessMessage(mensaje);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Editar,
                    "Empresas", empresa.Id.ToString(), $"Empresa editada: {empresa.Nombre}", null, empresa, idEmpresa: empresa.Id);
            }
            else
                SetErrorMessage(mensaje);

            return RedirectToAction("Empresas");
        }

        // POST: Eliminar empresa (solo Super Admin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EliminarEmpresa(int idEmpresa)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                SetErrorMessage(R("Emp_SinPermisoEliminar"));
                return RedirectToAction("Empresas");
            }

            bool eliminado = _empresaService.EliminarEmpresa(idEmpresa);

            if (eliminado)
            {
                EmpresasViewBagFilter.Invalidar();
                SetSuccessMessage(R("Emp_Eliminada"));
                new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_EmpresaEliminada"), null, "warning", "/Empresa/Empresas");
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Empresas, AuditoriaAccion.Eliminar,
                    "Empresas", idEmpresa.ToString(), $"Empresa eliminada (Id {idEmpresa})", idEmpresa: idEmpresa);
            }
            else
                SetErrorMessage(R("Emp_ErrorEliminar"));

            return RedirectToAction("Empresas");
        }
    }
}
