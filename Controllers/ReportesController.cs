using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Web.Mvc;
using bufinscustomers.Helpers;
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

        [HttpGet]
        public JsonResult ObtenerEmpresas()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = usuario?.Admin == 1;

            if (!esAdmin)
                return Json(new { success = false, message = "No autorizado" }, JsonRequestBehavior.AllowGet);

            var empresas = new List<object>();

            using (var conn = new SqlConnection(cadena))
            using (var cmd = new SqlCommand("SELECT EmpId, EmpNombre FROM dbo.Empresas", conn))
            {
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        empresas.Add(new
                        {
                            id = Convert.ToInt32(reader["EmpId"]),
                            nombre = reader["EmpNombre"].ToString()
                        });
                    }
                }
            }

            return Json(empresas, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerAniosReporte()
        {
            var iniciales = new HashSet<int>();
            var finales = new HashSet<int>();

            using (var conn = new SqlConnection(cadena))
            using (var cmd = new SqlCommand("SELECT añoinicial, añofinal FROM dbo.Reportes", conn))
            {
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (int.TryParse(reader["añoinicial"].ToString(), out int ai))
                            iniciales.Add(ai);

                        if (int.TryParse(reader["añofinal"].ToString(), out int af))
                            finales.Add(af);
                    }
                }
            }

            var inicialesOrdenados = new List<int>(iniciales);
            var finalesOrdenados = new List<int>(finales);
            inicialesOrdenados.Sort();
            finalesOrdenados.Sort();

            return Json(new
            {
                iniciales = inicialesOrdenados,
                finales = finalesOrdenados
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerReportes()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = usuario?.Admin == 1;
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var reportes = new List<object>();

            using (var conn = new SqlConnection(cadena))
            {
                conn.Open();

                SqlCommand cmd;

                if (esAdmin)
                {
                    // Trae todos los reportes para administrador
                    cmd = new SqlCommand("SELECT Id, Nombre FROM dbo.Reportes ORDER BY Nombre", conn);
                }
                else
                {
                    // Trae solo los reportes de la empresa del usuario
                    cmd = new SqlCommand("SELECT Id, Nombre FROM dbo.Reportes WHERE IdEmpresa = @IdEmpresa ORDER BY Nombre", conn);
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                }

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        reportes.Add(new
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Nombre = reader["Nombre"].ToString()
                        });
                    }
                }
            }

            return Json(reportes, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerReportesFiltrados(string nombre = "", int? anioInicial = null, int? anioFinal = null, string empresa = "")
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var esAdmin = usuario?.Admin == 1;
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var reportes = new List<object>();

            using (var conn = new SqlConnection(cadena))
            {
                var query = "SELECT Id, Nombre FROM dbo.Reportes INNER JOIN [dbo].[Empresas] ON EmpId = IdEmpresa WHERE 1=1";
                var parametros = new List<SqlParameter>();

                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    query += " AND Nombre LIKE @nombre";
                    parametros.Add(new SqlParameter("@nombre", $"%{nombre}%"));
                }

                if (anioInicial.HasValue && anioFinal.HasValue)
                {
                    // Filtrar por rango: AñoInicial mayor o igual y AñoFinal menor o igual
                    query += " AND AñoInicial >= @anioInicial AND AñoFinal <= @anioFinal";
                    parametros.Add(new SqlParameter("@anioInicial", anioInicial.Value));
                    parametros.Add(new SqlParameter("@anioFinal", anioFinal.Value));
                }
                else if (anioInicial.HasValue)
                {
                    // Solo registros cuyo AñoInicial sea exactamente el año indicado
                    query += " AND AñoInicial = @anioInicial";
                    parametros.Add(new SqlParameter("@anioInicial", anioInicial.Value));
                }
                else if (anioFinal.HasValue)
                {
                    // Solo registros cuyo AñoFinal sea exactamente el año indicado
                    query += " AND AñoFinal = @anioFinal";
                    parametros.Add(new SqlParameter("@anioFinal", anioFinal.Value));
                }

                if (!esAdmin)
                {
                    query += " AND IdEmpresa = @idEmpresa";
                    parametros.Add(new SqlParameter("@idEmpresa", idEmpresa));
                }
                else if (!string.IsNullOrEmpty(empresa))
                {
                    query += " AND EmpNombre = @empresaNombre";
                    parametros.Add(new SqlParameter("@empresaNombre", empresa));
                }

                query += " ORDER BY Nombre";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddRange(parametros.ToArray());
                    conn.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            reportes.Add(new
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Nombre = reader["Nombre"].ToString()
                            });
                        }
                    }
                }
            }

            return Json(reportes, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult ObtenerEnlaceReporte(int id)
        {
            string enlace = "";

            using (var conn = new SqlConnection(cadena))
            using (var cmd = new SqlCommand("SELECT EnlaceHTML FROM dbo.Reportes WHERE Id = @Id", conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                conn.Open();

                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    enlace = result.ToString();
                }
            }

            return Json(new { enlace }, JsonRequestBehavior.AllowGet);
        }

    }
}
