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
    /// Base de los informes ejecutivos de un mes de corte: «PYG en una hoja», «¿Cuánta plata me queda?» (caja disponible) e «Informe para
    /// comité». Comparten filtros (empresa, escenario, mes), exportaciones (Excel · PDF · imagen · correo auditado) y vista; cada hijo solo
    /// fija el tipo y su permiso de menú. Las cifras salen de InformeEjecutivoService (que reutiliza los servicios de PYG, Flujo de caja y
    /// Gerencia mensual). Acceso por empresa/grupo.
    /// </summary>
    public abstract class InformeEjecutivoBaseController(
        InformeEjecutivoService service,
        InformeTablasDatosService empresaService,
        EmailService emailService,
        AuditoriaService auditoriaService) : BaseController
    {
        /// <summary>"hoja" | "caja" | "comite" (la vista y el JS ramifican por este valor).</summary>
        protected abstract string Tipo { get; }
        protected abstract string NombreControlador { get; }
        /// <summary>Prefijo de recursos de textos propios: PH_ / CD_ / CO_.</summary>
        protected abstract string Pref { get; }

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
            // Al venir del selector de vistas de otro informe se conservan la empresa y el escenario (solo si el usuario tiene acceso).
            ViewBag.PreEmpresa = idEmpresa > 0 && TieneAccesoEmpresa(idEmpresa) ? idEmpresa : 0;
            ViewBag.PreEscenario = idEscenario > 0 ? idEscenario : 0;
            ViewBag.Tipo = Tipo;
            ViewBag.Pref = Pref;
            ViewBag.Controlador = NombreControlador;
            return View("~/Views/Informes/InformeEjecutivo.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerMeses(int idEmpresa, byte idEscenario)
        {
            try
            {
                if (!TieneAccesoEmpresa(idEmpresa)) return Json(new { success = false, message = R("Common_SinPermisos") });
                var lista = Tipo == "caja" ? service.MesesCaja(idEmpresa, idEscenario) : service.MesesPyg(idEmpresa, idEscenario);
                var meses = lista.OrderByDescending(m => m.Anio).ThenByDescending(m => m.Mes).Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
                return Json(new { success = true, meses });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ObtenerMeses");
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
                object reporte = Construir(filtros);
                var json = Json(new { success = true, reporte });
                json.MaxJsonLength = int.MaxValue;
                return json;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ConsultarReporte");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        private object Construir(FiltrosEjecutivo f)
        {
            switch (Tipo)
            {
                case "caja": return service.ConstruirCaja(f);
                case "comite": return service.ConstruirComite(f);
                default: return service.ConstruirPygHoja(f);
            }
        }

        private static bool SinDatos(object r)
        {
            if (r is PygHojaReporte p) return p.SinDatos;
            if (r is CajaDisponibleReporte c) return c.SinDatos;
            return r is ComiteReporte k && k.SinDatos;
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
                var reporte = Construir(filtros);
                if (SinDatos(reporte))
                {
                    TempData["InfoMessage"] = R("EJ_SinDatosDesc");
                    return RedirectToAction("Index");
                }
                return File(GenerarExcel(filtros, reporte, out string nombre), InformeCorreoHelper.MimeExcel, nombre);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".ExportarExcel");
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
                var r = Construir(filtros);
                if (SinDatos(r)) return Json(new { exito = false, mensaje = R("EJ_SinDatosDesc") });

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == filtros.IdEmpresa)?.Nombre ?? "Empresa";
                string escenario = EscenarioCacheHelper.ObtenerEscenariosCacheados().FirstOrDefault(e => e.Id == filtros.IdEscenario)?.Nombre ?? ("#" + filtros.IdEscenario);
                var bytes = GenerarExcel(filtros, r, out string nombreArchivo);
                var adjuntos = InformeCorreoHelper.Adjuntos(nombreArchivo, bytes, InformeCorreoHelper.PdfDesdeBase64(pdfBase64));

                string corte = NombreMes(filtros.Mes) + " " + filtros.Anio;
                var contexto = new List<(string etiqueta, string valor)> { (R("EJ_MesCorte"), corte), (R("Common_LabelEscenario"), escenario) };
                var kpis = KpisCorreo(r);

                string quien = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                emailService.EnviarInforme(string.Join(", ", lista), R(Pref + "PageTitle"), empresa, false, contexto, kpis, null, null, null,
                    mensaje, quien, adjuntos, esIngles, ExcelLogoHelper.Png(filtros.IdEmpresa),
                    ExcelTemaHelper.Resolver(filtros.Tema, filtros.TemaP, filtros.TemaS, filtros.TemaA));

                auditoriaService.RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Enviar, NombreControlador, filtros.IdEmpresa.ToString(),
                    $"{R(Pref + "PageTitle")} enviado por correo a {string.Join(", ", lista)}: '{empresa}' — corte {corte}, escenario {filtros.IdEscenario}.",
                    null, new { empresa, corte, escenario = filtros.IdEscenario, destinatarios = lista, conPdf = adjuntos.Count > 1 },
                    filtros.IdEmpresa, entidadNombre: empresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("Common_ExpCorreo_Exito"), string.Join(", ", lista)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, NombreControlador + ".EnviarPorCorreo");
                return Json(new { exito = false, mensaje = R("Common_ExpCorreo_Error") });
            }
        }

        // ===== Formatos =====

        private static string Millones(decimal v) => (v / 1000000m).ToString("N1") + " M";
        private static string Pct(decimal? v) => v.HasValue ? (v.Value * 100m).ToString("+0.0;-0.0;0.0") + " %" : "—";
        private static int Signo(decimal? v) => v.HasValue && v.Value != 0m ? (v.Value > 0m ? 1 : -1) : 0;

        private List<(string etiqueta, string valor, string detalle, int signo)> KpisCorreo(object r)
        {
            var kpis = new List<(string etiqueta, string valor, string detalle, int signo)>();
            if (r is PygHojaReporte p)
            {
                foreach (var d in new[] { "Ingresos", "EBITDA", "Utilidad neta" })
                {
                    var fila = p.Filas.FirstOrDefault(x => string.Equals(x.Descripcion, d, StringComparison.OrdinalIgnoreCase));
                    if (fila != null) kpis.Add((fila.Descripcion, Millones(fila.Acumulado), fila.VarPresupuesto.HasValue ? Pct(fila.VarPresupuesto) + " " + R("EJ_VsPptoCorto") : "", Signo(fila.VarPresupuesto)));
                }
            }
            else if (r is CajaDisponibleReporte c)
            {
                if (c.HayCajaCorte) kpis.Add((R("CD_Hoy"), Millones(c.CajaCorte), "", 0));
                if (c.HayProyeccion) kpis.Add((R("CD_Proy"), Millones(c.CajaProyectada), "", Signo(c.CajaProyectada)));
                kpis.Add((R("CD_Entradas"), Millones(c.TotalFuentes), "", 1));
                kpis.Add((R("CD_Salidas"), Millones(c.TotalUsos), "", -1));
            }
            else if (r is ComiteReporte k)
            {
                if (k.Gerencia != null)
                    foreach (var x in k.Gerencia.Resultados)
                        kpis.Add((R("GM_Res_" + x.Codigo), Millones(x.Real), x.VarPresupuesto.HasValue ? Pct(x.VarPresupuesto) + " " + R("EJ_VsPptoCorto") : "", Signo(x.VarPresupuesto)));
                if (k.Caja != null && k.Caja.HayCajaCorte) kpis.Add((R("CD_Hoy"), Millones(k.Caja.CajaCorte), "", 0));
                if (k.Caja != null && k.Caja.HayProyeccion) kpis.Add((R("CD_Proy"), Millones(k.Caja.CajaProyectada), "", Signo(k.Caja.CajaProyectada)));
            }
            return kpis;
        }

        // ===== Excel =====

        private string NombreTramo(string codigo)
        {
            switch (codigo)
            {
                case "GeneracionOperativa": return R("FlujoCaja_FU_GeneracionOperativa");
                case "Otros": return R("Flujo_CajaCascada_Otros");
                default: return R("FlujoCaja_Casc_" + codigo);
            }
        }

        private byte[] GenerarExcel(FiltrosEjecutivo f, object r, out string nombreArchivo)
        {
            string nombreEmpresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == f.IdEmpresa)?.Nombre ?? "Empresa";
            string corte = NombreMes(f.Mes) + " " + f.Anio;
            var tema = ExcelTemaHelper.Resolver(f.Tema, f.TemaP, f.TemaS, f.TemaA);
            var logo = ExcelLogoHelper.Obtener(f.IdEmpresa);
            int o = logo != null ? ExcelLogoHelper.FilasBloque : 0;
            string titulo = R(Pref + "PageTitle");

            using (var wb = new XLWorkbook())
            {
                Func<IXLWorksheet, string, int> nuevaHoja = (ws, sub) =>
                {
                    if (logo != null) ExcelLogoHelper.EscribirTitulo(ws, titulo + " — " + nombreEmpresa, sub);
                    return 1 + o;
                };
                Action<IXLWorksheet, int, string[]> cabecera = (ws, fila, textos) =>
                {
                    for (int c = 0; c < textos.Length; c++) ws.Cell(fila, c + 1).Value = textos[c];
                    var rg = ws.Range(fila, 1, fila, textos.Length);
                    rg.Style.Font.Bold = true; rg.Style.Font.FontColor = XLColor.White; rg.Style.Fill.BackgroundColor = ExcelTemaHelper.Principal(tema);
                };

                List<ComentarioGestion> comentarios = null;
                if (r is PygHojaReporte p) { HojaPyg(wb, p, nuevaHoja, cabecera); comentarios = Comentarios(f, "PygHoja"); }
                else if (r is CajaDisponibleReporte c) { HojaCaja(wb, c, nuevaHoja, cabecera); comentarios = Comentarios(f, "CajaDisponible"); }
                else if (r is ComiteReporte k)
                {
                    HojaComiteResumen(wb, k, nuevaHoja, cabecera);
                    if (k.Pyg != null && !k.Pyg.SinDatos) HojaPyg(wb, k.Pyg, nuevaHoja, cabecera);
                    if (k.Caja != null && !k.Caja.SinDatos) HojaCaja(wb, k.Caja, nuevaHoja, cabecera);
                    comentarios = k.Comentarios;
                }
                HojaComentarios(wb, comentarios, f, nuevaHoja);

                if (logo != null) ExcelLogoHelper.ColocarEnTodas(wb, logo);
                nombreArchivo = $"{Pref.TrimEnd('_')}_{nombreEmpresa}_{f.Anio}{f.Mes:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                nombreArchivo = string.Join("_", nombreArchivo.Split(Path.GetInvalidFileNameChars()));
                using (var ms = new MemoryStream()) { wb.SaveAs(ms); return ms.ToArray(); }
            }
        }

        private void HojaPyg(XLWorkbook wb, PygHojaReporte p, Func<IXLWorksheet, string, int> nueva, Action<IXLWorksheet, int, string[]> cab)
        {
            var ws = wb.Worksheets.Add(R("PH_ExcelHoja"));
            int fila = nueva(ws, NombreMes(p.MesCorte) + " " + p.Anio);
            var enc = new List<string> { R("PH_ColConcepto") };
            for (int m = 1; m <= 12; m++) enc.Add(NombreMes(m).Substring(0, Math.Min(3, NombreMes(m).Length)));
            enc.AddRange(new[] { R("PH_ColAcum"), R("PH_ColAv"), R("PH_ColPpto"), R("PH_ColVarPpto"), R("PH_ColAnioAnt"), R("PH_ColVarAnio") });
            cab(ws, fila, enc.ToArray());
            int filaEnc = fila; fila++;
            foreach (var x in p.Filas)
            {
                ws.Cell(fila, 1).Value = x.Descripcion;
                for (int m = 0; m < 12; m++) { ExcelCellHelper.SetValue(ws.Cell(fila, 2 + m), x.Meses[m]); ws.Cell(fila, 2 + m).Style.NumberFormat.Format = "#,##0"; }
                ExcelCellHelper.SetValue(ws.Cell(fila, 14), x.Acumulado); ws.Cell(fila, 14).Style.NumberFormat.Format = "#,##0";
                ExcelCellHelper.SetValue(ws.Cell(fila, 15), x.Av); ws.Cell(fila, 15).Style.NumberFormat.Format = "0.0%";
                ExcelCellHelper.SetValue(ws.Cell(fila, 16), p.HayPresupuesto ? (decimal?)x.AcumPresupuesto : null); ws.Cell(fila, 16).Style.NumberFormat.Format = "#,##0";
                ExcelCellHelper.SetValue(ws.Cell(fila, 17), x.VarPresupuesto); ws.Cell(fila, 17).Style.NumberFormat.Format = "0.0%";
                ExcelCellHelper.SetValue(ws.Cell(fila, 18), x.AcumAnioAnterior); ws.Cell(fila, 18).Style.NumberFormat.Format = "#,##0";
                ExcelCellHelper.SetValue(ws.Cell(fila, 19), x.VarAnioAnterior); ws.Cell(fila, 19).Style.NumberFormat.Format = "0.0%";
                if (x.EsSubtotal) ws.Range(fila, 1, fila, 19).Style.Font.Bold = true;
                fila++;
            }
            ws.Columns().AdjustToContents();
            ws.SheetView.Freeze(filaEnc, 1);
        }

        private void HojaCaja(XLWorkbook wb, CajaDisponibleReporte c, Func<IXLWorksheet, string, int> nueva, Action<IXLWorksheet, int, string[]> cab)
        {
            var ws = wb.Worksheets.Add(R("CD_ExcelHoja"));
            int fila = nueva(ws, NombreMes(c.MesCorte) + " " + c.Anio);
            cab(ws, fila, new[] { R("CD_PageTitle"), R("EJ_Valor") }); fila++;
            var items = new List<KeyValuePair<string, decimal?>>
            {
                new KeyValuePair<string, decimal?>(R("CD_Inicio"), c.HayCajaInicial ? (decimal?)c.CajaInicial : null),
                new KeyValuePair<string, decimal?>(R("CD_Entradas"), c.TotalFuentes),
                new KeyValuePair<string, decimal?>(R("CD_Salidas"), c.TotalUsos),
                new KeyValuePair<string, decimal?>(string.Format(R("CD_HoyFmt"), NombreMes(c.MesCorte)), c.HayCajaCorte ? (decimal?)c.CajaCorte : null),
                new KeyValuePair<string, decimal?>(R("CD_Proy"), c.HayProyeccion ? (decimal?)c.CajaProyectada : null),
                new KeyValuePair<string, decimal?>(R("CD_Minimo"), c.HayMinimo ? (decimal?)c.CajaMinima : null)
            };
            foreach (var it in items) { ws.Cell(fila, 1).Value = it.Key; ExcelCellHelper.SetValue(ws.Cell(fila, 2), it.Value); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0"; fila++; }
            // Los mismos avisos que muestra la pantalla.
            if (!c.HayCajaInicial) { ws.Cell(fila, 1).Value = R("CD_SinBalance"); fila++; }
            else
            {
                if (c.HayProyeccion && c.MesesEnDeficit > 0)
                    ws.Cell(fila, 1).Value = string.Format(R("CD_EstadoDeficitFmt"), c.MesesEnDeficit, Millones(c.CajaMinima), NombreMes(c.MesMinimo));
                else if (c.HayProyeccion)
                    ws.Cell(fila, 1).Value = R("CD_EstadoOk") + " " + string.Format(R("CD_MinimoFmt"), Millones(c.CajaMinima), NombreMes(c.MesMinimo));
                if (c.HayProyeccion) fila++;
                if (!c.Cuadra) { ws.Cell(fila, 1).Value = R("CD_NoCuadra"); fila++; }
            }
            fila++;
            cab(ws, fila, new[] { R("CD_TramosTitulo"), R("EJ_Valor"), R("EJ_Tipo") }); fila++;
            foreach (var t in c.Tramos)
            {
                ws.Cell(fila, 1).Value = NombreTramo(t.Codigo); ExcelCellHelper.SetValue(ws.Cell(fila, 2), t.Valor); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0";
                ws.Cell(fila, 3).Value = t.Tipo == "fuente" ? R("CD_Entrada") : R("CD_Salida"); fila++;
            }
            fila++;
            if (c.Meses.Count > 0)
            {
                cab(ws, fila, new[] { R("CD_ColMes"), R("CD_ColFlujo"), R("CD_ColCaja"), R("CD_ColTipo") }); fila++;
                foreach (var m in c.Meses)
                {
                    ws.Cell(fila, 1).Value = NombreMes(m.Mes);
                    ExcelCellHelper.SetValue(ws.Cell(fila, 2), m.Flujo); ws.Cell(fila, 2).Style.NumberFormat.Format = "#,##0";
                    ExcelCellHelper.SetValue(ws.Cell(fila, 3), m.CajaFin); ws.Cell(fila, 3).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(fila, 4).Value = m.Proyectado ? R("CD_Proyectado") : R("CD_Real"); fila++;
                }
            }
            ws.Columns().AdjustToContents();
        }

        private void HojaComiteResumen(XLWorkbook wb, ComiteReporte k, Func<IXLWorksheet, string, int> nueva, Action<IXLWorksheet, int, string[]> cab)
        {
            var g = k.Gerencia;
            var ws = wb.Worksheets.Add(R("CO_ExcelHojaResumen"));
            int fila = nueva(ws, NombreMes(k.Mes) + " " + k.Anio);
            if (g != null && g.HayResultados)
            {
                cab(ws, fila, new[] { R("GM_SecResultados"), R("GM_Col_Real"), R("GM_Col_Presupuesto"), R("GM_Col_VarPpto"), R("GM_Col_VarAnio"), R("GM_Col_AcumReal"), R("GM_Col_AcumPpto"), R("GM_Col_AcumVarPpto") }); fila++;
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
                cab(ws, fila, new[] { R("GM_SecBalance"), R("GM_Col_Valor"), R("GM_Col_VarCierre") }); fila++;
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
                cab(ws, fila, new[] { R("GM_SecCaja"), R("GM_Col_Valor") }); fila++;
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
                cab(ws, fila, new[] { R("GM_AtencionTitulo"), R("GM_Col_Valor"), R("GM_Col_Semaforo") }); fila++;
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
                cab(ws, fila, new[] { R("LN_Col_Linea"), R("LN_Col_Ingresos"), R("LN_Col_PctIngresos"), R("LN_Col_MargenOperacional") }); fila++;
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
            cab(ws, fila, new[] { R("CO_Decisiones") }); fila++;
            ws.Cell(fila, 1).Value = R("CO_DecisionesAyuda"); ws.Cell(fila, 1).Style.Font.Italic = true; fila += 4;
            cab(ws, fila, new[] { R("CO_Firmas"), R("CO_Nombre"), R("CO_Cargo"), R("CO_Fecha") }); fila++;
            foreach (var rol in new[] { "CO_Elaboro", "CO_Reviso", "CO_Aprobo" }) { ws.Cell(fila, 1).Value = R(rol); ws.Row(fila).Height = 28; fila++; }
            ws.Columns().AdjustToContents();
        }

        /// <summary>Comentarios de gestión del cierre de ESTE informe (null = tabla ausente o sin comentarios).</summary>
        private static List<ComentarioGestion> Comentarios(FiltrosEjecutivo f, string informe)
        {
            var lista = new ComentariosGestionService().Listar(f.IdEmpresa, f.Anio, f.Mes, informe, out bool disponible);
            return disponible ? lista : null;
        }

        private void HojaComentarios(XLWorkbook wb, List<ComentarioGestion> comentarios, FiltrosEjecutivo f, Func<IXLWorksheet, string, int> nueva)
        {
            if (comentarios == null || comentarios.Count == 0) return;
            var ws = wb.Worksheets.Add(R("CO_ExcelHojaComentarios"));
            int fila = nueva(ws, NombreMes(f.Mes) + " " + f.Anio);
            string[] enc = { R("Com_ColInforme"), R("Com_ColSobre"), R("Com_ColComentario"), R("Com_ColAutor"), R("Com_ColFecha") };
            for (int c = 0; c < enc.Length; c++) ws.Cell(fila, c + 1).Value = enc[c];
            ws.Range(fila, 1, fila, enc.Length).Style.Font.Bold = true;
            fila++;
            foreach (var c in comentarios)
            {
                ws.Cell(fila, 1).Value = R("Com_Inf_" + c.Informe); ws.Cell(fila, 2).Value = c.Clave ?? ""; ws.Cell(fila, 3).Value = c.Texto;
                ws.Cell(fila, 4).Value = c.NombreUsuario; ws.Cell(fila, 5).Value = c.Fecha; fila++;
            }
            ws.Column(3).Width = 70; ws.Column(3).Style.Alignment.WrapText = true;
        }
    }

    /// <summary>«PYG en una hoja»: vista del informe de PYG (se abre desde su selector de vistas) con el estado de resultados del año, mes a mes,
    /// con participación (AV %) y acumulado. Sin permiso propio: igual que el PYG, acceso acotado por empresa/grupo.</summary>
    [ValidarSesion]
    public class InformePygHojaController(InformeEjecutivoService service, InformeTablasDatosService empresaService, EmailService emailService, AuditoriaService auditoriaService)
        : InformeEjecutivoBaseController(service, empresaService, emailService, auditoriaService)
    {
        protected override string Tipo => "hoja";
        protected override string NombreControlador => "InformePygHoja";
        protected override string Pref => "PH_";
    }

    /// <summary>«¿Cuánta plata me queda?»: vista simple del Flujo de caja (se abre desde su selector de vistas): caja disponible, de dónde vino y a
    /// dónde se fue, y cómo terminará el año. Sin permiso propio: igual que el Flujo de caja, acceso acotado por empresa/grupo.</summary>
    [ValidarSesion]
    public class InformeCajaDisponibleController(InformeEjecutivoService service, InformeTablasDatosService empresaService, EmailService emailService, AuditoriaService auditoriaService)
        : InformeEjecutivoBaseController(service, empresaService, emailService, auditoriaService)
    {
        protected override string Tipo => "caja";
        protected override string NombreControlador => "InformeCajaDisponible";
        protected override string Pref => "CD_";
    }

    /// <summary>«Versión para comité» del Informe mensual de gerencia (se abre desde su selector de vistas): resultados, PYG en una hoja, caja, indicadores
    /// en atención, líneas de negocio y comentarios del cierre, con decisiones y firmas. Usa el permiso de Gerencia mensual.</summary>
    [ValidarSesion]
    [RequierePermiso("INFORMES_GERENCIA_MENSUAL")]
    public class InformeComiteController(InformeEjecutivoService service, InformeTablasDatosService empresaService, EmailService emailService, AuditoriaService auditoriaService)
        : InformeEjecutivoBaseController(service, empresaService, emailService, auditoriaService)
    {
        protected override string Tipo => "comite";
        protected override string NombreControlador => "InformeComite";
        protected override string Pref => "CO_";
    }
}
