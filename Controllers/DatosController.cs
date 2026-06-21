using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using OfficeOpenXml;
using OfficeOpenXml.Table;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class DatosController : BaseController
    {
        private readonly EmpresaService _empresaService = new EmpresaService();
        private readonly ConfiguracionEmpresaService _configuracionService = new ConfiguracionEmpresaService();
        private readonly ModeloService _modeloService = new ModeloService();
        private readonly HistorialVersionesCarguesService _historialService = new HistorialVersionesCarguesService();
        private StringBuilder _logBuilder = new StringBuilder();

        private static readonly Dictionary<string, string> _mapeoHistorico =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Z_BalancePrueba",             "Ini_BalancePrueba" },
                { "Z_CteYnoCte",                 "Ini_CteYnoCte" },
                { "Z_EjecPCH",                   "Ini_EjecPCH" },
                { "Z_PCH",                       "Ini_PCH" },
                { "Z_PptoPYGDetallado",          "Ini_PptoPYG" },
                { "Z_PptoPYGDetalladoConAjuste", "Ini_PptoPYGConAjuste" },
                { "Z_PresupuestoBalance",        "Ini_PresupuestoBalance" },
                { "Z_PYGDetallado",              "Ini_PYG" },
                { "Z_PYGDetalladoConAjuste",     "Ini_PYGDetalladoConAjuste" },
            };

        private void LogToFile(string mensaje)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logMessage = $"[{timestamp}] {mensaje}";
                _logBuilder.AppendLine(logMessage);
            }
            catch { /* Ignorar errores de logging */ }
        }

        private void GuardarLogEnSession()
        {
            if (_logBuilder.Length > 0)
            {
                Session["LogImportacion"] = _logBuilder.ToString();
            }
        }

        public ActionResult DescargarLog()
        {
            var log = Session["LogImportacion"] as string;
            if (string.IsNullOrEmpty(log))
            {
                return Content("No hay log disponible para descargar.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(log);
            string fileName = $"LogImportacion_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return File(bytes, "text/plain", fileName);
        }

        // Modelo actions
        public ActionResult Modelo()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = _empresaService.ObtenerEmpresas();
            if (!UsuarioSesionHelper.EsSuperAdmin())
                empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();

            var vm = new Models.ModeloPageViewModel
            {
                Empresas = empresas,
                Modelos = _modeloService.ObtenerModelosActivos()
            };
            return View("~/Views/Datos/Modelo.cshtml", vm);
        }

        [HttpGet]
        public ActionResult ObtenerAnioEjecucion(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                int anio = config != null ? config.AnioEjecucion : DateTime.Now.Year;

                return Json(new { success = true, anioEjecucion = anio }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener el año: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult ObtenerAnosDisponibles(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                if (config == null)
                    return Json(new { success = false, message = "La empresa no tiene configuración creada." }, JsonRequestBehavior.AllowGet);

                var anosHistoricos = config.AnosHistoricos?
                    .Select(a => a.NombreAno)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList() ?? new List<string>();

                return Json(new
                {
                    success = true,
                    anioEjecucion = config.AnioEjecucion,
                    anosHistoricos = anosHistoricos
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EjecutarModelo(int idEmpresa, string anio, int idModelo)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                {
                    SetErrorMessage("Sesión no válida. Por favor, inicie sesión nuevamente.");
                    return RedirectToAction("Login", "Acceso");
                }

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                {
                    SetErrorMessage("No tiene permisos para ejecutar el modelo en esta empresa.");
                    return RedirectToAction("Modelo");
                }

                // El nombre del SP viene SIEMPRE de la BD, nunca del form input
                var modelo = _modeloService.ObtenerModeloPorId(idModelo);
                if (modelo == null)
                {
                    SetErrorMessage("El modelo seleccionado no existe o no está activo.");
                    return RedirectToAction("Modelo");
                }

                using (SqlConnection connection = new SqlConnection(CadenaConexion))
                using (SqlCommand command = new SqlCommand(modelo.NombreSP, connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    command.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                    command.Parameters.AddWithValue("@Año", anio);
                    command.CommandTimeout = 300;

                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        // El SP puede retornar múltiples result sets intermedios.
                        // Iteramos todos y nos quedamos con los valores del último.
                        int codMessage = 0;
                        string mensaje = "No se recibió respuesta del procedimiento.";
                        bool leido = false;

                        do
                        {
                            while (reader.Read())
                            {
                                try
                                {
                                    codMessage = Convert.ToInt32(reader["CodMessage"]);
                                    mensaje = reader["ErrorMessage"].ToString();
                                    leido = true;
                                }
                                catch { /* result set intermedio sin columnas de estado */ }
                            }
                        } while (reader.NextResult());

                        if (leido)
                        {
                            if (codMessage == 1)
                            {
                                SetSuccessMessage(mensaje);
                                new NotificacionesService().Crear(usuario.Id, R("Notif_ModeloEjecutado"), mensaje, "success");
                            }
                            else
                            {
                                SetErrorMessage(mensaje);
                                new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorModelo"), mensaje, "error");
                            }
                        }
                        else
                        {
                            SetErrorMessage("No se recibió respuesta del procedimiento.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al ejecutar el modelo: " + ex.Message);
            }

            return RedirectToAction("Modelo");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EjecutarModeloAjax(int idEmpresa, int idModelo)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return Json(new { exito = false, mensaje = "Sesión no válida.", columnas = new List<string>(), filas = new List<List<string>>() });

            if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                return Json(new { exito = false, mensaje = "No tiene permisos para ejecutar el modelo en esta empresa.", columnas = new List<string>(), filas = new List<List<string>>() });

            var modelo = _modeloService.ObtenerModeloPorId(idModelo);
            if (modelo == null)
                return Json(new { exito = false, mensaje = "El modelo seleccionado no existe o no está activo.", columnas = new List<string>(), filas = new List<List<string>>() });

            try
            {
                using (SqlConnection cn = new SqlConnection(CadenaConexion))
                using (SqlCommand cmd = new SqlCommand(modelo.NombreSP, cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                    cmd.CommandTimeout = 300;
                    cn.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        int codMessage = 1;
                        string mensajeResp = "Ejecutado correctamente.";
                        var lastCols = new List<string>();
                        var lastFilas = new List<List<string>>();

                        do
                        {
                            int fieldCount = reader.FieldCount;
                            var cols = new List<string>();
                            for (int i = 0; i < fieldCount; i++)
                                cols.Add(reader.GetName(i));

                            bool esStatus = cols.Contains("CodMessage");
                            var filas = new List<List<string>>();

                            while (reader.Read())
                            {
                                if (esStatus)
                                {
                                    try { codMessage = Convert.ToInt32(reader["CodMessage"]); } catch { }
                                    try { var m = reader["ErrorMessage"]; if (m != DBNull.Value) mensajeResp = m.ToString(); } catch { }
                                }
                                else
                                {
                                    var fila = new List<string>();
                                    for (int i = 0; i < fieldCount; i++)
                                        fila.Add(reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString());
                                    filas.Add(fila);
                                }
                            }

                            if (!esStatus && cols.Count > 0)
                            {
                                lastCols = cols;
                                lastFilas = filas;
                            }
                        } while (reader.NextResult());

                        if (codMessage == 1)
                            new NotificacionesService().Crear(usuario.Id, R("Notif_ModeloEjecutado"), mensajeResp, "success");
                        else
                            new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorModelo"), mensajeResp, "error");

                        return Json(new { exito = codMessage == 1, mensaje = mensajeResp, columnas = lastCols, filas = lastFilas });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = "Error al ejecutar el modelo: " + ex.Message, columnas = new List<string>(), filas = new List<List<string>>() });
            }
        }

        public ActionResult ExportarTodosModelos(int idEmpresa)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return RedirectToAction("Login", "Acceso");

            if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
            {
                SetErrorMessage("No tiene permisos para exportar datos de esta empresa.");
                return RedirectToAction("Modelo");
            }

            try
            {
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                var modelos = _modeloService.ObtenerModelosActivos();

                using (var package = new ExcelPackage())
                {
                    EscribirHojaIndiceModelos(package.Workbook.Worksheets.Add("Índice"), empresa, modelos, DateTime.Now);

                    var erroresModelos = new List<(string Nombre, string NombreSP, string Mensaje)>();

                    using (SqlConnection cn = new SqlConnection(CadenaConexion))
                    {
                        cn.Open();
                        foreach (var modelo in modelos)
                        {
                            string sheetName = modelo.NombreSP.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
                                ? modelo.NombreSP.Substring(3)
                                : modelo.NombreSP;
                            if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);

                            var ws = package.Workbook.Worksheets.Add(sheetName);
                            try
                            {
                                EjecutarModeloYEscribirHoja(cn, ws, modelo, idEmpresa, usuario.Id);
                            }
                            catch (Exception exModelo)
                            {
                                package.Workbook.Worksheets.Delete(sheetName);
                                erroresModelos.Add((modelo.Nombre, modelo.NombreSP, exModelo.Message));
                            }
                        }
                    }

                    if (erroresModelos.Count > 0)
                    {
                        return Json(new
                        {
                            errores = erroresModelos.Select(e => new { nombre = e.Nombre, nombreSP = e.NombreSP, mensaje = e.Mensaje })
                        }, JsonRequestBehavior.AllowGet);
                    }

                    byte[] fileBytes = package.GetAsByteArray();
                    string empId = !string.IsNullOrWhiteSpace(empresa?.Abreviatura)
                        ? empresa.Abreviatura
                        : (empresa?.Nombre ?? "Empresa").Replace(" ", "_");
                    string fileName = $"Modelos_{empId}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al exportar los modelos: " + ex.Message);
                return RedirectToAction("Modelo");
            }
        }

        private void EscribirHojaIndiceModelos(ExcelWorksheet ws, Empresas empresa, List<ModeloEjecucion> modelos, DateTime fechaExportacion)
        {
            ws.Cells[1, 1].Value = "EXPORTACIÓN DE MODELOS FINANCIEROS";
            ws.Cells[1, 1, 1, 4].Merge = true;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Size = 14;
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            ws.Cells[1, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            ws.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
            ws.Cells[1, 1].Style.Font.Color.SetColor(System.Drawing.Color.White);

            int row = 3;
            Action<string, string> writeInfo = (label, value) =>
            {
                ws.Cells[row, 1].Value = label;
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 2].Value = value;
                ws.Cells[row, 2, row, 4].Merge = true;
                row++;
            };

            writeInfo("Empresa:", empresa?.Nombre ?? "—");
            writeInfo("NIT:", empresa?.Nit ?? "—");
            writeInfo("Dirección:", empresa?.Direccion ?? "—");
            writeInfo("Fecha de exportación:", fechaExportacion.ToString("dd/MM/yyyy HH:mm:ss"));
            writeInfo("Total modelos:", modelos.Count.ToString());

            row++;
            ws.Cells[row, 1].Value = "#";
            ws.Cells[row, 2].Value = "Modelo";
            ws.Cells[row, 3].Value = "Procedimiento";
            ws.Cells[row, 4].Value = "Descripción";
            var hdrRange = ws.Cells[row, 1, row, 4];
            hdrRange.Style.Font.Bold = true;
            hdrRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            hdrRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 250));
            row++;

            foreach (var m in modelos)
            {
                ws.Cells[row, 1].Value = m.Orden;
                ws.Cells[row, 2].Value = m.Nombre;
                ws.Cells[row, 3].Value = m.NombreSP;
                ws.Cells[row, 4].Value = m.Descripcion;
                row++;
            }

            ws.Column(1).Width = 6;
            ws.Column(2).Width = 28;
            ws.Column(3).Width = 32;
            ws.Column(4).Width = 42;
        }

        private static readonly HashSet<string> _moneyColNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Valor", "ValorAcumulado", "ValorFuturo" };

        private void EjecutarModeloYEscribirHoja(SqlConnection cn, ExcelWorksheet ws, ModeloEjecucion modelo, int idEmpresa, int idUsuario)
        {
            using (var cmd = new SqlCommand(modelo.NombreSP, cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.CommandTimeout = 180;

                using (var reader = cmd.ExecuteReader())
                {
                    List<string> lastCols      = null;
                    List<bool>   lastIsMoney   = null;
                    List<List<object>> lastData = null;

                    do
                    {
                        int fieldCount = reader.FieldCount;
                        var cols = new List<string>();
                        for (int i = 0; i < fieldCount; i++)
                            cols.Add(reader.GetName(i));

                        bool esStatus = cols.Contains("CodMessage");

                        if (!esStatus && fieldCount > 0)
                        {
                            // Marca qué columnas son dinero — se decide una sola vez por result set
                            var isMoney = cols.Select(c => _moneyColNames.Contains(c)).ToList();
                            var data    = new List<List<object>>();

                            while (reader.Read())
                            {
                                var row = new List<object>();
                                for (int i = 0; i < fieldCount; i++)
                                {
                                    if (reader.IsDBNull(i))
                                    {
                                        row.Add(null);
                                    }
                                    else if (isMoney[i])
                                    {
                                        // Lee el valor nativo del reader — SIN pasar por string.
                                        // Convert.ToDecimal maneja money, decimal, float, int, etc.
                                        try   { row.Add(Convert.ToDecimal(reader.GetValue(i))); }
                                        catch { row.Add(reader.GetValue(i).ToString()); }
                                    }
                                    else
                                    {
                                        row.Add(reader.GetValue(i).ToString());
                                    }
                                }
                                data.Add(row);
                            }

                            lastCols    = cols;
                            lastIsMoney = isMoney;
                            lastData    = data;
                        }
                        else
                        {
                            while (reader.Read()) { }
                        }
                    } while (reader.NextResult());

                    if (lastCols == null || lastData == null) return;

                    // Headers
                    for (int c = 0; c < lastCols.Count; c++)
                        ws.Cells[1, c + 1].Value = lastCols[c];

                    var headerRange = ws.Cells[1, 1, 1, lastCols.Count];
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
                    headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);

                    // Data — EPPlus escribe decimal como número real, string como texto
                    for (int r = 0; r < lastData.Count; r++)
                        for (int c = 0; c < lastData[r].Count; c++)
                            ws.Cells[r + 2, c + 1].Value = lastData[r][c];

                    // Formatos por rango de columna completa (una operación por columna)
                    if (lastData.Count > 0)
                    {
                        for (int c = 0; c < lastCols.Count; c++)
                        {
                            string fmt = lastIsMoney[c] ? "#,##0.00" : "@";
                            ws.Cells[2, c + 1, lastData.Count + 1, c + 1].Style.Numberformat.Format = fmt;
                        }

                        string safeName = "tbl_" + Regex.Replace(ws.Name, "[^A-Za-z0-9]", "_");
                        var tbl = ws.Tables.Add(ws.Cells[1, 1, lastData.Count + 1, lastCols.Count], safeName);
                        tbl.TableStyle = TableStyles.Medium2;
                    }

                    ws.View.FreezePanes(2, 1);
                    for (int c = 1; c <= lastCols.Count; c++)
                        ws.Column(c).Width = 22;
                }
            }
        }

        public ActionResult ExportarModeloIndividual(int idEmpresa, int idModelo)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return RedirectToAction("Login", "Acceso");

            if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
            {
                SetErrorMessage("No tiene permisos para exportar datos de esta empresa.");
                return RedirectToAction("Modelo");
            }

            var modelo = _modeloService.ObtenerModeloPorId(idModelo);
            if (modelo == null)
            {
                SetErrorMessage("El modelo seleccionado no existe o no está activo.");
                return RedirectToAction("Modelo");
            }

            try
            {
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);

                using (var package = new ExcelPackage())
                {
                    EscribirHojaIndiceModelos(
                        package.Workbook.Worksheets.Add("Índice"),
                        empresa, new List<ModeloEjecucion> { modelo }, DateTime.Now);

                    string sheetName = modelo.NombreSP.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
                        ? modelo.NombreSP.Substring(3) : modelo.NombreSP;
                    if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);

                    var ws = package.Workbook.Worksheets.Add(sheetName);
                    using (var cn = new SqlConnection(CadenaConexion))
                    {
                        cn.Open();
                        EjecutarModeloYEscribirHoja(cn, ws, modelo, idEmpresa, usuario.Id);
                    }

                    byte[] fileBytes = package.GetAsByteArray();
                    string empId = !string.IsNullOrWhiteSpace(empresa?.Abreviatura)
                        ? empresa.Abreviatura
                        : (empresa?.Nombre ?? "Empresa").Replace(" ", "_");
                    string fileName = $"{sheetName}_{empId}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al exportar el modelo: " + ex.Message);
                return RedirectToAction("Modelo");
            }
        }

        public ActionResult CargueExcel()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            int idEmpresa = usuario?.IdEmpresa ?? 0;

            List<Empresas> empresasDisponibles;
            if (UsuarioSesionHelper.EsSuperAdmin())
            {
                empresasDisponibles = _empresaService.ObtenerEmpresas();
            }
            else
            {
                var empresaUsuario = _empresaService.ObtenerEmpresas()
                    .FirstOrDefault(e => e.Id == idEmpresa);
                empresasDisponibles = empresaUsuario != null
                    ? new List<Empresas> { empresaUsuario }
                    : new List<Empresas>();
            }
            ViewBag.Empresas = empresasDisponibles;
            ViewBag.UltimoUsuarioCargue = ObtenerUltimoUsuarioCargue(idEmpresa);

            return View("~/Views/Datos/CargueExcel.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel, int idEmpresaSeleccionada, int anioSeleccionado, string modoSeleccionado)
        {
            var resultado = new ResultadoCargaExcel();
            string nombreArchivoOriginal = "";

            // Validar empresa
            if (idEmpresaSeleccionada == 0)
            {
                SetErrorMessage("Debe seleccionar una empresa antes de cargar el archivo.");
                return RedirectToAction("CargueExcel");
            }

            var usuarioValidacion = UsuarioSesionHelper.UsuarioActual;
            if (!UsuarioSesionHelper.EsSuperAdmin() && usuarioValidacion?.IdEmpresa != idEmpresaSeleccionada)
            {
                SetErrorMessage("No tiene permisos para cargar datos en esta empresa.");
                return RedirectToAction("CargueExcel");
            }

            var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresaSeleccionada);
            if (config == null)
            {
                SetErrorMessage("La empresa seleccionada no tiene configuración creada.");
                return RedirectToAction("CargueExcel");
            }

            // Validar modo
            bool modoEjecucion = string.Equals(modoSeleccionado, "ejecucion", StringComparison.OrdinalIgnoreCase);
            bool modoHistorico = string.Equals(modoSeleccionado, "historico", StringComparison.OrdinalIgnoreCase);

            if (!modoEjecucion && !modoHistorico)
            {
                SetErrorMessage("Debe seleccionar un año antes de cargar el archivo.");
                return RedirectToAction("CargueExcel");
            }

            if (modoEjecucion && anioSeleccionado != config.AnioEjecucion)
            {
                SetErrorMessage($"El año seleccionado ({anioSeleccionado}) no coincide con el año de ejecución configurado ({config.AnioEjecucion}).");
                return RedirectToAction("CargueExcel");
            }

            var anosHistoricosCfg = config.AnosHistoricos?
                .Select(a => a.NombreAno?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

            if (modoHistorico && !anosHistoricosCfg.Contains(anioSeleccionado.ToString()))
            {
                SetErrorMessage($"El año seleccionado ({anioSeleccionado}) no está configurado como año histórico para esta empresa.");
                return RedirectToAction("CargueExcel");
            }

            // Validar archivo
            if (archivoExcel == null || archivoExcel.ContentLength == 0)
            {
                SetErrorMessage("No se seleccionó ningún archivo.");
                return RedirectToAction("CargueExcel");
            }

            string extension = Path.GetExtension(archivoExcel.FileName)?.ToLower();
            if (extension != ".xlsx" && extension != ".xlsm")
            {
                SetErrorMessage("Solo se permiten archivos Excel (.xlsx, .xlsm).");
                return RedirectToAction("CargueExcel");
            }

            nombreArchivoOriginal = Path.GetFileName(archivoExcel.FileName);
            var usuarioActual = UsuarioSesionHelper.UsuarioActual;
            int idUsuario = usuarioActual?.Id ?? 0;

            try
            {
                using (var package = new ExcelPackage(archivoExcel.InputStream))
                    {
                        int hojasEsperadas = _mapeoHistorico.Count;
                        if (package.Workbook.Worksheets.Count != hojasEsperadas)
                        {
                            resultado.Exito = false;
                            resultado.Mensaje = $"La plantilla debe contener exactamente {hojasEsperadas} hojas. El archivo tiene {package.Workbook.Worksheets.Count} hojas.";
                            TempData["ResultadoCarga"] = resultado;
                            TempData["NombreArchivo"] = nombreArchivoOriginal;
                            SetErrorMessage(resultado.Mensaje);
                            return RedirectToAction("CargueExcel");
                        }

                        if (modoEjecucion)
                        {
                            bool errorEnCargaEjecucion = false;

                            using (var conn = new SqlConnection(CadenaConexion))
                            {
                                conn.Open();
                                using (var tx = conn.BeginTransaction())
                                {
                                    try
                                    {
                                        try
                                        {
                                            string _snapEmpresa = _empresaService.ObtenerEmpresas()
                                                .Find(e => e.Id == idEmpresaSeleccionada)?.Nombre ?? "Desconocida";
                                            string _snapUsuario = ((usuarioActual?.Nombre ?? "") + " " + (usuarioActual?.Apellidos ?? "")).Trim();
                                            _historialService.CrearSnapshotEnTransaccion(
                                                conn, tx, idEmpresaSeleccionada, _snapEmpresa,
                                                anioSeleccionado, 0, idUsuario, _snapUsuario, nombreArchivoOriginal);
                                        }
                                        catch (Exception snapEx) { LogToFile($"Advertencia snapshot ejecucion: {snapEx.Message}"); }

                                        EliminarEjecucionDeIni(conn, anioSeleccionado, idEmpresaSeleccionada, tx);

                                        foreach (var hoja in package.Workbook.Worksheets)
                                        {
                                            var detalle = new DetalleCargaHojaExcel { NombreHoja = hoja.Name };
                                            var nombreNorm = NormalizarNombre(hoja.Name);
                                            var dt = LeerHojaEnDataTable(hoja, detalle, anioSeleccionado.ToString());

                                            if (dt == null)
                                            {
                                                resultado.DetalleHojas.Add(detalle);
                                                resultado.TotalHojasIgnoradas++;
                                                continue;
                                            }

                                            bool exitoHoja;
                                            if (_mapeoHistorico.TryGetValue(nombreNorm, out string nombreTablaIni))
                                            {
                                                detalle.NombreTabla = nombreTablaIni;
                                                exitoHoja = GuardarEnIni(conn, dt, nombreTablaIni, idEmpresaSeleccionada, idUsuario, historicoLog: 0, tx: tx);
                                            }
                                            else
                                            {
                                                exitoHoja = GuardarEnSQLServer(dt, idEmpresaSeleccionada, idUsuario);
                                            }

                                            if (!exitoHoja)
                                            {
                                                detalle.Estado = "Error";
                                                detalle.MensajeError = TempData["Mensaje"]?.ToString() ?? "Error al guardar";
                                                resultado.DetalleHojas.Add(detalle);

                                                foreach (var d in resultado.DetalleHojas.Where(x => x.Estado == "Exitoso"))
                                                {
                                                    d.Estado = "Revertido";
                                                    d.MensajeError = "Revertido por error en otra tabla";
                                                }

                                                resultado.Exito = false;
                                                resultado.Mensaje = $"Error al procesar '{hoja.Name}'. Se revirtio toda la carga.";
                                                resultado.MostrarDescargaLog = TempData["MostrarDescargaLog"] != null && (bool)TempData["MostrarDescargaLog"];

                                                tx.Rollback();
                                                LimpiarTablasEnError(conn, idEmpresaSeleccionada);
                                                errorEnCargaEjecucion = true;
                                                break;
                                            }

                                            detalle.FilasInsertadas = dt.Rows.Count;
                                            detalle.Estado = "Exitoso";
                                            resultado.DetalleHojas.Add(detalle);
                                            resultado.TotalHojasProcesadas++;
                                            resultado.TotalFilasInsertadas += dt.Rows.Count;
                                        }

                                        if (!errorEnCargaEjecucion)
                                        {
                                            RegistrarAuditoria(conn, nombreArchivoOriginal, idEmpresaSeleccionada, tx);
                                            tx.Commit();
                                        }
                                    }
                                    catch (Exception txEx)
                                    {
                                        try { tx.Rollback(); } catch { }
                                        LogToFile($"Error inesperado en transaccion de ejecucion: {txEx.Message}");
                                        throw;
                                    }
                                }
                            }

                            if (errorEnCargaEjecucion)
                            {
                                TempData["ResultadoCarga"] = resultado;
                                TempData["NombreArchivo"] = nombreArchivoOriginal;
                                SetErrorMessage(resultado.Mensaje);
                                return RedirectToAction("CargueExcel");
                            }

                            var resultadoValidacion = EjecutarValidacionDatos(idUsuario);
                            if (resultadoValidacion.esExitoso)
                            {
                                resultado.Exito = true;
                                resultado.Mensaje = $"Carga exitosa: {resultado.TotalHojasProcesadas} tabla(s) con {resultado.TotalFilasInsertadas:N0} registros. {resultadoValidacion.mensaje}";
                            }
                            else
                            {
                                resultado.Exito = false;
                                resultado.Mensaje = resultadoValidacion.mensaje;
                                resultado.MostrarDescargaLog = true;
                            }
                        }
                        else // modoHistorico
                        {
                            bool errorEnCargaHistorico = false;

                            using (var conn = new SqlConnection(CadenaConexion))
                            {
                                conn.Open();
                                using (var tx = conn.BeginTransaction())
                                {
                                    try
                                    {
                                        try
                                        {
                                            string _snapEmpresa = _empresaService.ObtenerEmpresas()
                                                .Find(e => e.Id == idEmpresaSeleccionada)?.Nombre ?? "Desconocida";
                                            string _snapUsuario = ((usuarioActual?.Nombre ?? "") + " " + (usuarioActual?.Apellidos ?? "")).Trim();
                                            _historialService.CrearSnapshotEnTransaccion(
                                                conn, tx, idEmpresaSeleccionada, _snapEmpresa,
                                                anioSeleccionado, 1, idUsuario, _snapUsuario, nombreArchivoOriginal);
                                        }
                                        catch (Exception snapEx) { LogToFile($"Advertencia snapshot historico: {snapEx.Message}"); }

                                        EliminarAnosHistoricosDeIni(conn, anioSeleccionado, idEmpresaSeleccionada, tx);

                                        foreach (var hoja in package.Workbook.Worksheets)
                                        {
                                            var detalle = new DetalleCargaHojaExcel { NombreHoja = hoja.Name };
                                            var nombreNormalizado = NormalizarNombre(hoja.Name);

                                            if (!_mapeoHistorico.TryGetValue(nombreNormalizado, out string nombreTablaIni))
                                            {
                                                detalle.NombreTabla = "-";
                                                detalle.Estado = "Ignorada";
                                                detalle.MensajeError = "Sin tabla historica correspondiente";
                                                resultado.DetalleHojas.Add(detalle);
                                                resultado.TotalHojasIgnoradas++;
                                                continue;
                                            }

                                            detalle.NombreTabla = nombreTablaIni;
                                            var dt = LeerHojaEnDataTable(hoja, detalle, anioSeleccionado.ToString());

                                            if (dt == null)
                                            {
                                                resultado.DetalleHojas.Add(detalle);
                                                resultado.TotalHojasIgnoradas++;
                                                continue;
                                            }

                                            if (!GuardarEnIni(conn, dt, nombreTablaIni, idEmpresaSeleccionada, idUsuario, tx: tx))
                                            {
                                                detalle.Estado = "Error";
                                                detalle.MensajeError = TempData["Mensaje"]?.ToString() ?? "Error al guardar en tabla historica";
                                                resultado.DetalleHojas.Add(detalle);

                                                foreach (var d in resultado.DetalleHojas.Where(x => x.Estado == "Exitoso"))
                                                {
                                                    d.Estado = "Revertido";
                                                    d.MensajeError = "Revertido por error en otra tabla";
                                                }

                                                resultado.Exito = false;
                                                resultado.Mensaje = $"Error al procesar '{hoja.Name}'. Se revirtio toda la carga historica.";
                                                resultado.MostrarDescargaLog = true;

                                                tx.Rollback();
                                                errorEnCargaHistorico = true;
                                                break;
                                            }

                                            detalle.FilasInsertadas = dt.Rows.Count;
                                            detalle.Estado = "Exitoso";
                                            resultado.DetalleHojas.Add(detalle);
                                            resultado.TotalHojasProcesadas++;
                                            resultado.TotalFilasInsertadas += dt.Rows.Count;
                                        }

                                        if (!errorEnCargaHistorico)
                                        {
                                            RegistrarAuditoria(conn, nombreArchivoOriginal, idEmpresaSeleccionada, tx);
                                            tx.Commit();
                                        }
                                    }
                                    catch (Exception txEx)
                                    {
                                        try { tx.Rollback(); } catch { }
                                        LogToFile($"Error inesperado en transaccion historica: {txEx.Message}");
                                        throw;
                                    }
                                }
                            }

                            if (errorEnCargaHistorico)
                            {
                                TempData["ResultadoCarga"] = resultado;
                                TempData["NombreArchivo"] = nombreArchivoOriginal;
                                SetErrorMessage(resultado.Mensaje);
                                return RedirectToAction("CargueExcel");
                            }

                            resultado.Exito = true;
                            resultado.Mensaje = $"Carga historica exitosa: {resultado.TotalHojasProcesadas} tabla(s) con {resultado.TotalFilasInsertadas:N0} registros.";
                        }

                        GuardarLogEnSession();
                        TempData["ResultadoCarga"] = resultado;
                        TempData["NombreArchivo"] = nombreArchivoOriginal;

                        if (resultado.Exito)
                        {
                            SetSuccessMessage(resultado.Mensaje);
                            if (usuarioValidacion != null)
                                new NotificacionesService().Crear(usuarioValidacion.Id, R("Notif_CargueCompletado"), resultado.Mensaje, "success");
                        }
                        else
                        {
                            SetErrorMessage(resultado.Mensaje);
                            if (usuarioValidacion != null)
                                new NotificacionesService().Crear(usuarioValidacion.Id, R("Notif_ErrorCargue"), resultado.Mensaje, "error");
                        }
                    }
            }
            catch (Exception ex)
            {
                resultado.Exito = false;
                resultado.Mensaje = $"Error al procesar el archivo: {ex.Message}";
                GuardarLogEnSession();
                TempData["ResultadoCarga"] = resultado;
                TempData["NombreArchivo"] = nombreArchivoOriginal;
                SetErrorMessage(resultado.Mensaje);
            }

            return RedirectToAction("CargueExcel");
        }

        private (bool esExitoso, string mensaje) EjecutarValidacionDatos(int idUsuario)
        {
            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("dbo.SP_ValidarPlantillaInicial", conn))
                using (var adapter = new SqlDataAdapter(cmd))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);

                    var dt = new DataTable();
                    adapter.Fill(dt);

                    if (dt.Rows.Count == 1 &&
                        int.TryParse(dt.Rows[0]["CodMessage"]?.ToString(), out int codMessage) &&
                        codMessage == 1)
                    {
                        return (true, dt.Rows[0]["ErrorMessage"]?.ToString() ?? "Validación exitosa.");
                    }
                    else
                    {
                        var errores = new StringBuilder();
                        errores.AppendLine("Se detectaron errores en la validación:");
                        foreach (DataRow row in dt.Rows)
                        {
                            var valores = row.ItemArray.Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v));
                            errores.AppendLine(string.Join(" | ", valores));
                        }
                        return (false, errores.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Error en SP_ValidarPlantillaInicial: {ex.Message}");
                return (true, "Datos cargados (no se pudo ejecutar validación adicional).");
            }
        }

        private bool GuardarEnSQLServer(DataTable tabla, int idEmpresa, int idUsuario)
        {
            SqlConnection conn = null;
            try
            {
                DateTime fechaCargue = DateTime.Now;
                LogToFile($"========== INICIO GUARDADO TABLA: {tabla.TableName} ==========");
                LogToFile($"Filas en DataTable: {tabla.Rows.Count}");

                conn = new SqlConnection(CadenaConexion);
                conn.Open();
                LogToFile("Conexión a SQL Server abierta exitosamente");

                // Normalizar nombre
                tabla.TableName = NormalizarNombre(tabla.TableName);

                // Crear tabla si no existe
                CrearTablaSiNoExiste(conn, tabla);

                // Asegurar columnas extra
                if (!tabla.Columns.Contains("IdEmpresa"))
                    tabla.Columns.Add("IdEmpresa", typeof(int));
                if (!tabla.Columns.Contains("IdUsuario"))
                    tabla.Columns.Add("IdUsuario", typeof(int));
                if (!tabla.Columns.Contains("FechaCargue"))
                    tabla.Columns.Add("FechaCargue", typeof(DateTime));

                // Setear columnas para todas las filas
                foreach (DataRow row in tabla.Rows)
                {
                    row["IdEmpresa"] = idEmpresa;
                    row["IdUsuario"] = idUsuario;
                    row["FechaCargue"] = fechaCargue;
                }

                // Limpiar registros anteriores solo de esa empresa
                using (SqlCommand deleteCmd = new SqlCommand($@"
                    DELETE FROM [dbo].[{tabla.TableName}]
                    WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                {
                    deleteCmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    deleteCmd.ExecuteNonQuery();
                }

                // LOGGING: Información de la tabla
                LogToFile($"Columnas del DataTable ({tabla.Columns.Count}): {string.Join(", ", tabla.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");

#if DEBUG
                for (int i = 0; i < Math.Min(3, tabla.Rows.Count); i++)
                {
                    var valores = tabla.Rows[i].ItemArray.Select(v => v?.ToString() ?? "NULL").ToList();
                    LogToFile($"Fila {i + 1} (muestra): {string.Join(" | ", valores)}");
                }
#endif

                // Leer esquema una sola vez y reutilizar en conversión y validación
                var esquema = LeerEsquemaTabla(conn, tabla.TableName);

                // Convertir valores vacíos a 0 en columnas money/decimal
                LogToFile("Convirtiendo valores vacíos a 0 en columnas de dinero...");
                ConvertirValoresVaciosAZero(tabla, esquema);

                // Rechazar cargue si alguna celda numérica contiene texto (ej: "ggg")
                string errorNumerico = ValidarValoresNumericos(tabla, esquema);
                if (!string.IsNullOrEmpty(errorNumerico))
                {
                    LogToFile($"ERROR NUMÉRICO: {errorNumerico}");
                    GuardarLogEnSession();
                    TempData["Mensaje"] = $"ERROR NUMÉRICO en '{tabla.TableName}':\n\n{errorNumerico}";
                    TempData["MensajeTipo"] = "error";
                    TempData["MostrarDescargaLog"] = true;
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                // Normalizar separadores decimales/miles antes de insertar
                LogToFile("Sanitizando valores numéricos...");
                SanitizarColumnasNumericas(tabla, esquema);

                // Validar datos antes de insertar
                LogToFile("Iniciando validación de datos...");
                string errorValidacion = ValidarDatosParaBulkCopy(tabla, esquema);
                if (!string.IsNullOrEmpty(errorValidacion))
                {
                    LogToFile($"ERROR EN VALIDACIÓN: {errorValidacion}");
                    GuardarLogEnSession();
                    TempData["Mensaje"] = $"ERROR DE VALIDACIÓN en '{tabla.TableName}':\n\n{errorValidacion}";
                    TempData["MensajeTipo"] = "error";
                    TempData["MostrarDescargaLog"] = true;

                    // Limpiar tablas en caso de error
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                LogToFile("Validación previa completada sin errores");

                // Insertar lo nuevo
                try
                {
                    LogToFile($"Iniciando SqlBulkCopy hacia tabla: {tabla.TableName}");
                    using (SqlBulkCopy bulk = new SqlBulkCopy(conn))
                    {
                        bulk.DestinationTableName = $"[dbo].[{tabla.TableName}]";
                        bulk.BulkCopyTimeout = 120;
                        bulk.BatchSize = 5000;
                        bulk.WriteToServer(tabla);
                    }
                    LogToFile($"SqlBulkCopy completado exitosamente para {tabla.TableName}");
                }
                catch (Exception bulkEx)
                {
                    LogToFile($"ERROR en SqlBulkCopy: {bulkEx.Message}");
                    LogToFile($"StackTrace: {bulkEx.StackTrace}");

                    string detalleError = AnalizarErrorBulkCopy(conn, tabla, bulkEx);
                    LogToFile($"Análisis de error: {detalleError}");
                    GuardarLogEnSession();

                    TempData["Mensaje"] = $"ERROR AL INSERTAR en '{tabla.TableName}':\n\n{detalleError}";
                    TempData["MensajeTipo"] = "error";
                    TempData["MostrarDescargaLog"] = true;

                    // Limpiar tablas en caso de error
                    LimpiarTablasEnError(conn, idEmpresa);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"ERROR GENERAL: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
                GuardarLogEnSession();

                TempData["Mensaje"] = $"ERROR GENERAL al guardar en '{tabla.TableName}':\n{ex.Message}";
                TempData["MensajeTipo"] = "error";
                TempData["MostrarDescargaLog"] = true;

                LimpiarTablasEnError(conn, idEmpresa);

                return false;
            }
            finally
            {
                if (conn != null)
                {
                    conn.Close();
                    conn.Dispose();
                }
            }
        }

        private Dictionary<string, (string tipo, bool nullable)> LeerEsquemaTabla(SqlConnection conn, string tableName)
        {
            var esquema = new Dictionary<string, (string tipo, bool nullable)>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(
                "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                conn))
            {
                cmd.Parameters.AddWithValue("@t", tableName);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        esquema[reader["COLUMN_NAME"].ToString()] = (
                            reader["DATA_TYPE"].ToString(),
                            reader["IS_NULLABLE"].ToString() == "YES"
                        );
                    }
                }
            }
            return esquema;
        }

        private void ConvertirValoresVaciosAZero(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            try
            {
                var columnasMoneyDecimal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in esquema)
                {
                    string dataType = kv.Value.tipo.ToLower();
                    if (dataType == "money" || dataType == "smallmoney" ||
                        dataType == "decimal" || dataType == "numeric")
                    {
                        string colLower = kv.Key.ToLower();
                        if (!colLower.Contains("año") && !colLower.Contains("anio") &&
                            !colLower.Contains("year") && !colLower.Contains("mes") &&
                            !colLower.Contains("month") && !colLower.Contains("periodo") &&
                            !colLower.Contains("id"))
                        {
                            columnasMoneyDecimal.Add(kv.Key);
                        }
                    }
                }

                if (columnasMoneyDecimal.Count == 0)
                {
                    LogToFile("No se encontraron columnas de dinero para convertir");
                    return;
                }

                LogToFile($"Columnas de dinero encontradas: {string.Join(", ", columnasMoneyDecimal)}");

                int valoresConvertidos = 0;

                foreach (DataRow fila in tabla.Rows)
                {
                    for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                    {
                        string nombreColumna = tabla.Columns[colIndex].ColumnName;

                        if (columnasMoneyDecimal.Contains(nombreColumna))
                        {
                            object valor = fila[colIndex];
                            string valorStr = valor?.ToString()?.Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                fila[colIndex] = "0";
                                valoresConvertidos++;
                            }
                        }
                    }
                }

                LogToFile($"Se convirtieron {valoresConvertidos} valores vacíos a 0");
            }
            catch (Exception ex)
            {
                LogToFile($"Error al convertir valores vacíos: {ex.Message}");
            }
        }

        // Tablas Z_ que se usan en GuardarEnSQLServer pero no tienen mapeo en _mapeoHistorico
        private static readonly string[] _tablasZSinMapeo = { "Z_TablaPUC" };

        private void LimpiarTablasEnError(SqlConnection conn, int idEmpresa)
        {
            if (conn == null || conn.State != ConnectionState.Open) return;

            // Limpiar todas las tablas Z_: las del mapeo Ini_ + las que no tienen mapeo
            var tablasALimpiar = _mapeoHistorico.Keys.Concat(_tablasZSinMapeo);

            foreach (var tabla in tablasALimpiar)
            {
                try
                {
                    using (var cmd = new SqlCommand(
                        $"DELETE FROM [dbo].[{tabla}] WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL", conn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { /* tabla puede no existir, continuar con las demás */ }
            }
        }

        // ─── New helper methods ───────────────────────────────────────────────────

        private DataTable LeerHojaEnDataTable(ExcelWorksheet hoja, DetalleCargaHojaExcel detalle, string filtroAnio = null)
        {
            int totalCols = hoja.Dimension?.End.Column ?? 0;
            int totalRows = hoja.Dimension?.End.Row ?? 0;

            if (totalCols == 0 || totalRows == 0)
            {
                if (string.IsNullOrEmpty(detalle.NombreTabla)) detalle.NombreTabla = "-";
                detalle.Estado = "Ignorada";
                detalle.MensajeError = "La hoja está vacía";
                return null;
            }

            bool filaCabeceraValida = false;
            for (int col = 1; col <= totalCols; col++)
            {
                if (!string.IsNullOrWhiteSpace(hoja.Cells[1, col].Text))
                {
                    filaCabeceraValida = true;
                    break;
                }
            }
            if (!filaCabeceraValida)
            {
                if (string.IsNullOrEmpty(detalle.NombreTabla)) detalle.NombreTabla = "-";
                detalle.Estado = "Ignorada";
                detalle.MensajeError = "La hoja no tiene encabezados válidos";
                return null;
            }

            int columnasValidas = 0;
            for (int col = 1; col <= totalCols; col++)
            {
                if (!string.IsNullOrWhiteSpace(hoja.Cells[1, col].Text.Trim()))
                    columnasValidas++;
                else
                    break;
            }
            if (columnasValidas == 0)
            {
                if (string.IsNullOrEmpty(detalle.NombreTabla)) detalle.NombreTabla = "-";
                detalle.Estado = "Ignorada";
                detalle.MensajeError = "No se encontraron columnas con encabezado";
                return null;
            }

            // Localizar columna de año para filtrar (si aplica)
            int colAnioIdx = -1;
            if (filtroAnio != null)
            {
                for (int col = 1; col <= columnasValidas; col++)
                {
                    var header = hoja.Cells[1, col].Text?.Trim() ?? "";
                    if (string.Equals(header, "Año", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(header, "Anio", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(header, "Year", StringComparison.OrdinalIgnoreCase))
                    {
                        colAnioIdx = col;
                        break;
                    }
                }
            }

            string nombreTabla = NormalizarNombre(hoja.Name);
            if (string.IsNullOrEmpty(detalle.NombreTabla))
                detalle.NombreTabla = nombreTabla;
            detalle.TotalColumnas = columnasValidas;

            var dt = new DataTable(nombreTabla);
            for (int col = 1; col <= columnasValidas; col++)
                dt.Columns.Add(hoja.Cells[1, col].Text.Trim(), typeof(object));

            int filasVaciasConsecutivas = 0;
            int filasIgnoradasPorAnio = 0;
            for (int row = 2; row <= totalRows; row++)
            {
                bool filaVacia = true;
                var dr = dt.NewRow();
                for (int col = 1; col <= columnasValidas; col++)
                {
                    var cellObj = hoja.Cells[row, col].Value;
                    object drVal;
                    if (cellObj is double)
                    {
                        // Celda numérica: guardar como double nativo — sin conversión a texto
                        // para evitar toda ambigüedad de separadores decimales/miles
                        drVal = cellObj;
                        filaVacia = false; // cualquier número (incluso cero) es dato real
                    }
                    else
                    {
                        string valorStr = cellObj?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(valorStr)) filaVacia = false;
                        drVal = (object)valorStr;
                    }
                    dr[col - 1] = drVal;
                }

                if (filaVacia)
                {
                    filasVaciasConsecutivas++;
                    if (filasVaciasConsecutivas >= 5) break;
                    continue;
                }

                // Filtrar por año seleccionado: filas con otro año se omiten silenciosamente
                if (filtroAnio != null && colAnioIdx != -1)
                {
                    var valorAnio = dr[colAnioIdx - 1]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(valorAnio) && valorAnio != filtroAnio)
                    {
                        filasIgnoradasPorAnio++;
                        continue;
                    }
                }

                filasVaciasConsecutivas = 0;
                dt.Rows.Add(dr);
            }

            if (filasIgnoradasPorAnio > 0)
                LogToFile($"Hoja '{hoja.Name}': {filasIgnoradasPorAnio} fila(s) omitidas por año distinto a {filtroAnio}");

            if (dt.Rows.Count == 0)
            {
                detalle.Estado = "Ignorada";
                detalle.MensajeError = filtroAnio != null
                    ? $"No hay filas con año {filtroAnio} en esta hoja"
                    : "La hoja no contiene filas de datos";
                return null;
            }

            return dt;
        }

        private void EliminarAnosHistoricosDeIni(SqlConnection conn, int anio, int idEmpresa, SqlTransaction tx = null)
        {
            foreach (var tablaIni in _mapeoHistorico.Values.Distinct())
            {
                using (var cmd = new SqlCommand(
                    $"DELETE FROM [dbo].[{tablaIni}] WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Ano", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Ano", anio);
                    cmd.ExecuteNonQuery();
                }
                LogToFile($"Eliminados datos históricos de {tablaIni} año {anio} para empresa {idEmpresa}");
            }
        }

        private void EliminarEjecucionDeIni(SqlConnection conn, int anio, int idEmpresa, SqlTransaction tx = null)
        {
            foreach (var tablaIni in _mapeoHistorico.Values.Distinct())
            {
                using (var cmd = new SqlCommand(
                    $"DELETE FROM [dbo].[{tablaIni}] WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Ano AND Historico_Log = 0", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Ano", anio);
                    cmd.ExecuteNonQuery();
                }
                LogToFile($"Eliminados datos de ejecución de {tablaIni} año {anio} para empresa {idEmpresa}");
            }
        }

        private bool GuardarEnIni(SqlConnection conn, DataTable dtExcel, string nombreTablaIni, int idEmpresa, int idUsuario, byte historicoLog = 1, SqlTransaction tx = null)
        {
            try
            {
                LogToFile($"========== INICIO GUARDADO HISTÓRICO: {nombreTablaIni} ==========");
                LogToFile($"Filas en DataTable: {dtExcel.Rows.Count}");

                // Obtener esquema de la tabla Ini_ (nombre y tipo)
                var columnasIni = new List<string>();
                var columnasNumericas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                    conn, tx))
                {
                    cmd.Parameters.AddWithValue("@t", nombreTablaIni);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string col = reader["COLUMN_NAME"].ToString();
                            string tipo = reader["DATA_TYPE"].ToString().ToLower();
                            columnasIni.Add(col);
                            if (tipo == "money" || tipo == "smallmoney" || tipo == "decimal" ||
                                tipo == "numeric" || tipo == "float" || tipo == "real" ||
                                tipo == "int" || tipo == "bigint" || tipo == "smallint" || tipo == "tinyint")
                                columnasNumericas.Add(col);
                        }
                    }
                }

                if (columnasIni.Count == 0)
                {
                    LogToFile($"Tabla {nombreTablaIni} no encontrada en BD");
                    TempData["Mensaje"] = $"No se encontró la tabla '{nombreTablaIni}' en la base de datos.";
                    TempData["MostrarDescargaLog"] = true;
                    return false;
                }

                // Mapa de columnas del Excel (case-insensitive)
                var colsExcel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dtExcel.Columns.Count; i++)
                    colsExcel[dtExcel.Columns[i].ColumnName] = i;

                // DataTable destino con esquema de Ini_
                var dtDest = new DataTable();
                foreach (var col in columnasIni)
                    dtDest.Columns.Add(col, typeof(object));

                DateTime fechaCargue = DateTime.Now;

                foreach (DataRow srcRow in dtExcel.Rows)
                {
                    var destRow = dtDest.NewRow();

                    // Valores de referencia para la Llave
                    string cuenta  = colsExcel.ContainsKey("Cuenta")  ? srcRow[colsExcel["Cuenta"]].ToString()  : "";
                    string empresa = colsExcel.ContainsKey("Empresa") ? srcRow[colsExcel["Empresa"]].ToString() : "";
                    string ano     = colsExcel.ContainsKey("Año")     ? srcRow[colsExcel["Año"]].ToString() :
                                     colsExcel.ContainsKey("Anio")    ? srcRow[colsExcel["Anio"]].ToString() : "";
                    string mes     = colsExcel.ContainsKey("Mes")     ? srcRow[colsExcel["Mes"]].ToString()     : "";

                    foreach (var colIni in columnasIni)
                    {
                        switch (colIni)
                        {
                            case "Llave":
                                destRow[colIni] = cuenta + empresa + ano + mes;
                                break;
                            case "IdEmpresa_Log":
                                destRow[colIni] = idEmpresa;
                                break;
                            case "IdUsuarioCargue_Log":
                                destRow[colIni] = idUsuario;
                                break;
                            case "FechaCargue_Log":
                                destRow[colIni] = fechaCargue;
                                break;
                            case "Historico_Log":
                                destRow[colIni] = historicoLog;
                                break;
                            case "IdUsuarioEjecucion_Log":
                            case "FechaEjecucion_Log":
                            case "Observacion_Log":
                                destRow[colIni] = DBNull.Value;
                                break;
                            default:
                                if (colsExcel.ContainsKey(colIni))
                                {
                                    var cellValue = srcRow[colsExcel[colIni]];
                                    if (columnasNumericas.Contains(colIni))
                                    {
                                        if (cellValue is string sValIni && !string.IsNullOrWhiteSpace(sValIni))
                                        {
                                            string razonIni = DetectarErrorNumerico(sValIni);
                                            if (razonIni != null)
                                            {
                                                int filaExcel = dtExcel.Rows.IndexOf(srcRow) + 2;
                                                string errorFmt =
                                                    $"Fila {filaExcel}, Columna '{colIni}': {razonIni}. " +
                                                    $"Revise la hoja completa por errores similares antes de reintentar.";
                                                LogToFile($"ERROR NUMÉRICO en {nombreTablaIni}: Fila {filaExcel}, Columna '{colIni}', valor '{sValIni}'");
                                                GuardarLogEnSession();
                                                TempData["Mensaje"] = $"ERROR NUMÉRICO en '{nombreTablaIni}':\n\n{errorFmt}";
                                                TempData["MostrarDescargaLog"] = true;
                                                return false;
                                            }
                                            LogToFile($"Advertencia: Columna '{colIni}' fila {dtExcel.Rows.IndexOf(srcRow) + 2} " +
                                                      $"tiene valor texto '{sValIni}' — se parseará como número. " +
                                                      $"Considere usar formato Número en Excel.");
                                        }
                                        destRow[colIni] = (object)ExtraerDecimal(cellValue);
                                    }
                                    else
                                    {
                                        var rawVal = cellValue?.ToString()?.Trim() ?? "";
                                        destRow[colIni] = rawVal.Length > 0 ? (object)rawVal : DBNull.Value;
                                    }
                                }
                                else
                                {
                                    destRow[colIni] = DBNull.Value;
                                }
                                break;
                        }
                    }

                    dtDest.Rows.Add(destRow);
                }

                using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx))
                {
                    bulk.DestinationTableName = $"[dbo].[{nombreTablaIni}]";
                    bulk.BulkCopyTimeout = 120;
                    bulk.BatchSize = 5000;
                    foreach (DataColumn col in dtDest.Columns)
                        bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                    bulk.WriteToServer(dtDest);
                }

                LogToFile($"Guardado histórico exitoso: {dtDest.Rows.Count} filas en {nombreTablaIni}");
                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"ERROR en GuardarEnIni ({nombreTablaIni}): {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
                GuardarLogEnSession();
                TempData["Mensaje"] = $"ERROR al guardar en '{nombreTablaIni}': {ex.Message}";
                TempData["MostrarDescargaLog"] = true;
                return false;
            }
        }

        // ─── Existing helper methods ──────────────────────────────────────────────

        /// <summary>
        /// Convierte cualquier valor de celda EPPlus a decimal exacto para SqlBulkCopy.
        /// Para double nativo usa round-trip via string; para texto usa SanitizarValorNumerico.
        /// Nunca lanza excepción: retorna 0 ante cualquier caso inválido.
        /// </summary>
        private decimal ExtraerDecimal(object cellValue)
        {
            if (cellValue == null) return 0m;

            if (cellValue is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) return 0m;
                // Round-trip via string para preservar los decimales del double sin ruido flotante
                string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out decimal dec)
                    ? dec : 0m;
            }

            if (cellValue is decimal dm) return dm;
            if (cellValue is int iv)    return (decimal)iv;
            if (cellValue is long lv)   return (decimal)lv;

            // Texto: pasar siempre por la heurística colombiana (coma=decimal, punto=miles)
            string str = SanitizarValorNumerico(cellValue.ToString()?.Trim() ?? "");
            return decimal.TryParse(str, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out decimal r)
                ? r : 0m;
        }

        private string SanitizarValorNumerico(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return "0";
            var v = valor.Trim().Replace(" ", "").Replace("$", "");
            if (v == "-" || v == "–" || v == "—") return "0";
            if (string.IsNullOrEmpty(v)) return "0";

            int lastDot   = v.LastIndexOf('.');
            int lastComma = v.LastIndexOf(',');

            string s;
            if (lastDot >= 0 && lastComma >= 0)
            {
                // Ambos separadores: el último es el decimal
                s = lastDot > lastComma
                    ? v.Replace(",", "")                    // US: 15,549.00 → 15549.00
                    : v.Replace(".", "").Replace(",", "."); // Colombiano: 15.549,00 → 15549.00
            }
            else if (lastComma >= 0)
            {
                // En formato colombiano (es-CO) la coma es siempre separador decimal.
                // El separador de miles colombiano es el punto (manejado en el bloque de lastDot).
                // Ej: "86,751"→86.751  |  "15,549"→15.549  |  "15549,00"→15549.00
                s = v.Replace(",", ".");
            }
            else if (lastDot >= 0)
            {
                int dotCount = v.Count(c => c == '.');
                if (dotCount > 1)
                {
                    s = v.Replace(".", ""); // varios puntos = miles: 1.554.900 → 1554900
                }
                else
                {
                    // Único punto: miles si (1-2 dígitos numéricos antes Y exactamente 3 después)
                    // Ej: "15.549"→miles=15549  |  "150.534"→decimal  |  "15549.00"→decimal
                    string beforeDot = v.Substring(0, lastDot).TrimStart('-');
                    int afterDotLen  = v.Length - lastDot - 1;
                    bool esMiles = afterDotLen == 3
                                   && beforeDot.Length > 0 && beforeDot.Length <= 2
                                   && beforeDot != "0";
                    s = esMiles ? v.Replace(".", "") : v;
                }
            }
            else
            {
                s = v;
            }

            return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out _)
                ? s : "0";
        }

        private static readonly System.Text.RegularExpressions.Regex _regexCientifica =
            new System.Text.RegularExpressions.Regex(
                @"^-?\d+[\.,]?\d*[Ee][+\-]?\d+$",
                System.Text.RegularExpressions.RegexOptions.None);

        private static string DetectarErrorNumerico(string valor)
        {
            string v = valor.Trim();
            // Notación científica (ej: "1,489E-05"): rechazar, el usuario debe ajustar el formato en Excel
            if (_regexCientifica.IsMatch(v))
                return $"el valor '{valor}' está en notación científica. Amplíe el ancho de la columna en Excel y ajuste el formato a Número";
            if (v.Any(c => char.IsLetter(c)))
                return $"el valor '{valor}' contiene texto";
            if (v.Count(c => c == ',') > 1)
                return $"el valor '{valor}' tiene más de un separador decimal (coma)";
            return null;
        }

        private string ValidarValoresNumericos(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            foreach (DataColumn col in tabla.Columns)
            {
                if (!esquema.TryGetValue(col.ColumnName, out var info)) continue;
                string tipo = info.tipo.ToLower();
                bool esNumerico = tipo == "money" || tipo == "smallmoney" || tipo == "decimal"
                               || tipo == "numeric" || tipo == "float"   || tipo == "real"
                               || tipo == "int"    || tipo == "bigint"   || tipo == "smallint"
                               || tipo == "tinyint";
                if (!esNumerico) continue;

                string colLower = col.ColumnName.ToLower();
                if (colLower.Contains("año") || colLower.Contains("anio") || colLower.Contains("year")
                    || colLower.Contains("mes") || colLower.Contains("month") || colLower.Contains("id"))
                    continue;

                for (int r = 0; r < tabla.Rows.Count; r++)
                {
                    object val = tabla.Rows[r][col];
                    if (val is string sVal && !string.IsNullOrWhiteSpace(sVal))
                    {
                        string razon = DetectarErrorNumerico(sVal);
                        if (razon != null)
                            return $"Fila {r + 2}, Columna '{col.ColumnName}': {razon}. " +
                                   $"Revise la hoja completa por errores similares antes de reintentar.";
                    }
                }
            }
            return null;
        }

        private void SanitizarColumnasNumericas(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            foreach (DataRow fila in tabla.Rows)
            {
                for (int i = 0; i < tabla.Columns.Count; i++)
                {
                    string colName = tabla.Columns[i].ColumnName;
                    if (!esquema.ContainsKey(colName)) continue;
                    string tipo = esquema[colName].tipo.ToLower();
                    if (tipo != "money" && tipo != "smallmoney" && tipo != "decimal" &&
                        tipo != "numeric" && tipo != "float" && tipo != "real") continue;
                    string colLower = colName.ToLower();
                    if (colLower.Contains("año") || colLower.Contains("anio") || colLower.Contains("year") ||
                        colLower.Contains("mes") || colLower.Contains("month") || colLower.Contains("id"))
                        continue;
                    // Guardar como decimal nativo: SqlBulkCopy lo mapea directamente
                    fila[i] = (object)ExtraerDecimal(fila[i]);
                }
            }
        }

        private string AnalizarErrorBulkCopy(SqlConnection conn, DataTable tabla, Exception bulkEx)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Error original: {bulkEx.Message}");
            sb.AppendLine();

            try
            {
                var columnasSQL = new Dictionary<string, string>();
                using (var cmd = new SqlCommand(
                    "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                    conn))
                {
                    cmd.Parameters.AddWithValue("@t", tabla.TableName);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string colName = reader["COLUMN_NAME"].ToString();
                            string dataType = reader["DATA_TYPE"].ToString();
                            string nullable = reader["IS_NULLABLE"].ToString();
                            columnasSQL[colName] = $"{dataType} (NULL: {nullable})";
                        }
                    }
                }

                sb.AppendLine("ANÁLISIS DETALLADO:");
                sb.AppendLine();

                for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                {
                    string nombreColumna = tabla.Columns[colIndex].ColumnName;

                    if (!columnasSQL.ContainsKey(nombreColumna)) continue;

                    string tipoSQL = columnasSQL[nombreColumna];

                    if (tipoSQL.ToLower().Contains("money") || tipoSQL.ToLower().Contains("decimal"))
                    {
                        sb.AppendLine($"Columna '{nombreColumna}': Tipo SQL = {tipoSQL}");

                        for (int rowIndex = 0; rowIndex < Math.Min(tabla.Rows.Count, 100); rowIndex++)
                        {
                            object valor = tabla.Rows[rowIndex][colIndex];
                            string valorStr = valor?.ToString()?.Trim() ?? "";

                            if (!string.IsNullOrWhiteSpace(valorStr))
                            {
                                decimal test;
                                string valorLimpio = valorStr.Replace("$", "").Replace(",", "").Trim();

                                if (!decimal.TryParse(valorLimpio, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out test))
                                {
                                    sb.AppendLine($"   FILA {rowIndex + 2} (Excel): Valor = '{valorStr}' | Tipo = {valor?.GetType().Name ?? "null"}");
                                    sb.AppendLine($"      Este valor NO se puede convertir a decimal/money");
                                    return sb.ToString();
                                }
                            }
                        }

                        sb.AppendLine($"   Primeras 100 filas validadas OK");
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error al analizar: {ex.Message}");
            }

            return sb.ToString();
        }

        private string ValidarDatosParaBulkCopy(DataTable tabla, Dictionary<string, (string tipo, bool nullable)> esquema)
        {
            try
            {
                LogToFile($"Esquema SQL encontrado para {tabla.TableName}: {esquema.Count} columnas");
                foreach (var col in esquema)
                {
                    LogToFile($"   - {col.Key}: {col.Value.tipo} (Nullable: {col.Value.nullable})");
                }

                if (esquema.Count == 0)
                {
                    LogToFile($"Tabla {tabla.TableName} no existe en SQL Server, se creará dinámicamente");
                    return null;
                }

                for (int rowIndex = 0; rowIndex < tabla.Rows.Count; rowIndex++)
                {
                    DataRow fila = tabla.Rows[rowIndex];

                    for (int colIndex = 0; colIndex < tabla.Columns.Count; colIndex++)
                    {
                        string nombreColumna = tabla.Columns[colIndex].ColumnName;

                        if (!esquema.ContainsKey(nombreColumna)) continue;

                        var (tipoSQL, nullable) = esquema[nombreColumna];
                        object valor = fila[colIndex];
                        string valorStr = valor?.ToString()?.Trim() ?? "";

                        if (tipoSQL.ToLower() == "money" || tipoSQL.ToLower() == "decimal" ||
                            tipoSQL.ToLower() == "numeric" || tipoSQL.ToLower() == "smallmoney")
                        {
                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                if (!nullable)
                                {
                                    return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': Valor vacío pero la columna no acepta NULL. Tipo SQL: {tipoSQL}";
                                }
                                continue;
                            }

                            decimal valorDecimal;
                            // Usar SanitizarValorNumerico para aplicar el mismo criterio que en el insert
                            string valorLimpio = SanitizarValorNumerico(valorStr);

                            if (!decimal.TryParse(valorLimpio, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out valorDecimal))
                            {
                                string mensajeError = $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': El valor '{valorStr}' no se puede convertir a {tipoSQL}. " +
                                       $"Valor en DataTable: '{valor}' (Tipo: {valor?.GetType().Name ?? "null"})";
                                LogToFile($"ERROR DETECTADO EN VALIDACIÓN: {mensajeError}");
                                return mensajeError;
                            }
                        }
                        else if (tipoSQL.ToLower() == "int" || tipoSQL.ToLower() == "bigint" ||
                                 tipoSQL.ToLower() == "smallint" || tipoSQL.ToLower() == "tinyint")
                        {
                            if (string.IsNullOrWhiteSpace(valorStr))
                            {
                                if (!nullable)
                                {
                                    return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': Valor vacío pero la columna no acepta NULL. Tipo SQL: {tipoSQL}";
                                }
                                continue;
                            }

                            string valorLimpio = valorStr.Replace(",", "").Replace(" ", "").Trim();
                            int valorInt;
                            if (!int.TryParse(valorLimpio, out valorInt))
                            {
                                return $"Fila {rowIndex + 2} (Excel), Columna '{nombreColumna}': El valor '{valorStr}' no se puede convertir a {tipoSQL}. " +
                                       $"Valor en DataTable: '{valor}' (Tipo: {valor?.GetType().Name ?? "null"})";
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return $"Error en validación: {ex.Message}";
            }
        }

        private void CrearTablaSiNoExiste(SqlConnection conn, DataTable tabla)
        {
            int idEmpresa = UsuarioSesionHelper.UsuarioActual?.IdEmpresa ?? 0;
            int idUsuario = UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

            var columnasExcel = tabla.Columns.Cast<DataColumn>()
                                  .Select(c => $"[{c.ColumnName}] VARCHAR(MAX)");

            var columnasExtras = new List<string>
            {
                "[IdEmpresa] INT",
                "[IdUsuario] INT",
                "[FechaCargue] DATETIME"
            };

            string nombreTabla = $"[dbo].[{tabla.TableName}]";
            string sql = $@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{tabla.TableName}')
                BEGIN
                    CREATE TABLE {nombreTabla} (
                        {string.Join(", ", columnasExcel.Concat(columnasExtras))}
                    );
                END
            ";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void RegistrarAuditoria(SqlConnection conn, string nombreArchivo, int idEmpresaArchivo, SqlTransaction tx = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null) return;

            string nombreEmpresa = _empresaService.ObtenerEmpresas()
                                                   .FirstOrDefault(e => e.Id == idEmpresaArchivo)?.Nombre ?? "Desconocida";

            string sql = @"
        INSERT INTO dbo.AuditoriaCargues (FechaCargue, IdUsuario, Usuario, IdEmpresa, NombreEmpresa, NombreArchivo)
        VALUES (@Fecha, @IdUsuario, @Usuario, @IdEmpresa, @NombreEmpresa, @NombreArchivo)
    ";

            using (var cmd = new SqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@Fecha", DateTime.Now);
                cmd.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                cmd.Parameters.AddWithValue("@Usuario", usuario.Nombre+" "+usuario.Apellidos ?? "");
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresaArchivo);
                cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);

                cmd.ExecuteNonQuery();
            }
        }


        private string ObtenerUltimoUsuarioCargue(int idEmpresa)
        {
            string ultimoCargue = "";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
            SELECT  TOP 1 AuditoriaCargues.Usuario, FechaCargue, NombreEmpresa AS EmpNombre
            FROM  AuditoriaCargues
            WHERE AuditoriaCargues.IdEmpresa = @IdEmpresa
            ORDER BY FechaCargue DESC
        ", conn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string usuario = reader["Usuario"].ToString();
                        DateTime fecha = Convert.ToDateTime(reader["FechaCargue"]);
                        string empresa = reader["EmpNombre"].ToString();

                        ultimoCargue = $"Última carga de {empresa}: {fecha:dd/MM/yyyy HH:mm:ss} por {usuario}";
                    }
                }
            }

            return ultimoCargue;
        }



        private string NormalizarNombre(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return texto;

            var normalized = texto.Normalize(System.Text.NormalizationForm.FormD);

            var chars = normalized
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                            System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray();

            var sinTildes = new string(chars);

            sinTildes = sinTildes.Replace("ñ", "n").Replace("Ñ", "N");

            return sinTildes
                .Replace(" ", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("/", "")
                .Trim();
        }


    }
}
