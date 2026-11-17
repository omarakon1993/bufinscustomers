using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// «Versión para comité» del Informe mensual de gerencia (se abre desde su selector «Vista»): el cierre de un mes armado como documento para
    /// presentar, decidir y firmar — resultados, balance y caja del mes, caja disponible, PYG en una hoja, indicadores en atención, líneas de negocio,
    /// comentarios del cierre, recuadro de decisiones y firmas. Reutiliza los servicios de Gerencia mensual, PYG y Flujo de caja
    /// (InformeEjecutivoService) y su permiso (INFORMES_GERENCIA_MENSUAL). Acceso por empresa/grupo.
    /// </summary>
    [ValidarSesion]
    [RequierePermiso("INFORMES_GERENCIA_MENSUAL")]
    public class InformeComiteController(
        InformeEjecutivoService service,
        InformeTablasDatosService empresaService,
        EmailService emailService,
        AuditoriaService auditoriaService) : BaseController
    {
        private const string Nombre = "InformeComite";

        public ActionResult Index(int idEmpresa = 0, int idEscenario = 0)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var empresas = empresaService.ObtenerEmpresas();
            if (!esAdmin)
            {
                var ids = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => ids.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresas;
            ViewBag.Escenarios = EscenarioCacheHelper.ObtenerEscenariosCacheados();
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = usuario?.IdEmpresa ?? 0;
            // Al venir del selector de vistas se conservan la empresa y el escenario (solo si el usuario tiene acceso).
            ViewBag.PreEmpresa = idEmpresa > 0 && TieneAccesoEmpresa(idEmpresa) ? idEmpresa : 0;
            ViewBag.PreEscenario = idEscenario > 0 ? idEscenario : 0;
            return View("~/Views/Informes/InformeComite.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerMeses(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                var meses = service.MesesPyg(idEmpresa, idEscenario).OrderByDescending(m => m.Anio).ThenByDescending(m => m.Mes)
                    .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                return Json(new { success = true, meses });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, Nombre + ".ObtenerMeses");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ConsultarReporte(FiltrosEjecutivo filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa) || filtros.Mes < 1 || filtros.Mes > 12)
                    return Json(new { success = false, message = R("Common_SinPermisos") });
                var json = Json(new { success = true, reporte = service.ConstruirComite(filtros) });
                json.MaxJsonLength = int.MaxValue;
                return json;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, Nombre + ".ConsultarReporte");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        public ActionResult ExportarExcel(FiltrosEjecutivo filtros)
        {
            try
            {
                if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }
                var reporte = service.ConstruirComite(filtros);
                if (reporte.SinDatos)
                {
                    TempData["InfoMessage"] = R("EJ_SinDatosDesc");
                    return RedirectToAction("Index");
                }
                return File(GenerarExcel(filtros, reporte, out string nombre), InformeCorreoHelper.MimeExcel, nombre);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, Nombre + ".ExportarExcel");
                TempData["ErrorMessage"] = MensajeError(ex);
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarPorCorreo(FiltrosEjecutivo filtros, string correos = null, bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
                return Json(new { exito = false, mensaje = R("Common_SinPermisos") });
            if (!InformeCorreoHelper.TryDestinatarios(usuario, correos, incluirmeAMi, R, out var lista, out var error))
                return Json(new { exito = false, mensaje = error });
            try
            {
                // El informe se reconstruye en el servidor: nunca se confía en cifras que lleguen del navegador.
                var r = service.ConstruirComite(filtros);
                if (r.SinDatos) return Json(new { exito = false, mensaje = R("EJ_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados().FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);
                var bytes = GenerarExcel(filtros, r, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string corte = NombreMes(filtros.Mes) + " " + filtros.Anio;
                var contexto = new List<(string etiqueta, string valor)> { (R("EJ_MesCorte"), corte), (R("Common_LabelEscenario"), escenario) };
                var kpis = KpisCorreo(r);

                string quien = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                emailService.EnviarInforme(string.Join(", ", lista), R("CO_PageTitle"), empresa, false, contexto, kpis, null, null, null,
                    mensaje, quien, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa),
                    ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA));

                auditoriaService.RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Enviar, Nombre, filtros.IdEmpresa.ToString(),
                    $"{R("CO_PageTitle")} enviado por correo a {string.Join(", ", lista)}: '{empresa}' — corte {corte}, escenario {filtros.IdEscenario}.",
                    null, new { empresa, corte, escenario = filtros.IdEscenario, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, Nombre + ".EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        // ===== Formatos =====

        private static string Millones(decimal v) => EjecutivoExcel.Millones(v);
        private static string Pct(decimal? v) => v.HasValue ? (v.Value * 100m).ToString("+0.0;-0.0;0.0") + " %" : "—";
        private static int Signo(decimal? v) => v.HasValue && v.Value != 0m ? (v.Value > 0m ? 1 : -1) : 0;

        private List<(string etiqueta, string valor, string detalle, int signo)> KpisCorreo(ComiteReporte k)
        {
            var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>();
            if (k.Gerencia != null)
                foreach (var x in k.Gerencia.Resultados)
                    kpis.Add((R("GM_Res_" + x.Codigo), Millones(x.Real), x.VarPresupuesto.HasValue ? Pct(x.VarPresupuesto) + " " + R("EJ_VsPptoCorto") : "", Signo(x.VarPresupuesto)));
            if (k.Caja != null && k.Caja.HayCajaCorte) kpis.Add((R("CD_Hoy"), Millones(k.Caja.CajaCorte), "", 0));
            if (k.Caja != null && k.Caja.HayProyeccion) kpis.Add((R("CD_Proy"), Millones(k.Caja.CajaProyectada), "", Signo(k.Caja.CajaProyectada)));
            return kpis;
        }

        // ===== Excel =====

        private byte[] GenerarExcel(FiltrosEjecutivo f, ComiteReporte r, out string nombreArchivo)
        {
            string nombreEmpresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == f.IdEmpresa)?.Nombre ?? "Empresa";
            var tema = ExcelTemaHelper.Resolver(f.Tema, f.TemaP, f.TemaS, f.TemaA);
            var logo = ExcelLogoHelper.Obtener(f.IdEmpresa);
            var ctx = EjecutivoExcel.Crear(R, NombreMes, R("CO_PageTitle"), nombreEmpresa, logo, tema);

            using (var wb = new XLWorkbook())
            {
                HojaResumen(wb, r, ctx);
                if (r.Pyg != null && !r.Pyg.SinDatos) EjecutivoExcel.HojaPyg(wb, r.Pyg, ctx);
                if (r.Caja != null && !r.Caja.SinDatos) EjecutivoExcel.HojaCaja(wb, r.Caja, ctx);
                HojaComentarios(wb, r.Comentarios, f, ctx);

                if (logo != null) ExcelLogoHelper.ColocarEnTodas(wb, logo);
                nombreArchivo = $"Comite_{nombreEmpresa}_{f.Anio}{f.Mes:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
                using (var ms = new MemoryStream()) { wb.SaveAs(ms); return ms.ToArray(); }
            }
        }

        private void HojaResumen(XLWorkbook wb, ComiteReporte k, EjecutivoExcel.Contexto x)
        {
            var g = k.Gerencia;
            var ws = wb.Worksheets.Add(R("CO_ExcelHojaResumen"));
            int fila = x.Nueva(ws, NombreMes(k.Mes) + " " + k.Anio);
            if (g != null && g.HayResultados)
            {
                x.Cab(ws, fila, new[] { R("GM_SecResultados"), R("GM_Col_Real"), R("GM_Col_Presupuesto"), R("GM_Col_VarPpto"), R("GM_Col_VarAnio"), R("GM_Col_AcumReal"), R("GM_Col_AcumPpto"), R("GM_Col_AcumVarPpto") }); fila++;
                foreach (var c in g.Resultados)
                {
                    ws.Cell(fila, 1).Value = R("GM_Res_" + c.Codigo);
                    ExcelCellHelper.SetValue(ws.Cell(fila, 2), c.Real); ExcelCellHelper.SetValue(ws.Cell(fila, 3), c.Presupuesto);
                    ExcelCellHelper.SetValue(ws.Cell(fila, 4), c.VarPresupuesto); ExcelCellHelper.SetValue(ws.Cell(fila, 5), c.VarAnioAnterior);
                    ExcelCellHelper.SetValue(ws.Cell(fila, 6), c.AcumReal); ExcelCellHelper.SetValue(ws.Cell(fila, 7), c.AcumPresupuesto); ExcelCellHelper.SetValue(ws.Cell(fila, 8), c.AcumVarPresupuesto);
                    foreach (int col in new[] { 2, 3, 6, 7 }) ws.Cell(fila, col).Style.NumberFormat.Format = "#,##0";
                    foreach (int col in new[] { 4, 5, 8 }) ws.Cell(fila, col).Style.NumberFormat.Format = "0.0%";
                    fila++;
                }
                fila++;
            }
            if (g != null && g.HayBalance)
            {
                x.Cab(ws, fila, new[] { R("GM_SecBalance"), R("GM_Col_Valor"), R("GM_Col_VarCierre") }); fila++;
                foreach (var b in g.Balance)
                {
                    ws.Cell(fila, 1).Value = R("BAL_Kpi" + b.Codigo);
                    if (b.NoAplica) ws.Cell(fila, 2).Value = R("BAL_KpiNoAplica");
                    else { ExcelCellHelper.SetValue(ws.Cell(fila, 2), b.Valor); ws.Cell(fila, 2).Style.NumberFormat.Format = b.Codigo == "RazonCorriente" ? "0.00" : (b.Codigo == "Endeudamiento" ? "0.0%" : "#,##0"); }
                    ExcelCellHelper.SetValue(ws.Cell(fila, 3), b.Variacion); ws.Cell(fila, 3).Style.NumberFormat.Format = "0.0%";
                    fila++;
                }
                fila++;
            }
            if (g != null && g.HayCaja)
            {
                x.Cab(ws, fila, new[] { R("GM_SecCaja"), R("GM_Col_Valor") }); fila++;
                var cm = new List<KeyValuePair<string, decimal?>>
                {
                    new KeyValuePair<string, decimal?>(R("GM_CajaInicial"), g.HayCajaInicial ? (decimal?)g.CajaInicial : null),
                    new KeyValuePair<string, decimal?>(R("GM_FlujoNeto"), g.FlujoNeto),
                    new KeyValuePair<string, decimal?>(R("GM_FlujoNetoPpto"), g.FlujoNetoPresupuesto),
                    new KeyValuePair<string, decimal?>(R("GM_Fcl"), g.FclOperativo),
                    new KeyValuePair<string, decimal?>(R("GM_CajaFinal"), g.HayCajaFinal ? (decimal?)g.CajaFinal : null)
                };
                foreach (var it in cm) { ws.Cell(fila, 1).Value = it.Key; ExcelCellHelper.SetValue(ws.Cell(fila, 2), it.Value); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0"; fila++; }
                fila++;
            }
            if (g != null && g.HayIndicadores)
            {
                x.Cab(ws, fila, new[] { R("GM_AtencionTitulo"), R("GM_Col_Valor"), R("GM_Col_Semaforo") }); fila++;
                foreach (var a in g.Atencion)
                {
                    ws.Cell(fila, 1).Value = R("Ind_" + a.Codigo + "_Nombre");
                    ws.Cell(fila, 2).Value = a.Valor.HasValue ? (a.Formato == "pct" ? (a.Valor.Value * 100m).ToString("0.0") + " %" : (a.Formato == "x" ? a.Valor.Value.ToString("0.00") + "x" : (a.Formato == "dias" ? a.Valor.Value.ToString("0") + " d" : Millones(a.Valor.Value)))) : "—";
                    ws.Cell(fila, 3).Value = R("GM_Nivel_" + a.Nivel); fila++;
                }
                fila++;
            }
            if (g != null && g.HayLineas)
            {
                x.Cab(ws, fila, new[] { R("LN_Col_Linea"), R("LN_Col_Ingresos"), R("LN_Col_PctIngresos"), R("LN_Col_MargenOperacional") }); fila++;
                foreach (var l in g.Lineas)
                {
                    ws.Cell(fila, 1).Value = l.Nombre;
                    ExcelCellHelper.SetValue(ws.Cell(fila, 2), l.Ingresos); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0";
                    ExcelCellHelper.SetValue(ws.Cell(fila, 3), l.PctIngresos); ws.Cell(fila, 3).Style.NumberFormat.Format = "0.0%";
                    ExcelCellHelper.SetValue(ws.Cell(fila, 4), l.MargenOperacional); ws.Cell(fila, 4).Style.NumberFormat.Format = "0.0%";
                    fila++;
                }
                fila++;
            }
            // Decisiones y firmas: espacio para completar en el documento impreso (igual que en el PDF).
            x.Cab(ws, fila, new[] { R("CO_Decisiones") }); fila++;
            ws.Cell(fila, 1).Value = R("CO_DecisionesAyuda"); ws.Cell(fila, 1).Style.Font.Italic = true; fila += 4;
            x.Cab(ws, fila, new[] { R("CO_Firmas"), R("CO_Nombre"), R("CO_Cargo"), R("CO_Fecha") }); fila++;
            foreach (var rol in new[] { "CO_Elaboro", "CO_Reviso", "CO_Aprobo" }) { ws.Cell(fila, 1).Value = R(rol); ws.Row(fila).Height = 28; fila++; }
            ws.Columns().AdjustToContents();
        }

        private void HojaComentarios(XLWorkbook wb, List<ComentarioGestion> comentarios, FiltrosEjecutivo f, EjecutivoExcel.Contexto x)
        {
            if (comentarios == null || comentarios.Count == 0) return;
            var ws = wb.Worksheets.Add(R("CO_ExcelHojaComentarios"));
            int fila = x.Nueva(ws, NombreMes(f.Mes) + " " + f.Anio);
            string[] enc = { R("Com_ColInforme"), R("Com_ColSobre"), R("Com_ColComentario"), R("Com_ColAutor"), R("Com_ColFecha") };
            x.Cab(ws, fila, enc); fila++;
            foreach (var c in comentarios)
            {
                ws.Cell(fila, 1).Value = R("Com_Inf_" + c.Informe); ws.Cell(fila, 2).Value = c.Clave ?? ""; ws.Cell(fila, 3).Value = c.Texto;
                ws.Cell(fila, 4).Value = c.NombreUsuario; ws.Cell(fila, 5).Value = c.Fecha; fila++;
            }
            ws.Column(3).Width = 70; ws.Column(3).Style.Alignment.WrapText = true;
        }
    }
}
