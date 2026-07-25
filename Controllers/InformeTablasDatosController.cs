using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
                // Para usuarios no admin, mostrar su empresa y las de su mismo grupo empresarial
                var empresas = _service.ObtenerEmpresas();
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                ViewBag.Empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
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

                // Si no es admin, permitir su empresa o una del mismo grupo empresarial
                if (!esAdmin)
                {
                    idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                        ? idEmpresa
                        : usuario?.IdEmpresa;
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

                // Si no es admin, permitir su empresa o una del mismo grupo empresarial
                if (!esAdmin)
                {
                    idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                        ? idEmpresa
                        : usuario?.IdEmpresa;
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
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarDatos(FiltrosInformeTablasDatos filtros)
        {
            try
            {
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
                    totalRegistros = resultado.TotalRegistros,
                    resultadosTruncados = resultado.ResultadosTruncados
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
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ConsultarConIA(FiltrosInformeTablasDatos filtros, string pregunta, string historialJson = null)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin pueden ver su propia empresa o una de su mismo grupo empresarial
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                            return Json(new IAConsultaResponse { Exitoso = false, Error = "No tiene permisos para consultar datos de otra empresa." });
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Año obligatorio para el análisis IA (reduce el volumen de datos)
                if (!filtros.Anio.HasValue)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "Debe seleccionar un Año para el análisis IA." });
                }

                var resultado = await _service.ConsultarDatosAsync(filtros, esAdmin, idEmpresaUsuario);

                if (resultado.TotalRegistros == 0)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "No hay datos para analizar con los filtros seleccionados." });
                }

                // Deserializar historial de conversación si viene del cliente
                List<MensajeChatIA> historial = null;
                if (!string.IsNullOrWhiteSpace(historialJson))
                {
                    try { historial = JsonConvert.DeserializeObject<List<MensajeChatIA>>(historialJson); }
                    catch { historial = null; }
                }

                // Quota diaria por usuario (super admin queda exento)
                if (!esAdmin)
                {
                    int limiteDiario = ObtenerLimiteConsultasIA(usuario.Id);
                    if (limiteDiario <= 0)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });

                    int consultasHoy = new AuditoriaAnalisisIAService().ContarConsultasHoy(usuario.Id);
                    if (consultasHoy >= limiteDiario)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_LimiteConsultasMensaje") });
                }

                // Leer configuración en paralelo
                var cfgSvc     = new ConfiguracionSistemaService();
                var tApiKey    = cfgSvc.ObtenerValorAsync("OpenAIApiKey");
                var tModelo    = cfgSvc.ObtenerValorAsync("OpenAIModel");
                var tMaxTokens = cfgSvc.ObtenerValorAsync("OpenAIMaxTokens");
                var tTemp      = cfgSvc.ObtenerValorAsync("OpenAITemperature");
                await Task.WhenAll(tApiKey, tModelo, tMaxTokens, tTemp);

                string apiKey   = (tApiKey.Result ?? "").Trim();
                string modeloIA = (tModelo.Result ?? "gpt-4o").Trim();
                int maxTokensIA = (int.TryParse(tMaxTokens.Result, out int ptk) && ptk > 0) ? ptk : 8000;
                // Si OpenAITemperature está vacío o no existe → null → no se envía al API
                double? temperatureIA = double.TryParse(
                    tTemp.Result,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double tVal) ? (double?)tVal : null;

                var iaService = new IAService(apiKey);

                var tablaAmigable = _service.ObtenerTablasDisponibles()
                    .FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;

                var promptConfig = new GestorPromptsService().ObtenerPorCodigo("RESUMEN_GERENCIAL");
                string instrucciones = promptConfig?.TextoPrompt;

                var guardrailConfig = new GestorPromptsService().ObtenerPorCodigo("GUARDRAIL_SISTEMA");
                string guardrail = guardrailConfig?.TextoPrompt;

                // Serializar datos limitando el tamaño para no exceder el contexto del modelo.
                // ~200 000 chars ≈ 50 000 tokens; deja ~78 000 tokens libres para el prompt y la respuesta
                // dentro de los 128 000 tokens que soporta la mayoría de modelos GPT-4o.
                const int MaxCharsData = 200_000;
                string datosJson = null;
                int filasEnviadas = resultado.TotalRegistros;

                if (historial == null || historial.Count == 0)
                {
                    var jsonCompleto = JsonConvert.SerializeObject(resultado.Filas);
                    if (jsonCompleto.Length <= MaxCharsData)
                    {
                        datosJson = jsonCompleto;
                    }
                    else
                    {
                        // Construir JSON fila a fila hasta respetar el límite
                        var sb = new System.Text.StringBuilder("[");
                        filasEnviadas = 0;
                        foreach (var fila in resultado.Filas)
                        {
                            var filaJson = JsonConvert.SerializeObject(fila);
                            var sep      = filasEnviadas == 0 ? "" : ",";
                            if (sb.Length + sep.Length + filaJson.Length + 1 > MaxCharsData) break;
                            sb.Append(sep).Append(filaJson);
                            filasEnviadas++;
                        }
                        sb.Append("]");
                        datosJson = sb.ToString();
                    }
                }

                var request = new IAConsultaRequest
                {
                    Pregunta = string.IsNullOrWhiteSpace(pregunta) ? null : pregunta.Trim(),
                    DatosJson = datosJson,
                    NombreTabla = tablaAmigable,
                    FiltrosDescripcion = ConstruirDescripcionFiltros(filtros),
                    Historial = historial
                };

                var response = await iaService.ConsultarAsync(request, instrucciones, guardrail, modeloIA, maxTokensIA, temperatureIA);
                response.FilasEnviadas = filasEnviadas;
                response.TotalFilas = resultado.TotalRegistros;

                if (response.Exitoso)
                {
                    try
                    {
                        var empresa = _service.ObtenerEmpresas()
                            .FirstOrDefault(e => e.Id == filtros.IdEmpresa.GetValueOrDefault());
                        new AuditoriaAnalisisIAService().Registrar(new AuditoriaAnalisisIA
                        {
                            IdUsuario       = usuario.Id,
                            NombreUsuario   = $"{usuario.Nombre} {usuario.Apellidos}".Trim(),
                            IdEmpresa       = filtros.IdEmpresa ?? 0,
                            NombreEmpresa   = empresa?.Nombre ?? "—",
                            NombreTabla     = tablaAmigable,
                            Filtros         = ConstruirDescripcionFiltros(filtros),
                            Pregunta        = request.Pregunta,
                            Respuesta       = response.Respuesta,
                            FechaPregunta   = DateTime.Now,
                            FilasAnalizadas = response.FilasEnviadas
                        });
                    }
                    catch { }
                }

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
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin solo pueden exportar su propia empresa
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                        {
                            TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                            return RedirectToAction("InformeTablasDatos");
                        }
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Obtener datos
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaUsuario);

                if (resultado.TotalRegistros == 0)
                {
                    TempData["InfoMessage"] = "No hay datos para exportar con los filtros seleccionados";
                    return RedirectToAction("InformeTablasDatos");
                }

                // Crear archivo Excel
                using (var package = new XLWorkbook())
                {
                    // Obtener nombre amigable de la tabla
                    var tablas = _service.ObtenerTablasDisponibles();
                    var tablaSeleccionada = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla);
                    string nombreHoja = tablaSeleccionada?.NombreAmigable ?? filtros.NombreTabla;

                    // Limitar el nombre de la hoja a 31 caracteres (límite de Excel)
                    if (nombreHoja.Length > 31)
                        nombreHoja = nombreHoja.Substring(0, 31);

                    var worksheet = package.Worksheets.Add(nombreHoja);

                    // Agregar encabezados con nombres amigables
                    int col = 1;
                    foreach (var nombreColumna in resultado.Columnas)
                    {
                        var cell = worksheet.Cell(1, col);
                        cell.Value = _service.ObtenerNombreAmigableColumna(nombreColumna);

                        // Estilo del encabezado
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241); // Color del tema
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        col++;
                    }

                    // Agregar datos
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
                                // Asignar valor según tipo
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

                            // Estilo de la celda
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);

                            col++;
                        }
                        fila++;
                    }

                    // Autoajustar columnas
                    worksheet.Columns().AdjustToContents();

                    // Agregar filtros automáticos
                    worksheet.Range(1, 1, 1, resultado.Columnas.Count).SetAutoFilter();

                    // Congelar primera fila
                    worksheet.SheetView.Freeze(1, 0);

                    // Generar nombre de archivo
                    string nombreArchivo = $"Informe_{nombreHoja}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    // Retornar archivo
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
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("InformeTablasDatos");
            }
        }

        /// <summary>
        /// Exporta datos + análisis de IA en un Excel con dos hojas
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcelConIA(FiltrosInformeTablasDatos filtros, string textoAnalisis)
        {
            try
            {
                var usuario          = UsuarioSesionHelper.UsuarioActual;
                var esAdmin          = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue)
                    {
                        if (!EmpresaAccesoHelper.TieneAcceso(usuario, filtros.IdEmpresa.Value))
                        {
                            TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                            return RedirectToAction("InformeTablasDatos");
                        }
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaUsuario);
                var tablas    = _service.ObtenerTablasDisponibles();
                string nombreTabla = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;
                string hojaNombre  = nombreTabla.Length > 31 ? nombreTabla.Substring(0, 31) : nombreTabla;

                using (var package = new XLWorkbook())
                {
                    // ── Hoja 1: Datos ──────────────────────────────────────────
                    var wsData = package.Worksheets.Add(hojaNombre);
                    int col = 1;
                    foreach (var c in resultado.Columnas)
                    {
                        var cell = wsData.Cell(1, col);
                        cell.Value = _service.ObtenerNombreAmigableColumna(c);
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        col++;
                    }
                    int fila = 2;
                    foreach (var registro in resultado.Filas)
                    {
                        col = 1;
                        foreach (var c in resultado.Columnas)
                        {
                            var cell  = wsData.Cell(fila, col);
                            var valor = registro.ContainsKey(c) ? registro[c] : null;
                            if (valor != null)
                            {
                                if (valor is decimal || valor is double || valor is float)
                                { ExcelCellHelper.SetValue(cell, valor); cell.Style.NumberFormat.Format = "#,##0.00"; }
                                else if (valor is int || valor is long)
                                { ExcelCellHelper.SetValue(cell, valor); cell.Style.NumberFormat.Format = "#,##0"; }
                                else if (valor is DateTime)
                                { cell.Value = (DateTime)valor; cell.Style.NumberFormat.Format = "dd/mm/yyyy"; }
                                else cell.Value = valor.ToString();
                            }
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            cell.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
                            col++;
                        }
                        fila++;
                    }
                    wsData.Columns().AdjustToContents();
                    wsData.Range(1, 1, 1, resultado.Columnas.Count).SetAutoFilter();
                    wsData.SheetView.Freeze(1, 0);

                    // ── Hoja 2: Análisis IA ────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(textoAnalisis))
                    {
                        string hojaNombreIA = R("IA_Excel_HojaAnalisis") ?? "Análisis IA";
                        var wsIA = package.Worksheets.Add(hojaNombreIA);

                        wsIA.Cell(1, 1).Value = R("IA_PDF_Title") ?? "Análisis Gerencial IA";
                        wsIA.Cell(1, 1).Style.Font.Bold = true;
                        wsIA.Cell(1, 1).Style.Font.FontSize = 14;
                        wsIA.Cell(1, 1).Style.Font.FontColor = XLColor.FromArgb(79, 70, 229);

                        wsIA.Cell(2, 1).Value = ConstruirDescripcionFiltros(filtros);
                        wsIA.Cell(2, 1).Style.Font.Italic = true;
                        wsIA.Cell(2, 1).Style.Font.FontColor = XLColor.FromColor(Color.Gray);

                        wsIA.Cell(3, 1).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                        wsIA.Cell(3, 1).Style.Font.Italic = true;
                        wsIA.Cell(3, 1).Style.Font.FontSize = 9;
                        wsIA.Cell(3, 1).Style.Font.FontColor = XLColor.FromColor(Color.Gray);

                        string textoLimpio = LimpiarMarkdown(textoAnalisis);
                        var parrafos = textoLimpio.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        int filaIA = 5;
                        foreach (var parrafo in parrafos)
                        {
                            string linea = parrafo.Trim();
                            if (string.IsNullOrWhiteSpace(linea)) continue;
                            wsIA.Cell(filaIA, 1).Value      = linea;
                            wsIA.Cell(filaIA, 1).Style.Alignment.WrapText = true;
                            if (linea.StartsWith("•") == false && linea.Length < 100 && !linea.Contains(".") && !linea.Contains(","))
                                wsIA.Cell(filaIA, 1).Style.Font.Bold = true;
                            filaIA++;
                        }
                        wsIA.Column(1).Width = 110;
                    }

                    string archivo = $"AnalisisIA_{hojaNombre}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar: {ex.Message}";
                return RedirectToAction("InformeTablasDatos");
            }
        }

        private int ObtenerLimiteConsultasIA(int idUsuario)
        {
            try
            {
                using (var cn = new System.Data.SqlClient.SqlConnection(CadenaConexion))
                {
                    var cmd = new System.Data.SqlClient.SqlCommand(
                        "SELECT LimiteConsultasIA FROM Usuarios WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    var result = cmd.ExecuteScalar();
                    return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
            catch { return 0; }
        }

        private string LimpiarMarkdown(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return texto;
            return texto
                .Replace("**", string.Empty)
                .Replace("*",  string.Empty)
                .Replace("### ", string.Empty)
                .Replace("## ",  string.Empty)
                .Replace("# ",   string.Empty)
                .Replace("- ",   "• ");
        }
    }
}
