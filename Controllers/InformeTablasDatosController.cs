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
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarDatos(FiltrosInformeTablasDatos filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaUsuario = usuario?.IdEmpresa;

                // Usuarios no-SuperAdmin solo pueden ver su propia empresa
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                        return Json(new { success = false, message = "No tiene permisos para consultar datos de otra empresa" });
                    filtros.IdEmpresa = idEmpresaUsuario;
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

                // Usuarios no-SuperAdmin solo pueden ver su propia empresa
                if (!esAdmin)
                {
                    if (filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = "No tiene permisos para consultar datos de otra empresa." });
                    filtros.IdEmpresa = idEmpresaUsuario;
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
                await Task.WhenAll(tApiKey, tModelo, tMaxTokens);

                string apiKey   = (tApiKey.Result ?? (System.Configuration.ConfigurationManager.AppSettings["OpenAIApiKey"] ?? "")).Trim();
                string modeloIA = tModelo.Result ?? "gpt-4o-mini";
                int maxTokensIA = (int.TryParse(tMaxTokens.Result, out int ptk) && ptk > 0) ? ptk : 1024;

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

                var response = await iaService.ConsultarAsync(request, instrucciones, guardrail, modeloIA, maxTokensIA);
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
                    if (filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                    {
                        TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                        return RedirectToAction("InformeTablasDatos");
                    }
                    filtros.IdEmpresa = idEmpresaUsuario;
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
                    if (filtros.IdEmpresa.HasValue && filtros.IdEmpresa != idEmpresaUsuario)
                    {
                        TempData["ErrorMessage"] = "No tiene permisos para exportar datos de otra empresa";
                        return RedirectToAction("InformeTablasDatos");
                    }
                    filtros.IdEmpresa = idEmpresaUsuario;
                }

                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaUsuario);
                var tablas    = _service.ObtenerTablasDisponibles();
                string nombreTabla = tablas.FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;
                string hojaNombre  = nombreTabla.Length > 31 ? nombreTabla.Substring(0, 31) : nombreTabla;

                using (var package = new ExcelPackage())
                {
                    // ── Hoja 1: Datos ──────────────────────────────────────────
                    var wsData = package.Workbook.Worksheets.Add(hojaNombre);
                    int col = 1;
                    foreach (var c in resultado.Columnas)
                    {
                        var cell = wsData.Cells[1, col];
                        cell.Value = _service.ObtenerNombreAmigableColumna(c);
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(99, 102, 241));
                        cell.Style.Font.Color.SetColor(Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        col++;
                    }
                    int fila = 2;
                    foreach (var registro in resultado.Filas)
                    {
                        col = 1;
                        foreach (var c in resultado.Columnas)
                        {
                            var cell  = wsData.Cells[fila, col];
                            var valor = registro.ContainsKey(c) ? registro[c] : null;
                            if (valor != null)
                            {
                                if (valor is decimal || valor is double || valor is float)
                                { cell.Value = valor; cell.Style.Numberformat.Format = "#,##0.00"; }
                                else if (valor is int || valor is long)
                                { cell.Value = valor; cell.Style.Numberformat.Format = "#,##0"; }
                                else if (valor is DateTime)
                                { cell.Value = (DateTime)valor; cell.Style.Numberformat.Format = "dd/mm/yyyy"; }
                                else cell.Value = valor.ToString();
                            }
                            cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.LightGray);
                            col++;
                        }
                        fila++;
                    }
                    wsData.Cells[wsData.Dimension.Address].AutoFitColumns();
                    wsData.Cells[1, 1, 1, resultado.Columnas.Count].AutoFilter = true;
                    wsData.View.FreezePanes(2, 1);

                    // ── Hoja 2: Análisis IA ────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(textoAnalisis))
                    {
                        string hojaNombreIA = R("IA_Excel_HojaAnalisis") ?? "Análisis IA";
                        var wsIA = package.Workbook.Worksheets.Add(hojaNombreIA);

                        wsIA.Cells[1, 1].Value = R("IA_PDF_Title") ?? "Análisis Gerencial IA";
                        wsIA.Cells[1, 1].Style.Font.Bold = true;
                        wsIA.Cells[1, 1].Style.Font.Size = 14;
                        wsIA.Cells[1, 1].Style.Font.Color.SetColor(Color.FromArgb(79, 70, 229));

                        wsIA.Cells[2, 1].Value = ConstruirDescripcionFiltros(filtros);
                        wsIA.Cells[2, 1].Style.Font.Italic = true;
                        wsIA.Cells[2, 1].Style.Font.Color.SetColor(Color.Gray);

                        wsIA.Cells[3, 1].Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                        wsIA.Cells[3, 1].Style.Font.Italic = true;
                        wsIA.Cells[3, 1].Style.Font.Size = 9;
                        wsIA.Cells[3, 1].Style.Font.Color.SetColor(Color.Gray);

                        string textoLimpio = LimpiarMarkdown(textoAnalisis);
                        var parrafos = textoLimpio.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        int filaIA = 5;
                        foreach (var parrafo in parrafos)
                        {
                            string linea = parrafo.Trim();
                            if (string.IsNullOrWhiteSpace(linea)) continue;
                            wsIA.Cells[filaIA, 1].Value      = linea;
                            wsIA.Cells[filaIA, 1].Style.WrapText = true;
                            if (linea.StartsWith("•") == false && linea.Length < 100 && !linea.Contains(".") && !linea.Contains(","))
                                wsIA.Cells[filaIA, 1].Style.Font.Bold = true;
                            filaIA++;
                        }
                        wsIA.Column(1).Width = 110;
                    }

                    string archivo = $"AnalisisIA_{hojaNombre}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo);
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
