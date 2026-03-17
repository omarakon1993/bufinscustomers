using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Drawing;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class VariablesPBIController : BaseController
    {
        private readonly VariablesPBIService _service = new VariablesPBIService();

        private bool VerificarSuperAdmin()
        {
            return UsuarioSesionHelper.EsSuperAdmin();
        }

        /// <summary>
        /// Vista principal del informe de Variables PBI (solo Super Admin)
        /// </summary>
        public ActionResult Index()
        {
            if (!VerificarSuperAdmin())
                return RedirectToAction("Index", "Home");

            ViewBag.Tablas = _service.ObtenerTablasDisponibles();
            ViewBag.EstadoActual = _service.ObtenerEstadoActual();
            return View("~/Views/Informes/VariablesPBI.cshtml");
        }

        /// <summary>
        /// Consulta datos con filtros (AJAX POST)
        /// </summary>
        [HttpPost]
        public JsonResult ConsultarDatos(FiltrosVariablesPBI filtros)
        {
            if (!VerificarSuperAdmin())
                return Json(new { success = false, message = "Acceso denegado" });

            try
            {
                var resultado = _service.ConsultarDatos(filtros);

                var jsonResult = Json(new
                {
                    success = true,
                    datos = resultado.Filas,
                    totalRegistros = resultado.TotalRegistros,
                    usuarioCargo = resultado.UsuarioCargo,
                    fechaCarga = resultado.FechaCarga.HasValue
                        ? resultado.FechaCarga.Value.ToString("dd/MM/yyyy HH:mm")
                        : null
                });

                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al consultar: {ex.Message}" });
            }
        }

        /// <summary>
        /// Exporta datos a Excel con los filtros actuales
        /// </summary>
        [HttpGet]
        public ActionResult ExportarExcel(FiltrosVariablesPBI filtros)
        {
            if (!VerificarSuperAdmin())
                return RedirectToAction("Index");

            try
            {
                var resultado = _service.ConsultarDatos(filtros);

                if (resultado.TotalRegistros == 0)
                {
                    TempData["InfoMessage"] = "No hay datos para exportar con los filtros seleccionados";
                    return RedirectToAction("Index");
                }

                using (var package = new ExcelPackage())
                {
                    string nombreHoja = string.IsNullOrWhiteSpace(filtros?.NombreTabla) ? "Variables PBI" : filtros.NombreTabla;
                    if (nombreHoja.Length > 31) nombreHoja = nombreHoja.Substring(0, 31);
                    var ws = package.Workbook.Worksheets.Add(nombreHoja);

                    // Encabezados
                    var columnas = new[] {
                        "Tabla", "Variable", "Orden", "Subtotal", "Variable Padre",
                        "Variable Indicador", "Clase Variable", "Agrupación KEY",
                        "Agrupación KEY ABR", "Agrupación KEY Orden", "Var. Padre Real",
                        "Var. Padre ABR", "Var. Padre Real Orden", "Usuario Cargó", "Fecha Carga"
                    };

                    for (int i = 0; i < columnas.Length; i++)
                    {
                        var cell = ws.Cells[1, i + 1];
                        cell.Value = columnas[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(99, 102, 241));
                        cell.Style.Font.Color.SetColor(Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }

                    // Colores alternantes por NombreTabla
                    Color[] coloresTabla = new[]
                    {
                        Color.FromArgb(248, 250, 252),
                        Color.FromArgb(238, 242, 255),
                        Color.FromArgb(236, 253, 245),
                        Color.FromArgb(254, 249, 235)
                    };

                    int fila = 2;
                    string tablaAnterior = null;
                    int grupoIdx = -1;

                    foreach (var ov in resultado.Filas)
                    {
                        if (ov.NombreTabla != tablaAnterior)
                        {
                            grupoIdx++;
                            tablaAnterior = ov.NombreTabla;
                        }

                        Color colorFondo = coloresTabla[grupoIdx % coloresTabla.Length];

                        var valores = new object[]
                        {
                            ov.NombreTabla, ov.Variable, ov.OrdenVariable_,
                            ov.SubtotalVariable ? "Sí" : "No",
                            ov.VariablePadre, ov.VariableIndicador, ov.ClaseVariable,
                            ov.AgrupacionKEY, ov.AgrupacionKEYABR, ov.AgrupacionKEYOrden,
                            ov.VariablePadreReal, ov.VariablePadreAbr, ov.VariablePadreRealOrden,
                            ov.UsuarioCargo,
                            ov.FechaCarga.HasValue ? (object)ov.FechaCarga.Value : ""
                        };

                        for (int col = 0; col < valores.Length; col++)
                        {
                            var cell = ws.Cells[fila, col + 1];
                            cell.Value = valores[col];
                            if (valores[col] is DateTime dt)
                                cell.Style.Numberformat.Format = "dd/mm/yyyy hh:mm";
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(colorFondo);
                            cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.LightGray);
                        }

                        fila++;
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    ws.Cells[1, 1, 1, columnas.Length].AutoFilter = true;
                    ws.View.FreezePanes(2, 1);

                    string nombreArchivo = $"VariablesPBI_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    byte[] fileBytes = package.GetAsByteArray();
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}
