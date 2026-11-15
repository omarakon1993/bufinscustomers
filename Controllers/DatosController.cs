using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class DatosController : BaseController
    {
        private const string SESSION_LOG_IMPORTACION = "LogImportacion";
        /// <summary>Casos que la pantalla «Revisar cargue» trae por cada validación (el resto va solo en el Excel de errores).</summary>
        private const int CasosPorValidacionEnPantalla = 200;

        private readonly EmpresaService _empresaService = new EmpresaService();
        private readonly ConfiguracionEmpresaService _configuracionService = new ConfiguracionEmpresaService();
        private readonly ModeloService _modeloService = new ModeloService();
        private readonly PlantillaConDatosService _plantillaConDatosService = new PlantillaConDatosService();
        private readonly CargueStagingService _stagingService = new CargueStagingService();
        private readonly CargueImpactoService _impactoService = new CargueImpactoService();
        private CargueOrquestadorService _orq;
        /// <summary>Orquestador unico del cargue (lo comparten la pantalla de Excel y la API). Recibe R para traducir mensajes.</summary>
        private CargueOrquestadorService Orq => _orq ?? (_orq = new CargueOrquestadorService(R));

        // Mapeo hoja Z_ → tabla Ini_: Helpers/TablasCargueHelper.cs. Se resuelve POR EMPRESA
        // (TablasCargueHelper.MapeoParaEmpresa) porque algunas empresas tienen hojas personalizadas.

        public ActionResult DescargarLog()
        {
            var log = Session[SESSION_LOG_IMPORTACION] as string;
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
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

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

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                int anio = config != null ? config.AnioEjecucion : DateTime.Now.Year;

                return Json(new { success = true, anioEjecucion = anio }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al obtener el año: " + MensajeError(ex) }, JsonRequestBehavior.AllowGet);
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

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                if (config == null)
                    return Json(new { success = false, message = "La empresa no tiene configuración creada." }, JsonRequestBehavior.AllowGet);

                var anosHistoricos = config.AnosHistoricos?
                    .Select(a => a.NombreAno)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList() ?? new List<string>();

                // Solo las hojas personalizadas que ESTE usuario puede ver (Super Admin: todas las de la
                // empresa; resto: solo si su propia empresa también las tiene — el acceso por grupo no basta).
                var visibles = PersonalizacionesEmpresaHelper.VisiblesPara(usuario, idEmpresa);

                return Json(new
                {
                    success = true,
                    anioEjecucion = config.AnioEjecucion,
                    anosHistoricos = anosHistoricos,
                    // Hojas adicionales visibles para el usuario (vacío = plantilla estándar).
                    hojasPersonalizadas = visibles.Select(h => h.NombreHoja).ToArray()
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + MensajeError(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Hojas que van en la plantilla de la empresa (vacía o con datos): estándar + personalizadas que el
        /// usuario puede ver (Super Admin: todas las de la empresa; resto: solo las que también tiene su propia
        /// empresa). Ya no hay selección manual de plantilla: cada empresa recibe la que le corresponde.
        /// </summary>
        private List<HojaCargue> HojasParaDescarga(int idEmpresa) =>
            PersonalizacionesEmpresaHelper.HojasPlantillaPara(UsuarioSesionHelper.UsuarioActual, idEmpresa);

        /// <summary>
        /// Plantilla de cargue vacía de la empresa (obligatoria), generada con el catálogo de hojas:
        /// base PlantillaBUFINS.xlsx + sus hojas personalizadas visibles para el usuario.
        /// </summary>
        [HttpGet]
        public ActionResult DescargarPlantilla(int? idEmpresa = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            int id = idEmpresa ?? 0;
            if (usuario == null || id <= 0 || !EmpresaAccesoHelper.TieneAcceso(usuario, id))
            {
                SetErrorMessage(R("Datos_PlantillaSinEmpresa"));
                return RedirectToAction("CargueExcel");
            }

            try
            {
                byte[] bytes = new PlantillaCargueService().GenerarBytes(HojasParaDescarga(id));
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == id);
                string sufijo = Regex.Replace(!string.IsNullOrWhiteSpace(empresa?.Abreviatura) ? empresa.Abreviatura : (empresa?.Nombre ?? "Empresa"), "[^A-Za-z0-9]", "_");
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PlantillaBUFINS_{sufijo}.xlsx");
            }
            catch (Exception ex)
            {
                // No se entrega la base como respaldo: a una empresa con hojas personalizadas le faltarían.
                AppLogger.Error(ex, $"DescargarPlantilla: no se pudo generar la plantilla de la empresa {id}.");
                SetErrorMessage(R("Datos_PlantillaError"));
                return RedirectToAction("CargueExcel");
            }
        }
        // ── Descargar la plantilla BUFINS rellena con los datos actuales ─────────

        [HttpGet]
        public ActionResult ObtenerAniosConDatos(int idEmpresa, int idEscenario = 1)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (usuario == null)
                    return Json(new { success = false, message = "Sesión no válida." }, JsonRequestBehavior.AllowGet);

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                    return Json(new { success = false, message = "No tiene permisos para consultar esta empresa." }, JsonRequestBehavior.AllowGet);

                var anios = _plantillaConDatosService.ObtenerAniosConDatos(idEmpresa, idEscenario, HojasParaDescarga(idEmpresa))
                    .Select(a => new { anio = a.Anio, registros = a.Registros });

                return Json(new { success = true, anios }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ObtenerAniosConDatos");
                return Json(new { success = false, message = "Error: " + MensajeError(ex) }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult DescargarPlantillaConDatos(int idEmpresa, string anios, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null || !EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { error = R("Datos_PlantillaConDatosError") }, JsonRequestBehavior.AllowGet);

            var listaAnios = (anios ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                .Where(n => n > 0)
                .Distinct()
                .ToList();

            if (listaAnios.Count == 0)
                return Json(new { error = R("Datos_PlantillaConDatosSinSeleccion") }, JsonRequestBehavior.AllowGet);

            byte[] bytes;
            int totalFilas;
            try
            {
                bytes = _plantillaConDatosService.GenerarExcel(idEmpresa, listaAnios, idEscenario, HojasParaDescarga(idEmpresa), out totalFilas);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "DescargarPlantillaConDatos");
                return Json(new { error = R("Datos_PlantillaConDatosError") }, JsonRequestBehavior.AllowGet);
            }

            if (totalFilas == 0)
                return Json(new { error = R("Datos_PlantillaConDatosSinDatos") }, JsonRequestBehavior.AllowGet);

            var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
            string empresaNombre = empresa?.Nombre ?? "Empresa";
            string empresaSanit = Regex.Replace(
                !string.IsNullOrWhiteSpace(empresa?.Abreviatura) ? empresa.Abreviatura : empresaNombre,
                "[^A-Za-z0-9]", "_");
            var ordenados = listaAnios.OrderBy(a => a).ToList();
            string sufijoAnios = ordenados.Count == 1
                ? ordenados[0].ToString()
                : $"{ordenados.First()}-{ordenados.Last()}";
            string fileName = $"PlantillaConDatos_{empresaSanit}_Esc{idEscenario}_{sufijoAnios}.xlsx";

            new AuditoriaService().RegistrarCambio(
                AuditoriaTipo.Cargues, AuditoriaAccion.Exportar, "PlantillaConDatos", idEmpresa.ToString(),
                $"Descargó la plantilla con datos de '{empresaNombre}' (Escenario {idEscenario}) — año(s) {string.Join(", ", ordenados)}, {totalFilas} fila(s).",
                null,
                new { empresa = empresaNombre, anios = ordenados, totalFilas, archivo = fileName, escenario = idEscenario },
                idEmpresa, entidadNombre: empresaNombre, severidad: AuditoriaSeveridad.Advertencia);

            new NotificacionesService().Crear(
                usuario.Id, R("Notif_PlantillaConDatosDescargada"),
                $"{empresaNombre} — {string.Join(", ", ordenados)}", "success", "/Datos/CargueExcel");

            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EjecutarModeloAjax(int idEmpresa, int idModelo, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return Json(new { exito = false, mensaje = "Sesión no válida.", columnas = new List<string>(), filas = new List<List<string>>() });

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
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
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.CommandTimeout = 300;
                    cn.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        int codMessage = 1;
                        string mensajeResp = "Ejecutado correctamente.";
                        var lastCols = new List<string>();
                        var lastFilas = new List<List<string>>();
                        var lastNumericas = new List<bool>();

                        do
                        {
                            int fieldCount = reader.FieldCount;
                            var cols = new List<string>();
                            for (int i = 0; i < fieldCount; i++)
                                cols.Add(reader.GetName(i));

                            bool esStatus = cols.Contains("CodMessage");
                            var isMoney   = MarcarColumnasValor(reader, cols);
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
                                    {
                                        if (reader.IsDBNull(i))
                                            fila.Add("");
                                        else if (isMoney[i])
                                        {
                                            try { fila.Add(Convert.ToDecimal(reader.GetValue(i)).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                                            catch { fila.Add(reader.GetValue(i).ToString()); }
                                        }
                                        else
                                            fila.Add(reader.GetValue(i).ToString());
                                    }
                                    filas.Add(fila);
                                }
                            }

                            if (!esStatus && cols.Count > 0)
                            {
                                lastCols = cols;
                                lastFilas = filas;
                                lastNumericas = isMoney;
                            }
                        } while (reader.NextResult());

                        if (codMessage == 1)
                        {
                            new NotificacionesService().Crear(usuario.Id, R("Notif_ModeloEjecutado"), mensajeResp, "success", "/Datos/Modelo");

                            var empresaEjecutada = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                            new AuditoriaService().RegistrarCambio(
                                AuditoriaTipo.Modelos, AuditoriaAccion.Ejecutar, "ModelosEjecucion", idModelo.ToString(),
                                $"Modelo '{modelo.Nombre}' ejecutado para '{empresaEjecutada?.Nombre ?? "Empresa"}' (Escenario {idEscenario}).", null,
                                new { empresa = empresaEjecutada?.Nombre, modelo = modelo.Nombre, filas = lastFilas.Count, escenario = idEscenario },
                                idEmpresa, entidadNombre: modelo.Nombre);
                        }
                        else
                        {
                            new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorModelo"), mensajeResp, "error", "/Datos/Modelo");
                        }

                        return Json(new { exito = codMessage == 1, mensaje = mensajeResp, columnas = lastCols, filas = lastFilas, numericas = lastNumericas });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { exito = false, mensaje = "Error al ejecutar el modelo: " + MensajeError(ex), columnas = new List<string>(), filas = new List<List<string>>() });
            }
        }

        public ActionResult ExportarTodosModelos(int idEmpresa, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return RedirectToAction("Login", "Acceso");

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
            {
                SetErrorMessage("No tiene permisos para exportar datos de esta empresa.");
                return RedirectToAction("Modelo");
            }

            try
            {
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                var modelos = _modeloService.ObtenerModelosActivos();

                using (var package = new XLWorkbook())
                {
                    EscribirHojaIndiceModelos(package.Worksheets.Add("Índice"), empresa, modelos, DateTime.Now);

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

                            var ws = package.Worksheets.Add(sheetName);
                            try
                            {
                                EjecutarModeloYEscribirHoja(cn, ws, modelo, idEmpresa, usuario.Id, idEscenario);
                            }
                            catch (Exception exModelo)
                            {
                                package.Worksheets.Delete(sheetName);
                                erroresModelos.Add((modelo.Nombre, modelo.NombreSP, exModelo.Message));
                            }
                        }
                    }

                    if (erroresModelos.Count > 0)
                    {
                        var exitosos = modelos
                            .Where(m => !erroresModelos.Any(e => e.NombreSP == m.NombreSP))
                            .Select(m => m.Nombre);
                        return Json(new
                        {
                            errores = erroresModelos.Select(e => new { nombre = e.Nombre, nombreSP = e.NombreSP, mensaje = e.Mensaje }),
                            exitosos = exitosos
                        }, JsonRequestBehavior.AllowGet);
                    }

                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    string empId = !string.IsNullOrWhiteSpace(empresa?.Abreviatura)
                        ? empresa.Abreviatura
                        : (empresa?.Nombre ?? "Empresa").Replace(" ", "_");
                    string fileName = $"Modelos_{empId}_Esc{idEscenario}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";

                    new AuditoriaService().RegistrarCambio(
                        AuditoriaTipo.Modelos, AuditoriaAccion.Exportar, "ModelosEjecucion", idEmpresa.ToString(),
                        $"{modelos.Count} modelo(s) exportados de '{empresa?.Nombre ?? "Empresa"}' (Escenario {idEscenario}).", null,
                        new { empresa = empresa?.Nombre, modelos = modelos.Select(m => m.Nombre).ToList(), archivo = fileName, escenario = idEscenario },
                        idEmpresa, entidadNombre: empresa?.Nombre, severidad: AuditoriaSeveridad.Advertencia);

                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al exportar los modelos: " + ex.Message);
                return RedirectToAction("Modelo");
            }
        }

        private void EscribirHojaIndiceModelos(IXLWorksheet ws, Empresas empresa, List<ModeloEjecucion> modelos, DateTime fechaExportacion)
        {
            ws.Cell(1, 1).Value = "EXPORTACIÓN DE MODELOS FINANCIEROS";
            ws.Range(1, 1, 1, 4).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

            int row = 3;
            Action<string, string> writeInfo = (label, value) =>
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = value;
                ws.Range(row, 2, row, 4).Merge();
                row++;
            };

            writeInfo("Empresa:", empresa?.Nombre ?? "—");
            writeInfo("NIT:", empresa?.Nit ?? "—");
            writeInfo("Dirección:", empresa?.Direccion ?? "—");
            writeInfo("Fecha de exportación:", fechaExportacion.ToString("dd/MM/yyyy HH:mm:ss"));
            writeInfo("Total modelos:", modelos.Count.ToString());

            row++;
            ws.Cell(row, 1).Value = "#";
            ws.Cell(row, 2).Value = "Modelo";
            ws.Cell(row, 3).Value = "Procedimiento";
            ws.Cell(row, 4).Value = "Descripción";
            var hdrRange = ws.Range(row, 1, row, 4);
            hdrRange.Style.Font.Bold = true;
            hdrRange.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 230, 250);
            row++;

            foreach (var m in modelos)
            {
                ws.Cell(row, 1).Value = m.Orden;
                ws.Cell(row, 2).Value = m.Nombre;
                ws.Cell(row, 3).Value = m.NombreSP;
                ws.Cell(row, 4).Value = m.Descripcion;
                row++;
            }

            ws.Column(1).Width = 6;
            ws.Column(2).Width = 28;
            ws.Column(3).Width = 32;
            ws.Column(4).Width = 42;
        }

        private static readonly HashSet<string> _moneyColNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Valor", "ValorAcumulado", "ValorFuturo", "ValorFuturoAcumulado", "ValorForecast", "ValorPresupuesto", "ValorPresupuestoAcumulado", "ValorPresupuestoConAjuste" };

        // Columna de valor = nombre conocido O tipo decimal/money/float en el result set.
        // Así cualquier columna nueva de valores de un SP de modelo sale con formato numérico
        // sin tener que agregarla a la lista; los enteros (Año, Mes, Id, Ord…) no entran.
        private static List<bool> MarcarColumnasValor(IDataRecord reader, List<string> cols)
        {
            var marcas = new List<bool>(cols.Count);
            for (int i = 0; i < cols.Count; i++)
            {
                Type t = null;
                try { t = reader.GetFieldType(i); } catch { }
                marcas.Add(_moneyColNames.Contains(cols[i])
                    || t == typeof(decimal) || t == typeof(double) || t == typeof(float));
            }
            return marcas;
        }

        private void EjecutarModeloYEscribirHoja(SqlConnection cn, IXLWorksheet ws, ModeloEjecucion modelo, int idEmpresa, int idUsuario, int idEscenario)
        {
            using (var cmd = new SqlCommand(modelo.NombreSP, cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
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
                            var isMoney = MarcarColumnasValor(reader, cols);
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
                        ws.Cell(1, c + 1).Value = lastCols[c];

                    var headerRange = ws.Range(1, 1, 1, lastCols.Count);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(99, 102, 241);
                    headerRange.Style.Font.FontColor = XLColor.White;

                    // Data — se escribe decimal como número real, string como texto
                    for (int r = 0; r < lastData.Count; r++)
                        for (int c = 0; c < lastData[r].Count; c++)
                            ExcelCellHelper.SetValue(ws.Cell(r + 2, c + 1), lastData[r][c]);

                    // Formatos por rango de columna completa (una operación por columna)
                    if (lastData.Count > 0)
                    {
                        for (int c = 0; c < lastCols.Count; c++)
                        {
                            string fmt = lastIsMoney[c] ? "#,##0.00" : "@";
                            ws.Range(2, c + 1, lastData.Count + 1, c + 1).Style.NumberFormat.Format = fmt;
                        }

                        string safeName = "tbl_" + Regex.Replace(ws.Name, "[^A-Za-z0-9]", "_");
                        var tbl = ws.Range(1, 1, lastData.Count + 1, lastCols.Count).CreateTable(safeName);
                        tbl.Theme = XLTableTheme.TableStyleMedium2;
                    }

                    ws.SheetView.Freeze(1, 0);
                    for (int c = 1; c <= lastCols.Count; c++)
                        ws.Column(c).Width = 22;
                }
            }
        }

        public ActionResult ExportarModeloIndividual(int idEmpresa, int idModelo, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return RedirectToAction("Login", "Acceso");

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
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

                using (var package = new XLWorkbook())
                {
                    EscribirHojaIndiceModelos(
                        package.Worksheets.Add("Índice"),
                        empresa, new List<ModeloEjecucion> { modelo }, DateTime.Now);

                    string sheetName = modelo.NombreSP.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
                        ? modelo.NombreSP.Substring(3) : modelo.NombreSP;
                    if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);

                    var ws = package.Worksheets.Add(sheetName);
                    try
                    {
                        using (var cn = new SqlConnection(CadenaConexion))
                        {
                            cn.Open();
                            EjecutarModeloYEscribirHoja(cn, ws, modelo, idEmpresa, usuario.Id, idEscenario);
                        }
                    }
                    catch (Exception exModelo)
                    {
                        return Json(new
                        {
                            errores = new[] { new { nombre = modelo.Nombre, nombreSP = modelo.NombreSP, mensaje = exModelo.Message } },
                            exitosos = new string[0]
                        }, JsonRequestBehavior.AllowGet);
                    }

                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    string empId = !string.IsNullOrWhiteSpace(empresa?.Abreviatura)
                        ? empresa.Abreviatura
                        : (empresa?.Nombre ?? "Empresa").Replace(" ", "_");
                    string fileName = $"{sheetName}_{empId}_Esc{idEscenario}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";

                    new AuditoriaService().RegistrarCambio(
                        AuditoriaTipo.Modelos, AuditoriaAccion.Exportar, "ModelosEjecucion", idEmpresa.ToString(),
                        $"1 modelo(s) exportados de '{empresa?.Nombre ?? "Empresa"}' (Escenario {idEscenario}).", null,
                        new { empresa = empresa?.Nombre, modelos = new List<string> { modelo.Nombre }, archivo = fileName, escenario = idEscenario },
                        idEmpresa, entidadNombre: empresa?.Nombre, severidad: AuditoriaSeveridad.Advertencia);

                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al exportar el modelo: " + ex.Message);
                return RedirectToAction("Modelo");
            }
        }

        /// <summary>
        /// Exporta un subconjunto arbitrario de modelos activos (elegidos por el usuario) en un
        /// solo Excel, una hoja por modelo. Mismo contrato de respuesta que <see cref="ExportarTodosModelos"/>:
        /// archivo binario si todos los modelos se ejecutan bien, o JSON { errores, exitosos } si alguno falla.
        /// </summary>
        public ActionResult ExportarModelosSeleccionados(int idEmpresa, string idsModelos, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return RedirectToAction("Login", "Acceso");

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
            {
                SetErrorMessage("No tiene permisos para exportar datos de esta empresa.");
                return RedirectToAction("Modelo");
            }

            var ids = new HashSet<int>((idsModelos ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                .Where(n => n > 0));

            var modelos = _modeloService.ObtenerModelosActivos().Where(m => ids.Contains(m.Id)).ToList();
            if (modelos.Count == 0)
            {
                SetErrorMessage("Debe seleccionar al menos un modelo para exportar.");
                return RedirectToAction("Modelo");
            }

            try
            {
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);

                using (var package = new XLWorkbook())
                {
                    EscribirHojaIndiceModelos(package.Worksheets.Add("Índice"), empresa, modelos, DateTime.Now);

                    var erroresModelos = new List<(string Nombre, string NombreSP, string Mensaje)>();
                    string ultimaHoja = null;

                    using (SqlConnection cn = new SqlConnection(CadenaConexion))
                    {
                        cn.Open();
                        foreach (var modelo in modelos)
                        {
                            string sheetName = modelo.NombreSP.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
                                ? modelo.NombreSP.Substring(3)
                                : modelo.NombreSP;
                            if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);

                            var ws = package.Worksheets.Add(sheetName);
                            try
                            {
                                EjecutarModeloYEscribirHoja(cn, ws, modelo, idEmpresa, usuario.Id, idEscenario);
                                ultimaHoja = sheetName;
                            }
                            catch (Exception exModelo)
                            {
                                package.Worksheets.Delete(sheetName);
                                erroresModelos.Add((modelo.Nombre, modelo.NombreSP, exModelo.Message));
                            }
                        }
                    }

                    if (erroresModelos.Count > 0)
                    {
                        var exitosos = modelos
                            .Where(m => !erroresModelos.Any(e => e.NombreSP == m.NombreSP))
                            .Select(m => m.Nombre);
                        return Json(new
                        {
                            errores = erroresModelos.Select(e => new { nombre = e.Nombre, nombreSP = e.NombreSP, mensaje = e.Mensaje }),
                            exitosos = exitosos
                        }, JsonRequestBehavior.AllowGet);
                    }

                    byte[] fileBytes;
                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                    string empId = !string.IsNullOrWhiteSpace(empresa?.Abreviatura)
                        ? empresa.Abreviatura
                        : (empresa?.Nombre ?? "Empresa").Replace(" ", "_");
                    string fileName = (modelos.Count == 1 && ultimaHoja != null)
                        ? $"{ultimaHoja}_{empId}_Esc{idEscenario}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx"
                        : $"Modelos_{empId}_Esc{idEscenario}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";

                    new AuditoriaService().RegistrarCambio(
                        AuditoriaTipo.Modelos, AuditoriaAccion.Exportar, "ModelosEjecucion", idEmpresa.ToString(),
                        $"{modelos.Count} modelo(s) exportados de '{empresa?.Nombre ?? "Empresa"}' (Escenario {idEscenario}).", null,
                        new { empresa = empresa?.Nombre, modelos = modelos.Select(m => m.Nombre).ToList(), archivo = fileName, escenario = idEscenario },
                        idEmpresa, entidadNombre: empresa?.Nombre, severidad: AuditoriaSeveridad.Advertencia);

                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                SetErrorMessage("Error al exportar los modelos: " + ex.Message);
                return RedirectToAction("Modelo");
            }
        }

        private static readonly Regex _rxCorreo =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        /// <summary>
        /// Genera el Excel de los modelos indicados (uno, varios o todos los activos) para una
        /// empresa y lo envía como adjunto a uno o varios correos. Síncrono: la vista muestra un
        /// overlay de carga mientras dura. Devuelve JSON — { exito, mensaje } o { errores } cuando
        /// algún modelo falla al ejecutarse.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarModelosPorCorreo(int idEmpresa, string idsModelos, string correos, string mensaje, int idEscenario = 1)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return Json(new { exito = false, mensaje = "Sesión no válida." });

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { exito = false, mensaje = "No tiene permisos para enviar datos de esta empresa." });

            // ── Destinatarios ────────────────────────────────────────────────
            var listaCorreos = (correos ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (listaCorreos.Count == 0)
                return Json(new { exito = false, mensaje = R("Modelo_Correo_ValCorreo") });

            var invalidos = listaCorreos.Where(c => !_rxCorreo.IsMatch(c)).ToList();
            if (invalidos.Any())
                return Json(new { exito = false, mensaje = R("Modelo_Correo_ValCorreo") + " (" + string.Join(", ", invalidos) + ")" });

            if (listaCorreos.Count > 10)
                return Json(new { exito = false, mensaje = "Máximo 10 destinatarios por envío." });

            // ── Modelos a incluir ────────────────────────────────────────────
            var todos = _modeloService.ObtenerModelosActivos();
            bool esTodos = string.IsNullOrWhiteSpace(idsModelos)
                || string.Equals(idsModelos.Trim(), "todos", StringComparison.OrdinalIgnoreCase);

            List<ModeloEjecucion> modelos;
            if (esTodos)
            {
                modelos = todos;
            }
            else
            {
                var ids = new HashSet<int>(idsModelos
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                    .Where(n => n > 0));
                modelos = todos.Where(m => ids.Contains(m.Id)).ToList();
            }

            if (modelos.Count == 0)
                return Json(new { exito = false, mensaje = R("Modelo_Correo_ValModelos") });

            try
            {
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                var erroresModelos = new List<(string Nombre, string NombreSP, string Mensaje)>();
                string ultimaHoja = null;
                byte[] fileBytes;

                using (var package = new XLWorkbook())
                {
                    EscribirHojaIndiceModelos(package.Worksheets.Add("Índice"), empresa, modelos, DateTime.Now);

                    using (SqlConnection cn = new SqlConnection(CadenaConexion))
                    {
                        cn.Open();
                        foreach (var modelo in modelos)
                        {
                            string sheetName = modelo.NombreSP.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
                                ? modelo.NombreSP.Substring(3)
                                : modelo.NombreSP;
                            if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);

                            var ws = package.Worksheets.Add(sheetName);
                            try
                            {
                                EjecutarModeloYEscribirHoja(cn, ws, modelo, idEmpresa, usuario.Id, idEscenario);
                                ultimaHoja = sheetName;
                            }
                            catch (Exception exModelo)
                            {
                                package.Worksheets.Delete(sheetName);
                                erroresModelos.Add((modelo.Nombre, modelo.NombreSP, exModelo.Message));
                            }
                        }
                    }

                    if (erroresModelos.Count > 0)
                    {
                        return Json(new
                        {
                            exito = false,
                            errores = erroresModelos.Select(e => new { nombre = e.Nombre, nombreSP = e.NombreSP, mensaje = e.Mensaje })
                        });
                    }

                    using (var ms = new MemoryStream())
                    {
                        package.SaveAs(ms);
                        fileBytes = ms.ToArray();
                    }
                }

                string empId = !string.IsNullOrWhiteSpace(empresa?.Abreviatura)
                    ? empresa.Abreviatura
                    : (empresa?.Nombre ?? "Empresa").Replace(" ", "_");
                string fileName = (modelos.Count == 1 && ultimaHoja != null)
                    ? $"{ultimaHoja}_{empId}_Esc{idEscenario}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx"
                    : $"Modelos_{empId}_Esc{idEscenario}_{DateTime.Now:ddMMyyyy}_{DateTime.Now:fff}.xlsx";

                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();

                new EmailService().EnviarModelosExportados(
                    string.Join(", ", listaCorreos),
                    empresa?.Nombre ?? "Empresa",
                    modelos.Select(m => m.Nombre),
                    mensaje,
                    quienEnvia,
                    fileName,
                    fileBytes,
                    esIngles);

                string resumen = $"{modelos.Count} modelo(s) de '{empresa?.Nombre ?? "Empresa"}' (Escenario {idEscenario}) enviados a {string.Join(", ", listaCorreos)}.";
                string descripcionAuditoria = $"Enviado por correo electrónico: {resumen}";

                new NotificacionesService().Crear(usuario.Id, R("Notif_ModelosEnviadosCorreo"), resumen, "success", "/Datos/Modelo");

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Modelos, AuditoriaAccion.Enviar, "ModelosEjecucion", idEmpresa.ToString(),
                    descripcionAuditoria, null,
                    new
                    {
                        empresa = empresa?.Nombre,
                        modelos = modelos.Select(m => m.Nombre).ToList(),
                        destinatarios = listaCorreos,
                        archivo = fileName,
                        escenario = idEscenario
                    },
                    idEmpresa, entidadNombre: empresa?.Nombre, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = R("Modelo_JS_CorreoEnviado") });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "EnviarModelosPorCorreo");
                new NotificacionesService().Crear(usuario.Id, R("Notif_ErrorEnvioCorreoModelos"), ex.Message, "error", "/Datos/Modelo");
                return Json(new { exito = false, mensaje = "No se pudo enviar el correo: " + MensajeError(ex) });
            }
        }

        // Los parámetros opcionales preseleccionan empresa/año/escenario (los usa "Subir archivo corregido"
        // en RevisarCargue). Son solo preselección de UI: el acceso real se valida al cargar el archivo.
        public ActionResult CargueExcel(int? idEmpresa = null, int? anio = null, int? idEscenario = null)
        {
            ViewBag.PrefillEmpresa = idEmpresa;
            ViewBag.PrefillAnio = anio;
            ViewBag.PrefillEscenario = idEscenario;
            _stagingService.PurgarLotesVencidosSiToca();
            return CargueExcelView();
        }

        private ActionResult CargueExcelView()
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
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresasDisponibles = _empresaService.ObtenerEmpresas()
                    .Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresasDisponibles;
            ViewBag.UltimoUsuarioCargue = ObtenerUltimoUsuarioCargue(idEmpresa);

            return View("~/Views/Datos/CargueExcel.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CargarExcel(HttpPostedFileBase archivoExcel, int idEmpresaSeleccionada, int anioSeleccionado, string modoSeleccionado, int idEscenarioSeleccionado = 1)
        {
            // ── Validación en dos pasos ──────────────────────────────────────────────────
            // Esta acción YA NO escribe en las tablas Ini_* reales: el orquestador
            // (Services/CargueOrquestadorService.cs) parsea el Excel, lo escribe en su espejo de
            // staging (dbo.Staging_Ini_*) y corre dbo.sp_ValidarCargueStaging; aquí solo se
            // redirige a RevisarCargue, donde el usuario ve el informe y decide Confirmar o Descartar.
            var solicitud = new SolicitudCargue
            {
                Usuario = UsuarioSesionHelper.UsuarioActual,
                IdEmpresa = idEmpresaSeleccionada,
                Anio = anioSeleccionado,
                Modo = modoSeleccionado,
                IdEscenario = idEscenarioSeleccionado,
                NombreArchivo = archivoExcel?.FileName,
                LongitudArchivo = archivoExcel?.ContentLength ?? 0,
                Archivo = archivoExcel?.InputStream
            };

            var validacion = Orq.ValidarSolicitud(solicitud);
            if (!validacion.Ok)
            {
                SetErrorMessage(validacion.Mensaje);
                return RedirectToAction("CargueExcel");
            }

            // El lote queda atado a esta sesión apenas se crea: al cerrar/expirar la sesión se descarta.
            var proceso = Orq.ProcesarArchivo(solicitud, idLote => CargueLoteSesionHelper.Registrar(idLote));

            if (!string.IsNullOrEmpty(proceso.LogImportacion))
                Session[SESSION_LOG_IMPORTACION] = proceso.LogImportacion;

            switch (proceso.Estado)
            {
                case ProcesoCargueEstado.EnRevision:
                    return RedirectToAction("RevisarCargue", new { idLote = proceso.IdLote });

                case ProcesoCargueEstado.LoteActivoExistente:
                    // Ya hay un cargue en revisión para esta misma llave: se manda a revisarlo.
                    SetInfoMessage(proceso.Mensaje);
                    return RedirectToAction("RevisarCargue", new { idLote = proceso.IdLote });

                case ProcesoCargueEstado.SinFilasDelAnio:
                    CargueLoteSesionHelper.Quitar(proceso.IdLote);
                    SetErrorMessage(proceso.Mensaje);
                    return RedirectToAction("CargueExcel");

                case ProcesoCargueEstado.ErrorValidacion:
                    SetErrorMessage($"No se pudo validar el cargue: {proceso.Mensaje}");
                    return RedirectToAction("CargueExcel");

                default: // PlantillaIncompleta, ErrorEnHoja, ErrorInesperado
                    TempData.GuardarObjeto("ResultadoCarga", proceso.Resultado);
                    TempData["NombreArchivo"] = proceso.NombreArchivo;
                    SetErrorMessage(proceso.Mensaje);
                    return RedirectToAction("CargueExcel");
            }
        }

        /// <summary>Informe de validación de un lote en staging — antes de confirmar el cargue.</summary>
        [HttpGet]
        public ActionResult RevisarCargue(long idLote)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var lote = _stagingService.ObtenerLote(idLote);

            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
            {
                SetErrorMessage("El cargue en revisión indicado no existe o no tiene acceso a él.");
                return RedirectToAction("CargueExcel");
            }

            if (!PersonalizacionesEmpresaHelper.PuedeOperarCargue(usuario, lote.IdEmpresa))
            {
                SetErrorMessage(R("Datos_PlantillaPersonalizadaSinPermiso"));
                return RedirectToAction("CargueExcel");
            }

            // Si esta sesión es la dueña, el lote queda atado a ella: al cerrar/expirar la sesión se descarta.
            if (usuario != null && lote.IdUsuario == usuario.Id)
                CargueLoteSesionHelper.Registrar(idLote);
            _stagingService.PurgarLotesVencidosSiToca();

            var vm = new RevisarCargueViewModel
            {
                Lote = lote,
                // La pantalla muestra los primeros N casos de cada validación y el total real; el Excel de errores trae todos.
                Hallazgos = _stagingService.ObtenerHallazgos(idLote, CasosPorValidacionEnPantalla),
                TotalesPorRegla = _stagingService.ObtenerTotalesPorRegla(idLote),
                NombreArchivo = lote.NombreArchivo
            };
            try { vm.Impacto = _impactoService.Analizar(lote); }
            catch (Exception exImp) { AppLogger.Error(exImp, "RevisarCargue.Impacto"); }
            return View("~/Views/Datos/RevisarCargue.cshtml", vm);
        }

        /// <summary>
        /// Confirma un lote ya validado (Estado ValidadoOk/ConAdvertencias). Toda la lógica (impacto, regla de oro,
        /// snapshot, reemplazo real, auditoría, notificación) vive en <see cref="CargueOrquestadorService.Confirmar"/>.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfirmarCargue(long idLote, bool confirmarReemplazo = false)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var r = Orq.Confirmar(usuario, idLote, confirmarReemplazo);

            switch (r.Estado)
            {
                case ConfirmacionCargueEstado.Confirmado:
                    CargueLoteSesionHelper.Quitar(idLote);
                    TempData.GuardarObjeto("ResultadoCarga", r.Resultado);
                    TempData["NombreArchivo"] = r.Lote.NombreArchivo;
                    if (r.Resultado.Exito) SetSuccessMessage(r.Mensaje);
                    else SetErrorMessage(r.Mensaje);
                    return RedirectToAction("CargueExcel");

                case ConfirmacionCargueEstado.LoteNoExiste:
                case ConfirmacionCargueEstado.SinPermisoPlantilla:
                    SetErrorMessage(r.Mensaje);
                    return RedirectToAction("CargueExcel");

                case ConfirmacionCargueEstado.LoteNoDisponible:
                    CargueLoteSesionHelper.Quitar(idLote);
                    SetErrorMessage(r.Mensaje);
                    return RedirectToAction("CargueExcel");

                default: // NoConfirmable, ErrorImpacto, CargaVacia, Bloqueado, FaltaConfirmarReemplazo, Error
                    SetErrorMessage(r.Mensaje);
                    return RedirectToAction("RevisarCargue", new { idLote });
            }
        }

        /// <summary>Descarta un lote en revisión sin tocar ninguna tabla real.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DescartarCargue(long idLote)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (!Orq.DescartarCargue(usuario, idLote))
            {
                SetErrorMessage(R("RevisarCargue_ErrorDescartar"));
                return RedirectToAction("RevisarCargue", new { idLote });
            }

            CargueLoteSesionHelper.Quitar(idLote);
            SetInfoMessage("Se descartó el cargue en revisión.");
            return RedirectToAction("CargueExcel");
        }

        /// <summary>
        /// Descarte silencioso cuando el usuario sale de RevisarCargue sin confirmar ni descartar
        /// (otra opción del menú, "Subir archivo corregido", cerrar la pestaña…). Lo dispara la vista con
        /// navigator.sendBeacon en 'pagehide'. Solo descarta lotes del propio usuario y no confirmados.
        /// Con <paramref name="porRecarga"/> (la vista detectó que se recargó la página) además redirige a
        /// Cargue de Excel con el aviso, en vez de responder 204 al beacon.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AbandonarCargue(long idLote, bool porRecarga = false)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;

            if (Orq.AbandonarCargue(usuario, idLote))
                CargueLoteSesionHelper.Quitar(idLote);

            if (porRecarga)
            {
                SetInfoMessage(R("RevisarCargue_DescartadoPorRecarga"));
                return RedirectToAction("CargueExcel");
            }
            return new HttpStatusCodeResult(204);
        }

        // ── Exportar / enviar los hallazgos de validación (pantalla Revisar cargue) ──────────
        // Mismas reglas de acceso que RevisarCargue. Solo lectura: no cambian el estado del lote.

        private CargueLote ObtenerLoteParaExportarHallazgos(long idLote)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var lote = _stagingService.ObtenerLote(idLote);
            if (usuario == null || lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
                return null;
            if (!PersonalizacionesEmpresaHelper.PuedeOperarCargue(usuario, lote.IdEmpresa))
                return null;
            return lote;
        }

        private static string NombreArchivoHallazgos(CargueLote lote, string extension)
        {
            string emp = new string((lote.NombreEmpresa ?? "Empresa").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            if (emp.Length > 40) emp = emp.Substring(0, 40);
            return $"Validacion_Cargue_{emp}_{lote.Anio}_Esc{lote.IdEscenario}_{DateTime.Now:yyyyMMdd_HHmm}.{extension}";
        }

        /// <summary>Excel de los hallazgos de un lote: cabecera con el contexto del cargue + una fila por hallazgo.</summary>
        private byte[] GenerarExcelHallazgos(CargueLote lote, List<CargueLoteHallazgo> hallazgos, bool esIngles)
        {
            var ordenados = hallazgos.OrderBy(x => x.Severidad == CargueLoteSeveridad.Error ? 0 : 1).ToList();
            var morado = XLColor.FromHtml("#583AFF");

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(R("RevisarCargue_Exp_HojaExcel"));

                ws.Cell(1, 1).Value = R("RevisarCargue_Exp_TituloInforme");
                ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(morado);

                var meta = new List<(string, string)>
                {
                    (R("RevisarCargue_Empresa"), lote.NombreEmpresa),
                    (R("RevisarCargue_Anio"), lote.Anio.ToString()),
                    (R("RevisarCargue_Modo"), lote.Modo == 0 ? R("RevisarCargue_ModoEjecucion") : R("RevisarCargue_ModoHistorico")),
                    (R("RevisarCargue_Escenario"), lote.IdEscenario.ToString()),
                    (R("RevisarCargue_Exp_Archivo"), lote.NombreArchivo),
                    (R("RevisarCargue_Exp_Fecha"), (lote.FechaValidacion ?? lote.FechaCreacion).ToString("yyyy-MM-dd HH:mm")),
                    (R("RevisarCargue_Filtro_Errores"), lote.TotalErrores.ToString()),
                    (R("RevisarCargue_Filtro_Advertencias"), lote.TotalAdvertencias.ToString())
                };
                int fila = 3;
                foreach (var m in meta)
                {
                    ws.Cell(fila, 1).Value = m.Item1;
                    ws.Cell(fila, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#5B5570"));
                    ws.Cell(fila, 2).Value = m.Item2 ?? "";
                    fila++;
                }

                fila++;
                int filaEnc = fila;
                string[] encabezados =
                {
                    "#", R("RevisarCargue_Exp_ColSeveridad"), R("RevisarCargue_Exp_ColTitulo"), R("RevisarCargue_Exp_ColMensaje"),
                    R("RevisarCargue_Exp_ColHoja"), R("RevisarCargue_Exp_ColFila"), R("RevisarCargue_Val_ColPeriodo"), R("RevisarCargue_Val_ColCuenta"),
                    R("RevisarCargue_Val_ColDiferencia"), R("RevisarCargue_Exp_ColColumna"), R("RevisarCargue_Exp_ColRegla")
                };
                string[] mesesAbr = esIngles
                    ? new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }
                    : new[] { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
                for (int c = 0; c < encabezados.Length; c++)
                    ws.Cell(filaEnc, c + 1).Value = encabezados[c];
                var rngEnc = ws.Range(filaEnc, 1, filaEnc, encabezados.Length);
                rngEnc.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(morado);

                int n = 0;
                foreach (var h in ordenados)
                {
                    fila++; n++;
                    bool esError = h.Severidad == CargueLoteSeveridad.Error;
                    string titulo = esIngles && !string.IsNullOrWhiteSpace(h.TituloEn) ? h.TituloEn : h.Titulo;
                    string mensaje = esIngles && !string.IsNullOrWhiteSpace(h.MensajeEn) ? h.MensajeEn : h.Mensaje;

                    ws.Cell(fila, 1).Value = n;
                    ws.Cell(fila, 2).Value = esError ? R("RevisarCargue_SeveridadError") : R("RevisarCargue_SeveridadAdvertencia");
                    ws.Cell(fila, 2).Style.Font.SetBold()
                        .Font.SetFontColor(XLColor.FromHtml(esError ? "#B91C1C" : "#92400E"))
                        .Fill.SetBackgroundColor(XLColor.FromHtml(esError ? "#FEF2F2" : "#FFFBEB"));
                    ws.Cell(fila, 3).Value = titulo ?? "";
                    ws.Cell(fila, 4).Value = mensaje ?? "";
                    ws.Cell(fila, 5).Value = h.NombreHoja ?? "";
                    if (h.NumeroFilaExcel.HasValue) ws.Cell(fila, 6).Value = h.NumeroFilaExcel.Value;

                    // Periodo 'AAAA-MM' → «Jun 2026»; referencia '@N4' → «Nivel 4 dígitos».
                    string periodo = h.Periodo ?? "";
                    if (periodo.Length == 7 && periodo[4] == '-' && int.TryParse(periodo.Substring(5, 2), out int mm) && mm >= 1 && mm <= 12)
                        periodo = mesesAbr[mm - 1] + " " + periodo.Substring(0, 4);
                    ws.Cell(fila, 7).Value = periodo;
                    string referencia = h.Referencia ?? "";
                    if (referencia.StartsWith("@N")) referencia = string.Format(R("RevisarCargue_Val_Nivel"), referencia.Substring(2));
                    ws.Cell(fila, 8).Value = referencia;
                    if (h.Diferencia.HasValue)
                    {
                        ws.Cell(fila, 9).Value = (double)h.Diferencia.Value;
                        ws.Cell(fila, 9).Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00";
                    }
                    ws.Cell(fila, 10).Value = h.Columna ?? "";
                    ws.Cell(fila, 11).Value = h.CodigoRegla ?? "";
                }

                var rngDatos = ws.Range(filaEnc, 1, fila, encabezados.Length);
                rngDatos.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetOutsideBorderColor(XLColor.FromHtml("#E3E6F0"))
                    .Border.SetInsideBorderColor(XLColor.FromHtml("#E3E6F0"))
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(filaEnc, 1, fila, encabezados.Length).SetAutoFilter();

                ws.Columns(1, encabezados.Length).AdjustToContents();
                ws.Column(1).Width = Math.Max(ws.Column(1).Width, 16);
                ws.Column(3).Width = Math.Min(Math.Max(ws.Column(3).Width, 24), 45);
                ws.Column(4).Width = 80;
                ws.Column(4).Style.Alignment.SetWrapText(true);
                ws.SheetView.Freeze(filaEnc, 0);

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>Descarga en Excel los hallazgos de validación de un lote en revisión.</summary>
        [HttpGet]
        public ActionResult ExportarHallazgosExcel(long idLote)
        {
            var lote = ObtenerLoteParaExportarHallazgos(idLote);
            if (lote == null)
                return new HttpStatusCodeResult(403);

            try
            {
                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                var bytes = GenerarExcelHallazgos(lote, _stagingService.ObtenerHallazgos(idLote), esIngles);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", NombreArchivoHallazgos(lote, "xlsx"));
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"ExportarHallazgosExcel: lote {idLote}");
                return new HttpStatusCodeResult(500);
            }
        }

        /// <summary>
        /// Envía por correo el informe de validación de un lote: resumen en el cuerpo + Excel adjunto, y el PDF
        /// (generado en el navegador, el mismo que se descarga) si la vista lo manda. Destinatarios = los escritos
        /// y/o el correo registrado del usuario en Bufins.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnviarHallazgosPorCorreo(long idLote, string correos, bool incluirmeAMi = false, string mensaje = null, string pdfBase64 = null)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var lote = ObtenerLoteParaExportarHallazgos(idLote);
            if (usuario == null || lote == null)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Exp_SinAcceso") });

            var listaCorreos = (correos ?? "")
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();
            if (incluirmeAMi && !string.IsNullOrWhiteSpace(usuario.Correo))
                listaCorreos.Insert(0, usuario.Correo.Trim());
            listaCorreos = listaCorreos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (listaCorreos.Count == 0)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_ValDestino") });
            var invalidos = listaCorreos.Where(c => !_rxCorreo.IsMatch(c)).ToList();
            if (invalidos.Any())
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_ValDestino") + " (" + string.Join(", ", invalidos) + ")" });
            if (listaCorreos.Count > 10)
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_ValMaximo") });

            // PDF opcional generado en el navegador: se acepta solo si de verdad es un PDF y no excede 8 MB.
            byte[] pdfBytes = null;
            if (!string.IsNullOrWhiteSpace(pdfBase64))
            {
                try
                {
                    string b64 = pdfBase64.Contains(",") ? pdfBase64.Substring(pdfBase64.IndexOf(',') + 1) : pdfBase64;
                    var bytes = Convert.FromBase64String(b64);
                    if (bytes.Length <= 8 * 1024 * 1024 && bytes.Length > 4
                        && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F')
                        pdfBytes = bytes;
                }
                catch (FormatException) { /* se envía sin PDF */ }
            }

            try
            {
                bool esIngles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                var hallazgos = _stagingService.ObtenerHallazgos(idLote)
                    .OrderBy(x => x.Severidad == CargueLoteSeveridad.Error ? 0 : 1).ToList();

                var adjuntos = new List<(string nombre, byte[] datos, string mime)>
                {
                    (NombreArchivoHallazgos(lote, "xlsx"), GenerarExcelHallazgos(lote, hallazgos, esIngles),
                     "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                };
                if (pdfBytes != null)
                    adjuntos.Add((NombreArchivoHallazgos(lote, "pdf"), pdfBytes, "application/pdf"));

                // Cuerpo del correo: una línea por validación (no por caso; el detalle completo va en el Excel adjunto).
                var items = hallazgos.GroupBy(h => h.CodigoRegla ?? "").Select(g =>
                {
                    var h = g.First();
                    string regla = esIngles ? (h.ReglaEn ?? h.Regla) : h.Regla;
                    string msg = !string.IsNullOrWhiteSpace(regla) ? regla
                        : (esIngles && !string.IsNullOrWhiteSpace(h.MensajeEn) ? h.MensajeEn : h.Mensaje);
                    if (g.Count() > 1) msg += " (" + string.Format(R("RevisarCargue_Val_Casos"), g.Count().ToString("N0")) + ")";
                    return (
                        esError: h.Severidad == CargueLoteSeveridad.Error,
                        titulo: esIngles && !string.IsNullOrWhiteSpace(h.TituloEn) ? h.TituloEn : h.Titulo,
                        mensaje: msg);
                }).ToList();

                string quienEnvia = ((usuario.Nombre ?? "") + " " + (usuario.Apellidos ?? "")).Trim();
                string modoTexto = lote.Modo == 0 ? R("RevisarCargue_ModoEjecucion") : R("RevisarCargue_ModoHistorico");

                new EmailService().EnviarHallazgosCargue(
                    string.Join(", ", listaCorreos), lote.NombreEmpresa, lote.Anio, modoTexto, lote.IdEscenario,
                    lote.NombreArchivo, lote.TotalErrores, lote.TotalAdvertencias, items,
                    mensaje, quienEnvia, adjuntos, esIngles);

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Enviar, "CargueLote", idLote.ToString(),
                    $"Informe de validación del cargue enviado por correo a {string.Join(", ", listaCorreos)}: '{lote.NombreEmpresa}' — año {lote.Anio}, escenario {lote.IdEscenario}, archivo '{lote.NombreArchivo}' ({lote.TotalErrores} errores, {lote.TotalAdvertencias} advertencias).",
                    null,
                    new { empresa = lote.NombreEmpresa, anio = lote.Anio, modo = lote.Modo, escenario = lote.IdEscenario, archivo = lote.NombreArchivo, destinatarios = listaCorreos, conPdf = pdfBytes != null },
                    lote.IdEmpresa, entidadNombre: lote.NombreEmpresa, severidad: AuditoriaSeveridad.Advertencia);

                return Json(new { exito = true, mensaje = string.Format(R("RevisarCargue_Correo_Exito"), string.Join(", ", listaCorreos)) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"EnviarHallazgosPorCorreo: lote {idLote}");
                return Json(new { exito = false, mensaje = R("RevisarCargue_Correo_Error") });
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

    }
}
