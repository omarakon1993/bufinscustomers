using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Drawing;
using System.IO;
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
        [ValidateAntiForgeryToken]
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
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(FiltrosTablaPUC filtros)
        {
            try
            {
                if (!VerificarAcceso())
                    return RedirectToAction("TablaPUC");

                var (filas, _) = _service.ConsultarDatos(filtros ?? new FiltrosTablaPUC());

                using (var package = new XLWorkbook())
                {
                    var ws = package.Worksheets.Add("TablaPUC");
                    Func<string, string> R = key => HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;

                    string[] headers = { R("PUC_ThCuenta"), R("PUC_ThNombre"), R("PUC_ThLargo"), R("PUC_ThTipo") };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = ws.Cell(1, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                        cell.Style.Font.FontColor = XLColor.White;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    }

                    for (int i = 0; i < filas.Count; i++)
                    {
                        var fila = filas[i];
                        int row = i + 2;
                        ws.Cell(row, 1).Value = fila.Cuenta;
                        ws.Cell(row, 2).Value = fila.Nombre;
                        ws.Cell(row, 3).Value = fila.Largo;
                        ws.Cell(row, 4).Value = fila.Tipo;

                        var color = i % 2 == 0 ? XLColor.FromArgb(248, 250, 252) : XLColor.White;
                        for (int c = 1; c <= 4; c++)
                        {
                            ws.Cell(row, c).Style.Fill.BackgroundColor = color;
                            ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            ws.Cell(row, c).Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
                        }
                    }

                    ws.Columns().AdjustToContents();
                    ws.Range(1, 1, 1, 4).SetAutoFilter();
                    ws.SheetView.Freeze(1, 0);

                    string nombreArchivo = $"TablaPUC_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    return File(fileBytes,
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
