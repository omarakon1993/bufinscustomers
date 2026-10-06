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

        // ── Límites del historial de conversación que envía el cliente (S04) ──
        private const int MaxCharsHistorialJson    = 2_000_000; // ~2 MB de JSON crudo antes de parsear
        private const int MaxMensajesHistorial     = 7;         // 1 mensaje de contexto + 3 pares Q&A
        private const int MaxCharsRespuestaPrevia  = 3_000;     // respuestas previas del asistente (salvo la última) se abrevian a esto
        private const int MaxCharsMensajeHistorial = 60_000;    // por mensaje (salvo el de contexto)
        private const int MaxCharsHistorialTotal   = 500_000;   // suma de todos los mensajes

        /// <summary>
        /// true solo para las tablas de resultados de Ejecución de Modelos — Análisis IA solo
        /// muestra y acepta esas (ver InformeTablasDatosService.ObtenerTablasModelos()).
        /// </summary>
        private bool EsTablaDeModelo(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla)
                && _service.ObtenerTablasModelos().Any(t => t.NombreTabla == nombreTabla);
        }

        /// <summary>
        /// Obtiene los años disponibles para una tabla específica
        /// </summary>
        [HttpGet]
        public JsonResult ObtenerAnios(string nombreTabla, int? idEmpresa = null, int? idEscenario = null)
        {
            try
            {
                if (!EsTablaDeModelo(nombreTabla))
                    return Json(new { success = false, message = R("Common_TablaNoValida") }, JsonRequestBehavior.AllowGet);

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Si no es admin, permitir su empresa o una del mismo grupo empresarial
                if (!esAdmin)
                {
                    idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                        ? idEmpresa
                        : usuario?.IdEmpresa;
                }

                var anios = _service.ObtenerAñosDisponibles(nombreTabla, idEmpresa, idEscenario);

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
        public JsonResult ObtenerVariables(string nombreTabla, int? idEmpresa = null, int? idEscenario = null)
        {
            try
            {
                if (!EsTablaDeModelo(nombreTabla))
                    return Json(new { success = false, message = R("Common_TablaNoValida") }, JsonRequestBehavior.AllowGet);

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Si no es admin, permitir su empresa o una del mismo grupo empresarial
                if (!esAdmin)
                {
                    idEmpresa = (idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa.Value))
                        ? idEmpresa
                        : usuario?.IdEmpresa;
                }

                var variables = _service.ObtenerVariablesDisponibles(nombreTabla, idEmpresa, idEscenario);

                return Json(new { success = true, variables }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al obtener variables: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Consulta la IA con los datos actuales y una pregunta opcional
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        // historialJson trae el prompt/respuestas de turnos anteriores; puede contener texto con
        // < >, JSON, etc. que la validación de request de ASP.NET marcaría como "peligroso" (HTTP 500).
        // El endpoint solo reenvía ese texto a OpenAI, nunca lo devuelve como HTML.
        [ValidateInput(false)]
        public async Task<JsonResult> ConsultarConIA(FiltrosInformeTablasDatos filtros, string pregunta, string historialJson = null, string modo = null)
        {
            try
            {
                if (!EsTablaDeModelo(filtros?.NombreTabla))
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_TablaNoValida") });
                }

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

                // Escenario obligatorio (todas las tablas de modelo lo tienen y evita analizar sin
                // querer el escenario equivocado). Año es OPCIONAL a propósito: dejarlo en blanco
                // trae varios años a la vez (acotado por MaxFilasConsulta) para que el usuario pueda
                // pedirle a la IA comparaciones año a año en la pregunta/prompt.
                if (!filtros.IdEscenario.HasValue)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_JS_EscenarioSeleccionar") });
                }

                // El acceso a filtros.IdEmpresa ya se validó arriba (empresa propia o de su mismo
                // grupo empresarial). Cada análisis es de UNA empresa: se filtra por la empresa
                // seleccionada, no por la del usuario, para que el filtro de empresa funcione en grupos.
                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = await _service.ConsultarDatosAsync(filtros, esAdmin, idEmpresaConsulta);

                if (resultado.TotalRegistros == 0)
                {
                    return Json(new IAConsultaResponse { Exitoso = false, Error = "No hay datos para analizar con los filtros seleccionados." });
                }

                // Deserializar y SANEAR el historial que envía el cliente (no es de confianza):
                // se rechaza un blob desproporcionado antes de parsearlo y luego se acota nº de
                // turnos y tamaño para que un cliente manipulado no infle el coste de cada llamada.
                List<MensajeChatIA> historial = null;
                if (!string.IsNullOrWhiteSpace(historialJson))
                {
                    if (historialJson.Length > MaxCharsHistorialJson)
                        return Json(new IAConsultaResponse { Exitoso = false, Error = R("IA_HistorialGrandeMensaje") });

                    try { historial = JsonConvert.DeserializeObject<List<MensajeChatIA>>(historialJson); }
                    catch { historial = null; }

                    historial = SanearHistorial(historial);
                }

                // Acceso del usuario + presupuesto mensual de tokens de la empresa (IAUsoService).
                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresaConsulta.GetValueOrDefault(), IAFuncion.Chat);
                if (denegado != null) return denegado;

                var tablaAmigable = _service.ObtenerTablasModelos()
                    .FirstOrDefault(t => t.NombreTabla == filtros.NombreTabla)?.NombreAmigable ?? filtros.NombreTabla;

                // Modo de análisis (N06): elige el prompt y el tope de tokens del resumen inicial.
                var cfgModo = ConfigModo(modo);

                // Datos en CSV compacto (≈ 40 % menos tokens que JSON) acotados por tamaño para no
                // exceder el contexto del modelo. Solo en la primera llamada (los turnos siguientes
                // reutilizan el contexto ya enviado).
                const int MaxCharsData = 200_000;
                string datosCsv = null;
                string notaDatos = null;
                int filasEnviadas = resultado.TotalRegistros;

                if (historial == null || historial.Count == 0)
                    datosCsv = ConstruirCsv(resultado.Columnas, resultado.Filas, MaxCharsData, out filasEnviadas, out notaDatos);

                var request = new IAConsultaRequest
                {
                    Pregunta = string.IsNullOrWhiteSpace(pregunta) ? null : pregunta.Trim(),
                    DatosJson = datosCsv,
                    NotaDatos = notaDatos,
                    FormatoDatos = "csv",
                    NombreTabla = tablaAmigable,
                    FiltrosDescripcion = ConstruirDescripcionFiltros(filtros),
                    Historial = historial
                };

                var empresa = _service.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa.GetValueOrDefault());

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = IAFuncion.Chat,
                    Usuario = usuario,
                    IdEmpresa = filtros.IdEmpresa ?? 0,
                    NombreEmpresa = empresa?.Nombre ?? "—",
                    Request = request,
                    CodigoPrompt = cfgModo.Codigo,
                    PromptPorDefecto = cfgModo.Fallback,
                    MaxTokensResumen = cfgModo.MaxTokens,
                    Traducir = R,
                    Idioma = IdiomaIA,
                    NombreTablaAuditoria = tablaAmigable,
                    FiltrosDescripcion = request.FiltrosDescripcion,
                    FilasAnalizadas = filasEnviadas,
                    TotalFilas = resultado.TotalRegistros
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new IAConsultaResponse { Exitoso = false, Error = $"Error al procesar la consulta: {ex.Message}" });
            }
        }

        /// <summary>Valoración 👍/👎 de una respuesta de IA (N02). Solo el autor puede valorarla.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ValorarRespuestaIA(int id, int? valor)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return Json(new { ok = false });

            int? v = null;
            if (valor.HasValue && (valor.Value == 0 || valor.Value == 1)) v = valor.Value;

            bool ok = new AuditoriaAnalisisIAService().Valorar(id, usuario.Id, v);
            return Json(new { ok });
        }

        // ── Modos de análisis (N06) ────────────────────────────────────────
        // Cada modo elige un prompt de GestorPrompts (por código) y un tope de tokens para el
        // resumen inicial. Si el código no está en GestorPrompts se usa el texto de reserva.

        private const string FALLBACK_DETALLADO =
            "Elabora un análisis financiero detallado y estructurado (6 a 10 párrafos) con subtítulos. " +
            "Cubre desempeño por período, principales partidas, márgenes, variaciones relevantes, causas probables " +
            "y recomendaciones accionables. Usa cifras concretas de los datos.";
        private const string FALLBACK_CIFRAS =
            "Responde SOLO con cifras clave en viñetas y una tabla markdown cuando aplique: totales por período, " +
            "variaciones absolutas y porcentuales y los 5 valores más altos y más bajos. Sin narrativa, sin recomendaciones.";
        private const string FALLBACK_RIESGO =
            "Actúa como auditor: identifica señales de alerta y riesgos en los datos (caídas de ingresos, sobrecostos, " +
            "tensión de liquidez, partidas atípicas, inconsistencias). Devuelve una lista priorizada (alto/medio/bajo) " +
            "con la cifra que la sustenta y una acción sugerida para cada una.";

        private (string Codigo, int MaxTokens, string Fallback) ConfigModo(string modo)
        {
            switch ((modo ?? "").Trim().ToLowerInvariant())
            {
                case "detallado": return ("ANALISIS_DETALLADO", 4000, FALLBACK_DETALLADO);
                case "cifras":    return ("SOLO_CIFRAS",        1200, FALLBACK_CIFRAS);
                case "riesgo":    return ("ALERTAS_RIESGO",     1800, FALLBACK_RIESGO);
                default:          return ("RESUMEN_GERENCIAL",  1800, null); // null → IAService usa su default
            }
        }

        /// <summary>
        /// Serializa las filas a CSV compacto (separador ';', encabezados con nombres amigables,
        /// números con punto decimal invariante, fechas yyyy-MM-dd). Escribe filas hasta llegar a
        /// <paramref name="maxChars"/> y devuelve por <paramref name="filasEscritas"/> cuántas cupieron.
        /// </summary>
        private string ConstruirCsv(List<string> columnas, List<Dictionary<string, object>> filas,
            int maxChars, out int filasEscritas, out string notaDatos)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            Func<Dictionary<string, object>, string, string> valorTexto = (fila, c) =>
            {
                object v = (fila != null && fila.TryGetValue(c, out var val)) ? val : null;
                if (v == null || v == DBNull.Value)                 return "";
                if (v is DateTime dt)                               return dt.ToString("yyyy-MM-dd");
                // Normalize: 1234.5000 → 1234.5 (los decimales de SQL arrastran ceros de escala que cuestan tokens).
                if (v is decimal || v is double || v is float)      return (Convert.ToDecimal(v, inv) / 1.0000000000000000000000000000m).ToString(inv);
                if (v is int || v is long || v is short || v is byte) return Convert.ToInt64(v).ToString(inv);
                return v.ToString();
            };

            // Reducción SIN pérdida de información: las columnas vacías en todas las filas y las que
            // valen lo mismo en todas (IdEscenario, IdEmpresa, Año si se filtró uno…) no se repiten
            // en cada renglón; se declaran una sola vez en la nota que acompaña los datos.
            var columnasOmitidas = new List<string>();
            var constantes = new List<string>();
            var columnasUsadas = columnas;
            if (filas.Count >= 2)
            {
                columnasUsadas = new List<string>();
                foreach (var c in columnas)
                {
                    string primero = valorTexto(filas[0], c);
                    bool constante = true;
                    for (int i = 1; i < filas.Count && constante; i++)
                        constante = string.Equals(valorTexto(filas[i], c), primero, StringComparison.Ordinal);

                    if (!constante) { columnasUsadas.Add(c); continue; }

                    string nombre = _service.ObtenerNombreAmigableColumna(c);
                    if (primero.Length == 0) columnasOmitidas.Add(nombre);
                    else constantes.Add(nombre + "=" + primero);
                }
                if (columnasUsadas.Count == 0) { columnasUsadas = columnas; constantes.Clear(); columnasOmitidas.Clear(); }
            }

            var partesNota = new List<string>();
            if (constantes.Count > 0)
                partesNota.Add("Valores iguales en TODAS las filas (omitidos de la tabla CSV): " + string.Join("; ", constantes) + ".");
            if (columnasOmitidas.Count > 0)
                partesNota.Add("Columnas sin datos (omitidas): " + string.Join(", ", columnasOmitidas) + ".");
            notaDatos = partesNota.Count > 0 ? string.Join(" ", partesNota) : null;

            var sb = new System.Text.StringBuilder();
            sb.Append(string.Join(";", columnasUsadas.Select(c => CsvCampo(_service.ObtenerNombreAmigableColumna(c))))).Append('\n');

            int n = 0;
            foreach (var fila in filas)
            {
                var linea = string.Join(";", columnasUsadas.Select(c => CsvCampo(valorTexto(fila, c))));
                if (sb.Length + linea.Length + 1 > maxChars) break;
                sb.Append(linea).Append('\n');
                n++;
            }
            filasEscritas = n;
            return sb.ToString();
        }

        private static string CsvCampo(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return (s.IndexOf(';') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }

        private string ConstruirDescripcionFiltros(FiltrosInformeTablasDatos filtros)
        {
            var partes = new List<string>();

            if (filtros.IdEmpresa.HasValue)
                partes.Add($"Empresa ID {filtros.IdEmpresa}");

            if (filtros.IdEscenario.HasValue)
            {
                var nombreEscenario = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                    .FirstOrDefault(e => e.Id == filtros.IdEscenario.Value)?.Nombre ?? ("#" + filtros.IdEscenario.Value);
                partes.Add($"Escenario {nombreEscenario}");
            }

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
        /// Exporta datos + análisis de IA en un Excel con dos hojas
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcelConIA(FiltrosInformeTablasDatos filtros, string textoAnalisis)
        {
            try
            {
                if (!EsTablaDeModelo(filtros?.NombreTabla))
                {
                    TempData["ErrorMessage"] = R("Common_TablaNoValida");
                    return RedirectToAction("Index", "AnalisisIA");
                }

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
                            return RedirectToAction("Index", "AnalisisIA");
                        }
                    }
                    else
                    {
                        filtros.IdEmpresa = idEmpresaUsuario;
                    }
                }

                // Cada exportación es de UNA empresa: la seleccionada (ya validada arriba), no la del usuario.
                var idEmpresaConsulta = filtros.IdEmpresa ?? idEmpresaUsuario;
                var resultado = _service.ConsultarDatos(filtros, esAdmin, idEmpresaConsulta);
                var tablas    = _service.ObtenerTablasModelos();
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
                return RedirectToAction("Index", "AnalisisIA");
            }
        }

        /// <summary>Resuelve en español/inglés (R) el aviso de presupuesto que decidió
        /// IAUsoService.EvaluarAcceso y lo reparte a los Super Admin.</summary>

        /// <summary>
        /// Acota el historial que llega del cliente (S04): descarta mensajes con forma inválida,
        /// limita el tamaño de cada mensaje (salvo el primero, que es el contexto que generó el
        /// servidor), conserva solo el contexto + los últimos turnos, y aplica un tope de tamaño
        /// total soltando los mensajes intermedios más antiguos.
        /// </summary>
        private static List<MensajeChatIA> SanearHistorial(List<MensajeChatIA> historial)
        {
            if (historial == null || historial.Count == 0) return null;

            var limpio = historial
                .Where(m => m != null
                         && !string.IsNullOrEmpty(m.Contenido)
                         && (m.Rol == "user" || m.Rol == "assistant" || m.Rol == "system"))
                .ToList();
            if (limpio.Count == 0) return null;

            for (int i = 1; i < limpio.Count; i++)
                if (limpio[i].Contenido.Length > MaxCharsMensajeHistorial)
                    limpio[i].Contenido = limpio[i].Contenido.Substring(0, MaxCharsMensajeHistorial);

            if (limpio.Count > MaxMensajesHistorial)
            {
                var recortado = new List<MensajeChatIA> { limpio[0] };
                recortado.AddRange(limpio.Skip(limpio.Count - (MaxMensajesHistorial - 1)));
                limpio = recortado;
            }

            // Cada turno de seguimiento reenvía TODO el historial: las respuestas previas largas (un
            // análisis detallado son ~4.000 tokens) se pagan en cada pregunta. Se abrevian las respuestas
            // del asistente que no son la última; el contexto con los datos (índice 0) y la última
            // respuesta quedan intactos. El recorte es determinista, así que el prefijo sigue siendo
            // idéntico entre turnos y la caché de prompts de OpenAI lo sigue aprovechando.
            int ultimaAsistente = limpio.FindLastIndex(m => m.Rol == "assistant");
            for (int i = 1; i < limpio.Count; i++)
            {
                if (i == ultimaAsistente || limpio[i].Rol != "assistant") continue;
                if (limpio[i].Contenido.Length > MaxCharsRespuestaPrevia)
                    limpio[i].Contenido = limpio[i].Contenido.Substring(0, MaxCharsRespuestaPrevia) + " […]";
            }

            while (limpio.Count > 2 && limpio.Sum(m => (long)m.Contenido.Length) > MaxCharsHistorialTotal)
                limpio.RemoveAt(1);

            return limpio;
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
