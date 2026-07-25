using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Web;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class ConfiguracionRelacionamientoController : BaseController
    {
        private ConfiguracionRelacionamientoService _service = new ConfiguracionRelacionamientoService();

        private bool VerificarAdmin()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            return usuario?.Admin >= 1;
        }

        /// <summary>
        /// Vista principal de configuracion de relacionamiento (solo admin)
        /// </summary>
        public ActionResult ConfiguracionRelacionamiento()
        {
            if (!VerificarAdmin())
            {
                return RedirectToAction("Index", "Home");
            }

            ViewBag.TablasRel = _service.ObtenerTablasRelDisponibles();
            return View("~/Views/Configuracion/ConfiguracionRelacionamiento.cshtml");
        }

        /// <summary>
        /// Procesa el archivo Excel y carga los datos en las tablas REL_
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel)
        {
            if (!VerificarAdmin())
            {
                return RedirectToAction("ConfiguracionRelacionamiento");
            }

            if (archivoExcel == null || archivoExcel.ContentLength == 0)
            {
                TempData["ErrorMessage"] = "No se seleccionó ningún archivo";
                return RedirectToAction("ConfiguracionRelacionamiento");
            }

            string extension = Path.GetExtension(archivoExcel.FileName)?.ToLower();
            if (extension != ".xlsx" && extension != ".xlsm")
            {
                TempData["ErrorMessage"] = "Solo se permiten archivos Excel (.xlsx, .xlsm)";
                return RedirectToAction("ConfiguracionRelacionamiento");
            }

            try
            {
                using (var stream = new MemoryStream())
                {
                    archivoExcel.InputStream.CopyTo(stream);
                    stream.Position = 0;

                    using (var package = new XLWorkbook(stream))
                    {
                        var resultado = _service.CargarDatosDesdeExcel(package);

                        TempData["ResultadoCarga"] = resultado;
                        TempData["NombreArchivo"] = Path.GetFileName(archivoExcel.FileName);

                        if (resultado.Exito)
                        {
                            TempData["SuccessMessage"] = resultado.Mensaje;
                        }
                        else
                        {
                            TempData["ErrorMessage"] = resultado.Mensaje;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al procesar el archivo: {ex.Message}";
            }

            return RedirectToAction("ConfiguracionRelacionamiento");
        }

        /// <summary>
        /// Descarga un Excel con todos los datos actuales de las tablas REL_
        /// </summary>
        public ActionResult DescargarRelacionamientos()
        {
            if (!VerificarAdmin())
                return RedirectToAction("ConfiguracionRelacionamiento");

            try
            {
                byte[] fileBytes = _service.ExportarRelacionamientosExcel();
                string fileName = $"Relacionamientos_BUFINS_SQL_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al exportar relacionamientos: {ex.Message}";
                return RedirectToAction("ConfiguracionRelacionamiento");
            }
        }

        /// <summary>
        /// Descarga plantilla Excel con las hojas y columnas de las tablas REL_
        /// </summary>
        public ActionResult DescargarPlantilla()
        {
            if (!VerificarAdmin())
            {
                return RedirectToAction("ConfiguracionRelacionamiento");
            }

            try
            {
                byte[] fileBytes = _service.GenerarPlantillaExcel();
                string fileName = $"Plantilla_Relacionamientos_{DateTime.Now:yyyyMMdd}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al generar plantilla: {ex.Message}";
                return RedirectToAction("ConfiguracionRelacionamiento");
            }
        }
    }
}
