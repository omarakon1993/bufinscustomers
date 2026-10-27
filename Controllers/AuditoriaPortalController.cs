using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Auditoría del portal público (hoy «Pulso Bufins»): visitas, recorridos, qué consultan, origen, audiencia y salud del sitio.
    /// Solo Super Admin. Pestaña del hub de Auditoría (se carga con ?embed=1). Todo es anónimo: ver <see cref="PortalUsoService"/>.
    /// </summary>
    [ValidarSesion]
    [SoloSuperAdmin]
    public class AuditoriaPortalController : BaseController
    {
        private readonly PortalUsoService _servicio = new PortalUsoService();

        /// <summary>Textos (recursos AudPortal_*) que se envían al navegador; la pestaña los usa por su clave sin prefijo.</summary>
        private static readonly string[] ClavesTextos = { "X_Titulo", "BuscarPlaceholder", "F_Dispositivo", "F_Origen", "F_Idioma", "Nota", "SinDatos", "SinCarpeta", "SinEventos", "Ahora", "Desde", "K_Visitantes", "K_VisitantesDia", "K_Visitas", "K_Vistas", "K_PagVisita", "K_Duracion", "K_Rebote", "K_Descargas", "K_Contactos", "K_Busquedas", "KH_Visitantes", "KH_Visitas", "KH_Rebote", "S_Tendencia", "S_Mapa", "S_Consultan", "S_Llegan", "S_Quienes", "S_Hacen", "S_Recorridos", "S_Salud", "S_Empresas", "S_Sectores", "S_Paginas", "S_Rankings", "S_RankSeg", "S_Busquedas", "S_Comparaciones", "S_FiltrosSector", "S_Origen", "S_Sitios", "S_Campanas", "S_Dispositivos", "S_Navegadores", "S_Sistemas", "S_Idiomas", "S_Zonas", "S_Cta", "S_Descargas", "S_Otras", "S_Salidas", "S_Entradas", "S_SalidasPag", "S_Pasos", "S_Profundidad", "S_Embudo", "S_Lectura", "S_Bots", "S_404", "S_Limites", "S_Acciones", "S_Audiencia", "X_Resumen", "X_Diario", "X_Dia", "X_Dias", "X_Empresa", "X_Empresas", "X_Sector", "X_Clave", "X_Pagina", "X_Veces", "X_Tipo", "X_Descarga", "X_Otra", "X_Salida", "X_Categoria", "X_Sitio", "X_Dispositivo", "X_Navegador", "X_Sistema", "X_Idioma", "X_Zona", "X_Entrada", "X_Paso", "X_Profundidad", "X_Etapa", "X_PermSeg", "X_ScrollPct", "X_Informes", "X_Familia", "E_visitantes", "E_consulta", "E_cta", "E_contacto", "E_enviado", "P_inicio", "P_home", "P_empresa", "P_sector", "P_ranking", "P_comparar", "P_comparador", "P_sectores", "P_contacto", "P_informe", "O_directo", "O_buscador", "O_red-social", "O_correo", "O_otro-sitio", "O_interno", "D_m", "D_t", "D_d", "B_google", "B_bing", "B_ia-openai", "B_ia-anthropic", "B_ia-perplexity", "B_ia-otro", "B_buscador-otro", "B_facebook", "B_linkedin", "B_x-twitter", "B_mensajeria", "B_seo", "B_monitoreo", "B_otro", "A_export_xlsx", "A_export_csv", "A_informe_pdf", "A_imprimir", "A_copiar_enlace", "A_unidad", "Dias", "Serie_Vistas", "Serie_Visitantes", "Serie_Visitas", "Ver_Mas", "N_Nada", "Hdr_Empresa", "Hdr_Vistas", "Hdr_Visitantes", "Hdr_Informes" };

        // Fragmento de la pestaña (el hub lo inyecta). Sin ?embed=1 se redirige al hub.
        [HttpGet]
        public ActionResult Index()
        {
            if (!string.Equals(Request.QueryString["embed"], "1"))
                return RedirectToAction("Index", "AuditoriaHub");
            ViewBag.NombrePortal = PortalPublicoConfig.Nombre();
            var textos = new Dictionary<string, string>();
            foreach (var k in ClavesTextos) textos[k] = R("AudPortal_" + k);
            ViewBag.TextosJson = Newtonsoft.Json.JsonConvert.SerializeObject(textos);
            return View("~/Views/Informes/AuditoriaPortal.cshtml");
        }

        private static PortalFiltro Filtro(string desde, string hasta, string dispositivo, string origen, string idioma, string texto)
        {
            DateTime d, h;
            if (!DateTime.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out h)) h = DateTime.Today;
            if (!DateTime.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) d = h.AddDays(-29);
            Func<string, string> lim = s => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            return new PortalFiltro { Desde = d, Hasta = h, Dispositivo = lim(dispositivo), Origen = lim(origen), Idioma = lim(idioma), Texto = lim(texto) };
        }

        [HttpGet]
        public JsonResult Consultar(string desde, string hasta, string dispositivo, string origen, string idioma, string texto)
        {
            try
            {
                var r = _servicio.Consultar(Filtro(desde, hasta, dispositivo, origen, idioma, texto));
                var json = Json(r, JsonRequestBehavior.AllowGet);
                json.MaxJsonLength = int.MaxValue;
                return json;
            }
            catch (Exception ex)
            {
                return Json(new { error = MensajeError(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(string desde, string hasta, string dispositivo, string origen, string idioma, string texto)
        {
            try
            {
                var f = Filtro(desde, hasta, dispositivo, origen, idioma, texto);
                var r = _servicio.Consultar(f);
                if (!r.Disponible)
                {
                    SetErrorMessage(R("AudPortal_SinDatos"));
                    return RedirectToAction("Index", "AuditoriaHub");
                }

                byte[] bytes = ConstruirExcel(r);
                string nombrePortal = PortalPublicoConfig.Nombre();
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Exportar, "AuditoriaPortal", "excel",
                    $"Exportó a Excel la auditoría del portal público ({nombrePortal}), del {r.Desde} al {r.Hasta}.",
                    null, new { r.Desde, r.Hasta, f.Dispositivo, f.Origen, f.Idioma, f.Texto });
                string archivo = $"Auditoria_Portal_{r.Desde}_{r.Hasta}.xlsx";
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "AuditoriaPortal.ExportarExcel");
                SetErrorMessage(MensajeError(ex));
                return RedirectToAction("Index", "AuditoriaHub");
            }
        }

        /// <summary>Borra todo el registro de uso del sitio público para empezar de cero (irreversible; solo Super Admin).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Limpiar()
        {
            try
            {
                var r = _servicio.LimpiarTodo();
                if (!r.Item1) return Json(new { success = false, message = R("AudPortal_SinCarpeta") });

                string nombrePortal = PortalPublicoConfig.Nombre();
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Eliminar, "AuditoriaPortal", "limpiar",
                    $"Limpió la auditoría del portal público ({nombrePortal}): {r.Item2} archivo(s) borrado(s), {r.Item3} sin poder borrar.",
                    null, new { borrados = r.Item2, fallidos = r.Item3 });

                if (r.Item3 > 0)
                    return Json(new { success = false, message = string.Format(R("AudPortal_LimpiarParcial"), r.Item2, r.Item3) });
                return Json(new { success = true, message = string.Format(R("AudPortal_LimpiarExito"), r.Item2) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "AuditoriaPortal.Limpiar");
                return Json(new { success = false, message = MensajeError(ex) });
            }
        }

        // ------------------------------------------------------------------ Excel

        private byte[] ConstruirExcel(PortalResultado r)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(R("AudPortal_X_Resumen"));
                int fila = 1;
                ws.Cell(fila, 1).Value = R("AudPortal_X_Titulo") + " · " + PortalPublicoConfig.Nombre(); ws.Cell(fila, 1).Style.Font.Bold = true; ws.Cell(fila, 1).Style.Font.FontSize = 14; fila++;
                ws.Cell(fila, 1).Value = r.Desde + " → " + r.Hasta + " (" + r.Dias + " " + R("AudPortal_X_Dias") + ")"; fila += 2;
                var k = r.Kpis;
                var kp = new[]
                {
                    new[] { R("AudPortal_K_Visitantes"), k.Visitantes.ToString() }, new[] { R("AudPortal_K_VisitantesDia"), k.VisitantesPorDia.ToString("0.0") },
                    new[] { R("AudPortal_K_Visitas"), k.Visitas.ToString() }, new[] { R("AudPortal_K_Vistas"), k.Vistas.ToString() },
                    new[] { R("AudPortal_K_PagVisita"), k.PaginasPorVisita.ToString("0.0") },
                    new[] { R("AudPortal_K_Duracion"), (k.DuracionSeg / 60) + ":" + (k.DuracionSeg % 60).ToString("00") },
                    new[] { R("AudPortal_K_Rebote"), k.RebotePct.ToString("0.0") + " %" }, new[] { R("AudPortal_K_Descargas"), k.Descargas.ToString() },
                    new[] { R("AudPortal_K_Contactos"), k.Contactos.ToString() }, new[] { R("AudPortal_K_Busquedas"), k.Busquedas.ToString() }
                };
                foreach (var x in kp) { ws.Cell(fila, 1).Value = x[0]; ws.Cell(fila, 2).Value = x[1]; fila++; }
                ws.Columns().AdjustToContents();

                Hoja(wb, "AudPortal_X_Diario", new[] { R("AudPortal_X_Dia"), R("AudPortal_K_Vistas"), R("AudPortal_K_Visitantes"), R("AudPortal_K_Visitas") },
                    r.Serie.Select(s => new object[] { s.Dia, s.Vistas, s.Visitantes, s.Visitas }));
                Hoja(wb, "AudPortal_S_Empresas", new[] { "NIT", R("AudPortal_X_Empresa"), R("AudPortal_X_Sector"), R("AudPortal_K_Vistas"), R("AudPortal_K_Visitantes") },
                    r.Empresas.Select(e => new object[] { e.Clave, e.Nombre, (e.Extra ?? "").Split('|')[0], e.N, e.N2 }));
                Hoja(wb, "AudPortal_S_Sectores", new[] { R("AudPortal_X_Sector"), R("AudPortal_K_Vistas"), R("AudPortal_K_Visitantes") }, r.Sectores.Select(e => new object[] { e.Nombre, e.N, e.N2 }));
                Hoja(wb, "AudPortal_S_Rankings", new[] { R("AudPortal_X_Clave"), R("AudPortal_K_Vistas") }, r.Rankings.Concat(r.RankingsSegmento).Select(e => new object[] { e.Clave, e.N }));
                Hoja(wb, "AudPortal_S_Paginas", new[] { R("AudPortal_X_Pagina"), R("AudPortal_K_Vistas"), R("AudPortal_K_Visitantes") }, r.Paginas.Select(e => new object[] { e.Clave, e.N, e.N2 }));
                Hoja(wb, "AudPortal_S_Busquedas", new[] { "NIT", R("AudPortal_X_Empresa"), R("AudPortal_X_Veces") }, r.Busquedas.Select(e => new object[] { e.Clave, e.Nombre, e.N }));
                Hoja(wb, "AudPortal_S_Comparaciones", new[] { R("AudPortal_X_Empresas"), R("AudPortal_X_Veces") }, r.Comparaciones.Select(e => new object[] { e.Nombre, e.N }));
                Hoja(wb, "AudPortal_S_Acciones", new[] { R("AudPortal_X_Tipo"), R("AudPortal_X_Clave"), R("AudPortal_X_Veces") },
                    r.Cta.Select(e => new object[] { "CTA", e.Clave, e.N })
                        .Concat(r.Exportaciones.Select(e => new object[] { R("AudPortal_X_Descarga"), e.Clave, e.N }))
                        .Concat(r.OtrasAcciones.Select(e => new object[] { R("AudPortal_X_Otra"), e.Clave, e.N }))
                        .Concat(r.Salidas.Select(e => new object[] { R("AudPortal_X_Salida"), e.Clave, e.N })));
                Hoja(wb, "AudPortal_S_Origen", new[] { R("AudPortal_X_Tipo"), R("AudPortal_X_Clave"), R("AudPortal_X_Veces") },
                    r.Origen.Select(e => new object[] { R("AudPortal_X_Categoria"), e.Clave, e.N })
                        .Concat(r.Sitios.Select(e => new object[] { R("AudPortal_X_Sitio"), e.Clave, e.N }))
                        .Concat(r.Campanas.Select(e => new object[] { "UTM", e.Clave, e.N })));
                Hoja(wb, "AudPortal_S_Audiencia", new[] { R("AudPortal_X_Tipo"), R("AudPortal_X_Clave"), R("AudPortal_K_Visitantes") },
                    r.Dispositivos.Select(e => new object[] { R("AudPortal_X_Dispositivo"), e.Clave, e.N })
                        .Concat(r.Navegadores.Select(e => new object[] { R("AudPortal_X_Navegador"), e.Clave, e.N }))
                        .Concat(r.Sistemas.Select(e => new object[] { R("AudPortal_X_Sistema"), e.Clave, e.N }))
                        .Concat(r.Idiomas.Select(e => new object[] { R("AudPortal_X_Idioma"), e.Clave, e.N }))
                        .Concat(r.Zonas.Select(e => new object[] { R("AudPortal_X_Zona"), e.Clave, e.N })));
                Hoja(wb, "AudPortal_S_Recorridos", new[] { R("AudPortal_X_Tipo"), R("AudPortal_X_Clave"), R("AudPortal_X_Veces") },
                    r.Entradas.Select(e => new object[] { R("AudPortal_X_Entrada"), e.Clave, e.N })
                        .Concat(r.SalidasPagina.Select(e => new object[] { R("AudPortal_X_Salida"), e.Clave, e.N }))
                        .Concat(r.Transiciones.Select(e => new object[] { R("AudPortal_X_Paso"), e.De + " → " + e.A, e.N }))
                        .Concat(r.Profundidad.Select(e => new object[] { R("AudPortal_X_Profundidad"), e.Clave, e.N })));
                Hoja(wb, "AudPortal_S_Embudo", new[] { R("AudPortal_X_Etapa"), R("AudPortal_K_Visitantes"), "%" }, r.Embudo.Select(e => new object[] { R("AudPortal_E_" + e.Clave), e.N, e.N2 }));
                Hoja(wb, "AudPortal_S_Lectura", new[] { R("AudPortal_X_Pagina"), R("AudPortal_X_PermSeg"), R("AudPortal_X_ScrollPct"), R("AudPortal_X_Veces") },
                    r.Lectura.Select(e => new object[] { e.Pagina, e.PermanenciaSeg, e.ScrollPct, e.N }));
                Hoja(wb, "AudPortal_S_Salud", new[] { R("AudPortal_X_Tipo"), R("AudPortal_X_Clave"), R("AudPortal_X_Veces") },
                    r.Bots.Select(e => new object[] { "Bot", e.Familia, e.N })
                        .Concat(r.Errores404.Select(e => new object[] { "404", e.Clave, e.N }))
                        .Concat(r.Limites.Select(e => new object[] { "429", e.Clave, e.N })));

                using (var ms = new MemoryStream()) { wb.SaveAs(ms); return ms.ToArray(); }
            }
        }

        private void Hoja(XLWorkbook wb, string claveNombre, string[] encabezados, IEnumerable<object[]> filas)
        {
            string nombre = R(claveNombre); if (nombre.Length > 31) nombre = nombre.Substring(0, 31);
            var ws = wb.Worksheets.Add(nombre);
            for (int i = 0; i < encabezados.Length; i++)
            {
                var c = ws.Cell(1, i + 1); c.Value = encabezados[i]; c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromArgb(88, 58, 255); c.Style.Font.FontColor = XLColor.White;
            }
            int fila = 2;
            foreach (var f in filas)
            {
                for (int i = 0; i < f.Length; i++) ExcelCellHelper.SetValue(ws.Cell(fila, i + 1), f[i]);
                fila++;
            }
            ws.SheetView.Freeze(1, 0);
            ws.Columns().AdjustToContents();
        }
    }
}
