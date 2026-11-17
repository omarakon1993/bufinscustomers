using bufinscustomers.Helpers;
using bufinscustomers.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Orquestador único del cargue de datos: staging → validación → impacto → confirmación → descarte.
    /// No conoce la sesión ni el HTTP; el llamador (pantalla de Excel o API) le pasa el usuario, el archivo
    /// y, si quiere mensajes traducidos, un traductor (mismo patrón de <c>IASolicitud.Traducir</c>).
    /// Toda la lógica (staging, reglas del SP, regla de oro, snapshot, auditoría) vive aquí, una sola vez.
    /// </summary>
    public partial class CargueOrquestadorService : BaseService
    {
        private readonly EmpresaService _empresaService = new EmpresaService();
        private readonly ConfiguracionEmpresaService _configuracionService = new ConfiguracionEmpresaService();
        private readonly HistorialVersionesCarguesService _historialService = new HistorialVersionesCarguesService();
        private readonly CargueStagingService _stagingService = new CargueStagingService();
        private readonly CargueImpactoService _impactoService = new CargueImpactoService();
        private readonly CargueAuditoriaDetalleService _detalleService = new CargueAuditoriaDetalleService();
        private readonly CargueArchivosService _archivosService = new CargueArchivosService();
        private readonly Func<string, string> _traducir;
        private readonly StringBuilder _logBuilder = new StringBuilder();

        // Último error de lectura/escritura de una hoja (lo llena GuardarEnIni / GuardarEnSQLServer).
        private string UltimoMensajeHoja;
        private bool MostrarDescargaLog;

        /// <param name="traducir">Traductor de claves de recursos (p. ej. <c>R</c> del controlador). Si es null devuelve la clave.</param>
        public CargueOrquestadorService(Func<string, string> traducir = null)
        {
            _traducir = traducir ?? (k => k);
        }

        private string T(string clave) => _traducir(clave);

        private void LogToFile(string mensaje)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                _logBuilder.AppendLine($"[{timestamp}] {mensaje}");
            }
            catch { /* Ignorar errores de logging */ }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 1. Validar la solicitud (empresa, permisos, año, modo, archivo)
        // ═════════════════════════════════════════════════════════════════════════
        public ResultadoCargueSimple ValidarSolicitud(SolicitudCargue s, bool exigirArchivo = true)
        {
            if (s.IdEmpresa == 0)
                return ResultadoCargueSimple.Error("Debe seleccionar una empresa antes de cargar el archivo.");

            if (!EmpresaAccesoHelper.TieneAcceso(s.Usuario, s.IdEmpresa))
                return ResultadoCargueSimple.Error("No tiene permisos para cargar datos en esta empresa.");

            // Empresa con plantilla personalizada que este usuario no puede ver (p. ej. acceso solo por grupo):
            // no se le deja cargar — se le exigirían hojas que no conoce. El mensaje no nombra las hojas.
            if (!PersonalizacionesEmpresaHelper.PuedeOperarCargue(s.Usuario, s.IdEmpresa))
                return ResultadoCargueSimple.Error(T("Datos_PlantillaPersonalizadaSinPermiso"));

            var config = _configuracionService.ObtenerConfiguracionPorEmpresa(s.IdEmpresa);
            if (config == null)
                return ResultadoCargueSimple.Error("La empresa seleccionada no tiene configuración creada.");

            // El escenario debe existir y estar activo. El Excel solo lo ofrece en un selector y la API recibe un número libre:
            // la validación vive aquí para que valga igual en las dos entradas (paridad Excel ↔ API, ver CLAUDE.md).
            if (!EscenarioValido(s.IdEscenario))
                return ResultadoCargueSimple.Error(T("Datos_EscenarioInvalido"));

            bool modoEjecucion = string.Equals(s.Modo, "ejecucion", StringComparison.OrdinalIgnoreCase);
            bool modoHistorico = string.Equals(s.Modo, "historico", StringComparison.OrdinalIgnoreCase);

            if (!modoEjecucion && !modoHistorico)
                return ResultadoCargueSimple.Error("Debe seleccionar un año antes de cargar el archivo.");

            if (modoEjecucion && s.Anio != config.AnioEjecucion)
                return ResultadoCargueSimple.Error($"El año seleccionado ({s.Anio}) no coincide con el año de ejecución configurado ({config.AnioEjecucion}).");

            var anosHistoricosCfg = config.AnosHistoricos?
                .Select(a => a.NombreAno?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

            if (modoHistorico && !anosHistoricosCfg.Contains(s.Anio.ToString()))
                return ResultadoCargueSimple.Error($"El año seleccionado ({s.Anio}) no está configurado como año histórico para esta empresa.");

            if (exigirArchivo)
            {
                if (s.Archivo == null || s.LongitudArchivo == 0)
                    return ResultadoCargueSimple.Error("No se seleccionó ningún archivo.");

                string extension = Path.GetExtension(s.NombreArchivo)?.ToLower();
                if (extension != ".xlsx" && extension != ".xlsm")
                    return ResultadoCargueSimple.Error("Solo se permiten archivos Excel (.xlsx, .xlsm).");
            }

            return ResultadoCargueSimple.Correcto();
        }

        /// <summary>Escenario existente y activo. Si el catálogo no se puede leer, solo se admite el escenario base (1).</summary>
        private static bool EscenarioValido(int idEscenario)
        {
            try
            {
                var todos = new EscenarioService().ObtenerTodos();
                return todos.Count == 0 ? idEscenario == 1 : todos.Any(e => e.Id == idEscenario && e.Activo);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("CargueOrquestador.EscenarioValido: " + ex.Message);
                return idEscenario == 1;
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 2. Procesar el archivo: crear lote → staging → validar (sin tocar Ini_* reales)
        // ═════════════════════════════════════════════════════════════════════════
        /// <param name="s">Solicitud ya validada con <see cref="ValidarSolicitud"/>.</param>
        /// <param name="alCrearLote">Se invoca apenas existe el lote (la pantalla de Excel lo ata a la sesión).</param>
        public ResultadoProcesoCargue ProcesarArchivo(SolicitudCargue s, Action<long> alCrearLote = null)
        {
            var resultado = new ResultadoCargaExcel();
            string nombreArchivoOriginal = Path.GetFileName(s.NombreArchivo) ?? "";
            var usuarioActual = s.Usuario;
            int idUsuario = usuarioActual?.Id ?? 0;
            bool modoEjecucion = string.Equals(s.Modo, "ejecucion", StringComparison.OrdinalIgnoreCase);
            byte modo = modoEjecucion ? (byte)0 : (byte)1;
            long idLote = 0;
            int idEmpresaSeleccionada = s.IdEmpresa;
            int anioSeleccionado = s.Anio;
            int idEscenarioSeleccionado = s.IdEscenario;

            ResultadoProcesoCargue Fin(ProcesoCargueEstado estado, string mensaje, ResultadoCargaExcel res = null)
            {
                return new ResultadoProcesoCargue
                {
                    Estado = estado, IdLote = idLote, Mensaje = mensaje, Resultado = res,
                    NombreArchivo = nombreArchivoOriginal, LogImportacion = _logBuilder.ToString()
                };
            }

            // Purga oportunista de lotes de staging abandonados (nunca confirmados). No bloquea el cargue.
            _stagingService.PurgarLotesVencidosSiToca();

            // Subir un archivo (p. ej. el corregido) reemplaza cualquier cargue que este usuario hubiera
            // dejado en revisión para ESTA empresa (aunque sea de otro año/modo/escenario): así no queda
            // pegado. Los lotes de otras empresas nunca se tocan desde aquí.
            try { _stagingService.DescartarLotesPendientesDeUsuario(idUsuario, idEmpresaSeleccionada); }
            catch (Exception exDesc) { AppLogger.Error(exDesc, "CargarExcel.DescartarLotesPendientesDeUsuario"); }

            try
            {
                using (var package = new XLWorkbook(s.Archivo))
                {
                    // La plantilla debe traer las hojas Z_ esperadas. Se permiten hojas
                    // adicionales no reconocidas (p. ej. la hoja auxiliar "Datos" que solo
                    // alimenta las listas de validación del Excel): se ignoran más abajo.
                    var hojasLibro = package.Worksheets
                        .Select(w => NormalizarNombre(w.Name))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Hojas exigidas = las estándar + las personalizadas de ESTA empresa (si tiene paquete).
                    // Una hoja personalizada de otra empresa que venga en el libro no se reconoce: se ignora.
                    var mapeoEmpresa = TablasCargueHelper.MapeoParaEmpresa(idEmpresaSeleccionada);
                    var hojasReconocidas = HojasCargueReconocidas(mapeoEmpresa);

                    var hojasFaltantes = mapeoEmpresa.Keys
                        .Where(z => !hojasLibro.Contains(NormalizarNombre(z)))
                        .ToList();

                    if (hojasFaltantes.Count != 0)
                    {
                        resultado.Exito = false;
                        resultado.Mensaje = $"La plantilla no contiene todas las hojas requeridas. Faltan: {string.Join(", ", hojasFaltantes)}.";
                        if (TablasCargueHelper.HojasPersonalizadasParaEmpresa(idEmpresaSeleccionada).Length > 0)
                            resultado.Mensaje += " " + T("Datos_PlantillaPersonalizadaFaltan");
                        return Fin(ProcesoCargueEstado.PlantillaIncompleta, resultado.Mensaje, resultado);
                    }

                    string nombreEmpresaLote = _empresaService.ObtenerEmpresas()
                        .Find(e => e.Id == idEmpresaSeleccionada)?.Nombre ?? "Desconocida";
                    string nombreUsuarioLote = ((usuarioActual?.Nombre ?? "") + " " + (usuarioActual?.Apellidos ?? "")).Trim();

                    var (loteCreado, idLoteCreado, mensajeLote) = _stagingService.CrearLote(idEmpresaSeleccionada, nombreEmpresaLote, anioSeleccionado, modo,
                        idEscenarioSeleccionado, idUsuario, nombreUsuarioLote, nombreArchivoOriginal);

                    if (!loteCreado)
                    {
                        // Ya hay un cargue en revisión pendiente para esta misma empresa/año/modo/escenario:
                        // se manda directo a revisarlo en vez de dejar subir uno nuevo encima.
                        idLote = idLoteCreado;
                        return Fin(ProcesoCargueEstado.LoteActivoExistente, mensajeLote);
                    }
                    idLote = idLoteCreado;
                    alCrearLote?.Invoke(idLote);

                    // Se conserva el archivo original (últimos N confirmados por empresa) para poder reconstruir
                    // qué subió el usuario. No bloquea el cargue si falla; el IdLote lo ata hasta la confirmación.
                    try
                    {
                        var stream = s.Archivo;
                        if (stream.CanSeek) stream.Position = 0;
                        byte[] bytesArchivo;
                        using (var msArchivo = new MemoryStream()) { stream.CopyTo(msArchivo); bytesArchivo = msArchivo.ToArray(); }
                        if (stream.CanSeek) stream.Position = 0;
                        _archivosService.GuardarPendiente(new CargueLote
                        {
                            IdLote = idLote, IdEmpresa = idEmpresaSeleccionada, NombreEmpresa = nombreEmpresaLote,
                            Anio = anioSeleccionado, Modo = modo, IdEscenario = idEscenarioSeleccionado,
                            IdUsuario = idUsuario, NombreUsuario = nombreUsuarioLote, NombreArchivo = nombreArchivoOriginal
                        }, bytesArchivo);
                    }
                    catch (Exception exArch) { AppLogger.Error(exArch, "CargarExcel.GuardarArchivo"); }

                    bool errorEnStaging = false;

                    using (var conn = new SqlConnection(CadenaConexion))
                    {
                        conn.Open();
                        using (var tx = conn.BeginTransaction())
                        {
                            try
                            {
                                foreach (var hoja in package.Worksheets)
                                {
                                    var nombreNorm = NormalizarNombre(hoja.Name);

                                    // Hoja auxiliar de la plantilla (listas de validación, etc.): se omite.
                                    if (!hojasReconocidas.Contains(nombreNorm))
                                        continue;

                                    var detalle = new DetalleCargaHojaExcel { NombreHoja = hoja.Name };
                                    var dt = LeerHojaEnDataTable(hoja, detalle, anioSeleccionado.ToString());

                                    if (dt == null)
                                    {
                                        resultado.DetalleHojas.Add(detalle);
                                        continue;
                                    }

                                    bool exitoHoja;
                                    if (mapeoEmpresa.TryGetValue(nombreNorm, out string nombreTablaIni))
                                    {
                                        detalle.NombreTabla = nombreTablaIni;
                                        string nombreStaging = TablasCargueHelper.NombreStaging(nombreTablaIni);
                                        exitoHoja = GuardarEnIni(conn, dt, nombreStaging, idEmpresaSeleccionada, idUsuario, idEscenarioSeleccionado, historicoLog: modo, tx: tx, idLote: idLote);
                                    }
                                    else
                                    {
                                        // Z_TablaPUC no tiene tabla Ini_/Staging_Ini_ asociada (es un catálogo,
                                        // no participa en las reglas de negocio de sp_ValidarCargueStaging):
                                        // sigue guardándose igual que hoy, fuera de este flujo de staging.
                                        exitoHoja = GuardarEnSQLServer(dt, idEmpresaSeleccionada, idUsuario);
                                    }

                                    if (!exitoHoja)
                                    {
                                        detalle.Estado = "Error";
                                        // "Staging_Ini_X" solo es el nombre físico de la tabla de staging — al
                                        // usuario se le muestra "Ini_X" para que el mensaje no confunda.
                                        detalle.MensajeError = (UltimoMensajeHoja ?? "Error al leer la hoja").Replace("Staging_Ini_", "Ini_");
                                        resultado.DetalleHojas.Add(detalle);

                                        foreach (var d in resultado.DetalleHojas.Where(x => x.Estado == "Exitoso"))
                                        {
                                            d.Estado = "Revertido";
                                            d.MensajeError = "Revertido por error en otra hoja";
                                        }

                                        resultado.Exito = false;
                                        resultado.Mensaje = $"Error al procesar '{hoja.Name}'.";
                                        resultado.MostrarDescargaLog = MostrarDescargaLog;

                                        tx.Rollback();
                                        errorEnStaging = true;
                                        break;
                                    }

                                    detalle.FilasInsertadas = dt.Rows.Count;
                                    detalle.Estado = "Exitoso";
                                    resultado.DetalleHojas.Add(detalle);
                                    resultado.TotalHojasProcesadas++;
                                    resultado.TotalFilasInsertadas += dt.Rows.Count;
                                }

                                if (!errorEnStaging)
                                    tx.Commit();
                            }
                            catch (Exception txEx)
                            {
                                Helpers.TransaccionHelper.RollbackSeguro(tx);
                                LogToFile($"Error inesperado escribiendo en staging: {txEx.Message}");
                                throw;
                            }
                        }
                    }

                    if (errorEnStaging)
                    {
                        _stagingService.DescartarLote(idLote);
                        return Fin(ProcesoCargueEstado.ErrorEnHoja, resultado.Mensaje, resultado);
                    }

                    // Protección: confirmar un cargue sin una sola fila del año solo BORRARÍA los datos de ese año.
                    // (LeerHojaEnDataTable omite en silencio las filas de otro año, así que un archivo de otro
                    // año llega aquí «vacío» y, sin esta guarda, se podía confirmar.)
                    if (resultado.TotalFilasInsertadas == 0)
                    {
                        _stagingService.DescartarLote(idLote);
                        string msgVacio = string.Format(T("Datos_CargueSinFilasDelAnio"), anioSeleccionado);
                        new AuditoriaService().RegistrarCambio(
                            AuditoriaTipo.Cargues, AuditoriaAccion.Descartar, "CargueLote", idLote.ToString(),
                            $"Cargue bloqueado: el archivo '{nombreArchivoOriginal}' no trae ninguna fila del año {anioSeleccionado} para '{nombreEmpresaLote}' ({(modo == 0 ? "ejecución" : "histórico")}, escenario {idEscenarioSeleccionado}). No se tocó ningún dato.",
                            null, new { empresa = nombreEmpresaLote, anio = anioSeleccionado, modo, escenario = idEscenarioSeleccionado, archivo = nombreArchivoOriginal },
                            idEmpresaSeleccionada, entidadNombre: nombreEmpresaLote, severidad: AuditoriaSeveridad.Advertencia);
                        return Fin(ProcesoCargueEstado.SinFilasDelAnio, msgVacio, resultado);
                    }

                    _stagingService.ActualizarTotalFilas(idLote, resultado.TotalFilasInsertadas);

                    var (exitoValidacion, mensajeValidacion, _, _) = _stagingService.ValidarLote(idLote);
                    if (!exitoValidacion)
                    {
                        _stagingService.DescartarLote(idLote);
                        AppLogger.Error($"sp_ValidarCargueStaging falló para el lote {idLote}: {mensajeValidacion}", contexto: "CargarExcel");
                        return Fin(ProcesoCargueEstado.ErrorValidacion, mensajeValidacion, resultado);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "CargueOrquestador.ProcesarArchivo");
                resultado.Exito = false;
                resultado.Mensaje = $"Error al procesar el archivo: {ex.Message}";
                if (idLote > 0)
                {
                    try { _stagingService.DescartarLote(idLote); }
                    catch (Exception exDesc) { AppLogger.Error(exDesc, $"CargarExcel: no se pudo descartar el lote {idLote}"); }
                }
                return Fin(ProcesoCargueEstado.ErrorInesperado, resultado.Mensaje, resultado);
            }

            return Fin(ProcesoCargueEstado.EnRevision, null, resultado);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 3. Confirmar un lote validado: impacto → snapshot → reemplazo real → auditoría
        // ═════════════════════════════════════════════════════════════════════════
        /// <param name="simular">Sandbox: corre TODAS las comprobaciones (acceso, impacto, regla de oro, reducción fuerte) y, si pasan, devuelve
        /// <see cref="ConfirmacionCargueEstado.Simulado"/> y descarta el lote de pruebas SIN tocar ninguna tabla real, respaldo ni auditoría de confirmación.</param>
        public ResultadoConfirmacionCargue Confirmar(Usuarios usuario, long idLote, bool confirmarReemplazo = false, bool simular = false)
        {
            var lote = _stagingService.ObtenerLote(idLote);

            ResultadoConfirmacionCargue Sale(ConfirmacionCargueEstado estado, string mensaje, ResultadoCargaExcel res = null) =>
                new ResultadoConfirmacionCargue { Estado = estado, Mensaje = mensaje, Resultado = res, Lote = lote };

            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
                return Sale(ConfirmacionCargueEstado.LoteNoExiste, "El cargue en revisión indicado no existe o no tiene acceso a él.");

            if (!PersonalizacionesEmpresaHelper.PuedeOperarCargue(usuario, lote.IdEmpresa))
                return Sale(ConfirmacionCargueEstado.SinPermisoPlantilla, T("Datos_PlantillaPersonalizadaSinPermiso"));

            if (!lote.PuedeConfirmar)
                return Sale(ConfirmacionCargueEstado.NoConfirmable, "Este cargue tiene errores pendientes y no se puede confirmar. Corrija el Excel y vuelva a subirlo.");

            // Impacto sobre los datos actuales, calculado en el servidor (nunca con cifras del navegador):
            // un cargue sin filas jamás se confirma, y una reducción fuerte exige confirmación reforzada.
            ImpactoCargue impacto;
            try { impacto = _impactoService.Analizar(lote); }
            catch (Exception exImp)
            {
                AppLogger.Error(exImp, "ConfirmarCargue.Impacto");
                return Sale(ConfirmacionCargueEstado.ErrorImpacto, T("Datos_CargueImpactoError"));
            }

            if (impacto.CargaVacia)
                return Sale(ConfirmacionCargueEstado.CargaVacia, string.Format(T("Datos_CargueSinFilasDelAnio"), lote.Anio));

            // Regla de oro: ningún cargue puede dejar un año sin datos ni reducir una tabla por debajo del umbral de bloqueo.
            // No hay casilla ni rol que lo permita (ni siquiera el Super Admin): hay que corregir el archivo o el año.
            if (impacto.Bloqueado)
            {
                string tablasBloq = string.Join(", ", impacto.TablasBloqueadas.Select(t => t.Tabla));
                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Bloqueo, "CargueLote", idLote.ToString(),
                    $"Confirmación de cargue BLOQUEADA en '{lote.NombreEmpresa}' — año {lote.Anio}, {(lote.Modo == 0 ? "ejecución" : "histórico")}, escenario {lote.IdEscenario}: dejaría {impacto.TotalExistentes:N0} fila(s) existentes en solo {impacto.TotalNuevas:N0}" +
                    (tablasBloq.Length > 0 ? $" (tablas bajo el {impacto.UmbralBloqueoPct}%: {tablasBloq})" : "") + $", archivo '{lote.NombreArchivo}'. No se cambió ningún dato.",
                    null, new { empresa = lote.NombreEmpresa, anio = lote.Anio, modo = lote.Modo, escenario = lote.IdEscenario, archivo = lote.NombreArchivo, existentes = impacto.TotalExistentes, nuevas = impacto.TotalNuevas, tablas = tablasBloq },
                    lote.IdEmpresa, entidadNombre: lote.NombreEmpresa);
                return Sale(ConfirmacionCargueEstado.Bloqueado, string.Format(T("Datos_CargueBloqueadoReduccion"), tablasBloq, impacto.UmbralBloqueoPct));
            }

            if (impacto.RequiereConfirmacion && !confirmarReemplazo)
                return Sale(ConfirmacionCargueEstado.FaltaConfirmarReemplazo,
                    string.Format(T("Datos_CargueReemplazoSinConfirmar"), impacto.TotalExistentes.ToString("N0"), impacto.TotalNuevas.ToString("N0")));

            if (simular)
            {
                var conteo = new List<(string NombreHoja, string NombreTabla, int Filas)>();
                using (var cnSim = new SqlConnection(CadenaConexion))
                {
                    cnSim.Open();
                    conteo = _stagingService.ObtenerConteoPorHoja(cnSim, null, idLote);
                }
                var sim = new ResultadoCargaExcel
                {
                    Exito = true, TotalFilasInsertadas = lote.TotalFilas, TotalHojasProcesadas = conteo.Count,
                    DetalleHojas = conteo.Select(c => new DetalleCargaHojaExcel { NombreHoja = c.NombreHoja, NombreTabla = c.NombreTabla, FilasInsertadas = c.Filas, Estado = "Simulado" }).ToList(),
                    Mensaje = $"Simulación (pruebas): el cargue es válido y se habría confirmado con {lote.TotalFilas:N0} fila(s). No se modificó ningún dato real."
                };
                var webhooks = new WebhooksService();
                int? idIntegSim = webhooks.IdIntegracionDeLote(idLote);   // antes de descartar: el lote desaparece
                try { _stagingService.DescartarLote(idLote); }
                catch (Exception exSim) { AppLogger.Error(exSim, $"Confirmar(simulado): no se pudo descartar el lote {idLote}"); }
                webhooks.NotificarCargueConfirmado(idIntegSim, lote, sim, usuario?.Id ?? 0, simulado: true);
                new IntegracionesActividadService().Resolver(idIntegSim, idLote, "Simulado", usuario?.Id ?? 0);
                return Sale(ConfirmacionCargueEstado.Simulado, sim.Mensaje, sim);
            }

            int idUsuario = usuario?.Id ?? 0;
            var resultado = new ResultadoCargaExcel { TotalHojasProcesadas = 0, TotalFilasInsertadas = lote.TotalFilas };
            int idHistorialCargue = 0;
            int? idAuditoriaCargue = null;
            var existentesAntes = new List<(string Tabla, int Filas)>();
            List<CargueDetalleFila> detalleAuditoria = null;
            var conteoPorHoja = new List<(string NombreHoja, string NombreTabla, int Filas)>();
            bool errorEnConfirmacion = false;
            string mensajeError = null;

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // Candado sobre el lote ANTES de tocar nada real: ningún descarte/purga concurrente
                        // puede vaciar su staging mientras se confirma (y si ya lo vació, se aborta aquí).
                        var (loteOk, mensajeLoteBloqueo) = _stagingService.BloquearLoteParaConfirmar(conn, tx, idLote, lote.IdEmpresa);
                        if (!loteOk)
                        {
                            tx.Rollback();
                            return Sale(ConfirmacionCargueEstado.LoteNoDisponible, mensajeLoteBloqueo);
                        }

                        try
                        {
                            idHistorialCargue = _historialService.CrearSnapshotEnTransaccion(
                                conn, tx, lote.IdEmpresa, lote.NombreEmpresa,
                                lote.Anio, lote.Modo, idUsuario, lote.NombreUsuario, lote.NombreArchivo, lote.IdEscenario);
                        }
                        catch (Exception snapEx) { LogToFile($"Advertencia snapshot al confirmar lote {idLote}: {snapEx.Message}"); }

                        // Lo que de verdad se va a borrar, contado dentro de la transacción y justo antes del DELETE.
                        existentesAntes = _impactoService.ContarExistentes(conn, tx, lote.IdEmpresa, lote.Anio, lote.Modo, lote.IdEscenario);

                        if (lote.Modo == 0)
                            EliminarEjecucionDeIni(conn, lote.Anio, lote.IdEmpresa, lote.IdEscenario, tx);
                        else
                            EliminarAnosHistoricosDeIni(conn, lote.Anio, lote.IdEmpresa, lote.IdEscenario, tx);

                        var (exito, mensaje) = _stagingService.ConfirmarLote(conn, tx, idLote, idHistorialCargue > 0 ? idHistorialCargue : (int?)null);

                        // Segunda barrera: tras reemplazar, el año NO puede haber quedado sin filas. Si pasara, se cancela todo.
                        if (exito && _impactoService.ContarExistentes(conn, tx, lote.IdEmpresa, lote.Anio, lote.Modo, lote.IdEscenario).Sum(e => e.Filas) == 0)
                        {
                            exito = false;
                            mensaje = T("Datos_CargueAnioVacioCancelado");
                        }
                        if (!exito)
                        {
                            errorEnConfirmacion = true;
                            mensajeError = mensaje;
                            tx.Rollback();
                        }
                        else
                        {
                            idAuditoriaCargue = RegistrarAuditoria(conn, usuario, lote.NombreArchivo, lote.IdEmpresa, lote.IdEscenario, tx);

                            // Detalle por hoja para el resumen (cuenta el staging ANTES de borrarlo) y limpieza del
                            // lote en la MISMA transacción: o queda confirmado y limpio, o no cambia nada.
                            conteoPorHoja = _stagingService.ObtenerConteoPorHoja(conn, tx, idLote);

                            // Auditoría detallada: por tabla, cuántas filas había (y se reemplazaron) y cuántas quedaron.
                            detalleAuditoria = ConstruirDetalleCargue(lote, usuario, idAuditoriaCargue, idHistorialCargue, existentesAntes, conteoPorHoja);
                            _detalleService.Registrar(conn, tx, detalleAuditoria);
                            _archivosService.MarcarConfirmado(conn, tx, idLote, idAuditoriaCargue);

                            _stagingService.LimpiarLoteEnTransaccion(conn, tx, idLote);

                            tx.Commit();
                        }
                    }
                    catch (Exception txEx)
                    {
                        Helpers.TransaccionHelper.RollbackSeguro(tx);
                        LogToFile($"Error inesperado confirmando el lote {idLote}: {txEx.Message}");
                        errorEnConfirmacion = true;
                        mensajeError = txEx.Message;
                    }
                }
            }

            if (errorEnConfirmacion)
            {
                AppLogger.Error($"ConfirmarCargue: fallo confirmando el lote {idLote}: {mensajeError}", contexto: "ConfirmarCargue");
                return Sale(ConfirmacionCargueEstado.Error, $"No se pudo confirmar el cargue: {mensajeError}");
            }

            resultado.DetalleHojas = conteoPorHoja
                .Select(c => new DetalleCargaHojaExcel { NombreHoja = c.NombreHoja, NombreTabla = c.NombreTabla, FilasInsertadas = c.Filas, Estado = "Exitoso" })
                .ToList();
            resultado.TotalHojasProcesadas = resultado.DetalleHojas.Count;

            int filasReemplazadas = existentesAntes.Sum(e => e.Filas);
            var tablasAntes = existentesAntes.Where(e => e.Filas > 0).ToDictionary(e => e.Tabla, e => e.Filas);
            new AuditoriaService().RegistrarCambio(
                AuditoriaTipo.Cargues, AuditoriaAccion.Confirmar, "CargueLote", idLote.ToString(),
                $"Confirmó el cargue de '{lote.NombreEmpresa}' — año {lote.Anio}, {(lote.Modo == 0 ? "ejecución" : "histórico")}, escenario {lote.IdEscenario}: reemplazó {filasReemplazadas:N0} fila(s) existentes por {lote.TotalFilas:N0}, archivo '{lote.NombreArchivo}'" +
                (impacto.RequiereConfirmacion ? " (reducción fuerte confirmada por el usuario)." : "."),
                new { filasReemplazadas, filasPorTabla = tablasAntes, idHistorialSnapshot = idHistorialCargue > 0 ? idHistorialCargue : (int?)null },
                new { empresa = lote.NombreEmpresa, anio = lote.Anio, modo = lote.Modo, escenario = lote.IdEscenario, totalFilas = lote.TotalFilas, archivo = lote.NombreArchivo, idAuditoriaCargue, reduccionFuerteConfirmada = impacto.RequiereConfirmacion },
                lote.IdEmpresa, entidadNombre: lote.NombreEmpresa,
                severidad: impacto.RequiereConfirmacion ? AuditoriaSeveridad.Advertencia : null);

            // Deja solo los últimos N archivos confirmados de la empresa (parámetro Cargue_ArchivosConservados).
            _archivosService.Purgar(lote.IdEmpresa);

            // Webhook: si el cargue lo creó una integración, se le avisa (también cuando lo confirma una persona en «Revisar cargue»).
            var whConf = new WebhooksService();
            int? idIntegConf = whConf.IdIntegracionDeLote(idLote);
            whConf.NotificarCargueConfirmado(idIntegConf, lote, resultado, idUsuario, simulado: false);
            new IntegracionesActividadService().Resolver(idIntegConf, idLote, "Confirmado", idUsuario);

            resultado.Exito = true;
            resultado.TotalFilasInsertadas = lote.TotalFilas;

            string modoTexto = lote.Modo == 0 ? T("RevisarCargue_ModoEjecucion") : T("RevisarCargue_ModoHistorico");
            string resumen = string.Format(T("Datos_ResumenCargueConfirmado"),
                lote.NombreEmpresa, lote.Anio, modoTexto, lote.IdEscenario, lote.TotalFilas.ToString("N0"), resultado.TotalHojasProcesadas);

            resultado.NombreEmpresa = lote.NombreEmpresa;
            resultado.Anio = lote.Anio;
            resultado.ModoTexto = modoTexto;
            resultado.IdEscenario = lote.IdEscenario;

            // Red de seguridad adicional post-commit (solo modo ejecución), como ya existía.
            if (lote.Modo == 0)
            {
                var resultadoValidacion = EjecutarValidacionDatos(idUsuario);
                resultado.NotaExtra = resultadoValidacion.esExitoso ? resultadoValidacion.mensaje : null;
                resultado.Mensaje = resultadoValidacion.esExitoso
                    ? $"{resumen} {resultadoValidacion.mensaje}"
                    : resultadoValidacion.mensaje;
                if (!resultadoValidacion.esExitoso)
                {
                    resultado.Exito = false;
                    resultado.MostrarDescargaLog = true;
                }
            }
            else
            {
                resultado.Mensaje = resumen;
            }

            if (usuario != null)
            {
                if (resultado.Exito)
                    new NotificacionesService().Crear(usuario.Id, T("Notif_CargueCompletado"), resultado.Mensaje, "success", "/Datos/CargueExcel");
                else
                    new NotificacionesService().Crear(usuario.Id, T("Notif_ErrorCargue"), resultado.Mensaje, "error", "/Datos/CargueExcel");
            }

            return Sale(ConfirmacionCargueEstado.Confirmado, resultado.Mensaje, resultado);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 4. Descartar un lote sin tocar ninguna tabla real
        // ═════════════════════════════════════════════════════════════════════════
        /// <summary>Descarte pedido por el usuario. Devuelve false solo si falló al borrar el lote.</summary>
        public bool DescartarCargue(Usuarios usuario, long idLote)
        {
            var lote = _stagingService.ObtenerLote(idLote);

            if (lote != null && EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
            {
                var whDesc = new WebhooksService();
                int? idIntegDesc = whDesc.IdIntegracionDeLote(idLote);   // antes de descartar: el lote desaparece
                try { _stagingService.DescartarLote(idLote); }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, $"DescartarCargue: lote {idLote}");
                    return false;
                }
                whDesc.NotificarCargueDescartado(idIntegDesc, lote, usuario?.Id ?? 0);
                new IntegracionesActividadService().Resolver(idIntegDesc, idLote, "Descartado", usuario?.Id ?? 0);

                new AuditoriaService().RegistrarCambio(
                    AuditoriaTipo.Cargues, AuditoriaAccion.Descartar, "CargueLote", idLote.ToString(),
                    $"Descartó el cargue en revisión de '{lote.NombreEmpresa}' — año {lote.Anio}, {(lote.Modo == 0 ? "ejecución" : "histórico")}, escenario {lote.IdEscenario}, archivo '{lote.NombreArchivo}'.",
                    null,
                    new { empresa = lote.NombreEmpresa, anio = lote.Anio, modo = lote.Modo, escenario = lote.IdEscenario, archivo = lote.NombreArchivo, totalErrores = lote.TotalErrores, totalAdvertencias = lote.TotalAdvertencias },
                    lote.IdEmpresa, entidadNombre: lote.NombreEmpresa, severidad: AuditoriaSeveridad.Advertencia);
            }
            return true;
        }

        /// <summary>
        /// Descarte silencioso cuando el usuario sale de la revisión sin confirmar ni descartar. Solo descarta
        /// lotes del propio usuario y no confirmados. Devuelve true si descartó algo.
        /// </summary>
        public bool AbandonarCargue(Usuarios usuario, long idLote)
        {
            var lote = _stagingService.ObtenerLote(idLote);

            if (usuario != null && lote != null && lote.IdUsuario == usuario.Id
                && lote.Estado != CargueLoteEstado.Confirmado
                && EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa))
            {
                try
                {
                    _stagingService.DescartarLote(idLote);
                    new AuditoriaService().RegistrarCambio(
                        AuditoriaTipo.Cargues, AuditoriaAccion.Descartar, "CargueLote", idLote.ToString(),
                        $"Cargue en revisión descartado automáticamente al salir de la pantalla: '{lote.NombreEmpresa}' — año {lote.Anio}, {(lote.Modo == 0 ? "ejecución" : "histórico")}, escenario {lote.IdEscenario}, archivo '{lote.NombreArchivo}'.",
                        null,
                        new { empresa = lote.NombreEmpresa, anio = lote.Anio, modo = lote.Modo, escenario = lote.IdEscenario, archivo = lote.NombreArchivo, automatico = true },
                        lote.IdEmpresa, entidadNombre: lote.NombreEmpresa);
                    return true;
                }
                catch (Exception ex) { AppLogger.Error(ex, $"AbandonarCargue: lote {idLote}"); }
            }
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Helpers movidos tal cual desde DatosController (lectura del libro, escritura
        // a staging, borrado/reemplazo de Ini_*, auditoría, validación posterior).
        // ═════════════════════════════════════════════════════════════════════════
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
    }
}
