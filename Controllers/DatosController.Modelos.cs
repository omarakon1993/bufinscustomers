using bufinscustomers.Helpers;
using bufinscustomers.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace bufinscustomers.Controllers
{
    // Modelos: parte de DatosController (misma clase; solo se separa el codigo por responsabilidad).
    public partial class DatosController
    {
        // Modelo actions
        public ActionResult Modelo()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = empresaService.ObtenerEmpresas();
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
            }

            var vm = new Models.ModeloPageViewModel
            {
                Empresas = empresas,
                Modelos = modeloService.ObtenerModelosActivos()
            };
            return View("~/Views/Datos/Modelo.cshtml", vm);
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

            var modelo = modeloService.ObtenerModeloPorId(idModelo);
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
                            notificacionesService.Crear(usuario.Id, R("Notif_ModeloEjecutado"), mensajeResp, "success", "/Datos/Modelo");

                            var empresaEjecutada = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                            auditoriaService.RegistrarCambio(
                                AuditoriaTipo.Modelos, AuditoriaAccion.Ejecutar, "ModelosEjecucion", idModelo.ToString(),
                                $"Modelo '{modelo.Nombre}' ejecutado para '{empresaEjecutada?.Nombre ?? "Empresa"}' (Escenario {idEscenario}).", null,
                                new { empresa = empresaEjecutada?.Nombre, modelo = modelo.Nombre, filas = lastFilas.Count, escenario = idEscenario },
                                idEmpresa, entidadNombre: modelo.Nombre);
                        }
                        else
                        {
                            notificacionesService.Crear(usuario.Id, R("Notif_ErrorModelo"), mensajeResp, "error", "/Datos/Modelo");
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
                var empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                var modelos = modeloService.ObtenerModelosActivos();

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

                    auditoriaService.RegistrarCambio(
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

            var modelo = modeloService.ObtenerModeloPorId(idModelo);
            if (modelo == null)
            {
                SetErrorMessage("El modelo seleccionado no existe o no está activo.");
                return RedirectToAction("Modelo");
            }

            try
            {
                var empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);

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
                            exitosos = Array.Empty<string>()
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

                    auditoriaService.RegistrarCambio(
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

            var modelos = modeloService.ObtenerModelosActivos().Where(m => ids.Contains(m.Id)).ToList();
            if (modelos.Count == 0)
            {
                SetErrorMessage("Debe seleccionar al menos un modelo para exportar.");
                return RedirectToAction("Modelo");
            }

            try
            {
                var empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);

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

                    auditoriaService.RegistrarCambio(
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
            if (invalidos.Count != 0)
                return Json(new { exito = false, mensaje = R("Modelo_Correo_ValCorreo") + " (" + string.Join(", ", invalidos) + ")" });

            if (listaCorreos.Count > 10)
                return Json(new { exito = false, mensaje = "Máximo 10 destinatarios por envío." });

            // ── Modelos a incluir ────────────────────────────────────────────
            var todos = modeloService.ObtenerModelosActivos();
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
                var empresa = empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
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

                emailService.EnviarModelosExportados(
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

                notificacionesService.Crear(usuario.Id, R("Notif_ModelosEnviadosCorreo"), resumen, "success", "/Datos/Modelo");

                auditoriaService.RegistrarCambio(
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
                notificacionesService.Crear(usuario.Id, R("Notif_ErrorEnvioCorreoModelos"), ex.Message, "error", "/Datos/Modelo");
                return Json(new { exito = false, mensaje = "No se pudo enviar el correo: " + MensajeError(ex) });
            }
        }
    }
}
