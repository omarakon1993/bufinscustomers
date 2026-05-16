using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Drawing;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class TablaPUCController : BaseController
    {
        private readonly TablaPUCService _service = new TablaPUCService();

        private bool VerificarAcceso() => UsuarioSesionHelper.UsuarioActual?.Admin >= 1;

        public ActionResult TablaPUC()
        {
            if (!VerificarAcceso())
                return RedirectToAction("Index", "Home");

            return View("~/Views/Informes/TablaPUC.cshtml");
        }

        [HttpGet]
        public JsonResult ObtenerTipos()
        {
            try
            {
                if (!VerificarAcceso())
                    return Json(new { success = false, message = "Acceso denegado" }, JsonRequestBehavior.AllowGet);

                var tipos = _service.ObtenerTipos();
                return Json(new { success = true, tipos }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult ObtenerLargos()
        {
            try
            {
                if (!VerificarAcceso())
                    return Json(new { success = false, message = "Acceso denegado" }, JsonRequestBehavior.AllowGet);

                var largos = _service.ObtenerLargos();
                return Json(new { success = true, largos }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult ConsultarDatos(FiltrosTablaPUC filtros)
        {
            try
            {
                if (!VerificarAcceso())
                    return Json(new { success = false, message = "Acceso denegado" });

                var (filas, total) = _service.ConsultarDatos(filtros ?? new FiltrosTablaPUC());
                var result = Json(new { success = true, datos = filas, totalRegistros = total });
                result.MaxJsonLength = int.MaxValue;
                return result;
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public ActionResult ExportarExcel(FiltrosTablaPUC filtros)
        {
            try
            {
                if (!VerificarAcceso())
                    return RedirectToAction("TablaPUC");

                var (filas, _) = _service.ConsultarDatos(filtros ?? new FiltrosTablaPUC());

                using (var package = new ExcelPackage())
                {
                    var ws = package.Workbook.Worksheets.Add("TablaPUC");
                    Func<string, string> R = key => HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;

                    string[] headers = { R("PUC_ThCuenta"), R("PUC_ThNombre"), R("PUC_ThLargo"), R("PUC_ThTipo") };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cells[1, i + 1];
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(99, 102, 241));
                        cell.Style.Font.Color.SetColor(Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }

                    for (int i = 0; i < filas.Count; i++)
                    {
                        var fila = filas[i];
                        int row = i + 2;
                        ws.Cells[row, 1].Value = fila.Cuenta;
                        ws.Cells[row, 2].Value = fila.Nombre;
                        ws.Cells[row, 3].Value = fila.Largo;
                        ws.Cells[row, 4].Value = fila.Tipo;

                        var color = i % 2 == 0 ? Color.FromArgb(248, 250, 252) : Color.White;
                        for (int c = 1; c <= 4; c++)
                        {
                            ws.Cells[row, c].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            ws.Cells[row, c].Style.Fill.BackgroundColor.SetColor(color);
                            ws.Cells[row, c].Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.LightGray);
                        }
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    ws.Cells[1, 1, 1, 4].AutoFilter = true;
                    ws.View.FreezePanes(2, 1);

                    string nombreArchivo = $"TablaPUC_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    return File(package.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage($"Error al exportar: {ex.Message}");
                return RedirectToAction("TablaPUC");
            }
        }
    }
}
