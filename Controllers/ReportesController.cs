using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    public class ReportesController : Controller
    {
        private static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";
        private ReportesService _reportesService = new ReportesService();

        // Vista para mostrar reportes (existing functionality)
        public ActionResult VerReporte(string url, string titulo = "")
        {
            ViewBag.UrlReporte = url;
            ViewBag.Titulo = titulo;
            return View("Reportes"); 
        }

        // Maestro de reportes - Listar reportes
        public ActionResult MaestroReportes()
        {
            var reportes = _reportesService.ObtenerReportes();
            ViewBag.Empresas = _reportesService.ObtenerEmpresas();
            return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
        }

        // POST: Crear reporte
        [HttpPost]
        public ActionResult CrearReporte(Reportes reporte)
        {
            string mensaje;
            bool registrado = _reportesService.CrearReporte(reporte, out mensaje);

            if (registrado)
            {
                TempData["SuccessMessage"] = "Reporte creado correctamente.";
                return RedirectToAction("MaestroReportes");
            }
            else
            {
                ViewBag.ErrorMessage = mensaje;
                ViewBag.Empresas = _reportesService.ObtenerEmpresas();
                var reportes = _reportesService.ObtenerReportes();
                return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
            }
        }

        // POST: Editar reporte
        [HttpPost]
        public ActionResult EditarReporte(Reportes reporte)
        {
            bool actualizado = _reportesService.EditarReporte(reporte);

            if (actualizado)
            {
                TempData["SuccessMessage"] = "Reporte actualizado correctamente.";
                return RedirectToAction("MaestroReportes");
            }
            else
            {
                ViewBag.ErrorMessage = "Error al actualizar el reporte.";
                ViewBag.Empresas = _reportesService.ObtenerEmpresas();
                var reportes = _reportesService.ObtenerReportes();
                return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
            }
        }

        // POST: Eliminar reporte
        [HttpPost]
        public ActionResult EliminarReporte(int idReporte)
        {
            bool eliminado = _reportesService.EliminarReporte(idReporte);

            if (eliminado)
                TempData["SuccessMessage"] = "Reporte eliminado correctamente.";
            else
                TempData["ErrorMessage"] = "Error al eliminar el reporte.";

            return RedirectToAction("MaestroReportes");
        }

        // Existing static methods for layout functionality
        public static List<Reportes> ObtenerReportesParaLayout()
        {
            var reportes = new List<Reportes>();

            string sql = @"
                            SELECT R.Descripcion, R.EnlaceHTML, E.EmpNombre AS NombreEmpresa
                            FROM dbo.Reportes R
                            INNER JOIN dbo.Empresas E ON R.IdEmpresa = E.EmpId
                        ";

            using (var conn = new SqlConnection(cadena))
            using (var cmd = new SqlCommand(sql, conn))
            {
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        reportes.Add(new Reportes
                        {
                            Descripcion = reader["Descripcion"] != DBNull.Value ? reader["Descripcion"].ToString() : "",
                            EnlaceHTML = reader["EnlaceHTML"] != DBNull.Value ? reader["EnlaceHTML"].ToString() : "",
                            NombreEmpresa = reader["NombreEmpresa"] != DBNull.Value ? reader["NombreEmpresa"].ToString() : ""
                        });
                    }
                }
            }

            return reportes;
        }

        public static List<Reportes> ObtenerReportesPorEmpresaParaLayout(int idEmpresa)
        {
            var reportes = new List<Reportes>();

            string sql = @"SELECT Descripcion, EnlaceHTML 
                   FROM dbo.Reportes 
                   WHERE IdEmpresa = @IdEmpresa";

            using (var conn = new SqlConnection(cadena))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        reportes.Add(new Reportes
                        {
                            Descripcion = reader["Descripcion"] != DBNull.Value ? reader["Descripcion"].ToString() : "",
                            EnlaceHTML = reader["EnlaceHTML"] != DBNull.Value ? reader["EnlaceHTML"].ToString() : ""
                        });
                    }
                }
            }

            return reportes;
        }

        public static string ObtenerNombreEmpresaPorId(int idEmpresa)
        {
            string nombreEmpresa = "";

            using (var conn = new SqlConnection(cadena))
            using (var cmd = new SqlCommand("SELECT EmpNombre FROM Empresas WHERE EmpId = @Id", conn))
            {
                cmd.Parameters.AddWithValue("@Id", idEmpresa);
                conn.Open();

                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    nombreEmpresa = result.ToString();
                }
            }

            return nombreEmpresa;
        }
    }
}
