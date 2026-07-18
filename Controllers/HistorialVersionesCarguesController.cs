using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class HistorialVersionesCarguesController : BaseController
    {
        private readonly HistorialVersionesCarguesService _service = new HistorialVersionesCarguesService();
        private readonly EmpresaService _empresaService = new EmpresaService();

        [RequierePermiso("DATOS_HISTORIAL_CARGUES")]
        public ActionResult Index(int? idEmpresa, int? anio, byte? modo)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();

            if (!esSuperAdmin)
            {
                // Permitir su empresa o una de su mismo grupo empresarial (solo consulta del historial)
                idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                    ? idEmpresa
                    : usuario.IdEmpresa;
            }

            var versiones = _service.ObtenerHistorial(idEmpresa, anio, modo);
            var todasEmpresas = _empresaService.ObtenerEmpresas();
            var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario);

            var vm = new HistorialVersionesPageViewModel
            {
                Versiones       = versiones,
                Empresas        = idsPermitidos == null
                                    ? todasEmpresas
                                    : todasEmpresas.Where(e => idsPermitidos.Contains(e.Id)).ToList(),
                IdEmpresaFiltro = idEmpresa,
                AnioFiltro      = anio,
                ModoFiltro      = modo,
                MaxVersiones    = HistorialVersionesCarguesService.MaxVersionesPorEscenario
            };

            return View("~/Views/Informes/HistorialVersionesCargues.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Restaurar(int idHistorial, int? idEmpresaFiltro, int? anioFiltro, byte? modoFiltro)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (usuario == null || usuario.Admin < 1)
            {
                SetErrorMessage(R("Hist_ErrorSinPermiso"));
                return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro });
            }

            var version = _service.ObtenerPorId(idHistorial);
            if (version == null)
            {
                SetErrorMessage(R("Hist_ErrorVersionNoEncontrada"));
                return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro });
            }

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, version.IdEmpresa))
            {
                SetErrorMessage(R("Hist_ErrorSinPermiso"));
                return RedirectToAction("Index");
            }

            if (version.EsVersionActual)
            {
                SetErrorMessage(R("Hist_ErrorYaEsActual"));
                return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro });
            }

            string nombreUsuario = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
            bool exito = _service.EjecutarRollback(idHistorial, usuario.Id, nombreUsuario);

            if (exito)
            {
                string msg = string.Format(R("Hist_RollbackExitoso"), version.Anio, version.NombreEmpresa);
                SetSuccessMessage(msg);
                new NotificacionesService().Crear(usuario.Id, R("Notif_RollbackEjecutado"), msg, "success");
            }
            else
            {
                string msg = R("Hist_ErrorRollback");
                SetErrorMessage(msg);
                new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorRollback"), msg, "error");
            }

            return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro });
        }
    }
}
