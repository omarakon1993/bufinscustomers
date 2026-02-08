using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class InformeRelacionamientosController : BaseController
    {
        private InformeRelacionamientosService _service = new InformeRelacionamientosService();

        /// <summary>
        /// Verifica si el usuario actual es administrador
        /// </summary>
        private bool VerificarAdmin()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return usuario?.Admin == 1;
        }

        /// <summary>
        /// Vista principal del informe de relacionamientos BUFINS (solo admin)
        /// </summary>
        public ActionResult InformeRelacionamientos()
        {
            if (!VerificarAdmin())
            {
                TempData["ErrorMessage"] = "No tiene permisos para acceder a esta sección";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Tablas = _service.ObtenerTablasDisponibles();
            return View("~/Views/Informes/InformeRelacionamientos.cshtml");
        }

        /// <summary>
        /// Obtiene los tipos disponibles para una tabla
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerTipos(string nombreTabla)
        {
            try
            {
                if (!VerificarAdmin())
                {
                    return Json(new { success = false, message = "No tiene permisos" }, JsonRequestBehavior.AllowGet);
                }

                var tipos = _service.ObtenerTiposDisponibles(nombreTabla);
                return Json(new { success = true, tipos }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener tipos: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Obtiene las descripciones disponibles para una tabla, opcionalmente filtradas por tipo
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerDescripciones(string nombreTabla, string tipo = null)
        {
            try
            {
                if (!VerificarAdmin())
                {
                    return Json(new { success = false, message = "No tiene permisos" }, JsonRequestBehavior.AllowGet);
                }

                var descripciones = _service.ObtenerDescripcionesDisponibles(nombreTabla, tipo);
                return Json(new { success = true, descripciones }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener descripciones: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }


        /// <summary>
        /// Consulta los datos con filtros aplicados
        /// </summary>
        [HttpPost]
        public JsonResult ConsultarDatos(FiltrosInformeRelacionamientos filtros)
        {
            try
            {
                if (!VerificarAdmin())
                {
                    return Json(new { success = false, message = "No tiene permisos" });
                }

                var resultado = _service.ConsultarDatos(filtros);

                // Mapear nombres de columnas a nombres amigables
                var columnasAmigables = resultado.Columnas.Select(c => new
                {
                    nombreTecnico = c,
                    nombreAmigable = _service.ObtenerNombreAmigableColumna(c)
                }).ToList();

                // Formatear los datos para la vista
                var datosFormateados = resultado.Filas.Select(fila =>
                {
                    var filaFormateada = new Dictionary<string, object>();
                    foreach (var kvp in fila)
                    {
                        object valorFormateado = kvp.Value;

                        if (kvp.Value != null)
                        {
                            if (kvp.Value is decimal)
                            {
                                valorFormateado = ((decimal)kvp.Value).ToString("N2");
                            }
                            else if (kvp.Value is double)
                            {
                                valorFormateado = ((double)kvp.Value).ToString("N2");
                            }
                            else if (kvp.Value is DateTime)
                            {
                                valorFormateado = ((DateTime)kvp.Value).ToString("dd/MM/yyyy");
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
                    totalRegistros = resultado.TotalRegistros
                }, JsonRequestBehavior.AllowGet);

                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al consultar datos: {ex.Message}" });
            }
        }

        /// <summary>
        /// Exporta los datos consultados a Excel
        /// </summary>
        [HttpPost]
        public ActionResult ExportarExcel(FiltrosInformeRelacionamientos filtros)
        {
            try
            {
                if (!VerificarAdmin())
                {
                    TempData["ErrorMessage"] = "No tiene permisos para exportar";
                    return RedirectToAction("InformeRelacionamientos");
                }

                var resultado = _service.ConsultarDatos(filtros);

                if (resultado.TotalRegistros == 0)
                {
                    TempData["InfoMessage"] = "No hay datos para exportar con los filtros seleccionados";
                    return RedirectToAction("InformeRelacionamientos");
                }

                using (var package = new ExcelPackage())
                {
                    var tablas = _service.ObtenerTablasDisponibles();
                    var tablaSeleccionada = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
                    string nombreHoja = tablaSeleccionada?.NombreAmigable ?? filtros.NombreTabla;

                    if (nombreHoja.Length > 31)
                        nombreHoja = nombreHoja.Substring(0, 31);

                    var worksheet = package.Workbook.Worksheets.Add(nombreHoja);

                    // Encabezados
                    int col = 1;
                    foreach (var nombreColumna in resultado.Columnas)
                    {
                        var cell = worksheet.Cells[1, col];
                        cell.Value = _service.ObtenerNombreAmigableColumna(nombreColumna);

                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(99, 102, 241));
                        cell.Style.Font.Color.SetColor(Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        col++;
                    }

                    // Datos con colores de agrupacion por descripcion
                    int fila = 2;
                    string descripcionAnterior = null;
                    int grupoIndex = 0;
                    Color[] coloresGrupo = new Color[]
                    {
                        Color.FromArgb(248, 250, 252),  // Gris muy claro
                        Color.FromArgb(238, 242, 255),  // Indigo muy claro
                        Color.FromArgb(236, 253, 245),  // Verde muy claro
                        Color.FromArgb(254, 249, 235)   // Amarillo muy claro
                    };

                    // Buscar indice de columna Descripcion
                    int idxDescripcion = resultado.Columnas.IndexOf("Descripcion");
                    if (idxDescripcion == -1)
                        idxDescripcion = resultado.Columnas.FindIndex(c => c.Equals("Descripcion", StringComparison.OrdinalIgnoreCase));

                    foreach (var registro in resultado.Filas)
                    {
                        // Determinar grupo por descripcion
                        string descripcionActual = null;
                        if (idxDescripcion >= 0)
                        {
                            var colDesc = resultado.Columnas[idxDescripcion];
                            descripcionActual = registro.ContainsKey(colDesc) ? registro[colDesc]?.ToString() : null;
                        }

                        if (descripcionActual != descripcionAnterior)
                        {
                            grupoIndex++;
                            descripcionAnterior = descripcionActual;
                        }

                        Color colorFondo = coloresGrupo[grupoIndex % coloresGrupo.Length];

                        col = 1;
                        foreach (var nombreColumna in resultado.Columnas)
                        {
                            var cell = worksheet.Cells[fila, col];
                            var valor = registro.ContainsKey(nombreColumna) ? registro[nombreColumna] : null;

                            if (valor != null)
                            {
                                if (valor is DateTime)
                                {
                                    cell.Value = (DateTime)valor;
                                    cell.Style.Numberformat.Format = "dd/mm/yyyy";
                                }
                                else if (valor is decimal || valor is double || valor is float)
                                {
                                    cell.Value = valor;
                                    cell.Style.Numberformat.Format = "#,##0.00";
                                }
                                else if (valor is int || valor is long)
                                {
                                    cell.Value = valor;
                                    cell.Style.Numberformat.Format = "#,##0";
                                }
                                else
                                {
                                    cell.Value = valor.ToString();
                                }
                            }

                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(colorFondo);
                            cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.LightGray);

                            col++;
                        }
                        fila++;
                    }

                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                    worksheet.Cells[1, 1, 1, resultado.Columnas.Count].AutoFilter = true;
                    worksheet.View.FreezePanes(2, 1);

                    string nombreArchivo = $"Relacionamientos_{nombreHoja}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    byte[] fileBytes = package.GetAsByteArray();
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("InformeRelacionamientos");
            }
        }
    }
}
