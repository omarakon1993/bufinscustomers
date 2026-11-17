using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace bufinscustomers.Controllers
{
    // Cargue: parte de DatosController (misma clase; solo se separa el codigo por responsabilidad).
    public partial class DatosController
    {
        // Los parámetros opcionales preseleccionan empresa/año/escenario (los usa "Subir archivo corregido"
        // en RevisarCargue). Son solo preselección de UI: el acceso real se valida al cargar el archivo.
        public ActionResult CargueExcel(int? idEmpresa = null, int? anio = null, int? idEscenario = null)
        {
            ViewBag.PrefillEmpresa = idEmpresa;
            ViewBag.PrefillAnio = anio;
            ViewBag.PrefillEscenario = idEscenario;
            stagingService.PurgarLotesVencidosSiToca();
            return CargueExcelView();
        }

        private ActionResult CargueExcelView()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            int idEmpresa = usuario?.IdEmpresa ?? 0;

            List<Empresas> empresasDisponibles;
            if (UsuarioSesionHelper.EsSuperAdmin())
            {
                empresasDisponibles = empresaService.ObtenerEmpresas();
            }
            else
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresasDisponibles = empresaService.ObtenerEmpresas()
                    .Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresasDisponibles;
            ViewBag.UltimoUsuarioCargue = ObtenerUltimoUsuarioCargue(idEmpresa);

            return View("~/Views/Datos/CargueExcel.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CargarExcel(IFormFile archivoExcel, int idEmpresaSeleccionada, int anioSeleccionado, string modoSeleccionado, int idEscenarioSeleccionado = 1)
        {
            // ── Validación en dos pasos ──────────────────────────────────────────────────
            // Esta acción YA NO escribe en las tablas Ini_* reales: el orquestador
            // (Services/CargueOrquestadorService.cs) parsea el Excel, lo escribe en su espejo de
            // staging (dbo.Staging_Ini_*) y corre dbo.sp_ValidarCargueStaging; aquí solo se
            // redirige a RevisarCargue, donde el usuario ve el informe y decide Confirmar o Descartar.
            var solicitud = new SolicitudCargue
            {
                Usuario = UsuarioSesionHelper.UsuarioActual,
                IdEmpresa = idEmpresaSeleccionada,
                Anio = anioSeleccionado,
                Modo = modoSeleccionado,
                IdEscenario = idEscenarioSeleccionado,
                NombreArchivo = archivoExcel?.FileName,
                LongitudArchivo = archivoExcel?.ContentLength ?? 0,
                Archivo = archivoExcel?.InputStream
            };

            var validacion = Orq.ValidarSolicitud(solicitud);
            if (!validacion.Ok)
            {
                SetErrorMessage(validacion.Mensaje);
                return RedirectToAction("CargueExcel");
            }

            // El lote queda atado a esta sesión apenas se crea: al cerrar/expirar la sesión se descarta.
            var proceso = Orq.ProcesarArchivo(solicitud, idLote => CargueLoteSesionHelper.Registrar(idLote));

            if (!string.IsNullOrEmpty(proceso.LogImportacion))
                Session[SESSION_LOG_IMPORTACION] = proceso.LogImportacion;

            switch (proceso.Estado)
            {
                case ProcesoCargueEstado.EnRevision:
                    return RedirectToAction("RevisarCargue", new { idLote = proceso.IdLote });

                case ProcesoCargueEstado.LoteActivoExistente:
                    // Ya hay un cargue en revisión para esta misma llave: se manda a revisarlo.
                    SetInfoMessage(proceso.Mensaje);
                    return RedirectToAction("RevisarCargue", new { idLote = proceso.IdLote });

                case ProcesoCargueEstado.SinFilasDelAnio:
                    CargueLoteSesionHelper.Quitar(proceso.IdLote);
                    SetErrorMessage(proceso.Mensaje);
                    return RedirectToAction("CargueExcel");

                case ProcesoCargueEstado.ErrorValidacion:
                    SetErrorMessage($"No se pudo validar el cargue: {proceso.Mensaje}");
                    return RedirectToAction("CargueExcel");

                default: // PlantillaIncompleta, ErrorEnHoja, ErrorInesperado
                    TempData.GuardarObjeto("ResultadoCarga", proceso.Resultado);
                    TempData["NombreArchivo"] = proceso.NombreArchivo;
                    SetErrorMessage(proceso.Mensaje);
                    return RedirectToAction("CargueExcel");
            }
        }

        /// <summary>Informe de validación de un lote en staging — antes de confirmar el cargue.</summary>
        [HttpGet]
        public ActionResult RevisarCargue(long idLote)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var lote = stagingService.ObtenerLote(idLote);

            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
            {
                SetErrorMessage("El cargue en revisión indicado no existe o no tiene acceso a él.");
                return RedirectToAction("CargueExcel");
            }

            if (!PersonalizacionesEmpresaHelper.PuedeOperarCargue(usuario, lote.IdEmpresa))
            {
                SetErrorMessage(R("Datos_PlantillaPersonalizadaSinPermiso"));
                return RedirectToAction("CargueExcel");
            }

            // Si esta sesión es la dueña, el lote queda atado a ella: al cerrar/expirar la sesión se descarta.
            if (usuario != null && lote.IdUsuario == usuario.Id)
                CargueLoteSesionHelper.Registrar(idLote);
            stagingService.PurgarLotesVencidosSiToca();

            var vm = new RevisarCargueViewModel
            {
                Lote = lote,
                // La pantalla muestra los primeros N casos de cada validación y el total real; el Excel de errores trae todos.
                Hallazgos = stagingService.ObtenerHallazgos(idLote, CasosPorValidacionEnPantalla),
                TotalesPorRegla = stagingService.ObtenerTotalesPorRegla(idLote),
                NombreArchivo = lote.NombreArchivo
            };
            try { vm.Impacto = impactoService.Analizar(lote); }
            catch (Exception exImp) { AppLogger.Error(exImp, "RevisarCargue.Impacto"); }
            return View("~/Views/Datos/RevisarCargue.cshtml", vm);
        }

        /// <summary>
        /// Confirma un lote ya validado (Estado ValidadoOk/ConAdvertencias). Toda la lógica (impacto, regla de oro,
        /// snapshot, reemplazo real, auditoría, notificación) vive en <see cref="CargueOrquestadorService.Confirmar"/>.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfirmarCargue(long idLote, bool confirmarReemplazo = false)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var r = Orq.Confirmar(usuario, idLote, confirmarReemplazo);

            switch (r.Estado)
            {
                case ConfirmacionCargueEstado.Confirmado:
                    CargueLoteSesionHelper.Quitar(idLote);
                    TempData.GuardarObjeto("ResultadoCarga", r.Resultado);
                    TempData["NombreArchivo"] = r.Lote.NombreArchivo;
                    if (r.Resultado.Exito) SetSuccessMessage(r.Mensaje);
                    else SetErrorMessage(r.Mensaje);
                    return RedirectToAction("CargueExcel");

                case ConfirmacionCargueEstado.LoteNoExiste:
                case ConfirmacionCargueEstado.SinPermisoPlantilla:
                    SetErrorMessage(r.Mensaje);
                    return RedirectToAction("CargueExcel");

                case ConfirmacionCargueEstado.LoteNoDisponible:
                    CargueLoteSesionHelper.Quitar(idLote);
                    SetErrorMessage(r.Mensaje);
                    return RedirectToAction("CargueExcel");

                default: // NoConfirmable, ErrorImpacto, CargaVacia, Bloqueado, FaltaConfirmarReemplazo, Error
                    SetErrorMessage(r.Mensaje);
                    return RedirectToAction("RevisarCargue", new { idLote });
            }
        }

        /// <summary>Descarta un lote en revisión sin tocar ninguna tabla real.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DescartarCargue(long idLote)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (!Orq.DescartarCargue(usuario, idLote))
            {
                SetErrorMessage(R("RevisarCargue_ErrorDescartar"));
                return RedirectToAction("RevisarCargue", new { idLote });
            }

            CargueLoteSesionHelper.Quitar(idLote);
            SetInfoMessage("Se descartó el cargue en revisión.");
            return RedirectToAction("CargueExcel");
        }

        /// <summary>
        /// Descarte silencioso cuando el usuario sale de RevisarCargue sin confirmar ni descartar
        /// (otra opción del menú, "Subir archivo corregido", cerrar la pestaña…). Lo dispara la vista con
        /// navigator.sendBeacon en 'pagehide'. Solo descarta lotes del propio usuario y no confirmados.
        /// Con <paramref name="porRecarga"/> (la vista detectó que se recargó la página) además redirige a
        /// Cargue de Excel con el aviso, en vez de responder 204 al beacon.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AbandonarCargue(long idLote, bool porRecarga = false)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (Orq.AbandonarCargue(usuario, idLote))
                CargueLoteSesionHelper.Quitar(idLote);

            if (porRecarga)
            {
                SetInfoMessage(R("RevisarCargue_DescartadoPorRecarga"));
                return RedirectToAction("CargueExcel");
            }
            return new StatusCodeResult(204);
        }

        private string ObtenerUltimoUsuarioCargue(int idEmpresa)
        {
            string ultimoCargue = "";

            Bd.LeerPrimera(CadenaConexion, @"
            SELECT  TOP 1 AuditoriaCargues.Usuario, FechaCargue, NombreEmpresa AS EmpNombre
            FROM  AuditoriaCargues
            WHERE AuditoriaCargues.IdEmpresa = @IdEmpresa
            ORDER BY FechaCargue DESC
        ",
                new { IdEmpresa = idEmpresa },
                reader =>
                {
                    string usuario = reader["Usuario"].ToString();
                    DateTime fecha = Convert.ToDateTime(reader["FechaCargue"]);
                    string empresa = reader["EmpNombre"].ToString();

                    ultimoCargue = $"Última carga de {empresa}: {fecha:dd/MM/yyyy HH:mm:ss} por {usuario}";
                });

            return ultimoCargue;
        }
    }
}
