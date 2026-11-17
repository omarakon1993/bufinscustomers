using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public partial class DatosController(
        EmpresaService empresaService,
        ConfiguracionEmpresaService configuracionService,
        ModeloService modeloService,
        PlantillaConDatosService plantillaConDatosService,
        CargueStagingService stagingService,
        CargueImpactoService impactoService,
        PlantillaCargueService plantillaCargueService,
        AuditoriaService auditoriaService,
        NotificacionesService notificacionesService,
        EmailService emailService) : BaseController
    {
        private const string SESSION_LOG_IMPORTACION = "LogImportacion";
        /// <summary>Casos que la pantalla «Revisar cargue» trae por cada validación (el resto va solo en el Excel de errores).</summary>
        private const int CasosPorValidacionEnPantalla = 200;

        private CargueOrquestadorService _orq;
        /// <summary>Orquestador unico del cargue (lo comparten la pantalla de Excel y la API). Recibe R para traducir mensajes.</summary>
        private CargueOrquestadorService Orq => _orq ?? (_orq = new CargueOrquestadorService(R));

        // Mapeo hoja Z_ → tabla Ini_: Helpers/TablasCargueHelper.cs. Se resuelve POR EMPRESA
        // (TablasCargueHelper.MapeoParaEmpresa) porque algunas empresas tienen hojas personalizadas.

        public ActionResult DescargarLog()
        {
            var log = Session[SESSION_LOG_IMPORTACION] as string;
            if (string.IsNullOrEmpty(log))
            {
                return Content("No hay log disponible para descargar.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(log);
            string fileName = $"LogImportacion_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return File(bytes, "text/plain", fileName);
        }

        [HttpGet]
        public ActionResult ObtenerAnioEjecucion(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                int anio = config != null ? config.AnioEjecucion : DateTime.Now.Year;

                return Json(new { success = true, anioEjecucion = anio }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener el año: " + MensajeError(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult ObtenerAnosDisponibles(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                if (config == null)
                    return Json(new { success = false, message = "La empresa no tiene configuración creada." }, JsonRequestBehavior.AllowGet);

                var anosHistoricos = config.AnosHistoricos?
                    .Select(a => a.NombreAno)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList() ?? new List<string>();

                // Solo las hojas personalizadas que ESTE usuario puede ver (Super Admin: todas las de la
                // empresa; resto: solo si su propia empresa también las tiene — el acceso por grupo no basta).
                var visibles = PersonalizacionesEmpresaHelper.VisiblesPara(usuario, idEmpresa);

                return Json(new
                {
                    success = true,
                    anioEjecucion = config.AnioEjecucion,
                    anosHistoricos = anosHistoricos,
                    // Hojas adicionales visibles para el usuario (vacío = plantilla estándar).
                    hojasPersonalizadas = visibles.Select(h => h.NombreHoja).ToArray()
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + MensajeError(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Hojas que van en la plantilla de la empresa (vacía o con datos): estándar + personalizadas que el
        /// usuario puede ver (Super Admin: todas las de la empresa; resto: solo las que también tiene su propia
        /// empresa). Ya no hay selección manual de plantilla: cada empresa recibe la que le corresponde.
        /// </summary>
        private List<HojaCargue> HojasParaDescarga(int idEmpresa) =>
            PersonalizacionesEmpresaHelper.HojasPlantillaPara(UsuarioSesionHelper.UsuarioActual, idEmpresa);

        /// <summary>
        /// Plantilla de cargue vacía de la empresa (obligatoria), generada con el catálogo de hojas:
        /// base PlantillaBUFINS.xlsx + sus hojas personalizadas visibles para el usuario.
        /// </summary>
        [HttpGet]
        public ActionResult DescargarPlantilla(int? idEmpresa = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            int id = idEmpresa ?? 0;
            if (usuario == null || id <= 0 || !EmpresaAccesoHelper.TieneAcceso(usuario, id))
            {
                SetErrorMessage(R("Datos_PlantillaSinEmpresa"));
                return RedirectToAction("CargueExcel");
            }

            try
            {
                byte[] bytes = plantillaCargueService.GenerarBytes(HojasParaDescarga(id));
                var empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == id);
                string sufijo = Regex.Replace(!string.IsNullOrWhiteSpace(empresa?.Abreviatura) ? empresa.Abreviatura : (empresa?.Nombre ?? "Empresa"), "[^A-Za-z0-9]", "_");
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PlantillaBUFINS_{sufijo}.xlsx");
            }
            catch (Exception ex)
            {
                // No se entrega la base como respaldo: a una empresa con hojas personalizadas le faltarían.
                AppLogger.Error(ex, $"DescargarPlantilla: no se pudo generar la plantilla de la empresa {id}.");
                SetErrorMessage(R("Datos_PlantillaError"));
                return RedirectToAction("CargueExcel");
            }
        }
        // ── Descargar la plantilla BUFINS rellena con los datos actuales ─────────

        [HttpGet]
        public ActionResult ObtenerAniosConDatos(int idEmpresa, int idEscenario = 1)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var anios = plantillaConDatosService.ObtenerAniosConDatos(idEmpresa, idEscenario, HojasParaDescarga(idEmpresa))
                    .Select(a => new { anio = a.Anio, registros = a.Registros });

                return Json(new { success = true, anios }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ObtenerAniosConDatos");
                return Json(new { success = false, message = "Error: " + MensajeError(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult DescargarPlantillaConDatos(int idEmpresa, string anios, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || !EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { error = R("Datos_PlantillaConDatosError") }, JsonRequestBehavior.AllowGet);

            var listaAnios = (anios ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                .Where(n => n > 0)
                .Distinct()
                .ToList();

            if (listaAnios.Count == 0)
                return Json(new { error = R("Datos_PlantillaConDatosSinSeleccion") }, JsonRequestBehavior.AllowGet);

            byte[] bytes;
            int totalFilas;
            try
            {
                bytes = plantillaConDatosService.GenerarExcel(idEmpresa, listaAnios, idEscenario, HojasParaDescarga(idEmpresa), out totalFilas);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "DescargarPlantillaConDatos");
                return Json(new { error = R("Datos_PlantillaConDatosError") }, JsonRequestBehavior.AllowGet);
            }

            if (totalFilas == 0)
                return Json(new { error = R("Datos_PlantillaConDatosSinDatos") }, JsonRequestBehavior.AllowGet);

            var empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
            string empresaNombre = empresa?.Nombre ?? "Empresa";
            string empresaSanit = Regex.Replace(
                !string.IsNullOrWhiteSpace(empresa?.Abreviatura) ? empresa.Abreviatura : empresaNombre,
                "[^A-Za-z0-9]", "_");
            var ordenados = listaAnios.OrderBy(a => a).ToList();
            string sufijoAnios = ordenados.Count == 1
                ? ordenados[0].ToString()
                : $"{ordenados.First()}-{ordenados.Last()}";
            string fileName = $"PlantillaConDatos_{empresaSanit}_Esc{idEscenario}_{sufijoAnios}.xlsx";

            auditoriaService.RegistrarCambio(
                AuditoriaTipo.Cargues, AuditoriaAccion.Exportar, "PlantillaConDatos", idEmpresa.ToString(),
                $"Descargó la plantilla con datos de '{empresaNombre}' (Escenario {idEscenario}) — año(s) {string.Join(", ", ordenados)}, {totalFilas} fila(s).",
                null,
                new { empresa = empresaNombre, anios = ordenados, totalFilas, archivo = fileName, escenario = idEscenario },
                idEmpresa, entidadNombre: empresaNombre, severidad: AuditoriaSeveridad.Advertencia);

            notificacionesService.Crear(
                usuario.Id, R("Notif_PlantillaConDatosDescargada"),
                $"{empresaNombre} — {string.Join(", ", ordenados)}", "success", "/Datos/CargueExcel");

            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        private static readonly Regex _rxCorreo =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    }
}
