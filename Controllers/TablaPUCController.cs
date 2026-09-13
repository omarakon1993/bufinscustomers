using bufinscustomers.Helpers;
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
        public JsonResult ObtenerHijos(string cuenta)
        {
            try
            {
                if (!VerificarAcceso())
                    return Json(new { success = false, message = "Acceso denegado" }, JsonRequestBehavior.AllowGet);

                var nodos = _service.ObtenerHijos(cuenta);
                return Json(new { success = true, nodos }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult ObtenerRuta(string cuenta)
        {
            try
            {
                if (!VerificarAcceso())
                    return Json(new { success = false, message = "Acceso denegado" }, JsonRequestBehavior.AllowGet);

                var ruta = _service.ObtenerRuta(cuenta);
                return Json(new { success = true, ruta }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public JsonResult Buscar(string texto)
        {
            try
            {
                if (!VerificarAcceso())
                    return Json(new { success = false, message = "Acceso denegado" }, JsonRequestBehavior.AllowGet);

                var resultados = _service.Buscar(texto);
                return Json(new { success = true, resultados }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarExcel(string cuenta)
        {
            try
            {
                if (!VerificarAcceso())
                    return RedirectToAction("TablaPUC");

                var filas = _service.ObtenerSubarbol(cuenta);

                using (var package = new XLWorkbook())
                {
                    var ws = package.Worksheets.Add("TablaPUC");
                    Func<string, string> R = key => HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;

                    string[] headers = { R("PUC_ThCuenta"), R("PUC_ThNombre"), R("PUC_ThLargo"), R("PUC_ThTipo"), R("PUC_Descripcion") };
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
                        ws.Cell(row, 5).Value = fila.Descripcion;

                        var color = i % 2 == 0 ? XLColor.FromArgb(248, 250, 252) : XLColor.White;
                        for (int c = 1; c <= 5; c++)
                        {
                            ws.Cell(row, c).Style.Fill.BackgroundColor = color;
                            ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            ws.Cell(row, c).Style.Border.OutsideBorderColor = XLColor.FromColor(Color.LightGray);
                        }
                    }

                    ws.Columns().AdjustToContents();
                    ws.Range(1, 1, 1, 5).SetAutoFilter();
                    ws.SheetView.Freeze(1, 0);

                    string sufijo = string.IsNullOrWhiteSpace(cuenta) ? "Completo" : cuenta;
                    string nombreArchivo = $"TablaPUC_{sufijo}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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
