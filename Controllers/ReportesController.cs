using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System.Text.RegularExpressions;
using System.Configuration;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class ReportesController : BaseController
    {
        private ReportesService _reportesService = new ReportesService();

        private bool EsURLValida(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true; 

            try
            {
                if (string.IsNullOrWhiteSpace(url) || url.Trim() == "")
                    return true;

                url = url.Trim();

                string pattern = @"^(https?:\/\/)?([\w\-]+\.)+[\w\-]+(\/.*)?$";
                Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);

                try
                {
                    string urlParaValidar = url;
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        urlParaValidar = "https://" + url;
                    }
                    
                    Uri uriResult;
                    bool esUriValida = Uri.TryCreate(urlParaValidar, UriKind.Absolute, out uriResult) 
                                      && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                    
                    if (esUriValida)
                    {
                        string dominioPattern = @"^[\w\-]+(\.[\w\-]+)+$";
                        Regex dominioRegex = new Regex(dominioPattern);
                        bool dominioValido = dominioRegex.IsMatch(uriResult.Host);
                        
                        return dominioValido || regex.IsMatch(url);
                    }
                    else
                    {
                        return regex.IsMatch(url);
                    }
                }
                catch (Exception)
                {
                    return regex.IsMatch(url);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public ActionResult VerReporte(string url, string titulo = "")
        {
            ViewBag.UrlReporte = url;
            ViewBag.Titulo = titulo;
            return View("Reportes");
        }

        public ActionResult MaestroReportes()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var reportes = _reportesService.ObtenerReportes();

            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                reportes = reportes.FindAll(r => r.IdEmpresa == usuario.IdEmpresa);
            }

            ViewBag.Empresas = UsuarioSesionHelper.EsSuperAdmin()
                ? _reportesService.ObtenerEmpresas()
                : _reportesService.ObtenerEmpresas().FindAll(e => e.Id == usuario.IdEmpresa);

            return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
        }

        [HttpPost]
        public ActionResult CrearReporte(Reportes reporte)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (!UsuarioSesionHelper.EsSuperAdmin() && reporte.IdEmpresa != usuario.IdEmpresa)
            {
                TempData["ErrorMessage"] = "No tiene permisos para crear reportes en otra empresa.";
                return RedirectToAction("MaestroReportes");
            }

            if (!string.IsNullOrWhiteSpace(reporte.EnlaceHTML) && !EsURLValida(reporte.EnlaceHTML))
            {
                TempData["ErrorMessage"] = "El enlace HTML no tiene un formato válido. Formato esperado: https://ejemplo.com";
                ViewBag.Empresas = _reportesService.ObtenerEmpresas();
                var reportes = _reportesService.ObtenerReportes();
                return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
            }

            string mensaje;
            bool registrado = _reportesService.CrearReporte(reporte, out mensaje);

            if (registrado)
            {
                TempData["SuccessMessage"] = "Reporte creado correctamente.";
                return RedirectToAction("MaestroReportes");
            }
            else
            {
                TempData["ErrorMessage"] = mensaje;
                ViewBag.Empresas = _reportesService.ObtenerEmpresas();
                var reportes = _reportesService.ObtenerReportes();
                return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
            }
        }

    
        [HttpPost]
        public ActionResult EditarReporte(Reportes reporte)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (!UsuarioSesionHelper.EsSuperAdmin() && reporte.IdEmpresa != usuario.IdEmpresa)
            {
                TempData["ErrorMessage"] = "No tiene permisos para editar reportes de otra empresa.";
                return RedirectToAction("MaestroReportes");
            }

            if (!string.IsNullOrWhiteSpace(reporte.EnlaceHTML) && !EsURLValida(reporte.EnlaceHTML))
            {
                TempData["ErrorMessage"] = "El enlace HTML no tiene un formato válido. Formato esperado: https://ejemplo.com";
                ViewBag.Empresas = _reportesService.ObtenerEmpresas();
                var reportes = _reportesService.ObtenerReportes();
                return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
            }

            bool actualizado = _reportesService.EditarReporte(reporte);

            if (actualizado)
            {
                TempData["SuccessMessage"] = "Reporte actualizado correctamente.";
                return RedirectToAction("MaestroReportes");
            }
            else
            {
                TempData["ErrorMessage"] = "Error al actualizar el reporte.";
                ViewBag.Empresas = _reportesService.ObtenerEmpresas();
                var reportes = _reportesService.ObtenerReportes();
                return View("~/Views/Configuracion/MaestroReportes.cshtml", reportes);
            }
        }

      
        [HttpPost]
        public ActionResult EliminarReporte(int idReporte)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                var reporte = _reportesService.ObtenerReportes().Find(r => r.Id == idReporte);
                if (reporte != null && reporte.IdEmpresa != usuario.IdEmpresa)
                {
                    TempData["ErrorMessage"] = "No tiene permisos para eliminar reportes de otra empresa.";
                    return RedirectToAction("MaestroReportes");
                }
            }

            bool eliminado = _reportesService.EliminarReporte(idReporte);

            if (eliminado)
                TempData["SuccessMessage"] = "Reporte eliminado correctamente.";
            else
                TempData["ErrorMessage"] = "Error al eliminar el reporte.";

            return RedirectToAction("MaestroReportes");
        }

 
        [HttpPost]
        public JsonResult ValidarURL(string url)
        {
            bool esValida = EsURLValida(url);
            return Json(new { valida = esValida }, JsonRequestBehavior.AllowGet);
        }

        public static string ObtenerNombreEmpresaPorId(int idEmpresa)
        {
            string nombreEmpresa = "";
            string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var conn = new SqlConnection(connectionString))
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

            var empresas = new List<object>();

            using (var conn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd;

                if (UsuarioSesionHelper.EsSuperAdmin())
                {
                    cmd = new SqlCommand("SELECT EmpId, EmpNombre FROM dbo.Empresas", conn);
                }
                else
                {
                    cmd = new SqlCommand("SELECT EmpId, EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa", conn);
                    cmd.Parameters.AddWithValue("@IdEmpresa", usuario.IdEmpresa);
                }

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

            using (var conn = new SqlConnection(CadenaConexion))
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
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var reportes = new List<object>();

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();

                SqlCommand cmd;

                if (esAdmin)
                {
                    cmd = new SqlCommand("SELECT Id, Nombre FROM dbo.Reportes ORDER BY Nombre", conn);
                }
                else
                {
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
            var esAdmin = UsuarioSesionHelper.EsSuperAdmin();
            var idEmpresa = usuario?.IdEmpresa ?? 0;

            var reportes = new List<object>();

            using (var conn = new SqlConnection(CadenaConexion))
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
            int idEmpresaReporte = 0;

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT EnlaceHTML, IdEmpresa FROM dbo.Reportes WHERE Id = @Id", conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                conn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        enlace = reader["EnlaceHTML"]?.ToString() ?? "";
                        idEmpresaReporte = Convert.ToInt32(reader["IdEmpresa"]);
                    }
                }
            }

            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (idEmpresaReporte != usuario.IdEmpresa)
                {
                    return Json(new { enlace = "", error = "No tiene permisos para ver este reporte" }, JsonRequestBehavior.AllowGet);
                }
            }

            // Validar que el enlace sea válido antes de retornarlo
            if (!string.IsNullOrWhiteSpace(enlace) && !EsURLValida(enlace))
            {
                return Json(new { enlace = "", error = "El enlace almacenado no tiene un formato válido" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { enlace }, JsonRequestBehavior.AllowGet);
        }

    }
}
