using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using OfficeOpenXml;
using System;
using System.IO;
using System.Web;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class ConfiguracionVariablesPBIController : BaseController
    {
        private readonly ConfiguracionVariablesPBIService _service = new ConfiguracionVariablesPBIService();

        private bool VerificarSuperAdmin()
        {
            return UsuarioSesionHelper.EsSuperAdmin();
        }

        /// <summary>
        /// Vista principal de configuración de Variables PBI (solo Super Admin)
        /// </summary>
        public ActionResult Index()
        {
            if (!VerificarSuperAdmin())
                return RedirectToAction("Index", "Home");

            ViewBag.EstadoActual = _service.ObtenerEstadoActual();
            return View("~/Views/Configuracion/ConfiguracionVariablesPBI.cshtml");
        }

        /// <summary>
        /// Procesa el archivo Excel y carga los datos en OrdenVariables
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel)
        {
            if (!VerificarSuperAdmin())
                return RedirectToAction("Index");

            if (archivoExcel == null || archivoExcel.ContentLength == 0)
            {
                TempData["ErrorMessage"] = "No se seleccionó ningún archivo";
                return RedirectToAction("Index");
            }

            string extension = Path.GetExtension(archivoExcel.FileName)?.ToLower();
            if (extension != ".xlsx" && extension != ".xlsm")
            {
                TempData["ErrorMessage"] = "Solo se permiten archivos Excel (.xlsx, .xlsm)";
                return RedirectToAction("Index");
            }

            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                string usuarioNombre = $"{usuario?.Nombre} {usuario?.Apellidos}".Trim();
                if (string.IsNullOrWhiteSpace(usuarioNombre))
                    usuarioNombre = usuario?.Usuario ?? "Sistema";

                using (var stream = new MemoryStream())
                {
                    archivoExcel.InputStream.CopyTo(stream);
                    stream.Position = 0;

                    using (var package = new ExcelPackage(stream))
                    {
                        var resultado = _service.CargarDesdeExcel(package, usuarioNombre);
                        resultado.NombreArchivo = Path.GetFileName(archivoExcel.FileName);

                        TempData["ResultadoCarga"] = resultado;

                        if (resultado.Exito)
                            TempData["SuccessMessage"] = resultado.Mensaje;
                        else
                            TempData["ErrorMessage"] = resultado.Mensaje;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al procesar el archivo: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// Descarga plantilla Excel con los encabezados de OrdenVariables
        /// </summary>
        public ActionResult DescargarPlantilla()
        {
            if (!VerificarSuperAdmin())
                return RedirectToAction("Index");

            try
            {
                byte[] fileBytes = _service.GenerarPlantillaExcel();
                string fileName = $"Plantilla_VariablesPBI_{DateTime.Now:yyyyMMdd}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al generar plantilla: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}
