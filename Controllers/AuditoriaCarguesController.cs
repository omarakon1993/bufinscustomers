using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;
using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using System.IO;
using System;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class AuditoriaCarguesController : BaseController
    {
        private AuditoriaCarguesService _auditoriaCarguesService = new AuditoriaCarguesService();

        /// <summary>
        /// Cargues visibles para el usuario: Super Admin todos; el resto su empresa y las de su mismo grupo empresarial.
        /// </summary>
        private List<AuditoriaCargues> ObtenerSegunAlcance()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var todas = _auditoriaCarguesService.ObtenerAuditoriaCargues();
            if (UsuarioSesionHelper.EsSuperAdmin()) return todas;

            var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
            return todas.Where(a => idsPermitidos.Contains(a.IdEmpresa)).ToList();
        }

        /// <summary>Aplica los filtros de la pantalla (los mismos para la grilla y para el Excel).</summary>
        private static List<AuditoriaCargues> Filtrar(List<AuditoriaCargues> auditorias, int? idEmpresa, int? idUsuario,
            int? escenario, string texto, string fechaDesde, string fechaHasta)
        {
            if (idEmpresa.HasValue)
                auditorias = auditorias.Where(a => a.IdEmpresa == idEmpresa.Value).ToList();

            if (idUsuario.HasValue)
                auditorias = auditorias.Where(a => a.IdUsuario == idUsuario.Value).ToList();

            if (escenario.HasValue)
                auditorias = auditorias.Where(a => a.IdEscenario == escenario.Value).ToList();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                var t = texto.Trim();
                auditorias = auditorias.Where(a =>
                    (a.NombreArchivo ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (a.Usuario ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (a.NombreEmpresa ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            if (DateTime.TryParse(fechaDesde, out var desde))
                auditorias = auditorias.Where(a => a.FechaCargue.Date >= desde.Date).ToList();

            if (DateTime.TryParse(fechaHasta, out var hasta))
                auditorias = auditorias.Where(a => a.FechaCargue.Date <= hasta.Date).ToList();

            return auditorias;
        }

        // Pantalla: solo trae lo necesario para armar los selectores; la grilla se llena al pulsar «Consultar».
        public ActionResult AuditoriaCargues()
        {
            ViewBag.Embed = string.Equals(Request.QueryString["embed"], "1");
            return View("~/Views/Informes/AuditoriaCargues.cshtml", ObtenerSegunAlcance());
        }

        // Datos de la grilla (solo al pulsar «Consultar»)
        [HttpGet]
        public JsonResult ObtenerCargues(int? idEmpresa, int? idUsuario, int? escenario, string texto = "",
            string fechaDesde = "", string fechaHasta = "")
        {
            var filas = Filtrar(ObtenerSegunAlcance(), idEmpresa, idUsuario, escenario, texto, fechaDesde, fechaHasta)
                .OrderByDescending(a => a.FechaCargue)
                .ToList();

            var nombresEscenario = EscenarioCacheHelper.ObtenerEscenariosCacheados().ToDictionary(e => e.Id, e => e.Nombre);

            return Json(new
            {
                success = true,
                items = filas.Select(a => new
                {
                    a.Id,
                    Fecha   = a.FechaCargue.ToString("dd/MM/yyyy HH:mm:ss"),
                    a.IdEmpresa,
                    Empresa = a.NombreEmpresa,
                    a.IdUsuario,
                    Usuario = a.Usuario,
                    Archivo = a.NombreArchivo,
                    Escenario = a.IdEscenario.HasValue
                        ? (nombresEscenario.TryGetValue(a.IdEscenario.Value, out var n) ? n : "#" + a.IdEscenario.Value)
                        : ""
                })
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Detalle de un cargue auditado: por tabla, filas que había (y se reemplazaron) y filas que quedaron, más el
        /// archivo original si se conservó. Respeta el mismo alcance que la grilla.
        /// </summary>
        [HttpGet]
        public JsonResult Detalle(int id)
        {
            var fila = ObtenerSegunAlcance().FirstOrDefault(a => a.Id == id);
            if (fila == null)
                return Json(new { success = false, message = R("Audit_Det_NoEncontrado") }, JsonRequestBehavior.AllowGet);

            var detalle = new CargueAuditoriaDetalleService().ObtenerPorAuditoria(id);
            var archivo = new CargueArchivosService().ObtenerInfoPorAuditoria(id);

            return Json(new
            {
                success = true,
                archivoCargue = fila.NombreArchivo,
                fecha = fila.FechaCargue.ToString("dd/MM/yyyy HH:mm:ss"),
                usuario = fila.Usuario,
                empresa = fila.NombreEmpresa,
                snapshot = detalle.Select(d => d.IdHistorial).FirstOrDefault(h => h.HasValue),
                filas = detalle.Select(d => new
                {
                    hoja = d.NombreHoja,
                    tabla = d.NombreTabla,
                    antes = d.FilasAntes,
                    nuevas = d.FilasNuevas,
                    nota = d.Nota,
                    origen = d.Origen,
                    anio = d.Anio,
                    modo = d.Modo
                }),
                archivo = archivo == null ? null : new
                {
                    id = archivo.Id,
                    nombre = archivo.NombreArchivo,
                    tamano = archivo.TamanoBytes,
                    fecha = archivo.FechaCarga.ToString("dd/MM/yyyy HH:mm"),
                    sha256 = archivo.Sha256
                },
                puedeDescargar = UsuarioSesionHelper.EsSuperAdmin()
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Descarga el Excel original de un cargue conservado. SOLO Super Admin (contiene datos financieros completos de la
        /// empresa) y cada descarga queda auditada.
        /// </summary>
        [HttpGet]
        public ActionResult DescargarArchivo(long id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return new HttpStatusCodeResult(403);

            var r = new CargueArchivosService().Obtener(id);
            if (r == null)
            {
                SetErrorMessage(R("Audit_Det_ArchivoNoDisponible"));
                return RedirectToAction("AuditoriaCargues");
            }

            var (info, contenido) = r.Value;
            new AuditoriaService().RegistrarCambio(
                AuditoriaTipo.Cargues, AuditoriaAccion.Exportar, "CargueArchivo", info.Id.ToString(),
                $"Descargó el archivo original '{info.NombreArchivo}' del cargue del {info.FechaCarga:dd/MM/yyyy HH:mm}.",
                null, new { info.Id, info.NombreArchivo, info.Sha256, info.TamanoBytes },
                info.IdEmpresa, severidad: AuditoriaSeveridad.Advertencia);

            string nombre = string.IsNullOrWhiteSpace(info.NombreArchivo) ? $"cargue_{info.Id}.xlsx" : info.NombreArchivo;
            return File(contenido, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
        }

        // Exportar auditoría a Excel (mismos filtros que la pantalla)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarAuditoriaExcel(int? idEmpresa, int? idUsuario, int? escenario, string texto = "",
            string fechaDesde = "", string fechaHasta = "")
        {
            try
            {
                var auditorias = Filtrar(ObtenerSegunAlcance(), idEmpresa, idUsuario, escenario, texto, fechaDesde, fechaHasta);

                var nombresEscenario = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                    .ToDictionary(e => e.Id, e => e.Nombre);
                bool conEscenario = auditorias.Any(a => a.IdEscenario.HasValue);

                // Generar archivo Excel
                using (var package = new XLWorkbook())
                {
                    var worksheet = package.Worksheets.Add("Auditoría Cargues");

                    // Configurar encabezados
                    Func<string, string> R = key => HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;
                    var headers = new List<string>
                    {
                        R("Audit_ExcelFecha"),
                        R("Audit_ExcelHora"),
                        R("Audit_ThEmpresa"),
                        R("Audit_ThUsuario"),
                        R("Audit_ThArchivo")
                    };
                    if (conEscenario) headers.Add(R("Ax_Escenario"));

                    // Aplicar encabezados
                    for (int i = 0; i < headers.Count; i++)
                    {
                        worksheet.Cell(1, i + 1).Value = headers[i];
                        worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                        worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(88, 58, 255);
                        worksheet.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                        worksheet.Cell(1, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }

                    // Llenar datos
                    int row = 2;
                    foreach (var auditoria in auditorias.OrderByDescending(x => x.FechaCargue))
                    {
                        int col = 1;

                        worksheet.Cell(row, col++).Value = auditoria.FechaCargue.ToString("dd/MM/yyyy");
                        worksheet.Cell(row, col++).Value = auditoria.FechaCargue.ToString("HH:mm:ss");
                        worksheet.Cell(row, col++).Value = auditoria.NombreEmpresa;
                        worksheet.Cell(row, col++).Value = auditoria.Usuario;
                        worksheet.Cell(row, col++).Value = auditoria.NombreArchivo;
                        if (conEscenario)
                        {
                            string nombreEsc = "";
                            if (auditoria.IdEscenario.HasValue)
                                nombreEsc = nombresEscenario.TryGetValue(auditoria.IdEscenario.Value, out var n) ? n : "#" + auditoria.IdEscenario.Value;
                            worksheet.Cell(row, col++).Value = nombreEsc;
                        }
                        row++;
                    }

                    // Agregar total de registros al final
                    if (auditorias.Any())
                    {
                        row += 1; // Espacio
                        worksheet.Cell(row, 1).Value = "Total de registros:";
                        worksheet.Cell(row, 1).Style.Font.Bold = true;
                        worksheet.Cell(row, 2).Value = auditorias.Count;
                        worksheet.Cell(row, 2).Style.Font.Bold = true;
                    }

                    // Ajustar anchos de columna
                    worksheet.Columns().AdjustToContents();

                    // Aplicar bordes
                    var dataRange = worksheet.Range(1, 1, row, headers.Count);
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    // Generar nombre de archivo
                    var hayFiltros = idEmpresa.HasValue || idUsuario.HasValue || escenario.HasValue
                                  || !string.IsNullOrWhiteSpace(texto)
                                  || !string.IsNullOrEmpty(fechaDesde) || !string.IsNullOrEmpty(fechaHasta);
                    var nombreArchivo = $"Auditoria_Cargues_{DateTime.Now:yyyyMMdd_HHmmss}{(hayFiltros ? "_Filtrado" : "")}.xlsx";

                    // Convertir a bytes
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }

                    // Retornar archivo para descarga
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage($"Error al exportar: {ex.Message}");
                return RedirectToAction("AuditoriaCargues");
            }
        }
    }
}
