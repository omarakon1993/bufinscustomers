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
        public ActionResult Index(int? idEmpresa, int? anio, byte? modo, int? escenario)
        {
            // Sin ?embed=1 se entrega la página anfitriona (encabezado + pestañas); ella pide por AJAX el contenido de «Versiones».
            if (!string.Equals(Request.QueryString["embed"], "1"))
            {
                ViewBag.Tab = "versiones";
                return View("~/Views/Informes/HistorialCargues.cshtml");
            }

            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();

            if (!esSuperAdmin)
            {
                // Permitir su empresa o una de su mismo grupo empresarial (solo consulta del historial)
                idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                    ? idEmpresa
                    : usuario.IdEmpresa;
            }

            var versiones = _service.ObtenerHistorial(idEmpresa, anio, modo, escenario);
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
                EscenarioFiltro = escenario,
                MaxVersiones    = HistorialVersionesCarguesService.MaxVersionesPorLlaveCargue
            };

            // Respaldos automáticos previos a restaurar (solo quien puede restaurar y solo de sus empresas/grupo).
            if (esSuperAdmin || usuario.Admin >= 1)
            {
                vm.Respaldos = _service.ObtenerRespaldos(idsPermitidos)
                    .Where(r => !idEmpresa.HasValue || r.IdEmpresa == idEmpresa.Value)
                    .ToList();
            }

            return View("~/Views/Informes/HistorialVersionesCargues.cshtml", vm);
        }

        /// <summary>Mensaje (traducido) de por qué no se permite aplicar una copia: deja el año vacío o una tabla bajo el umbral de bloqueo.</summary>
        private string MensajeBloqueo(ImpactoCargue b, int anio) =>
            b.CargaVacia
                ? string.Format(R("Hist_Bloqueo_Vacio"), anio)
                : string.Format(R("Hist_Bloqueo_Reduccion"), string.Join(", ", b.TablasBloqueadas.Select(t => t.Tabla)), b.UmbralBloqueoPct);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Restaurar(int idHistorial, int? idEmpresaFiltro, int? anioFiltro, byte? modoFiltro, int? escenarioFiltro)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (usuario == null || usuario.Admin < 1)
            {
                SetErrorMessage(R("Hist_ErrorSinPermiso"));
                return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro, escenario = escenarioFiltro });
            }

            var version = _service.ObtenerPorId(idHistorial);
            if (version == null)
            {
                SetErrorMessage(R("Hist_ErrorVersionNoEncontrada"));
                return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro, escenario = escenarioFiltro });
            }

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, version.IdEmpresa))
            {
                SetErrorMessage(R("Hist_ErrorSinPermiso"));
                return RedirectToAction("Index");
            }

            // Nota: la copia de cada versión guarda los datos de ANTES de ese cargue, así que restaurar la versión
            // «Actual» (la del último cargue) es válido y es justo la forma de deshacer ese último cargue.

            string nombreUsuario = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
            bool exito = _service.EjecutarRollback(idHistorial, usuario.Id, nombreUsuario, out int? idRespaldo, out ImpactoCargue bloqueo);

            if (exito)
            {
                string msg = string.Format(R("Hist_RollbackExitoso"), version.Anio, version.NombreEmpresa);
                SetSuccessMessage(idRespaldo.HasValue ? msg + " " + R("Hist_RollbackRespaldo") : msg);
                new NotificacionesService().Crear(usuario.Id, R("Notif_RollbackEjecutado"), msg, "success", "/HistorialVersionesCargues");

                // El rollback reemplaza los datos del año por los de una versión anterior: queda en la auditoría central.
                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Restaurar, "HistorialVersionCargue", idHistorial.ToString(),
                    $"Restauró el estado previo al cargue #{idHistorial} de '{version.NombreEmpresa}' — año {version.Anio}, {(version.Modo == 0 ? "ejecución" : "histórico")}, escenario {version.IdEscenario} (cargue del {version.FechaCargue:dd/MM/yyyy HH:mm} por {version.NombreUsuario}, archivo '{version.NombreArchivo}'). Los datos actuales de ese año fueron reemplazados por la copia de seguridad de {version.TotalFilas:N0} fila(s) tomada justo antes de ese cargue." + (idRespaldo.HasValue ? $" Antes de reemplazar, se guardó un respaldo de los datos que había (respaldo #{idRespaldo})." : " No se guardó respaldo de los datos reemplazados (falta el script 034)."),
                    null,
                    new { idHistorial, empresa = version.NombreEmpresa, anio = version.Anio, modo = version.Modo, escenario = version.IdEscenario, archivoVersion = version.NombreArchivo, usuarioVersion = version.NombreUsuario, totalFilasVersion = version.TotalFilas, idRespaldo },
                    version.IdEmpresa, entidadNombre: version.NombreEmpresa, severidad: AuditoriaSeveridad.Advertencia);
            }
            else if (bloqueo != null)
            {
                // Regla de oro: nunca se deja un año vacío ni una tabla bajo el umbral de bloqueo. No se cambió nada.
                string msgBloq = MensajeBloqueo(bloqueo, version.Anio);
                SetErrorMessage(msgBloq);
                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Bloqueo, "HistorialVersionCargue", idHistorial.ToString(),
                    $"Deshacer BLOQUEADO en '{version.NombreEmpresa}' — año {version.Anio}, {(version.Modo == 0 ? "ejecución" : "histórico")}, escenario {version.IdEscenario}: la copia dejaría {bloqueo.TotalExistentes:N0} fila(s) en solo {bloqueo.TotalNuevas:N0}. No se cambió ningún dato.",
                    null, new { idHistorial, existentes = bloqueo.TotalExistentes, copia = bloqueo.TotalNuevas },
                    version.IdEmpresa, entidadNombre: version.NombreEmpresa);
            }
            else
            {
                string msg = R("Hist_ErrorRollback");
                SetErrorMessage(msg);
                new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorRollback"), msg, "error", "/HistorialVersionesCargues");
            }

            return RedirectToAction("Index", new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro, escenario = escenarioFiltro });
        }

        /// <summary>
        /// Recupera un respaldo automático: devuelve los datos de esa empresa/año/modo/escenario a como estaban antes de la
        /// restauración que lo generó. Antes de reemplazar guarda otro respaldo (la recuperación también se puede deshacer).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RecuperarRespaldo(int idRespaldo, int? idEmpresaFiltro, int? anioFiltro, byte? modoFiltro, int? escenarioFiltro)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var volver = new { idEmpresa = idEmpresaFiltro, anio = anioFiltro, modo = modoFiltro, escenario = escenarioFiltro };

            if (usuario == null || usuario.Admin < 1)
            {
                SetErrorMessage(R("Hist_ErrorSinPermiso"));
                return RedirectToAction("Index", volver);
            }

            var resp = _service.ObtenerRespaldoPorId(idRespaldo);
            if (resp == null || !EmpresaAccesoHelper.TieneAcceso(usuario, resp.IdEmpresa))
            {
                SetErrorMessage(R("Hist_Resp_NoEncontrado"));
                return RedirectToAction("Index", volver);
            }

            string nombreUsuario = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
            if (_service.RecuperarRespaldo(idRespaldo, usuario.Id, nombreUsuario, out int? idRespaldoNuevo, out ImpactoCargue bloqueoRec))
            {
                string msg = string.Format(R("Hist_Resp_Ok"), resp.Anio, resp.NombreEmpresa);
                SetSuccessMessage(idRespaldoNuevo.HasValue ? msg + " " + R("Hist_RollbackRespaldo") : msg);
                new NotificacionesService().Crear(usuario.Id, R("Notif_RollbackEjecutado"), msg, "success", "/HistorialVersionesCargues");

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Restaurar, "RespaldoRestauracion", idRespaldo.ToString(),
                    $"Recuperó el respaldo #{idRespaldo} de '{resp.NombreEmpresa}' — año {resp.Anio}, {(resp.Modo == 0 ? "ejecución" : "histórico")}, escenario {resp.IdEscenario} (tomado el {resp.Fecha:dd/MM/yyyy HH:mm} al restaurar la versión #{resp.IdHistorialRestaurado}). Los datos actuales de ese año fueron reemplazados por esos {resp.TotalFilas:N0} fila(s)." +
                    (idRespaldoNuevo.HasValue ? $" Se guardó antes un respaldo de lo reemplazado (respaldo #{idRespaldoNuevo})." : ""),
                    null,
                    new { idRespaldo, idRespaldoNuevo, empresa = resp.NombreEmpresa, anio = resp.Anio, modo = resp.Modo, escenario = resp.IdEscenario, totalFilas = resp.TotalFilas },
                    resp.IdEmpresa, entidadNombre: resp.NombreEmpresa, severidad: AuditoriaSeveridad.Advertencia);
            }
            else if (bloqueoRec != null)
            {
                SetErrorMessage(MensajeBloqueo(bloqueoRec, resp.Anio));
                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Bloqueo, "RespaldoRestauracion", idRespaldo.ToString(),
                    $"Recuperar respaldo BLOQUEADO en '{resp.NombreEmpresa}' — año {resp.Anio}: el respaldo dejaría {bloqueoRec.TotalExistentes:N0} fila(s) en solo {bloqueoRec.TotalNuevas:N0}. No se cambió ningún dato.",
                    null, new { idRespaldo }, resp.IdEmpresa, entidadNombre: resp.NombreEmpresa);
            }
            else
            {
                SetErrorMessage(R("Hist_Resp_Error"));
                new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorRollback"), R("Hist_Resp_Error"), "error", "/HistorialVersionesCargues");
            }

            return RedirectToAction("Index", volver);
        }
    }
}
