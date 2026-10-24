using bufinscustomers.Helpers;
using bufinscustomers.Models;
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
    /// <summary>
    /// Base de los informes gerenciales de Flujo de caja e Flujo de efectivo (comparten estructura, filtros, exportaciones e
    /// IA; cada uno lee su modelo — ver InformeFlujoService). Las clases hijas solo fijan el tipo y el nombre del controlador.
    /// Habilitados para todos los roles; el acceso real se acota por empresa/grupo.
    /// </summary>
    public abstract class InformeFlujoBaseController : BaseController
    {
        protected abstract FlujoTipo TipoFlujo { get; }
        protected abstract string NombreControlador { get; }
        protected abstract string FuncionIA { get; }
        protected abstract string CodigoPrompt { get; }
        protected abstract string PromptPorDefecto { get; }

        private InformeFlujoService _servicio;
        protected InformeFlujoService Servicio { get { return _servicio ?? (_servicio = new InformeFlujoService(TipoFlujo)); } }
        private readonly InformeTablasDatosService _empresaService = new InformeTablasDatosService();

        private string Pref { get { return "Flujo" + TipoFlujo + "_"; } }   // FlujoCaja_ / FlujoEfectivo_
        private string NombreModelo { get { return TipoFlujo == FlujoTipo.Caja ? "Flujo de Caja" : "Flujo de Efectivo"; } }

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

            var empresas = _empresaService.ObtenerEmpresas();
            if (!esAdmin)
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            ViewBag.Empresas = empresas;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = usuario?.IdEmpresa ?? 0;
            ViewBag.Tipo = TipoFlujo.ToString();
            ViewBag.Controlador = NombreControlador;

            return View("~/Views/Informes/InformeFlujo.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerMeses(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var meses = Servicio.ObtenerMesesDisponibles(idEmpresa, idEscenario)
                    .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                var escenariosConDatos = Servicio.ObtenerEscenariosConDatos(idEmpresa);
                var ult = Servicio.ObtenerUltimoMesReal(idEmpresa, idEscenario);
                return Json(new { success = true, meses, escenariosConDatos, ultimoReal = ult == null ? (object)null : new { anio = ult.Anio, mes = ult.Mes } });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ObtenerMeses");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosFlujo filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var reporte = Servicio.ConstruirReporte(filtros);
                var jsonResult = Json(new { success = true, reporte });
                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ConsultarReporte");
                return Json(new { success = false, message = "Error al consultar el reporte: " + MensajeError(ex) });
            }
        }

        // ===== IA =====

        /// <summary>Datos que se envían a la IA: ya calculados y sin nulos (KPIs, flujo mensual y el total de cada línea).</summary>
        private static string DatosParaIA(FlujoReporteViewModel r)
        {
            return JsonConvert.SerializeObject(new
            {
                medida = r.Medida,
                hayPresupuesto = r.HayPresupuesto,
                ultimoMesReal = r.UltimoMesReal,
                kpis = r.Kpis,
                meses = r.Meses,
                puente = r.Cascada,
                filas = r.Filas.Select(f => new
                {
                    f.Descripcion, f.Codigo, f.EsSubtotal, f.EsTotal, f.Total, f.TotalPpto, f.TotalAnt,
                    f.VariacionAbsoluta, f.VariacionPorcentual, f.VariacionAnioPorcentual
                })
            }, JsonIASinNulos);
        }

        private string DescripcionFiltros(FlujoReporteViewModel r, string nombreEmpresa)
        {
            return $"{NombreModelo}. Empresa {nombreEmpresa}, {r.MesDesde:00}/{r.AnioDesde} a {r.MesHasta:00}/{r.AnioHasta}, escenario {r.IdEscenario}, " +
                   $"cifras {(r.Medida == FlujoMedida.Proyectado ? "proyectadas (real hasta el mes " + r.UltimoMesReal + " y proyeccion despues)" : "reales")}" +
                   (r.HayPresupuesto ? ", comparadas contra presupuesto" : "") +
                   (r.HayEscenarioComparar ? $", escenario de comparacion {r.IdEscenarioComparar}" : "");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> GenerarInsightsIA(FiltrosFlujo filtros)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, FuncionIA);
                if (denegado != null) return denegado;

                var reporte = Servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R(Pref + "SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string filtrosDescripcion = DescripcionFiltros(reporte, nombreEmpresa);

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = FuncionIA,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = null,
                        DatosJson = DatosParaIA(reporte),
                        NombreTabla = NombreModelo,
                        FiltrosDescripcion = filtrosDescripcion
                    },
                    CodigoPrompt = CodigoPrompt,
                    PromptPorDefecto = PromptPorDefecto,
                    // La vista separa los insights por etiqueta (acepta español e inglés): en inglés se fijan las etiquetas.
                    Traducir = R,
                    Idioma = IdiomaIA,
                    InstruccionEnIngles = "Respond in English. Start the three lines with exactly these labels, in this order: \"Cash generation:\", \"Use of cash:\", \"Recommended action:\". Do not translate or change the labels.",
                    NombreTablaAuditoria = NombreModelo,
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Filas.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".GenerarInsightsIA");
                return Json(new IAConsultaResponse { Exitoso = false, Error = "Error al generar el resumen: " + MensajeError(ex) });
            }
        }

        /// <summary>"Pregúntale a este informe": pregunta libre sobre el reporte ya calculado (se reconstruye en el servidor con
        /// los mismos filtros, nunca con datos del cliente). Se mide con la misma función de IA que los insights.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> PreguntarInforme(FiltrosFlujo filtros, string pregunta)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                int idEmpresa = filtros?.IdEmpresa ?? 0;

                if (filtros == null || !TieneAccesoEmpresa(idEmpresa))
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R("Common_SinPermisos") });

                pregunta = (pregunta ?? "").Trim();
                int maxPregunta = IAModuloHelper.MaxCharsPregunta();
                if (pregunta.Length == 0 || pregunta.Length > maxPregunta)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = string.Format(R("IAInforme_PreguntaInvalida"), maxPregunta) });

                var denegado = ValidarAccesoIA(usuario, esAdmin, idEmpresa, FuncionIA);
                if (denegado != null) return denegado;

                var reporte = Servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos)
                    return Json(new IAConsultaResponse { Exitoso = false, Error = R(Pref + "SinDatosDesc") });

                string nombreEmpresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? "—";
                string filtrosDescripcion = DescripcionFiltros(reporte, nombreEmpresa);

                var response = await new IAGateway().EjecutarAsync(new IASolicitud
                {
                    Funcion = FuncionIA,
                    Usuario = usuario,
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = nombreEmpresa,
                    Request = new IAConsultaRequest
                    {
                        Pregunta = pregunta,
                        DatosJson = DatosParaIA(reporte),
                        NombreTabla = NombreModelo,
                        FiltrosDescripcion = filtrosDescripcion
                    },
                    Traducir = R,
                    Idioma = IdiomaIA,
                    NombreTablaAuditoria = NombreModelo,
                    FiltrosDescripcion = filtrosDescripcion,
                    FilasAnalizadas = reporte.Filas.Count
                });

                return Json(response);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".PreguntarInforme");
                return Json(new IAConsultaResponse { Exitoso = false, Error = R("IAInforme_Error") });
            }
        }

        // ===== Exportaciones =====

        public ActionResult ExportarExcel(FiltrosFlujo filtros, bool soloSubtotales = false)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var reporte = Servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos || reporte.Filas.Count == 0)
                {
                    TempData["InfoMessage"] = R(Pref + "SinDatosDesc");
                    return RedirectToAction("Index");
                }

                var bytes = GenerarExcel(reporte, soloSubtotales, out string nombreArchivo);
                return File(bytes, InformeCorreoHelper.MimeExcel, nombreArchivo);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ExportarExcel");
                TempData["ErrorMessage"] = "Error al exportar a Excel: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>Envía el informe por correo desde el botón Exportar (componente estándar): Excel (el mismo de la descarga) y
        /// PDF (el que arma el navegador) como adjuntos y, en el cuerpo, los indicadores. El reporte se reconstruye en el
        /// servidor con los filtros recibidos.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosFlujo filtros, bool soloSubtotales = false, string correos = null,
            bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });

            try
            {
                var reporte = Servicio.ConstruirReporte(filtros);
                if (reporte.SinDatos || reporte.Filas.Count == 0)
                    return Json(new { exito = false, mensaje = R(Pref + "SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados()
                    .FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);

                var bytes = GenerarExcel(reporte, soloSubtotales, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string periodo = NombrePeriodo(reporte);
                var contexto = new List<(string etiqueta, string valor)>
                {
                    (R("Flujo_CorreoPeriodo"), periodo), (R("Common_LabelEscenario"), escenario),
                    (R("Flujo_LabelMedida"), R("Flujo_Medida_" + reporte.Medida))
                };

                // Indicadores: moneda en millones, porcentajes en %; el color depende de si el movimiento es favorable.
                var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>();
                foreach (var k in reporte.Kpis)
                {
                    string etiqueta = R(Pref + "Kpi_" + k.Codigo);
                    string valor = k.NoAplica ? R("Flujo_KpiNoAplica")
                        : (k.Formato == "pct" ? (k.Valor * 100m).ToString("0.0") + " %" : (k.Valor / 1000000m).ToString("N1") + " M");
                    string detalle = "";
                    if (reporte.HayPresupuesto && !k.NoAplica && k.VariacionPptoPorcentual.HasValue)
                        detalle = (k.VariacionPptoPorcentual.Value * 100m).ToString("+0.0;-0.0;0.0") + (k.Formato == "pct" ? " pp " : " % ") + R("Flujo_CorreoVsPpto");
                    int signo = k.Neutral || k.NoAplica ? 0 : (k.Valor >= 0m ? 1 : -1);
                    kpis.Add((etiqueta, valor, detalle, signo));
                }

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                new EmailService().EnviarInforme(string.Join(", ", lista), R(Pref + "PageTitle"), empresa, false, contexto, kpis,
                    null, null, null, mensaje, quienEnvia, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa));

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Informes, AuditoriaAccion.Enviar, "InformeFlujo" + TipoFlujo, filtros.IdEmpresa.ToString(),
                    $"Informe de {NombreModelo.ToLowerInvariant()} enviado por correo a {string.Join(", ", lista)}: '{empresa}' — {periodo}, escenario {filtros.IdEscenario}.",
                    null,
                    new { empresa, periodo, escenario = filtros.IdEscenario, medida = reporte.Medida, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        // ===== Excel =====

        private static readonly string[] PaletaSeries = { "6366F1", "D97706", "94A3B8" };

        /// <summary>Libro de Excel del informe (lo usan la descarga y el correo): Resumen (indicadores), Flujo mensual (con gráfico
        /// nativo), Detalle por mes y Puente de caja.</summary>
        private byte[] GenerarExcel(FlujoReporteViewModel r, bool soloSubtotales, out string nombreArchivo)
        {
            string empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == r.IdEmpresa)?.Nombre ?? "Empresa";
            string periodo = NombrePeriodo(r);
            string medida = R("Flujo_Medida_" + r.Medida);
            string titulo = R(Pref + "PageTitle");
            string nombreEsc = r.HayEscenarioComparar
                ? (EscenarioCacheHelper.ObtenerEscenariosCacheados().FirstOrDefault(e => e.Id == r.IdEscenarioComparar)?.Nombre ?? ("#" + r.IdEscenarioComparar))
                : null;

            string hojaResumen = R("Flujo_ExcelHojaResumen"), hojaFlujo = R("Flujo_ExcelHojaFlujoMensual"),
                   hojaDetalle = R("Flujo_ExcelHojaDetalle"), hojaPuente = R("Flujo_ExcelHojaPuente");
            const string fmtNum = "#,##0;[Red]-#,##0", fmtPct = "0.0%";
            byte[] bytes;
            int filaEnc = 4, colFin = 3, colPpto = 0;
            var etiquetasMes = r.Meses.Select(m => NombreMes(m.Mes).Substring(0, Math.Min(3, NombreMes(m.Mes).Length)) + " " + m.Anio + (m.Proyectado ? "*" : "")).ToList();

            using (var wb = new XLWorkbook())
            {
                // --- Resumen: indicadores
                var wsR = wb.Worksheets.Add(hojaResumen);
                EncabezadoHoja(wsR, titulo + " — " + empresa, periodo + " · " + medida);
                var colsR = new List<string> { R("Flujo_ColIndicador"), medida };
                if (r.HayPresupuesto) { colsR.Add(R("Flujo_Medida_ppto")); colsR.Add(R("Flujo_ColVarPpto")); }
                if (r.HayAnioAnterior) { colsR.Add(R("Flujo_ColAnioAnterior")); colsR.Add(R("Flujo_ColVarAnio")); }
                if (r.HayEscenarioComparar) colsR.Add(nombreEsc);
                for (int c = 0; c < colsR.Count; c++) wsR.Cell(4, 1 + c).Value = colsR[c];
                int f = 5;
                foreach (var k in r.Kpis)
                {
                    string fm = k.Formato == "pct" ? fmtPct : fmtNum;
                    int c = 1;
                    wsR.Cell(f, c++).Value = R(Pref + "Kpi_" + k.Codigo);
                    if (k.NoAplica) { wsR.Cell(f, c).Value = R("Flujo_KpiNoAplica"); }
                    else EscribirValor(wsR.Cell(f, c), k.Valor, fm);
                    c++;
                    if (r.HayPresupuesto) { EscribirValor(wsR.Cell(f, c++), k.NoAplica ? (decimal?)null : k.ValorPpto, fm); EscribirValor(wsR.Cell(f, c++), k.NoAplica ? (decimal?)null : k.VariacionPptoPorcentual, k.Formato == "pct" ? "0.0%" : fmtPct); }
                    if (r.HayAnioAnterior) { EscribirValor(wsR.Cell(f, c++), k.NoAplica ? (decimal?)null : k.ValorAnt, fm); EscribirValor(wsR.Cell(f, c++), k.NoAplica ? (decimal?)null : k.VariacionAnioPorcentual, fmtPct); }
                    if (r.HayEscenarioComparar) EscribirValor(wsR.Cell(f, c++), k.NoAplica ? (decimal?)null : k.ValorEsc, fm);
                    f++;
                }
                EstiloTabla(wsR, 4, f - 1, colsR.Count);

                // --- Flujo mensual (+ gráfico)
                var wsF = wb.Worksheets.Add(hojaFlujo);
                EncabezadoHoja(wsF, R("Flujo_ExcelHojaFlujoMensual") + " — " + empresa, periodo + " · " + medida);
                wsF.Cell(filaEnc, 1).Value = R("Flujo_ColMes");
                wsF.Cell(filaEnc, 2).Value = R(Pref + "FlujoNeto") + " (" + medida + ")";
                wsF.Cell(filaEnc, 3).Value = R("Flujo_NetoAcumulado");
                if (r.HayPresupuesto)
                {
                    colPpto = 4;
                    wsF.Cell(filaEnc, 4).Value = R(Pref + "FlujoNeto") + " (" + R("Flujo_Medida_ppto") + ")";
                    wsF.Cell(filaEnc, 5).Value = R("Flujo_NetoAcumulado") + " (" + R("Flujo_Medida_ppto") + ")";
                    colFin = 5;
                }
                for (int i = 0; i < r.Meses.Count; i++)
                {
                    var m = r.Meses[i];
                    wsF.Cell(filaEnc + 1 + i, 1).Value = etiquetasMes[i];
                    EscribirValor(wsF.Cell(filaEnc + 1 + i, 2), m.Neto, fmtNum);
                    EscribirValor(wsF.Cell(filaEnc + 1 + i, 3), m.NetoAcum, fmtNum);
                    if (r.HayPresupuesto)
                    {
                        EscribirValor(wsF.Cell(filaEnc + 1 + i, 4), m.NetoPpto, fmtNum);
                        EscribirValor(wsF.Cell(filaEnc + 1 + i, 5), m.NetoPptoAcum, fmtNum);
                    }
                }
                EstiloTabla(wsF, filaEnc, filaEnc + r.Meses.Count, colFin);
                if (r.HayMesesProyectados) wsF.Cell(filaEnc + r.Meses.Count + 2, 1).Value = "* " + R("Flujo_NotaMesProyectado");

                // --- Detalle por mes
                var wsD = wb.Worksheets.Add(hojaDetalle);
                EncabezadoHoja(wsD, R("Flujo_ExcelHojaDetalle") + " — " + empresa, periodo + " · " + medida);
                int nMes = r.MesesRango.Count;
                wsD.Cell(4, 1).Value = R("Flujo_ColLinea");
                for (int i = 0; i < nMes; i++)
                    wsD.Cell(4, 2 + i).Value = NombreMes(r.MesesRango[i].Mes).Substring(0, Math.Min(3, NombreMes(r.MesesRango[i].Mes).Length)) + " " + r.MesesRango[i].Anio + (r.MesesRango[i].Proyectado ? "*" : "");
                int cc = 2 + nMes;
                wsD.Cell(4, cc++).Value = R("Flujo_ColTotal");
                int cPpto = 0, cVar = 0, cVarPct = 0, cAnt = 0, cVarAnio = 0, cEsc = 0;
                if (r.HayPresupuesto) { cPpto = cc; wsD.Cell(4, cc++).Value = R("Flujo_Medida_ppto"); cVar = cc; wsD.Cell(4, cc++).Value = R("Flujo_ColVarAbs"); cVarPct = cc; wsD.Cell(4, cc++).Value = R("Flujo_ColVarPct"); }
                if (r.HayAnioAnterior) { cAnt = cc; wsD.Cell(4, cc++).Value = R("Flujo_ColAnioAnterior"); cVarAnio = cc; wsD.Cell(4, cc++).Value = R("Flujo_ColVarAnio"); }
                if (r.HayEscenarioComparar) { cEsc = cc; wsD.Cell(4, cc++).Value = string.Format(R("Flujo_ColVsEscenarioFmt"), nombreEsc); }
                int colFinD = cc - 1;
                var filas = r.Filas.Where(x => !soloSubtotales || x.EsSubtotal).ToList();
                int fd = 5;
                foreach (var x in filas)
                {
                    wsD.Cell(fd, 1).Value = NombreLinea(x);
                    for (int i = 0; i < nMes; i++) EscribirValor(wsD.Cell(fd, 2 + i), x.Valores[i], fmtNum);
                    EscribirValor(wsD.Cell(fd, 2 + nMes), x.Total, fmtNum);
                    if (cPpto > 0) { EscribirValor(wsD.Cell(fd, cPpto), x.TotalPpto, fmtNum); EscribirValor(wsD.Cell(fd, cVar), x.VariacionAbsoluta, fmtNum); EscribirValor(wsD.Cell(fd, cVarPct), x.VariacionPorcentual, fmtPct); }
                    if (cAnt > 0) { EscribirValor(wsD.Cell(fd, cAnt), x.TotalAnt, fmtNum); EscribirValor(wsD.Cell(fd, cVarAnio), x.VariacionAnioPorcentual, fmtPct); }
                    if (cEsc > 0) EscribirValor(wsD.Cell(fd, cEsc), x.VariacionEscenarioPorcentual, fmtPct);
                    if (x.EsSubtotal)
                    {
                        var rg = wsD.Range(fd, 1, fd, colFinD);
                        rg.Style.Font.Bold = true;
                        rg.Style.Fill.BackgroundColor = XLColor.FromArgb(244, 243, 251);
                    }
                    fd++;
                }
                EstiloTabla(wsD, 4, fd - 1, colFinD);

                // --- Puente
                var wsP = wb.Worksheets.Add(hojaPuente);
                EncabezadoHoja(wsP, R("Flujo_ExcelHojaPuente") + " — " + empresa, periodo + " · " + medida);
                wsP.Cell(4, 1).Value = R("Flujo_ColConcepto");
                wsP.Cell(4, 2).Value = R("Flujo_ColMovimiento");
                wsP.Cell(4, 3).Value = R("Flujo_ColNivel");
                int fp = 5;
                decimal nivel = 0m;
                foreach (var b in r.Cascada)
                {
                    wsP.Cell(fp, 1).Value = R(Pref + "Casc_" + b.Etiqueta);
                    if (b.Tipo == "total") { nivel = b.Hasta; EscribirValor(wsP.Cell(fp, 2), b.Hasta, fmtNum); }
                    else { nivel += b.Delta; EscribirValor(wsP.Cell(fp, 2), b.Delta, fmtNum); }
                    EscribirValor(wsP.Cell(fp, 3), nivel, fmtNum);
                    if (b.Tipo == "total") wsP.Range(fp, 1, fp, 3).Style.Font.Bold = true;
                    fp++;
                }
                EstiloTabla(wsP, 4, fp - 1, 3);

                // Logo de la empresa (si tiene) junto al título de cada hoja; sin logo el libro queda igual que siempre.
                ExcelLogoHelper.ColocarEnTodas(wb, ExcelLogoHelper.Obtener(r.IdEmpresa));
                using (var ms = new MemoryStream()) { wb.SaveAs(ms); bytes = ms.ToArray(); }
            }

            // Gráfico nativo en "Flujo mensual": barras del flujo neto (principal y, si hay, presupuesto).
            try
            {
                var series = new List<SerieGraficoExcel>
                {
                    new SerieGraficoExcel { Nombre = R(Pref + "FlujoNeto") + " (" + medida + ")", Columna = 2, ColorHex = PaletaSeries[0],
                        Valores = r.Meses.Select(m => (double?)m.Neto).ToList() }
                };
                if (r.HayPresupuesto)
                    series.Add(new SerieGraficoExcel { Nombre = R(Pref + "FlujoNeto") + " (" + R("Flujo_Medida_ppto") + ")", Columna = colPpto, ColorHex = PaletaSeries[1],
                        Valores = r.Meses.Select(m => (double?)m.NetoPpto).ToList() });
                bytes = ExcelChartHelper.AgregarGrafico(bytes, hojaFlujo, R("Flujo_GraficoTitulo"), true, filaEnc, filaEnc + 1,
                    filaEnc + r.Meses.Count, 1, etiquetasMes, series, "#,##0", colFin + 2, filaEnc - 1);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".GenerarExcel (gráfico)");
            }

            nombreArchivo = $"{NombreModelo.Replace(" ", "")}_{empresa}_{r.AnioHasta}{r.MesHasta:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
            return bytes;
        }

        private string NombreLinea(FlujoFilaReporte f)
        {
            return string.IsNullOrEmpty(f.Codigo) ? f.Descripcion : R(Pref + "Fila_" + f.Codigo);
        }

        private static void EncabezadoHoja(IXLWorksheet ws, string titulo, string subtitulo)
        {
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(2, 1).Value = subtitulo + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        private static void EscribirValor(IXLCell celda, decimal? valor, string formato)
        {
            if (!valor.HasValue) return;
            celda.Value = (double)valor.Value;
            celda.Style.NumberFormat.Format = formato;
        }

        private static void EstiloTabla(IXLWorksheet ws, int filaEnc, int filaFin, int colFin)
        {
            var enc = ws.Range(filaEnc, 1, filaEnc, colFin);
            enc.Style.Font.Bold = true;
            enc.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
            enc.Style.Font.FontColor = XLColor.White;
            enc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var cuerpo = ws.Range(filaEnc, 1, filaFin, colFin);
            cuerpo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cuerpo.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            cuerpo.Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
            ws.Column(1).Style.Font.Bold = true;
            ws.Columns().AdjustToContents();
            ws.SheetView.Freeze(filaEnc, 1);
        }

        private string NombrePeriodo(FlujoReporteViewModel r)
        {
            string a = NombreMes(r.MesDesde) + " " + r.AnioDesde, b = NombreMes(r.MesHasta) + " " + r.AnioHasta;
            return a == b ? a : a + " – " + b;
        }

        private string NombreMes(int mes)
        {
            return mes >= 1 && mes <= 12 ? R("Common_Mes" + mes) : mes.ToString();
        }

        private bool TieneAccesoEmpresa(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return UsuarioSesionHelper.EsSuperAdmin() || EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa);
        }
    }
}
