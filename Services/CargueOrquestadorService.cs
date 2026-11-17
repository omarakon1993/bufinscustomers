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
    // ─────────────────────────────────────────────────────────────────────────────
    // Modelos de entrada / salida del orquestador. Sin dependencias de HTTP ni de la
    // sesión: los usa por igual la pantalla de Cargue de Excel y, más adelante, la API.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Todo lo necesario para iniciar un cargue desde un archivo Excel.</summary>
    public class SolicitudCargue
    {
        public Usuarios Usuario { get; set; }
        public int IdEmpresa { get; set; }
        public int Anio { get; set; }
        /// <summary>"ejecucion" o "historico".</summary>
        public string Modo { get; set; }
        public int IdEscenario { get; set; } = 1;
        /// <summary>Nombre original del archivo (con o sin ruta).</summary>
        public string NombreArchivo { get; set; }
        public long LongitudArchivo { get; set; }
        public Stream Archivo { get; set; }
    }

    /// <summary>Resultado simple (válido / mensaje) de una validación previa.</summary>
    public class ResultadoCargueSimple
    {
        public bool Ok { get; set; }
        public string Mensaje { get; set; }
        public static ResultadoCargueSimple Correcto() => new ResultadoCargueSimple { Ok = true };
        public static ResultadoCargueSimple Error(string mensaje) => new ResultadoCargueSimple { Ok = false, Mensaje = mensaje };
    }

    public enum ProcesoCargueEstado
    {
        /// <summary>El archivo quedó en staging, validado y listo para revisar/confirmar.</summary>
        EnRevision,
        /// <summary>Ya había un cargue activo para la misma llave: se devuelve su <c>IdLote</c>.</summary>
        LoteActivoExistente,
        PlantillaIncompleta,
        ErrorEnHoja,
        SinFilasDelAnio,
        ErrorValidacion,
        ErrorInesperado
    }

    public class ResultadoProcesoCargue
    {
        public ProcesoCargueEstado Estado { get; set; }
        public long IdLote { get; set; }
        /// <summary>Mensaje listo para el usuario (ya traducido cuando aplica).</summary>
        public string Mensaje { get; set; }
        /// <summary>Detalle por hoja y totales (null cuando no hubo lectura del libro).</summary>
        public ResultadoCargaExcel Resultado { get; set; }
        public string NombreArchivo { get; set; }
        /// <summary>Bitácora de la importación (vacía si no se llegó a leer el libro).</summary>
        public string LogImportacion { get; set; }
    }

    public enum ConfirmacionCargueEstado
    {
        LoteNoExiste,
        SinPermisoPlantilla,
        NoConfirmable,
        ErrorImpacto,
        CargaVacia,
        Bloqueado,
        FaltaConfirmarReemplazo,
        LoteNoDisponible,
        Error,
        Confirmado,
        /// <summary>Entorno de pruebas (sandbox): todas las comprobaciones pasaron y se habría confirmado, pero NO se tocó ningún dato.</summary>
        Simulado
    }

    public class ResultadoConfirmacionCargue
    {
        public ConfirmacionCargueEstado Estado { get; set; }
        public string Mensaje { get; set; }
        public ResultadoCargaExcel Resultado { get; set; }
        public CargueLote Lote { get; set; }
        public bool Confirmado => Estado == ConfirmacionCargueEstado.Confirmado;
    }

    public enum HojaApiEstado
    {
        Ok,
        /// <summary>El lote no existe, no es accesible o ya está confirmado/descartado.</summary>
        LoteNoDisponible,
        HojaNoSoportada,
        ColumnaDesconocida,
        DemasiadasFilas,
        /// <summary>Un valor no se pudo convertir (número, fecha, Sí/No) o la tabla no existe.</summary>
        DatosInvalidos
    }

    public class ResultadoHojaApi
    {
        public HojaApiEstado Estado { get; set; }
        public string Mensaje { get; set; }
        public string Hoja { get; set; }
        public string Tabla { get; set; }
        public int FilasRecibidas { get; set; }
        public int FilasGuardadas { get; set; }
        public int FilasOmitidasPorAnio { get; set; }
        /// <summary>Filas que tiene la hoja en el lote después de esta llamada.</summary>
        public int FilasEnHoja { get; set; }
    }

    public enum ValidacionApiEstado { Ok, LoteNoDisponible, HojasFaltantes, SinFilasDelAnio, ErrorValidacion }

    public class ResultadoValidacionApi
    {
        public ValidacionApiEstado Estado { get; set; }
        public string Mensaje { get; set; }
        public List<string> HojasFaltantes { get; set; } = new List<string>();
        public CargueLote Lote { get; set; }
    }

    /// <summary>
    /// Orquestador único del cargue de datos: staging → validación → impacto → confirmación → descarte.
    /// No conoce la sesión ni el HTTP; el llamador (pantalla de Excel o API) le pasa el usuario, el archivo
    /// y, si quiere mensajes traducidos, un traductor (mismo patrón de <c>IASolicitud.Traducir</c>).
    /// Toda la lógica (staging, reglas del SP, regla de oro, snapshot, auditoría) vive aquí, una sola vez.
    /// </summary>
    public class CargueOrquestadorService : BaseService
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
        // 5. Envío por tandas (API en JSON): abrir lote → agregar hojas → validar
        //    El resto del flujo (revisión, confirmar, descartar) es el de siempre.
        // ═════════════════════════════════════════════════════════════════════════
        private const int MaxFilasPorLlamada = 50000;

        private static readonly HashSet<string> ColumnasDeSistemaApi = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Llave", "IdEmpresa_Log", "IdEscenario", "IdUsuarioCargue_Log", "FechaCargue_Log", "Historico_Log",
            "IdUsuarioEjecucion_Log", "FechaEjecucion_Log", "Observacion_Log", "IdLote", "NumeroFilaExcel"
        };

        /// <summary>Abre un lote vacío en staging (sin archivo) para llenarlo por hojas. Misma llave y mismas reglas que un cargue de Excel.</summary>
        public ResultadoProcesoCargue IniciarLoteSinArchivo(SolicitudCargue s, Action<long> alCrearLote = null)
        {
            bool modoEjecucion = string.Equals(s.Modo, "ejecucion", StringComparison.OrdinalIgnoreCase);
            byte modo = modoEjecucion ? (byte)0 : (byte)1;
            int idUsuario = s.Usuario?.Id ?? 0;
            string referencia = string.IsNullOrWhiteSpace(s.NombreArchivo) ? "API" : s.NombreArchivo.Trim();
            if (referencia.Length > 200) referencia = referencia.Substring(0, 200);

            _stagingService.PurgarLotesVencidosSiToca();
            try { _stagingService.DescartarLotesPendientesDeUsuario(idUsuario, s.IdEmpresa); }
            catch (Exception exDesc) { AppLogger.Error(exDesc, "IniciarLoteSinArchivo.DescartarLotesPendientesDeUsuario"); }

            string nombreEmpresa = _empresaService.ObtenerEmpresas().Find(e => e.Id == s.IdEmpresa)?.Nombre ?? "Desconocida";
            string nombreUsuario = ((s.Usuario?.Nombre ?? "") + " " + (s.Usuario?.Apellidos ?? "")).Trim();

            var (creado, idLote, mensaje) = _stagingService.CrearLote(s.IdEmpresa, nombreEmpresa, s.Anio, modo, s.IdEscenario, idUsuario, nombreUsuario, referencia);
            if (!creado)
                return new ResultadoProcesoCargue { Estado = ProcesoCargueEstado.LoteActivoExistente, IdLote = idLote, Mensaje = mensaje, NombreArchivo = referencia };

            alCrearLote?.Invoke(idLote);
            return new ResultadoProcesoCargue { Estado = ProcesoCargueEstado.EnRevision, IdLote = idLote, NombreArchivo = referencia };
        }

        /// <summary>
        /// Agrega (o reemplaza) las filas de una hoja en el staging del lote. Las filas llegan ya como valores simples
        /// (número → double, texto → string, Sí/No → bool, vacío → null). Todo o nada: si una fila no se puede guardar, no queda ninguna.
        /// El lote vuelve a «EnValidacion»: hay que volver a validarlo antes de poder confirmarlo.
        /// </summary>
        public ResultadoHojaApi AgregarFilasHoja(Usuarios usuario, long idLote, string hoja, IReadOnlyList<IDictionary<string, object>> filas, bool reemplazar)
        {
            var res = new ResultadoHojaApi { Hoja = hoja, FilasRecibidas = filas?.Count ?? 0 };
            var lote = _stagingService.ObtenerLote(idLote);
            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)
                || lote.Estado == CargueLoteEstado.Confirmado || lote.Estado == CargueLoteEstado.Descartado)
            { res.Estado = HojaApiEstado.LoteNoDisponible; return res; }

            var mapeo = TablasCargueHelper.MapeoParaEmpresa(lote.IdEmpresa);
            string clave = NormalizarNombre(hoja ?? "");
            if (!mapeo.TryGetValue(clave, out string tablaIni))
            { res.Estado = HojaApiEstado.HojaNoSoportada; res.Mensaje = hoja; return res; }
            res.Hoja = clave; res.Tabla = tablaIni;

            if (res.FilasRecibidas > MaxFilasPorLlamada)
            { res.Estado = HojaApiEstado.DemasiadasFilas; res.Mensaje = MaxFilasPorLlamada.ToString(); return res; }

            string nombreStaging = TablasCargueHelper.NombreStaging(tablaIni);

            // Columnas de negocio de la hoja (esquema real de la tabla de staging); los nombres que llegan se resuelven contra él.
            var columnas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cn0 = new SqlConnection(CadenaConexion))
            using (var cmd0 = new SqlCommand(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t AND COLUMNPROPERTY(OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + '.' + QUOTENAME(TABLE_NAME)), COLUMN_NAME, 'IsIdentity') = 0", cn0))
            {
                cmd0.Parameters.AddWithValue("@t", nombreStaging);
                cn0.Open();
                using (var r = cmd0.ExecuteReader())
                    while (r.Read())
                    {
                        string nombre = r.GetString(0);
                        if (!ColumnasDeSistemaApi.Contains(nombre)) columnas[nombre] = nombre;
                    }
            }
            if (columnas.Count == 0)
            { res.Estado = HojaApiEstado.DatosInvalidos; res.Mensaje = "La hoja no tiene una tabla de destino disponible."; return res; }
            if (columnas.ContainsKey("Año")) columnas["Anio"] = "Año";   // alias sin tilde

            // Construye el DataTable con los mismos tipos que entrega la lectura de Excel (double / string).
            var usadas = new List<string>();
            foreach (var fila in filas)
                foreach (var k in fila.Keys)
                {
                    if (!columnas.TryGetValue((k ?? "").Trim(), out string canon))
                    { res.Estado = HojaApiEstado.ColumnaDesconocida; res.Mensaje = k; return res; }
                    if (!usadas.Contains(canon)) usadas.Add(canon);
                }

            var dt = new DataTable(clave);
            foreach (var c in usadas) dt.Columns.Add(c, typeof(object));
            string anioTexto = lote.Anio.ToString();
            int omitidas = 0;
            foreach (var fila in filas)
            {
                var dr = dt.NewRow();
                foreach (DataColumn col in dt.Columns) dr[col] = "";
                bool vacia = true;
                foreach (var kv in fila)
                {
                    string canon = columnas[(kv.Key ?? "").Trim()];
                    object v = kv.Value;
                    object celda;
                    if (v is double d) { celda = d; vacia = false; }
                    else
                    {
                        string txt = v == null ? "" : (v is bool b ? (b ? "true" : "false") : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture).Trim());
                        if (txt.Length > 0) vacia = false;
                        celda = txt;
                    }
                    dr[canon] = celda;
                }
                if (vacia) continue;

                // Igual que el Excel: una fila de otro año se omite en silencio (y se cuenta para avisar).
                if (usadas.Contains("Año"))
                {
                    string a = dr["Año"] is double da ? ((long)da).ToString() : Convert.ToString(dr["Año"]).Trim();
                    if (a.Length > 0 && a != anioTexto) { omitidas++; continue; }
                }
                dt.Rows.Add(dr);
            }
            res.FilasOmitidasPorAnio = omitidas;

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        int existentes = 0;
                        if (!reemplazar)
                            using (var cmdC = new SqlCommand($"SELECT COUNT(1) FROM dbo.[{nombreStaging}] WHERE IdLote = @L", conn, tx))
                            {
                                cmdC.Parameters.AddWithValue("@L", idLote);
                                existentes = Convert.ToInt32(cmdC.ExecuteScalar());
                            }
                        else
                            using (var cmdD = new SqlCommand($"DELETE FROM dbo.[{nombreStaging}] WHERE IdLote = @L", conn, tx))
                            {
                                cmdD.Parameters.AddWithValue("@L", idLote);
                                cmdD.ExecuteNonQuery();
                            }

                        if (dt.Rows.Count > 0)
                        {
                            UltimoMensajeHoja = null;
                            if (!GuardarEnIni(conn, dt, nombreStaging, lote.IdEmpresa, usuario?.Id ?? 0, lote.IdEscenario, historicoLog: lote.Modo, tx: tx, idLote: idLote, numeroFilaBase: existentes + 2))
                            {
                                tx.Rollback();
                                res.Estado = HojaApiEstado.DatosInvalidos;
                                res.Mensaje = (UltimoMensajeHoja ?? "").Replace("Staging_Ini_", "Ini_");
                                return res;
                            }
                        }

                        int total = existentes + dt.Rows.Count;
                        using (var cmdH = new SqlCommand(@"
                            UPDATE dbo.CarguesLotesHojas SET Filas = @F, Declarada = GETDATE() WHERE IdLote = @L AND Hoja = @H;
                            IF @@ROWCOUNT = 0 INSERT INTO dbo.CarguesLotesHojas (IdLote, Hoja, Filas) VALUES (@L, @H, @F);", conn, tx))
                        {
                            cmdH.Parameters.AddWithValue("@L", idLote);
                            cmdH.Parameters.AddWithValue("@H", clave);
                            cmdH.Parameters.AddWithValue("@F", total);
                            cmdH.ExecuteNonQuery();
                        }

                        _stagingService.ReabrirParaEdicion(conn, tx, idLote);
                        tx.Commit();

                        res.Estado = HojaApiEstado.Ok;
                        res.FilasGuardadas = dt.Rows.Count;
                        res.FilasEnHoja = total;
                        return res;
                    }
                    catch (Exception ex)
                    {
                        Helpers.TransaccionHelper.RollbackSeguro(tx);
                        AppLogger.Error(ex, "CargueOrquestador.AgregarFilasHoja");
                        res.Estado = HojaApiEstado.DatosInvalidos;
                        res.Mensaje = "No se pudo guardar la hoja. Inténtelo de nuevo o contacte a soporte.";
                        return res;
                    }
                }
            }
        }

        /// <summary>
        /// Cierra el envío por hojas y lo valida: exige que se haya declarado CADA hoja de la empresa (aunque sea vacía; una hoja olvidada
        /// borraría sus datos al confirmar), que el archivo traiga al menos una fila del año, y corre las mismas reglas del Excel.
        /// </summary>
        public ResultadoValidacionApi ValidarLoteApi(Usuarios usuario, long idLote)
        {
            var lote = _stagingService.ObtenerLote(idLote);
            if (lote == null || !EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)
                || lote.Estado == CargueLoteEstado.Confirmado || lote.Estado == CargueLoteEstado.Descartado)
                return new ResultadoValidacionApi { Estado = ValidacionApiEstado.LoteNoDisponible };

            var declaradas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT Hoja, Filas FROM dbo.CarguesLotesHojas WHERE IdLote = @L", cn))
            {
                cmd.Parameters.AddWithValue("@L", idLote);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) declaradas[r.GetString(0)] = r.GetInt32(1);
            }

            var faltantes = TablasCargueHelper.MapeoParaEmpresa(lote.IdEmpresa).Keys
                .Where(k => !declaradas.ContainsKey(NormalizarNombre(k))).ToList();
            if (faltantes.Count > 0)
                return new ResultadoValidacionApi
                {
                    Estado = ValidacionApiEstado.HojasFaltantes, HojasFaltantes = faltantes, Lote = lote,
                    Mensaje = "Faltan hojas por enviar (envíelas aunque vayan sin filas): " + string.Join(", ", faltantes) + "."
                };

            int totalFilas = declaradas.Values.Sum();
            if (totalFilas == 0)
                return new ResultadoValidacionApi
                {
                    Estado = ValidacionApiEstado.SinFilasDelAnio, Lote = lote,
                    Mensaje = string.Format(T("Datos_CargueSinFilasDelAnio"), lote.Anio)
                };

            _stagingService.ActualizarTotalFilas(idLote, totalFilas);
            var (ok, mensaje, _, _) = _stagingService.ValidarLote(idLote);
            if (!ok)
            {
                AppLogger.Error($"sp_ValidarCargueStaging falló para el lote {idLote}: {mensaje}", contexto: "ValidarLoteApi");
                return new ResultadoValidacionApi { Estado = ValidacionApiEstado.ErrorValidacion, Mensaje = mensaje, Lote = lote };
            }
            return new ResultadoValidacionApi { Estado = ValidacionApiEstado.Ok, Lote = _stagingService.ObtenerLote(idLote) };
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 6. Aviso a los revisores de la empresa cuando la API deja un cargue esperando
        // ═════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Avisa (campana) a los administradores de la empresa que un cargue enviado por la API espera revisión, o que llegó con errores.
        /// Va en español: los destinatarios son usuarios de la empresa, no el cliente que llama a la API. Nunca lanza.
        /// </summary>
        public void NotificarRevisores(CargueLote lote, string nombreIntegracion, int idUsuarioTecnico)
        {
            try
            {
                if (lote == null) return;
                bool conErrores = lote.Estado == CargueLoteEstado.ConErrores;
                if (!conErrores && !lote.PuedeConfirmar) return;

                var es = new System.Globalization.CultureInfo("es-CO");
                string Txt(string k) => bufinscustomers.App_GlobalResources.Strings.ResourceManager.GetString(k, es) ?? k;
                string modo = lote.Modo == 0 ? Txt("RevisarCargue_ModoEjecucion") : Txt("RevisarCargue_ModoHistorico");
                string titulo = Txt(conErrores ? "Notif_CargueApiConErrores" : "Notif_CargueApiPendiente");
                string mensaje = string.Format(Txt(conErrores ? "Notif_CargueApiConErroresMsg" : "Notif_CargueApiPendienteMsg"),
                    nombreIntegracion, lote.NombreEmpresa, lote.Anio, modo, lote.TotalFilas.ToString("N0"), lote.TotalErrores, lote.TotalAdvertencias);
                string url = "/Datos/RevisarCargue?idLote=" + lote.IdLote;

                var destinatarios = new List<int>();
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Id FROM dbo.Usuarios WHERE IdEmpresa = @E AND Admin = 1 AND Id <> @T", cn))
                {
                    cmd.Parameters.AddWithValue("@E", lote.IdEmpresa);
                    cmd.Parameters.AddWithValue("@T", idUsuarioTecnico);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) destinatarios.Add(r.GetInt32(0));
                }
                var notif = new NotificacionesService();
                foreach (var id in destinatarios)
                    notif.Crear(id, titulo, mensaje, conErrores ? "warning" : "info", url);
            }
            catch (Exception ex) { AppLogger.Warn("CargueOrquestador.NotificarRevisores: " + ex.Message); }
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
                    UltimoMensajeHoja = $"ERROR NUMÉRICO en '{tabla.TableName}':\n\n{errorNumerico}";
                    MostrarDescargaLog = true;
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
                    UltimoMensajeHoja = $"ERROR DE VALIDACIÓN en '{tabla.TableName}':\n\n{errorValidacion}";
                    MostrarDescargaLog = true;

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

                    UltimoMensajeHoja = $"ERROR AL INSERTAR en '{tabla.TableName}':\n\n{detalleError}";
                    MostrarDescargaLog = true;

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

                UltimoMensajeHoja = $"ERROR GENERAL al guardar en '{tabla.TableName}':\n{ex.Message}";
                MostrarDescargaLog = true;

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

        // Tablas Z_ que se usan en GuardarEnSQLServer pero no tienen mapeo Z_ → Ini_
        private static readonly string[] _tablasZSinMapeo = { "Z_TablaPUC" };

        // Nombres (normalizados) de las hojas que el cargue reconoce y procesa para una empresa:
        // las de su mapeo Z_ → Ini_ (estándar + personalizadas) más las Z_ que van directo a SQL.
        // Cualquier otra hoja del libro (p. ej. la hoja auxiliar "Datos" con las listas de
        // validación de la plantilla) se ignora por completo: no se valida ni se carga.
        private static HashSet<string> HojasCargueReconocidas(IReadOnlyDictionary<string, string> mapeoEmpresa) =>
            new HashSet<string>(mapeoEmpresa.Keys.Concat(_tablasZSinMapeo), StringComparer.OrdinalIgnoreCase);

        private void LimpiarTablasEnError(SqlConnection conn, int idEmpresa)
        {
            if (conn == null || conn.State != ConnectionState.Open) return;

            // Limpiar todas las tablas Z_: las del mapeo Ini_ + las que no tienen mapeo
            var tablasALimpiar = TablasCargueHelper.TablasZ.Concat(_tablasZSinMapeo);

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

        private DataTable LeerHojaEnDataTable(IXLWorksheet hoja, DetalleCargaHojaExcel detalle, string filtroAnio = null)
        {
            int totalCols = hoja.LastColumnUsed()?.ColumnNumber() ?? 0;
            int totalRows = hoja.LastRowUsed()?.RowNumber() ?? 0;

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
                if (!string.IsNullOrWhiteSpace(hoja.Cell(1, col).GetFormattedString()))
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
                if (!string.IsNullOrWhiteSpace(hoja.Cell(1, col).GetFormattedString().Trim()))
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
                    var header = hoja.Cell(1, col).GetFormattedString()?.Trim() ?? "";
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

            var dt = new DataTable(nombreTabla);
            for (int col = 1; col <= columnasValidas; col++)
                dt.Columns.Add(hoja.Cell(1, col).GetFormattedString().Trim(), typeof(object));

            int filasVaciasConsecutivas = 0;
            int filasIgnoradasPorAnio = 0;
            for (int row = 2; row <= totalRows; row++)
            {
                bool filaVacia = true;
                var dr = dt.NewRow();
                for (int col = 1; col <= columnasValidas; col++)
                {
                    var cellValue = hoja.Cell(row, col).Value;
                    object drVal;
                    if (cellValue.IsNumber)
                    {
                        // Celda numérica: guardar como double nativo — sin conversión a texto
                        // para evitar toda ambigüedad de separadores decimales/miles
                        drVal = cellValue.GetNumber();
                        filaVacia = false; // cualquier número (incluso cero) es dato real
                    }
                    else
                    {
                        string valorStr = cellValue.IsBlank ? "" : cellValue.ToString().Trim();
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

        private void EliminarAnosHistoricosDeIni(SqlConnection conn, int anio, int idEmpresa, int idEscenario, SqlTransaction tx = null)
        {
            foreach (var tablaIni in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                using (var cmd = new SqlCommand(
                    $"DELETE FROM [dbo].[{tablaIni}] WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Ano AND ISNULL(IdEscenario,1) = @IdEscenario", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Ano", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.ExecuteNonQuery();
                }
                LogToFile($"Eliminados datos históricos de {tablaIni} año {anio} escenario {idEscenario} para empresa {idEmpresa}");
            }
        }

        private void EliminarEjecucionDeIni(SqlConnection conn, int anio, int idEmpresa, int idEscenario, SqlTransaction tx = null)
        {
            foreach (var tablaIni in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                using (var cmd = new SqlCommand(
                    $"DELETE FROM [dbo].[{tablaIni}] WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Ano AND Historico_Log = 0 AND ISNULL(IdEscenario,1) = @IdEscenario", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Ano", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.ExecuteNonQuery();
                }
                LogToFile($"Eliminados datos de ejecución de {tablaIni} año {anio} escenario {idEscenario} para empresa {idEmpresa}");
            }
        }

        /// <summary>
        /// Copia un DataTable leído del Excel a la tabla destino indicada (esquema resuelto
        /// dinámicamente vía INFORMATION_SCHEMA.COLUMNS, así que sirve tanto para las tablas
        /// Ini_* reales como para su espejo Staging_Ini_* — ver validación en dos pasos en
        /// <see cref="bufinscustomers.Services.CargueStagingService"/>). Cuando <paramref name="idLote"/>
        /// tiene valor y la tabla destino tiene columna IdLote/NumeroFilaExcel (solo las de staging
        /// las tienen), se completan también esas dos columnas.
        /// </summary>
        private bool GuardarEnIni(SqlConnection conn, DataTable dtExcel, string nombreTablaIni, int idEmpresa, int idUsuario, int idEscenario, byte historicoLog = 1, SqlTransaction tx = null, long? idLote = null, int numeroFilaBase = 2)
        {
            try
            {
                LogToFile($"========== INICIO GUARDADO HISTÓRICO: {nombreTablaIni} ==========");
                LogToFile($"Filas en DataTable: {dtExcel.Rows.Count}");

                // Obtener esquema de la tabla Ini_ (nombre y tipo)
                var columnasIni = new List<string>();
                var columnasNumericas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Fecha / Sí-No: solo existen en hojas diseñadas en el Gestor de hojas de cargue (las
                // tablas anteriores no tienen columnas de negocio de estos tipos). Se convierten
                // explícitamente para no depender de la conversión implícita de texto de SqlBulkCopy.
                var columnasFecha = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var columnasBit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                            else if (tipo == "date" || tipo == "datetime" || tipo == "datetime2" || tipo == "smalldatetime")
                                columnasFecha.Add(col);
                            else if (tipo == "bit")
                                columnasBit.Add(col);
                        }
                    }
                }

                if (columnasIni.Count == 0)
                {
                    LogToFile($"Tabla {nombreTablaIni} no encontrada en BD");
                    UltimoMensajeHoja = $"No se encontró la tabla '{nombreTablaIni}' en la base de datos.";
                    MostrarDescargaLog = true;
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

                int indiceFila = 0;
                foreach (DataRow srcRow in dtExcel.Rows)
                {
                    var destRow = dtDest.NewRow();
                    int filaExcelActual = numeroFilaBase + indiceFila++;

                    // Valores de referencia para la Llave
                    string cuenta  = colsExcel.TryGetValue("Cuenta", out int value)  ? srcRow[value].ToString()  : "";
                    string empresa = colsExcel.TryGetValue("Empresa", out int value1) ? srcRow[value1].ToString() : "";
                    string ano     = colsExcel.TryGetValue("Año", out int value2)     ? srcRow[value2].ToString() :
                                     colsExcel.TryGetValue("Anio", out int value3)    ? srcRow[value3].ToString() : "";
                    string mes     = colsExcel.TryGetValue("Mes", out int value4)     ? srcRow[value4].ToString()     : "";

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
                            case "IdEscenario":
                                destRow[colIni] = idEscenario;
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
                            case "IdLote":
                                destRow[colIni] = idLote.HasValue ? (object)idLote.Value : DBNull.Value;
                                break;
                            case "NumeroFilaExcel":
                                destRow[colIni] = filaExcelActual;
                                break;
                            case "IdUsuarioEjecucion_Log":
                            case "FechaEjecucion_Log":
                            case "Observacion_Log":
                                destRow[colIni] = DBNull.Value;
                                break;
                            default:
                                if (colsExcel.TryGetValue(colIni, out int value5))
                                {
                                    var cellValue = srcRow[value5];
                                    if (columnasNumericas.Contains(colIni))
                                    {
                                        if (cellValue is string sValIni && !string.IsNullOrWhiteSpace(sValIni))
                                        {
                                            string razonIni = DetectarErrorNumerico(sValIni);
                                            if (razonIni != null)
                                            {
                                                int filaExcel = filaExcelActual;
                                                string errorFmt =
                                                    $"Fila {filaExcel}, Columna '{colIni}': {razonIni}. " +
                                                    $"Revise la hoja completa por errores similares antes de reintentar.";
                                                LogToFile($"ERROR NUMÉRICO en {nombreTablaIni}: Fila {filaExcel}, Columna '{colIni}', valor '{sValIni}'");
                                                UltimoMensajeHoja = $"ERROR NUMÉRICO en '{nombreTablaIni}':\n\n{errorFmt}";
                                                MostrarDescargaLog = true;
                                                return false;
                                            }
                                            LogToFile($"Advertencia: Columna '{colIni}' fila {filaExcelActual} " +
                                                      $"tiene valor texto '{sValIni}' — se parseará como número. " +
                                                      $"Considere usar formato Número en Excel.");
                                        }
                                        destRow[colIni] = (object)ExtraerDecimal(cellValue);
                                    }
                                    else if (columnasFecha.Contains(colIni) || columnasBit.Contains(colIni))
                                    {
                                        bool esFecha = columnasFecha.Contains(colIni);
                                        object convertido = esFecha ? ConvertirCeldaFecha(cellValue) : ConvertirCeldaBit(cellValue);
                                        if (convertido == null)
                                        {
                                            int filaExcel = filaExcelActual;
                                            string razon = esFecha
                                                ? "no es una fecha válida (use el formato de fecha de Excel o AAAA-MM-DD)"
                                                : "no es un valor Sí/No válido (use 1/0, Sí/No o Verdadero/Falso)";
                                            LogToFile($"ERROR DE FORMATO en {nombreTablaIni}: Fila {filaExcel}, Columna '{colIni}', valor '{cellValue}'");
                                            UltimoMensajeHoja = $"ERROR DE FORMATO en '{nombreTablaIni}':\n\nFila {filaExcel}, Columna '{colIni}': el valor '{cellValue}' {razon}.";
                                            MostrarDescargaLog = true;
                                            return false;
                                        }
                                        destRow[colIni] = convertido;
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

                // En staging (idLote) se verifican las FK: ninguna fila puede apuntar a un lote inexistente.
                var opcionesBulk = idLote.HasValue ? SqlBulkCopyOptions.CheckConstraints : SqlBulkCopyOptions.Default;
                using (var bulk = new SqlBulkCopy(conn, opcionesBulk, tx))
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
                UltimoMensajeHoja = $"ERROR al guardar en '{nombreTablaIni}': {ex.Message}";
                MostrarDescargaLog = true;
                return false;
            }
        }

        // ─── Existing helper methods ──────────────────────────────────────────────

        /// <summary>
        /// Celda → fecha para una columna DATE/DATETIME (hojas del Gestor de hojas de cargue).
        /// Vacía → DBNull; número → fecha serial de Excel; texto → cultura actual o AAAA-MM-DD.
        /// Devuelve <c>null</c> si el valor no es una fecha.
        /// </summary>
        private static object ConvertirCeldaFecha(object cellValue)
        {
            if (cellValue == null || cellValue == DBNull.Value) return DBNull.Value;
            if (cellValue is DateTime dtv) return dtv;
            if (cellValue is double d)
            {
                try { return DateTime.FromOADate(d); } catch { return null; }
            }
            string s = cellValue.ToString().Trim();
            if (s.Length == 0) return DBNull.Value;
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd" },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime iso))
                return iso;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out DateTime local))
                return local;
            return null;
        }

        /// <summary>
        /// Celda → Sí/No para una columna BIT (hojas del Gestor de hojas de cargue).
        /// Vacía → DBNull; acepta 1/0, Sí/No, Si, Verdadero/Falso, True/False, X. Devuelve <c>null</c> si no es válido.
        /// </summary>
        private static object ConvertirCeldaBit(object cellValue)
        {
            if (cellValue == null || cellValue == DBNull.Value) return DBNull.Value;
            if (cellValue is double d) return d != 0;
            string s = cellValue.ToString().Trim().ToLowerInvariant();
            if (s.Length == 0) return DBNull.Value;
            switch (s)
            {
                case "1": case "si": case "sí": case "s": case "x": case "true": case "verdadero": case "yes": case "y":
                    return true;
                case "0": case "no": case "n": case "false": case "falso":
                    return false;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Convierte cualquier valor de celda de Excel a decimal exacto para SqlBulkCopy.
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

        /// <summary>
        /// Arma el detalle de auditoría de un cargue confirmado: una fila por tabla Ini_ con las filas que había
        /// (y se reemplazaron) y las que quedaron cargadas.
        /// </summary>
        private static List<CargueDetalleFila> ConstruirDetalleCargue(CargueLote lote, Usuarios usuario, int? idAuditoriaCargue,
            int idHistorial, List<(string Tabla, int Filas)> antes, List<(string NombreHoja, string NombreTabla, int Filas)> despues)
        {
            var filas = new List<CargueDetalleFila>();
            var tablas = antes.Select(a => a.Tabla).Union(despues.Select(d => d.NombreTabla), StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var tabla in tablas)
            {
                int nAntes = antes.Where(a => string.Equals(a.Tabla, tabla, StringComparison.OrdinalIgnoreCase)).Sum(a => a.Filas);
                var nuevas = despues.Where(d => string.Equals(d.NombreTabla, tabla, StringComparison.OrdinalIgnoreCase)).ToList();
                int nNuevas = nuevas.Sum(d => d.Filas);
                if (nAntes == 0 && nNuevas == 0) continue;
                filas.Add(new CargueDetalleFila
                {
                    IdAuditoriaCargue = idAuditoriaCargue, Origen = CargueOrigen.Cargue,
                    IdEmpresa = lote.IdEmpresa, NombreEmpresa = lote.NombreEmpresa,
                    Anio = lote.Anio, Modo = lote.Modo, IdEscenario = lote.IdEscenario,
                    IdUsuario = usuario?.Id, NombreUsuario = ((usuario?.Nombre ?? "") + " " + (usuario?.Apellidos ?? "")).Trim(),
                    NombreArchivo = lote.NombreArchivo, IdLote = lote.IdLote,
                    IdHistorial = idHistorial > 0 ? idHistorial : (int?)null,
                    NombreHoja = nuevas.FirstOrDefault().NombreHoja, NombreTabla = tabla,
                    FilasAntes = nAntes, FilasNuevas = nNuevas
                });
            }
            return filas;
        }

        /// <summary>Registra el cargue en AuditoriaCargues y devuelve su Id (null si no hay usuario en sesión).</summary>
        private int? RegistrarAuditoria(SqlConnection conn, Usuarios usuario, string nombreArchivo, int idEmpresaArchivo, int idEscenario, SqlTransaction tx = null)
        {
            if (usuario == null) return null;

            string nombreEmpresa = _empresaService.ObtenerEmpresas()
                                                   .FirstOrDefault(e => e.Id == idEmpresaArchivo)?.Nombre ?? "Desconocida";

            // IdEscenario es una columna incremental (ver Sql/004_AuditoriaCargues_AddEscenario.sql);
            // si aún no existe en la BD, el INSERT explícito fallaría — se intenta primero con la
            // columna y, si la BD todavía no la tiene, se cae al INSERT sin ella.
            string sqlConEscenario = @"
        INSERT INTO dbo.AuditoriaCargues (FechaCargue, IdUsuario, Usuario, IdEmpresa, NombreEmpresa, NombreArchivo, IdEscenario)
        VALUES (@Fecha, @IdUsuario, @Usuario, @IdEmpresa, @NombreEmpresa, @NombreArchivo, @IdEscenario);
        SELECT CAST(SCOPE_IDENTITY() AS INT);
    ";
            string sqlSinEscenario = @"
        INSERT INTO dbo.AuditoriaCargues (FechaCargue, IdUsuario, Usuario, IdEmpresa, NombreEmpresa, NombreArchivo)
        VALUES (@Fecha, @IdUsuario, @Usuario, @IdEmpresa, @NombreEmpresa, @NombreArchivo);
        SELECT CAST(SCOPE_IDENTITY() AS INT);
    ";

            int? EjecutarInsert(string sql, bool incluirEscenario)
            {
                using (var cmd = new SqlCommand(sql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@Fecha", DateTime.Now);
                    cmd.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                    cmd.Parameters.AddWithValue("@Usuario", usuario.Nombre + " " + usuario.Apellidos ?? "");
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresaArchivo);
                    cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                    cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);
                    if (incluirEscenario)
                        cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);

                    var id = cmd.ExecuteScalar();
                    return id == null || id == DBNull.Value ? (int?)null : Convert.ToInt32(id);
                }
            }

            try
            {
                return EjecutarInsert(sqlConEscenario, incluirEscenario: true);
            }
            catch (SqlException ex) when (ex.Message.IndexOf("IdEscenario", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return EjecutarInsert(sqlSinEscenario, incluirEscenario: false);
            }
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
