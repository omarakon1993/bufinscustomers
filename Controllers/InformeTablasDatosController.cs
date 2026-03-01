using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using Newtonsoft.Json;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class InformeTablasDatosController : BaseController
    {
        private InformeTablasDatosService _service = new InformeTablasDatosService();

        /// <summary>
        /// Vista principal de consulta de informes de tablas de datos
        /// </summary>
        public ActionResult InformeTablasDatos()
        {
            // Obtener información del usuario actual
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            // Cargar tablas disponibles
            ViewBag.Tablas = _service.ObtenerTablasDisponibles();

            // Cargar empresas (solo para admin)
            if (esAdmin)
            {
                ViewBag.Empresas = _service.ObtenerEmpresas();
            }
            else
            {
                // Para usuarios no admin, solo mostrar su empresa
                var empresas = _service.ObtenerEmpresas();
                ViewBag.Empresas = empresas.Where(e => e.Id == idEmpresa).ToList();
            }

            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            return View("~/Views/Informes/InformeTablasDatos.cshtml");
        }

        /// <summary>
        /// Obtiene los años disponibles para una tabla específica
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerAnios(string nombreTabla, int? idEmpresa = null)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Si no es admin, usar su empresa
                if (!esAdmin)
                {
                    idEmpresa = usuario?.IdEmpresa;
                }

                var anios = _service.ObtenerAñosDisponibles(nombreTabla, idEmpresa);

                return Json(new { success = true, anios }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener años: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Obtiene las variables disponibles para una tabla específica
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerVariables(string nombreTabla, int? idEmpresa = null)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Si no es admin, usar su empresa
                if (!esAdmin)
                {
                    idEmpresa = usuario?.IdEmpresa;
                }

                var variables = _service.ObtenerVariablesDisponibles(nombreTabla, idEmpresa);

                return Json(new { success = true, variables }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener variables: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Consulta los datos con filtros aplicados
        /// </summary>
        [HttpPost]
        public JsonResult ConsultarDatos(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Validar que el usuario tenga permiso para ver la empresa solicitada
                if (!esAdmin && filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                {
                    return Json(new { success = false, message = "No tiene permisos para consultar datos de otra empresa" });
                }

                // Realizar consulta
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaUsuario);

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

                        // Formatear valores según el tipo
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
                                // Formatear fechas de ejecución con hora
                                if (kvp.Key.Contains("Ejecucion") || kvp.Key.Contains("FechaEjecucion"))
                                {
                                    valorFormateado = ((DateTime)kvp.Value).ToString("dd/MM/yyyy HH:mm:ss");
                                }
                                else
                                {
                                    valorFormateado = ((DateTime)kvp.Value).ToString("dd/MM/yyyy");
                                }
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
        /// Consulta la IA con los datos actuales y una pregunta opcional
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> ConsultarConIA(FiltrosInformeTablasDatos filtros, string pregunta)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Misma validación de permisos que ConsultarDatos
                if (!esAdmin && filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "No tiene permisos para consultar datos de otra empresa." });
                }

                // Año obligatorio para el análisis IA (reduce el volumen de datos)
                if (!filtros.Anio.HasValue)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "Debe seleccionar un Año para el análisis IA." });
                }

                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaUsuario);

                if (resultado.TotalRegistros == 0)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "No hay datos para analizar con los filtros seleccionados." });
                }

                string apiKey = (System.Configuration.ConfigurationManager.AppSettings["OpenAIApiKey"] ?? "").Trim();
                var iaService = new IAService(apiKey);

                var tablaAmigable = _service.ObtenerTablasDisponibles()
                    .FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;

                var promptConfig = new GestorPromptsService().ObtenerPorCodigo("RESUMEN_GERENCIAL");
                string instrucciones = promptConfig?.TextoPrompt;

                var request = new IAConsultaRequest
                {
                    Pregunta = string.IsNullOrWhiteSpace(pregunta) ? null : pregunta.Trim(),
                    DatosJson = JsonConvert.SerializeObject(resultado.Filas),
                    NombreTabla = tablaAmigable,
                    FiltrosDescripcion = ConstruirDescripcionFiltros(filtros)
                };

                var response = await iaService.ConsultarAsync(request, instrucciones);
                response.FilasEnviadas = resultado.TotalRegistros;
                response.TotalFilas = resultado.TotalRegistros;
                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = $"Error al procesar la consulta: {ex.Message}" });
            }
        }

        private string ConstruirDescripcionFiltros(FiltrosInformeTablasDatos filtros)
        {
            var partes = new List<string>();

            if (filtros.IdEmpresa.HasValue)
                partes.Add($"Empresa ID {filtros.IdEmpresa}");

            if (filtros.Anio.HasValue)
                partes.Add($"Año {filtros.Anio}");

            if (filtros.Mes.HasValue)
            {
                string[] meses = { "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
                                   "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
                int mes = filtros.Mes.Value;
                partes.Add($"Mes {(mes >= 1 && mes <= 12 ? meses[mes] : mes.ToString())}");
            }

            if (!string.IsNullOrWhiteSpace(filtros.Variable))
                partes.Add($"Variable '{filtros.Variable}'");

            return partes.Count > 0 ? string.Join(", ", partes) : "Sin filtros adicionales";
        }

        /// <summary>
        /// Exporta los datos consultados a Excel
        /// </summary>
        [HttpPost]
        public ActionResult ExportarExcel(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Validar permisos
                if (!esAdmin && filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                {
                    TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                    return RedirectToAction("InformeTablasDatos");
                }

                // Obtener datos
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaUsuario);

                if (resultado.TotalRegistros == 0)
                {
                    TempData["InfoMessage"] = "No hay datos para exportar con los filtros seleccionados";
                    return RedirectToAction("InformeTablasDatos");
                }

                // Crear archivo Excel
                using (var package = new ExcelPackage())
                {
                    // Obtener nombre amigable de la tabla
                    var tablas = _service.ObtenerTablasDisponibles();
                    var tablaSeleccionada = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
                    string nombreHoja = tablaSeleccionada?.NombreAmigable ?? filtros.NombreTabla;

                    // Limitar el nombre de la hoja a 31 caracteres (límite de Excel)
                    if (nombreHoja.Length > 31)
                        nombreHoja = nombreHoja.Substring(0, 31);

                    var worksheet = package.Workbook.Worksheets.Add(nombreHoja);

                    // Agregar encabezados con nombres amigables
                    int col = 1;
                    foreach (var nombreColumna in resultado.Columnas)
                    {
                        var cell = worksheet.Cells[1, col];
                        cell.Value = _service.ObtenerNombreAmigableColumna(nombreColumna);

                        // Estilo del encabezado
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(99, 102, 241)); // Color del tema
                        cell.Style.Font.Color.SetColor(Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);

                        col++;
                    }

                    // Agregar datos
                    int fila = 2;
                    foreach (var registro in resultado.Filas)
                    {
                        col = 1;
                        foreach (var nombreColumna in resultado.Columnas)
                        {
                            var cell = worksheet.Cells[fila, col];
                            var valor = registro.ContainsKey(nombreColumna) ? registro[nombreColumna] : null;

                            if (valor != null)
                            {
                                // Asignar valor según tipo
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

                            // Estilo de la celda
                            cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.LightGray);

                            col++;
                        }
                        fila++;
                    }

                    // Autoajustar columnas
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                    // Agregar filtros automáticos
                    worksheet.Cells[1, 1, 1, resultado.Columnas.Count].AutoFilter = true;

                    // Congelar primera fila
                    worksheet.View.FreezePanes(2, 1);

                    // Generar nombre de archivo
                    string nombreArchivo = $"Informe_{nombreHoja}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    // Retornar archivo
                    byte[] fileBytes = package.GetAsByteArray();
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("InformeTablasDatos");
            }
        }
    }
}
