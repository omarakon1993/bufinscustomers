using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;
using bufinscustomers.Helpers;
using bufinscustomers.Permisos;
using System.Collections.Generic;
using System.Linq;
using OfficeOpenXml;
using System.IO;
using System;
using OfficeOpenXml.Style;
using System.Drawing;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class AuditoriaCarguesController : BaseController
    {
        private AuditoriaCarguesService _auditoriaCarguesService = new AuditoriaCarguesService();

        // Listar auditorias de cargues
        public ActionResult AuditoriaCargues()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            
            List<AuditoriaCargues> auditorias;
            
            if (esAdmin)
            {
                // Si es admin, obtener todos los registros
                auditorias = _auditoriaCarguesService.ObtenerAuditoriaCargues();
            }
            else
            {
                // Si no es admin, mostrar su empresa y las de su mismo grupo empresarial
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                auditorias = _auditoriaCarguesService.ObtenerAuditoriaCargues()
                    .Where(a => idsPermitidos.Contains(a.IdEmpresa)).ToList();
            }

            return View("~/Views/Informes/AuditoriaCargues.cshtml", auditorias);
        }

        // Exportar auditor�a a Excel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExportarAuditoriaExcel(string empresa = "", string usuario = "", string fechaDesde = "", string fechaHasta = "")
        {
            try
            {
                var usuarioActual = UsuarioSesionHelper.UsuarioActual;
                var esAdmin = UsuarioSesionHelper.EsSuperAdmin();

                // Obtener datos seg�n permisos
                List<AuditoriaCargues> auditorias;

                if (esAdmin)
                {
                    auditorias = _auditoriaCarguesService.ObtenerAuditoriaCargues();
                }
                else
                {
                    var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuarioActual) ?? new List<int>();
                    auditorias = _auditoriaCarguesService.ObtenerAuditoriaCargues()
                        .Where(a => idsPermitidos.Contains(a.IdEmpresa)).ToList();
                }

                // Aplicar filtros
                if (!string.IsNullOrEmpty(empresa))
                    auditorias = auditorias.Where(a => a.NombreEmpresa == empresa).ToList();

                if (!string.IsNullOrEmpty(usuario))
                    auditorias = auditorias.Where(a => a.Usuario.IndexOf(usuario, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                if (DateTime.TryParse(fechaDesde, out var desde))
                    auditorias = auditorias.Where(a => a.FechaCargue.Date >= desde.Date).ToList();

                if (DateTime.TryParse(fechaHasta, out var hasta))
                    auditorias = auditorias.Where(a => a.FechaCargue.Date <= hasta.Date).ToList();

                // Generar archivo Excel
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Auditor�a Cargues");
                    
                    // Configurar encabezados
                    Func<string, string> R = key => HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;
                    var headers = new List<string>
                    {
                        R("Audit_ExcelFecha"),
                        R("Audit_ExcelHora"),
                        R("Audit_ThEmpresa"),
                        R("Audit_ThUsuario"),
                        R("Audit_ThArchivo")
                    };
                    
                    // Aplicar encabezados
                    for (int i = 0; i < headers.Count; i++)
                    {
                        worksheet.Cells[1, i + 1].Value = headers[i];
                        worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(88, 58, 255));
                        worksheet.Cells[1, i + 1].Style.Font.Color.SetColor(Color.White);
                        worksheet.Cells[1, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    }
                    
                    // Llenar datos
                    int row = 2;
                    foreach (var auditoria in auditorias.OrderByDescending(x => x.FechaCargue))
                    {
                        int col = 1;
                        
                        worksheet.Cells[row, col++].Value = auditoria.FechaCargue.ToString("dd/MM/yyyy");
                        worksheet.Cells[row, col++].Value = auditoria.FechaCargue.ToString("HH:mm:ss");
                        worksheet.Cells[row, col++].Value = auditoria.NombreEmpresa;
                        worksheet.Cells[row, col++].Value = auditoria.Usuario;
                        worksheet.Cells[row, col++].Value = auditoria.NombreArchivo;
                        row++;
                    }
                    
                    // Agregar total de registros al final
                    if (auditorias.Any())
                    {
                        row += 1; // Espacio
                        worksheet.Cells[row, 1].Value = "Total de registros:";
                        worksheet.Cells[row, 1].Style.Font.Bold = true;
                        worksheet.Cells[row, 2].Value = auditorias.Count;
                        worksheet.Cells[row, 2].Style.Font.Bold = true;
                    }
                    
                    // Ajustar anchos de columna
                    worksheet.Cells.AutoFitColumns();
                    
                    // Aplicar bordes
                    var dataRange = worksheet.Cells[1, 1, row, headers.Count];
                    dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    
                    // Generar nombre de archivo
                    var hayFiltros = !string.IsNullOrEmpty(empresa) || !string.IsNullOrEmpty(usuario)
                                  || !string.IsNullOrEmpty(fechaDesde) || !string.IsNullOrEmpty(fechaHasta);
                    var nombreArchivo = $"Auditoria_Cargues_{DateTime.Now:yyyyMMdd_HHmmss}{(hayFiltros ? "_Filtrado" : "")}.xlsx";
                    
                    // Convertir a bytes
                    var fileBytes = package.GetAsByteArray();
                    
                    // Retornar archivo para descarga
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombreArchivo);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage($"Error al exportar: {ex.Message}");
                return RedirectToAction("AuditoriaCargues");
            }
        }
    }
}