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

                var columnasAmigables = resultado.Columnas.Select(c => new
                {
                    nombreTecnico = c,
                    nombreAmigable = _service.ObtenerNombreAmigableColumna(c)
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
                return Json(new { success = false, message = $"Error al consultar datos: {ex.Message}" });
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

                using (var package = new XLWorkbook())
                {
                    var tablas = _service.ObtenerTablasModelos();
                    var tablaSeleccionada = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
                    string nombreHoja = tablaSeleccionada?.NombreAmigable ?? filtros.NombreTabla;

                    if (nombreHoja.Length > 31)
                        nombreHoja = nombreHoja.Substring(0, 31);

                    var worksheet = package.Worksheets.Add(nombreHoja);

                    int col = 1;
                    foreach (var nombreColumna in resultado.Columnas)
                    {
                        var cell = worksheet.Cell(1, col);
                        cell.Value = _service.ObtenerNombreAmigableColumna(nombreColumna);

                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        col++;
                    }

                    int fila = 2;
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
                    worksheet.Range(1, 1, 1, resultado.Columnas.Count).SetAutoFilter();
                    worksheet.SheetView.Freeze(1, 0);

                    string nombreArchivo = $"InformeModelos_{nombreHoja}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeModelosController.ExportarExcel");
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}
