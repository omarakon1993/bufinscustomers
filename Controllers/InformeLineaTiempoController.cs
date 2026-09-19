using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Informe "Línea de Tiempo Financiera": evolución mes a mes de un indicador de las tablas
    /// de resultados de Ejecución de Modelos (dbo.Modelo*), con opción de comparar Escenario 1
    /// vs Escenario 2. Reutiliza InformeTablasDatosService para catálogo (tablas/variables) e
    /// InformeLineaTiempoService para la consulta agregada de la serie. Habilitado para todos
    /// los roles, igual criterio que InformeModelosController — el acceso real se controla por
    /// empresa/grupo vía EmpresaAccesoHelper, no por rol.
    /// </summary>
    [ValidarSesion]
    public class InformeLineaTiempoController : BaseController
    {
        private readonly InformeTablasDatosService _catalogo = new InformeTablasDatosService();
        private readonly InformeLineaTiempoService _service = new InformeLineaTiempoService();

        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var empresas = _catalogo.ObtenerEmpresas();
            if (!esAdmin)
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            ViewBag.Tablas = _catalogo.ObtenerTablasModelos();
            ViewBag.Empresas = empresas;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.IdEmpresaUsuario = idEmpresa;

            return View("~/Views/Informes/InformeLineaTiempo.cshtml");
        }

        private bool EsTablaDeModelo(string nombreTabla)
        {
            return !string.IsNullOrWhiteSpace(nombreTabla)
                && _catalogo.ObtenerTablasModelos().Any(t => t.NombreTabla == nombreTabla);
        }

        /// <summary>Resuelve y valida la empresa a consultar para el usuario actual; null = sin acceso.</summary>
        private int? ResolverEmpresaConsulta(int? idEmpresaSolicitada, bool esAdmin, int? idEmpresaUsuario)
        {
            if (esAdmin)
                return idEmpresaSolicitada ?? idEmpresaUsuario;

            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (idEmpresaSolicitada.HasValue)
                return EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresaSolicitada.Value) ? idEmpresaSolicitada : (int?)null;

            return idEmpresaUsuario;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerIndicadores(string nombreTabla, int? idEmpresa, int? idEscenario)
        {
            if (!EsTablaDeModelo(nombreTabla))
                return Json(new { success = false, message = R("Common_TablaNoValida") });

            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresaConsulta = ResolverEmpresaConsulta(idEmpresa, esAdmin, usuario?.IdEmpresa);
            if (!idEmpresaConsulta.HasValue)
                return Json(new { success = false, message = R("Common_SinPermisos") });

            var indicadores = _service.ObtenerIndicadoresDisponibles(nombreTabla, idEmpresaConsulta, idEscenario);
            return Json(new { success = true, indicadores });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerCampos(string nombreTabla)
        {
            if (!EsTablaDeModelo(nombreTabla))
                return Json(new { success = false, message = R("Common_TablaNoValida") });

            var campos = _service.ObtenerCamposNumericos(nombreTabla)
                .Select(c => new { nombreTecnico = c.NombreTecnico, nombreAmigable = c.NombreAmigable });
            return Json(new { success = true, campos });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerRangoMeses(string nombreTabla, int? idEmpresa, int? idEscenario)
        {
            if (!EsTablaDeModelo(nombreTabla))
                return Json(new { success = false, message = R("Common_TablaNoValida") });

            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresaConsulta = ResolverEmpresaConsulta(idEmpresa, esAdmin, usuario?.IdEmpresa);
            if (!idEmpresaConsulta.HasValue)
                return Json(new { success = false, message = R("Common_SinPermisos") });

            // Se proyecta a camelCase explícito: ASP.NET MVC 5 serializa JSON respetando el
            // PascalCase de C# tal cual (no hay auto-camelCase como en ASP.NET Core), y el JS
            // de la vista lee minúscula inicial en todas sus propiedades.
            var meses = _service.ObtenerMesesDisponibles(nombreTabla, idEmpresaConsulta, idEscenario)
                .Select(m => new { anio = m.Anio, mes = m.Mes, etiqueta = m.Etiqueta });
            return Json(new { success = true, meses });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ObtenerSerie(FiltrosLineaTiempo filtros)
        {
            try
            {
                if (filtros == null || !EsTablaDeModelo(filtros.NombreTabla))
                    return Json(new { success = false, message = R("Common_TablaNoValida") });

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaConsulta = ResolverEmpresaConsulta(filtros.IdEmpresa, esAdmin, usuario?.IdEmpresa);
                if (!idEmpresaConsulta.HasValue)
                    return Json(new { success = false, message = R("Common_SinPermisos") });

                var resultado = _service.ObtenerSerieTiempo(filtros, idEmpresaConsulta);

                return Json(new
                {
                    success = true,
                    series = resultado.Series.Select(s => new
                    {
                        indicador = s.Indicador,
                        puntos = s.Puntos.Select(p => new { anio = p.Anio, mes = p.Mes, etiqueta = p.Etiqueta, valor = p.Valor })
                    }),
                    comparacion = resultado.Comparacion?.Select(p => new { anio = p.Anio, mes = p.Mes, etiqueta = p.Etiqueta, valor = p.Valor }),
                    nombreEscenarioPrincipal = resultado.NombreEscenarioPrincipal,
                    nombreEscenarioComparacion = resultado.NombreEscenarioComparacion
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "InformeLineaTiempoController.ObtenerSerie");
                return Json(new { success = false, message = $"Error al consultar la serie: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosLineaTiempo filtros)
        {
            try
            {
                if (filtros == null || !EsTablaDeModelo(filtros.NombreTabla))
                {
                    TempData["ErrorMessage"] = R("Common_TablaNoValida");
                    return RedirectToAction("Index");
                }

                var usuario = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
                var idEmpresaConsulta = ResolverEmpresaConsulta(filtros.IdEmpresa, esAdmin, usuario?.IdEmpresa);
                if (!idEmpresaConsulta.HasValue)
                {
                    TempData["ErrorMessage"] = R("Common_SinPermisos");
                    return RedirectToAction("Index");
                }

                var resultado = _service.ObtenerSerieTiempo(filtros, idEmpresaConsulta);
                if (resultado.Series.Count == 0 || resultado.Series.All(s => s.Puntos.Count == 0))
                {
                    TempData["InfoMessage"] = R("LineaTiempo_SinDatosExportar");
                    return RedirectToAction("Index");
                }

                using (var package = new XLWorkbook())
                {
                    var worksheet = package.Worksheets.Add("LineaTiempo");
                    bool unaVariable = resultado.Series.Count == 1;
                    bool tieneComparacion = unaVariable && resultado.Comparacion != null && resultado.Comparacion.Count > 0;

                    worksheet.Cell(1, 1).Value = R("LineaTiempo_ColPeriodo");
                    if (unaVariable)
                    {
                        worksheet.Cell(1, 2).Value = !string.IsNullOrWhiteSpace(resultado.NombreEscenarioPrincipal)
                            ? resultado.NombreEscenarioPrincipal
                            : (resultado.Series[0].Indicador ?? R("LineaTiempo_ColValor"));
                        if (tieneComparacion)
                            worksheet.Cell(1, 3).Value = resultado.NombreEscenarioComparacion ?? R("LineaTiempo_ColComparacion");
                    }
                    else
                    {
                        for (int i = 0; i < resultado.Series.Count; i++)
                            worksheet.Cell(1, i + 2).Value = resultado.Series[i].Indicador;
                    }

                    int totalColumnas = unaVariable ? (tieneComparacion ? 3 : 2) : (resultado.Series.Count + 1);
                    var headerRange = worksheet.Range(1, 1, 1, totalColumnas);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                    headerRange.Style.Font.FontColor = XLColor.White;

                    // Unión ordenada de todos los períodos presentes en cualquiera de las series
                    // (normalmente coinciden, pero cada serie se consulta por separado).
                    var periodos = resultado.Series
                        .SelectMany(s => s.Puntos)
                        .Select(p => new { p.Anio, p.Mes, p.Etiqueta })
                        .Distinct()
                        .OrderBy(p => p.Anio).ThenBy(p => p.Mes)
                        .ToList();

                    int fila = 2;
                    foreach (var periodo in periodos)
                    {
                        worksheet.Cell(fila, 1).Value = periodo.Etiqueta;

                        if (unaVariable)
                        {
                            var punto = resultado.Series[0].Puntos.FirstOrDefault(p => p.Anio == periodo.Anio && p.Mes == periodo.Mes);
                            if (punto != null)
                                ExcelCellHelper.SetValue(worksheet.Cell(fila, 2), punto.Valor);

                            if (tieneComparacion)
                            {
                                var comparado = resultado.Comparacion.FirstOrDefault(c => c.Anio == periodo.Anio && c.Mes == periodo.Mes);
                                if (comparado != null)
                                    ExcelCellHelper.SetValue(worksheet.Cell(fila, 3), comparado.Valor);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < resultado.Series.Count; i++)
                            {
                                var punto = resultado.Series[i].Puntos.FirstOrDefault(p => p.Anio == periodo.Anio && p.Mes == periodo.Mes);
                                if (punto != null)
                                    ExcelCellHelper.SetValue(worksheet.Cell(fila, i + 2), punto.Valor);
                            }
                        }

                        fila++;
                    }

                    worksheet.Columns().AdjustToContents();
                    string nombreArchivo = $"LineaTiempoFinanciera_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

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
                AppLogger.Error(ex, "InformeLineaTiempoController.ExportarExcel");
                TempData["ErrorMessage"] = $"Error al exportar a Excel: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}
