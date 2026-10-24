using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe de solo lectura sobre las tablas de resultados de Ejecución de Modelos
    /// (ModeloBalance, ModeloPYG, etc. — ver InformeTablasDatosService.ObtenerTablasModelos()).
    /// Independiente de InformeTablasDatosController (que quedó reducido a lo que usa Análisis
    /// IA): tiene sus propios endpoints de consulta y exportación, reutilizando únicamente el
    /// motor genérico de acceso a datos (InformeTablasDatosService) — no la otra pantalla.
    /// Habilitado para todos los roles (ver Sql/006_MenuOpcion_InformeModelos.sql).
    /// </summary>
    [ValidarSesion]
    public class InformeModelosController : BaseController
    {
        private readonly InformeTablasDatosService _service = new InformeTablasDatosService();

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var empresas = _service.ObtenerEmpresas();
            if (!esAdmin)
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            ViewBag.Tablas = _service.ObtenerTablasModelos();
            ViewBag.Empresas = empresas;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            return View("~/Views/Informes/InformeModelos.cshtml");
        }

        /// <summary>true solo para las 8 tablas de Ejecución de Modelos — nunca las *_VT de Análisis IA.</summary>
        private bool EsTablaDeModelo(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla)
                && _service.ObtenerTablasModelos().Any(t => t.NombreTabla == nombreTabla);
        }

        /// <summary>
        /// Consulta los datos de una tabla de modelo con filtros aplicados (AJAX, JSON).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarDatos(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                if (!EsTablaDeModelo(filtros?.NombreTabla))
                    return Json(new { success = false, message = R("Common_TablaNoValida") });

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin pueden ver su propia empresa o una de su mismo grupo empresarial
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                            return Json(new { success = false, message = "No tiene permisos para consultar datos de otra empresa" });
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);

                // numerica = la columna trae decimal/money/float (el primer valor no nulo manda):
                // la vista la pinta como 1.236.285.416,35 aunque su nombre no esté en su lista.
                var columnasAmigables = resultado.Columnas.Select(c => new
                {
                    nombreTecnico = c,
                    nombreAmigable = _service.ObtenerNombreAmigableColumna(c),
                    numerica = resultado.Filas
                        .Select(f => f.ContainsKey(c) ? f[c] : null)
                        .Where(v => v != null && v != DBNull.Value)
                        .Select(v => v is decimal || v is double || v is float)
                        .FirstOrDefault()
                }).ToList();

                // Los números se envían SIN formatear (decimal/double crudos, no "N2" en texto):
                // el filtro dinámico por campo del cliente hace comparaciones "mayor que/menor
                // que" con parseFloat, y eso rompe si el valor ya trae separador de miles. El
                // formato de miles/decimales para mostrar en pantalla lo aplica el JS al pintar.
                var datosFormateados = resultado.Filas.Select(fila =>
                {
                    var filaFormateada = new Dictionary<string, object>();
                    foreach (var kvp in fila)
                    {
                        object valorFormateado = kvp.Value;

                        if (kvp.Value != null)
                        {
                            if (kvp.Value is DateTime)
                            {
                                valorFormateado = (kvp.Key.Contains("Ejecucion") || kvp.Key.Contains("FechaEjecucion"))
                                    ? ((DateTime)kvp.Value).ToString("dd/MM/yyyy HH:mm:ss")
                                    : ((DateTime)kvp.Value).ToString("dd/MM/yyyy");
                            }
                        }
                        else
                        {
                            valorFormateado = "";
                        }

                        filaFormateada[kvp.Key] = valorFormateado;
                    }
                    return filaFormateada;
                }).ToList();

                var jsonResult = Json(new
                {
                    success = true,
                    datos = datosFormateados,
                    columnas = columnasAmigables,
                    totalRegistros = resultado.TotalRegistros,
                    resultadosTruncados = resultado.ResultadosTruncados
                }, JsonRequestBehavior.AllowGet);

                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeModelosController.ConsultarDatos");
                return Json(new { success = false, message = $"Error al consultar datos: {MensajeError(ex)}" });
            }
        }

        /// <summary>
        /// Exporta a Excel los datos de una tabla de modelo con los filtros aplicados.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                if (!EsTablaDeModelo(filtros?.NombreTabla))
                {
                    TempData["ErrorMessage"] = R("Common_TablaNoValida");
                    return RedirectToAction("Index");
                }

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                        {
                            TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                            return RedirectToAction("Index");
                        }
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);

                if (resultado.TotalRegistros == 0)
                {
                    TempData["InfoMessage"] = "No hay datos para exportar con los filtros seleccionados";
                    return RedirectToAction("Index");
                }

                var bytes = GenerarExcel(filtros, resultado, out string nombreArchivo);
                return File(bytes, InformeCorreoHelper.MimeExcel, nombreArchivo);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeModelosController.ExportarExcel");
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// Envía por correo el Excel de la tabla consultada desde el botón Exportar (componente estándar). Este informe es una
        /// grilla de datos: no tiene PDF ni gráficos, así que el correo lleva solo el Excel y un resumen de los filtros. Se
        /// aplican las mismas reglas de acceso por empresa que la descarga.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosInformeTablasDatos filtros, string correos = null, bool incluirmeAMi = false, string mensaje = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || !EsTablaDeModelo(filtros?.NombreTabla))
                return Json(new { exito = false, mensaje = R("Common_TablaNoValida") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });

            try
            {
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                            return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
                    }
                    else
                    {
                        filtros.IdEmpresa = usuario.IdEmpresa;
                    }
                }

                var idEmpresaConsulta = filtros.IdEmpresa ?? usuario.IdEmpresa;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);
                if (resultado.TotalRegistros == 0)
                    return Json(new { exito = false, mensaje = R("InformeModelos_Correo_SinDatos") });

                var bytes = GenerarExcel(filtros, resultado, out string nombreArchivo);
                if (bytes.Length > 15 * 1024 * 1024)
                    return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Grande") });
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, null);

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                var tabla = _service.ObtenerTablasModelos().FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
                string nombreTabla = tabla?.NombreAmigable ?? filtros.NombreTabla;
                string empresa = idEmpresaConsulta.HasValue
                    ? (_service.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresaConsulta.Value)?.Nombre ?? "")
                    : R("Common_ExpCorreo_TodasEmpresas");

                var contexto = new List<(string etiqueta, string valor)>
                {
                    (R("Common_ExpCorreo_Tabla"), nombreTabla),
                    (R("Common_ExpCorreo_Registros"), resultado.TotalRegistros.ToString("N0"))
                };
                if (filtros.Año.HasValue) contexto.Add((R("Common_ExpCorreo_Anio"), filtros.Año.Value.ToString()));
                if (!string.IsNullOrWhiteSpace(filtros.Variable)) contexto.Add((R("Common_ExpCorreo_Variable"), filtros.Variable));

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                new EmailService().EnviarInforme(string.Join(", ", lista), R("InformeModelos_PageTitle"), empresa, false, contexto, null,
                    null, null, null, mensaje, quienEnvia, adjuntos, esIngles, ExcelLogoHelper.Png(idEmpresaConsulta));

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeModelos", (idEmpresaConsulta ?? 0).ToString(),
                    $"Informe de modelos enviado por correo a {string.Join(", ", lista)}: '{empresa}' — {nombreTabla} ({resultado.TotalRegistros} registros).",
                    null,
                    new { empresa, tabla = filtros.NombreTabla, registros = resultado.TotalRegistros, destinatarios = lista },
                    idEmpresaConsulta, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeModelosController.EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        /// <summary>Libro de Excel del informe (lo usan la descarga y el envío por correo del botón Exportar).</summary>
        private byte[] GenerarExcel(FiltrosInformeTablasDatos filtros, ResultadoInformeTablasDatos resultado, out string nombreArchivo)
        {
        using (var package = new XLWorkbook())
        {
            var tablas = _service.ObtenerTablasModelos();
            var tablaSeleccionada = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
            string nombreHoja = tablaSeleccionada?.NombreAmigable ?? filtros.NombreTabla;

            if (nombreHoja.Length > 31)
                nombreHoja = nombreHoja.Substring(0, 31);

            var worksheet = package.Worksheets.Add(nombreHoja);

            // Logo de la empresa (si la consulta es de UNA empresa y esta tiene logo): se reservan 3 filas arriba con título + logo.
            // Sin logo el libro queda igual que siempre (encabezado en la fila 1).
            var logo = filtros.IdEmpresa.HasValue ? ExcelLogoHelper.Obtener(filtros.IdEmpresa.Value) : null;
            int o = logo != null ? ExcelLogoHelper.FilasBloque : 0;
            if (logo != null)
            {
                string nombreEmpresaXls = _service.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa.Value)?.Nombre ?? "";
                ExcelLogoHelper.EscribirTitulo(worksheet, R("InformeModelos_PageTitle") + " — " + nombreEmpresaXls,
                    nombreHoja + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            }

            int col = 1;
            foreach (var nombreColumna in resultado.Columnas)
            {
                var cell = worksheet.Cell(1 + o, col);
                cell.Value = _service.ObtenerNombreAmigableColumna(nombreColumna);

                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                col++;
            }

            int fila = 2 + o;
            foreach (var registro in resultado.Filas)
            {
                col = 1;
                foreach (var nombreColumna in resultado.Columnas)
                {
                    var cell = worksheet.Cell(fila, col);
                    var valor = registro.ContainsKey(nombreColumna) ? registro[nombreColumna] : null;

                    if (valor != null)
                    {
                        if (valor is DateTime)
                        {
                            cell.Value = (DateTime)valor;
                            cell.Style.NumberFormat.Format = "dd/mm/yyyy";
                        }
                        else if (valor is decimal || valor is double || valor is float)
                        {
                            ExcelCellHelper.SetValue(cell, valor);
                            cell.Style.NumberFormat.Format = "#,##0.00";
                        }
                        else if (valor is int || valor is long)
                        {
                            ExcelCellHelper.SetValue(cell, valor);
                            cell.Style.NumberFormat.Format = "#,##0";
                        }
                        else
                        {
                            cell.Value = valor.ToString();
                        }
                    }

                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);

                    col++;
                }
                fila++;
            }

            worksheet.Columns().AdjustToContents();
            if (logo != null) ExcelLogoHelper.Colocar(worksheet, logo);
            worksheet.Range(1 + o, 1, 1 + o, resultado.Columnas.Count).SetAutoFilter();
            worksheet.SheetView.Freeze(1 + o, 0);

            nombreArchivo = $"InformeModelos_{nombreHoja}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                package.SaveAs(ms);
                fileBytes = ms.ToArray();
            }
            return fileBytes;
        }
        }
    }
}
