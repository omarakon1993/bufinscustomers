using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Web.Mvc;
using bufinscustomers.Models;

namespace bufinscustomers.Controllers
{
    public class ReportesController : Controller
    {
        private static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        public ActionResult VerReporte(string url, string titulo = "")
        {
            ViewBag.UrlReporte = url;
            ViewBag.Titulo = titulo;
            return View("Reportes"); 
        }

        public static List<Reporte> ObtenerReportesParaLayout()
        {
            var reportes = new List<Reporte>();

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
                        reportes.Add(new Reporte
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


        public static List<Reporte> ObtenerReportesPorEmpresaParaLayout(int idEmpresa)
        {
            var reportes = new List<Reporte>();

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
                        reportes.Add(new Reporte
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
