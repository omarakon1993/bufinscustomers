using bufinscustomers.Helpers;
using bufinscustomers.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace bufinscustomers.Services
{
    public class HistorialVersionesCarguesService : BaseService
    {
        // Versiones que se conservan por llave de cargue (empresa + año + modo + escenario). Al
        // superarlo, el cargue purga la más antigua junto con sus snapshots. La vista muestra este
        // número en el banner de límite vía HistorialVersionesPageViewModel.MaxVersiones.
        // (Antes se llamaba MaxVersionesPorEscenario — renombrado para no chocar con el concepto
        // de negocio "Escenario 1/2" introducido después: aquí "escenario" solo significaba
        // "la combinación empresa+año+modo", no tiene relación con Models.Escenario.)
        public const int MaxVersionesPorLlaveCargue = 2;

        // Tablas versionadas: las de las hojas de la empresa (Helpers/TablasCargueHelper.cs →
        // TablasIniParaEmpresa, catálogo del Gestor de hojas de cargue). El rollback exige el snapshot
        // de cada hoja vigente desde antes de la versión (HojaCargue.VigenteDesdePara).

        /// <summary>
        /// Predicado único de lectura/borrado sobre una tabla Ini_ para (empresa, año, modo,
        /// escenario). Antes este WHERE estaba duplicado literalmente 4 veces en este archivo;
        /// unificado aquí para que agregar una dimensión nueva (como Escenario) sea un solo cambio.
        /// ISNULL(IdEscenario,1) tolera la columna recién agregada en filas antiguas sin backfill.
        /// </summary>
        private static string ConstruirWhere(byte modo) =>
            modo == 0
                ? "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio AND Historico_Log = 0 AND ISNULL(IdEscenario,1) = @IdEscenario"
                : "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio AND ISNULL(IdEscenario,1) = @IdEscenario";

        // ── Llamado desde DatosController DENTRO de la transacción ───────────

        public int CrearSnapshotEnTransaccion(
            SqlConnection conn,
            SqlTransaction tx,
            int idEmpresa,
            string nombreEmpresa,
            int anio,
            byte modo,
            int idUsuario,
            string nombreUsuario,
            string nombreArchivo,
            int idEscenario = 1)
        {
            // El snapshot comparte la transacción del cargue. Un savepoint lo vuelve
            // atómico: si falla a mitad (p. ej. una tabla no se puede serializar), se
            // deshace SOLO lo del snapshot y el cargue continúa sin fila de historial,
            // en vez de dejar una versión parcial (que un rollback futuro aplicaría mal).
            const string savePoint = "PreSnapshotHistorial";
            tx.Save(savePoint);
            try
            {
                return CrearSnapshotInterno(
                    conn, tx, idEmpresa, nombreEmpresa, anio, modo,
                    idUsuario, nombreUsuario, nombreArchivo, idEscenario);
            }
            catch
            {
                // La tx puede quedar condenada por el error; si el savepoint ya no es
                // válido el caller hará rollback total. Se ignora ese caso aquí.
                try { tx.Rollback(savePoint); } catch { }
                throw;
            }
        }

        private int CrearSnapshotInterno(
            SqlConnection conn,
            SqlTransaction tx,
            int idEmpresa,
            string nombreEmpresa,
            int anio,
            byte modo,
            int idUsuario,
            string nombreUsuario,
            string nombreArchivo,
            int idEscenario)
        {
            using (var cmd = new SqlCommand(@"
                UPDATE dbo.HistorialVersionesCargues
                SET EsVersionActual = 0
                WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND ISNULL(IdEscenario,1) = @IdEscenario", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cmd.ExecuteNonQuery();
            }

            int totalFilas = ContarFilasActuales(conn, tx, idEmpresa, anio, modo, idEscenario);

            // Número global inmutable: siempre crece, nunca reinicia por mes
            int numeroVersionMes;
            using (var cmd = new SqlCommand(@"
                SELECT ISNULL(MAX(NumeroVersionMes), 0) + 1
                FROM dbo.HistorialVersionesCargues
                WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND ISNULL(IdEscenario,1) = @IdEscenario", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                numeroVersionMes = Convert.ToInt32(cmd.ExecuteScalar());
            }

            int idHistorial;
            // IdEscenario es una columna incremental (ver Sql/003_HistorialVersionesCargues_AddEscenario.sql);
            // si aún no existe en la BD, se cae al INSERT sin ella (misma técnica que
            // DatosController.RegistrarAuditoria con AuditoriaCargues).
            try
            {
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.HistorialVersionesCargues
                        (IdEmpresa, NombreEmpresa, Anio, Modo, FechaCargue,
                         IdUsuario, NombreUsuario, NombreArchivo, TotalFilas, EsVersionActual, NumeroVersionMes, IdEscenario)
                    VALUES
                        (@IdEmpresa, @NombreEmpresa, @Anio, @Modo, GETDATE(),
                         @IdUsuario, @NombreUsuario, @NombreArchivo, @TotalFilas, 1, @NumeroVersionMes, @IdEscenario);
                    SELECT SCOPE_IDENTITY();", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    cmd.Parameters.AddWithValue("@Modo", modo);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmd.Parameters.AddWithValue("@NombreUsuario", nombreUsuario);
                    cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);
                    cmd.Parameters.AddWithValue("@TotalFilas", totalFilas);
                    cmd.Parameters.AddWithValue("@NumeroVersionMes", numeroVersionMes);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    idHistorial = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (SqlException ex) when (ex.Message.IndexOf("IdEscenario", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.HistorialVersionesCargues
                        (IdEmpresa, NombreEmpresa, Anio, Modo, FechaCargue,
                         IdUsuario, NombreUsuario, NombreArchivo, TotalFilas, EsVersionActual, NumeroVersionMes)
                    VALUES
                        (@IdEmpresa, @NombreEmpresa, @Anio, @Modo, GETDATE(),
                         @IdUsuario, @NombreUsuario, @NombreArchivo, @TotalFilas, 1, @NumeroVersionMes);
                    SELECT SCOPE_IDENTITY();", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@NombreEmpresa", nombreEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    cmd.Parameters.AddWithValue("@Modo", modo);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmd.Parameters.AddWithValue("@NombreUsuario", nombreUsuario);
                    cmd.Parameters.AddWithValue("@NombreArchivo", nombreArchivo);
                    cmd.Parameters.AddWithValue("@TotalFilas", totalFilas);
                    cmd.Parameters.AddWithValue("@NumeroVersionMes", numeroVersionMes);
                    idHistorial = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            string whereClause = ConstruirWhere(modo);

            foreach (var tabla in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                string json;
                try
                {
                    json = SerializarTabla(conn, tx, tabla, whereClause, idEmpresa, anio, idEscenario);
                }
                catch (Exception ex)
                {
                    // No enmascarar como tabla vacía: abortar el snapshot completo.
                    throw new InvalidOperationException(
                        $"No se pudo capturar el snapshot de la tabla '{tabla}': {ex.Message}", ex);
                }

                // El JSON se guarda comprimido (GZip) en DatosGzip; DatosJson queda vacío.
                // El formato GZip es el mismo que produce/lee T-SQL COMPRESS()/DECOMPRESS().
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.SnapshotsCargues (IdHistorial, NombreTabla, DatosJson, DatosGzip)
                    VALUES (@IdHistorial, @NombreTabla, '', @DatosGzip)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdHistorial", idHistorial);
                    cmd.Parameters.AddWithValue("@NombreTabla", tabla);
                    cmd.Parameters.Add("@DatosGzip", SqlDbType.VarBinary, -1).Value = Comprimir(json);
                    cmd.ExecuteNonQuery();
                }
            }

            PurgarVersionesAntiguas(conn, tx, idEmpresa, anio, modo, idEscenario);
            return idHistorial;
        }

        // ── Consultas ─────────────────────────────────────────────────────────

        public List<HistorialVersiones> ObtenerHistorial(int? idEmpresa, int? anio, byte? modo, int? idEscenario = null)
        {
            var list = new List<HistorialVersiones>();
            string sql = @"
                SELECT *
                FROM dbo.HistorialVersionesCargues
                WHERE (@IdEmpresa   IS NULL OR IdEmpresa           = @IdEmpresa)
                  AND (@Anio        IS NULL OR Anio                = @Anio)
                  AND (@Modo        IS NULL OR Modo                = @Modo)
                  AND (@IdEscenario IS NULL OR ISNULL(IdEscenario,1) = @IdEscenario)
                ORDER BY IdEmpresa, Anio, Modo, FechaCargue DESC";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", (object)idEmpresa ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Anio",      (object)anio     ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Modo",      (object)modo     ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdEscenario", (object)idEscenario ?? DBNull.Value);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(MapHistorial(r));
            }

            foreach (var v in list)
            {
                if (v.EsVersionActual)
                    v.EstadoDisplay = "Actual";
                else if (v.FechaReversion.HasValue)
                    v.EstadoDisplay = "Revertido";
                else
                    v.EstadoDisplay = "Anterior";
            }

            return list;
        }

        public HistorialVersiones ObtenerPorId(int id)
        {
            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "SELECT * FROM dbo.HistorialVersionesCargues WHERE Id = @Id", conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) return MapHistorial(r);
            }
            return null;
        }

        // ── Rollback ──────────────────────────────────────────────────────────

        /// <summary>
        /// Restaura la versión indicada (devuelve los datos de su llave de cargue al estado que guardó su snapshot).
        /// ANTES de reemplazar nada, guarda un respaldo de los datos actuales (dbo.RespaldosRestauracion, Sql/034) en
        /// la MISMA transacción, para poder recuperarlos si la restauración fue un error. Si ese respaldo no se puede
        /// guardar por un error real, toda la restauración se aborta sin tocar datos. Sin la tabla (script sin
        /// ejecutar) restaura como antes, sin respaldo (<paramref name="idRespaldo"/> queda null).
        /// </summary>
        public bool EjecutarRollback(int idHistorial, int idUsuarioReversion, string nombreUsuarioReversion, out int? idRespaldo, out ImpactoCargue bloqueo)
        {
            idRespaldo = null;
            bloqueo = null;
            var version = ObtenerPorId(idHistorial);
            if (version == null) return false;

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        var snapshots = new Dictionary<string, string>();
                        using (var cmd = new SqlCommand(
                            "SELECT NombreTabla, DatosJson, DatosGzip FROM dbo.SnapshotsCargues WHERE IdHistorial = @IdHistorial",
                            conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@IdHistorial", idHistorial);
                            using (var r = cmd.ExecuteReader())
                                while (r.Read())
                                {
                                    // Preferir la versión comprimida; las filas antiguas
                                    // (pre-compresión) siguen en DatosJson como texto plano.
                                    string json = r["DatosGzip"] != DBNull.Value
                                        ? Descomprimir((byte[])r["DatosGzip"])
                                        : r["DatosJson"].ToString();
                                    snapshots[r["NombreTabla"].ToString()] = json;
                                }
                        }

                        // Versión incompleta: faltan snapshots de tablas que ya formaban parte del
                        // cargue de la empresa cuando se cargó la versión (hoja vigente desde antes).
                        // Restaurar parcialmente dejaría los estados financieros inconsistentes — se
                        // aborta sin tocar ninguna tabla. Una hoja que empezó a aplicar DESPUÉS de la
                        // versión (nueva, recién asignada o reactivada) no tiene estado previo que
                        // restaurar: esa tabla se deja como está.
                        var tablasFaltantes = TablasCargueHelper.HojasParaEmpresa(version.IdEmpresa)
                            .Where(h => h.VigenteDesdePara(version.IdEmpresa) <= version.FechaCargue)
                            .Select(h => h.TablaIni)
                            .Where(t => !snapshots.ContainsKey(t))
                            .ToList();
                        if (tablasFaltantes.Count > 0)
                        {
                            try { tx.Rollback(); } catch { }
                            return false;
                        }

                        // Se restaura todo lo que la versión trae en su snapshot, siempre que la tabla
                        // siga en el catálogo de hojas (lista blanca: el nombre viene de la BD) —
                        // incluso si hoy la hoja está inactiva o dejó de aplicar a la empresa: el
                        // rollback devuelve los datos exactamente al estado de esa versión.
                        var catalogo = new HashSet<string>(TablasCargueHelper.MapeoCompleto.Values, StringComparer.OrdinalIgnoreCase);
                        var tablasARestaurar = snapshots.Keys
                            .Where(catalogo.Contains)
                            .ToList();

                        // Regla de oro: deshacer NUNCA puede dejar el año sin datos ni reducir una tabla bajo el umbral de bloqueo
                        // (p. ej. deshacer el primer cargue de un año, cuya copia está vacía). No hay forma de forzarlo.
                        var evaluacion = EvaluarReemplazo(conn, tx, version.IdEmpresa, version.Anio, version.Modo, version.IdEscenario, snapshots, tablasARestaurar);
                        if (evaluacion.Bloqueado)
                        {
                            bloqueo = evaluacion;
                            try { tx.Rollback(); } catch { }
                            return false;
                        }

                        // Respaldo de lo que se va a reemplazar (antes de borrar nada).
                        idRespaldo = CrearRespaldoPreRestauracion(conn, tx, version.IdEmpresa, version.NombreEmpresa,
                            version.Anio, version.Modo, version.IdEscenario, tablasARestaurar,
                            idUsuarioReversion, nombreUsuarioReversion, idHistorial);

                        AplicarEstado(conn, tx, version.IdEmpresa, version.Anio, version.Modo, version.IdEscenario, snapshots, tablasARestaurar);
                        VerificarAnioConDatos(conn, tx, version.IdEmpresa, version.Anio, version.Modo, version.IdEscenario, evaluacion.TotalExistentes);

                        using (var cmd = new SqlCommand(@"
                            UPDATE dbo.HistorialVersionesCargues
                            SET EsVersionActual = 0
                            WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND ISNULL(IdEscenario,1) = @IdEscenario;

                            UPDATE dbo.HistorialVersionesCargues
                            SET EsVersionActual          = 1,
                                FechaReversion           = GETDATE(),
                                IdUsuarioReversion       = @IdUsrRev,
                                NombreUsuarioReversion   = @NombreUsrRev
                            WHERE Id = @IdHistorial;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@IdEmpresa",    version.IdEmpresa);
                            cmd.Parameters.AddWithValue("@Anio",         version.Anio);
                            cmd.Parameters.AddWithValue("@Modo",         version.Modo);
                            cmd.Parameters.AddWithValue("@IdEscenario",  version.IdEscenario);
                            cmd.Parameters.AddWithValue("@IdUsrRev",     idUsuarioReversion);
                            cmd.Parameters.AddWithValue("@NombreUsrRev", nombreUsuarioReversion);
                            cmd.Parameters.AddWithValue("@IdHistorial",  idHistorial);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error(ex, "HistorialVersionesCarguesService.EjecutarRollback");
                        idRespaldo = null;
                        try { tx.Rollback(); } catch { }
                        return false;
                    }
                }
            }
        }

        /// <summary>Borra los datos de la llave de cargue en las tablas indicadas y reinserta lo que traen los snapshots.</summary>
        private void AplicarEstado(SqlConnection conn, SqlTransaction tx, int idEmpresa, int anio, byte modo, int idEscenario,
            Dictionary<string, string> snapshots, IEnumerable<string> tablas)
        {
            string deleteWhere = ConstruirWhere(modo);
            foreach (var tabla in tablas)
            {
                using (var cmd = new SqlCommand($"DELETE FROM dbo.[{tabla}] {deleteWhere}", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.ExecuteNonQuery();
                }

                // json != "[]" ⇒ la tabla tenía filas en el snapshot; si estaba
                // vacía se deja vacía tras el DELETE (comportamiento correcto).
                string json = snapshots[tabla];
                if (!string.IsNullOrWhiteSpace(json) && json != "[]")
                    ReinsertarDesdeJson(conn, tx, tabla, json);
            }
        }

        // ── Regla de oro: nunca dejar un año vacío ────────────────────────────

        private static int ContarFilasJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return 0;
            return Newtonsoft.Json.Linq.JArray.Parse(json).Count;
        }

        /// <summary>
        /// Compara, por tabla, lo que hay hoy con lo que dejaría la copia (versión o respaldo) que se va a aplicar.
        /// Usa la misma regla que el cargue de Excel (<see cref="CargueImpactoService.Evaluar"/>).
        /// </summary>
        private ImpactoCargue EvaluarReemplazo(SqlConnection conn, SqlTransaction tx, int idEmpresa, int anio, byte modo, int idEscenario,
            Dictionary<string, string> copia, IEnumerable<string> tablas)
        {
            var actuales = new CargueImpactoService().ContarExistentes(conn, tx, idEmpresa, anio, modo, idEscenario)
                .ToDictionary(x => x.Tabla, x => x.Filas, StringComparer.OrdinalIgnoreCase);
            return CargueImpactoService.Evaluar(tablas.Select(t => (t, actuales.TryGetValue(t, out int f) ? f : 0, ContarFilasJson(copia[t]))));
        }

        /// <summary>
        /// Segunda barrera, dentro de la transacción y después de aplicar: si el año tenía datos y quedó sin ninguna fila,
        /// se lanza y toda la operación se revierte.
        /// </summary>
        private void VerificarAnioConDatos(SqlConnection conn, SqlTransaction tx, int idEmpresa, int anio, byte modo, int idEscenario, int filasAntes)
        {
            if (filasAntes <= 0) return;
            int despues = new CargueImpactoService().ContarExistentes(conn, tx, idEmpresa, anio, modo, idEscenario).Sum(x => x.Filas);
            if (despues == 0)
                throw new InvalidOperationException("La operación habría dejado el año sin datos; se canceló sin cambios.");
        }

        // ── Respaldos automáticos previos a restaurar (Sql/034) ───────────────

        /// <summary>
        /// Guarda, dentro de la transacción del llamador, una copia de los datos actuales de la llave de cargue en las
        /// tablas indicadas. Devuelve su Id, o null si la tabla de respaldos no existe (script 034 sin ejecutar).
        /// Cualquier otro error sube y aborta la restauración: nunca se reemplaza sin respaldo cuando este es posible.
        /// </summary>
        private int? CrearRespaldoPreRestauracion(SqlConnection conn, SqlTransaction tx, int idEmpresa, string nombreEmpresa,
            int anio, byte modo, int idEscenario, IEnumerable<string> tablas, int idUsuario, string nombreUsuario, int? idHistorialRestaurado)
        {
            using (var chk = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.RespaldosRestauracion', N'U') IS NULL THEN 0 ELSE 1 END", conn, tx))
            {
                if (Convert.ToInt32(chk.ExecuteScalar()) == 0)
                {
                    AppLogger.Warn("Restauración sin respaldo previo: falta dbo.RespaldosRestauracion (Sql/034).", "HistorialVersionesCarguesService");
                    return null;
                }
            }

            string where = ConstruirWhere(modo);
            var datos = new Dictionary<string, string>();
            foreach (var tabla in tablas)
                datos[tabla] = SerializarTabla(conn, tx, tabla, where, idEmpresa, anio, idEscenario);

            int totalFilas = ContarFilasActuales(conn, tx, idEmpresa, anio, modo, idEscenario);

            int id;
            using (var cmd = new SqlCommand(@"
                INSERT INTO dbo.RespaldosRestauracion
                    (Fecha, IdEmpresa, NombreEmpresa, Anio, Modo, IdEscenario, IdUsuario, NombreUsuario, IdHistorialRestaurado, TotalFilas, DatosGzip)
                VALUES
                    (GETDATE(), @IdEmpresa, @NombreEmpresa, @Anio, @Modo, @IdEscenario, @IdUsuario, @NombreUsuario, @IdHistorial, @TotalFilas, @Datos);
                SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@NombreEmpresa", (object)nombreEmpresa ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@NombreUsuario", (object)nombreUsuario ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdHistorial", (object)idHistorialRestaurado ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TotalFilas", totalFilas);
                cmd.Parameters.Add("@Datos", SqlDbType.VarBinary, -1).Value = Comprimir(JsonConvert.SerializeObject(datos));
                id = Convert.ToInt32(cmd.ExecuteScalar());
            }

            // Se conservan solo los últimos N respaldos de esta llave de cargue.
            using (var cmd = new SqlCommand(@"
                DELETE FROM dbo.RespaldosRestauracion
                 WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND ISNULL(IdEscenario,1) = @IdEscenario
                   AND Id NOT IN (SELECT TOP (@N) Id FROM dbo.RespaldosRestauracion
                                   WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND ISNULL(IdEscenario,1) = @IdEscenario
                                   ORDER BY Fecha DESC, Id DESC);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cmd.Parameters.AddWithValue("@N", CargueImpactoService.LeerRespaldosRestauracion());
                cmd.ExecuteNonQuery();
            }
            return id;
        }

        /// <summary>Respaldos disponibles (sin su contenido), más recientes primero. Vacío si la tabla no existe.</summary>
        public List<RespaldoRestauracion> ObtenerRespaldos(IEnumerable<int> idsEmpresa)
        {
            var lista = new List<RespaldoRestauracion>();
            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.RespaldosRestauracion', N'U') IS NOT NULL
                        SELECT Id, Fecha, IdEmpresa, NombreEmpresa, Anio, Modo, IdEscenario, IdUsuario, NombreUsuario, IdHistorialRestaurado, TotalFilas
                          FROM dbo.RespaldosRestauracion
                         ORDER BY Fecha DESC, Id DESC", conn))
                {
                    conn.Open();
                    var permitidos = idsEmpresa == null ? null : new HashSet<int>(idsEmpresa);
                    using (var r = cmd.ExecuteReader())
                        while (r != null && r.Read())
                        {
                            var item = MapRespaldo(r);
                            if (permitidos == null || permitidos.Contains(item.IdEmpresa)) lista.Add(item);
                        }
                }
            }
            catch (SqlException ex) { AppLogger.Error(ex, "HistorialVersionesCarguesService.ObtenerRespaldos"); }
            return lista;
        }

        public RespaldoRestauracion ObtenerRespaldoPorId(int id)
        {
            try
            {
                using (var conn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.RespaldosRestauracion', N'U') IS NOT NULL
                        SELECT Id, Fecha, IdEmpresa, NombreEmpresa, Anio, Modo, IdEscenario, IdUsuario, NombreUsuario, IdHistorialRestaurado, TotalFilas
                          FROM dbo.RespaldosRestauracion WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    conn.Open();
                    using (var r = cmd.ExecuteReader())
                        return r != null && r.Read() ? MapRespaldo(r) : null;
                }
            }
            catch (SqlException ex) { AppLogger.Error(ex, "HistorialVersionesCarguesService.ObtenerRespaldoPorId"); return null; }
        }

        private static RespaldoRestauracion MapRespaldo(SqlDataReader r) => new RespaldoRestauracion
        {
            Id = Convert.ToInt32(r["Id"]),
            Fecha = Convert.ToDateTime(r["Fecha"]),
            IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
            NombreEmpresa = r["NombreEmpresa"] as string,
            Anio = Convert.ToInt32(r["Anio"]),
            Modo = Convert.ToByte(r["Modo"]),
            IdEscenario = Convert.ToInt32(r["IdEscenario"]),
            IdUsuario = r["IdUsuario"] as int?,
            NombreUsuario = r["NombreUsuario"] as string,
            IdHistorialRestaurado = r["IdHistorialRestaurado"] as int?,
            TotalFilas = Convert.ToInt32(r["TotalFilas"])
        };

        /// <summary>
        /// Recupera un respaldo: devuelve los datos de su llave de cargue a como estaban antes de la restauración que lo
        /// generó. Antes de reemplazar, guarda un respaldo de lo que hay hoy (así la recuperación también se puede deshacer).
        /// </summary>
        public bool RecuperarRespaldo(int idRespaldo, int idUsuario, string nombreUsuario, out int? idRespaldoNuevo, out ImpactoCargue bloqueo)
        {
            idRespaldoNuevo = null;
            bloqueo = null;
            var resp = ObtenerRespaldoPorId(idRespaldo);
            if (resp == null) return false;

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        byte[] gz;
                        using (var cmd = new SqlCommand("SELECT DatosGzip FROM dbo.RespaldosRestauracion WHERE Id = @Id", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Id", idRespaldo);
                            gz = (byte[])cmd.ExecuteScalar();
                        }
                        var datos = JsonConvert.DeserializeObject<Dictionary<string, string>>(Descomprimir(gz));
                        if (datos == null) { try { tx.Rollback(); } catch { } return false; }

                        var catalogo = new HashSet<string>(TablasCargueHelper.MapeoCompleto.Values, StringComparer.OrdinalIgnoreCase);
                        var tablas = datos.Keys.Where(catalogo.Contains).ToList();

                        // Misma regla de oro que al deshacer: recuperar un respaldo tampoco puede dejar el año vacío.
                        var evaluacion = EvaluarReemplazo(conn, tx, resp.IdEmpresa, resp.Anio, resp.Modo, resp.IdEscenario, datos, tablas);
                        if (evaluacion.Bloqueado)
                        {
                            bloqueo = evaluacion;
                            try { tx.Rollback(); } catch { }
                            return false;
                        }

                        idRespaldoNuevo = CrearRespaldoPreRestauracion(conn, tx, resp.IdEmpresa, resp.NombreEmpresa, resp.Anio,
                            resp.Modo, resp.IdEscenario, tablas, idUsuario, nombreUsuario, null);

                        AplicarEstado(conn, tx, resp.IdEmpresa, resp.Anio, resp.Modo, resp.IdEscenario, datos, tablas);
                        VerificarAnioConDatos(conn, tx, resp.IdEmpresa, resp.Anio, resp.Modo, resp.IdEscenario, evaluacion.TotalExistentes);
                        tx.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error(ex, "HistorialVersionesCarguesService.RecuperarRespaldo");
                        idRespaldoNuevo = null;
                        try { tx.Rollback(); } catch { }
                        return false;
                    }
                }
            }
        }

        // ── Helpers privados ──────────────────────────────────────────────────

        private int ContarFilasActuales(SqlConnection conn, SqlTransaction tx, int idEmpresa, int anio, byte modo, int idEscenario)
        {
            int total = 0;
            string where = ConstruirWhere(modo);

            foreach (var tabla in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM dbo.[{tabla}] {where}", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    total += Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            return total;
        }

        private string SerializarTabla(SqlConnection conn, SqlTransaction tx,
            string tabla, string whereClause, int idEmpresa, int anio, int idEscenario)
        {
            // Sin catch: si la lectura falla, la excepción sube y CrearSnapshotInterno
            // aborta el snapshot completo. Una tabla realmente vacía devuelve "[]" de
            // forma natural (lista sin filas), que NO es un error.
            var rows = new List<Dictionary<string, object>>();
            using (var cmd = new SqlCommand($"SELECT * FROM dbo.[{tabla}] {whereClause}", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < r.FieldCount; i++)
                            row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                        rows.Add(row);
                    }
                }
            }
            return JsonConvert.SerializeObject(rows, new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-ddTHH:mm:ss"
            });
        }

        private void ReinsertarDesdeJson(SqlConnection conn, SqlTransaction tx, string tabla, string json)
        {
            var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            if (rows == null || rows.Count == 0) return;

            var columnas = new List<string>();
            using (var cmd = new SqlCommand(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION",
                conn, tx))
            {
                cmd.Parameters.AddWithValue("@t", tabla);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        columnas.Add(r["COLUMN_NAME"].ToString());
            }

            var dt = new DataTable();
            foreach (var col in columnas) dt.Columns.Add(col, typeof(object));

            foreach (var row in rows)
            {
                var dr = dt.NewRow();
                foreach (var col in columnas)
                    dr[col] = row.TryGetValue(col, out object val) ? (val ?? DBNull.Value) : DBNull.Value;
                dt.Rows.Add(dr);
            }

            using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx))
            {
                bulk.DestinationTableName = $"[dbo].[{tabla}]";
                bulk.BulkCopyTimeout = 120;
                bulk.BatchSize = 5000;
                foreach (DataColumn col in dt.Columns)
                    bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                bulk.WriteToServer(dt);
            }
        }

        private void PurgarVersionesAntiguas(SqlConnection conn, SqlTransaction tx,
            int idEmpresa, int anio, byte modo, int idEscenario)
        {
            // No se depende de un FK ON DELETE CASCADE: se borran primero los snapshots
            // (hijos) de las versiones que sobran en esta llave de cargue, luego las versiones,
            // y por último se barre cualquier snapshot huérfano que pudiera existir.
            // El ORDER BY EsVersionActual DESC garantiza que la versión activa (que tras
            // un rollback puede NO ser la de fecha más reciente) nunca se purgue.
            using (var cmd = new SqlCommand(@"
                DECLARE @Sobrantes TABLE (Id INT PRIMARY KEY);

                INSERT INTO @Sobrantes (Id)
                SELECT Id FROM (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               ORDER BY EsVersionActual DESC, FechaCargue DESC) AS rn
                    FROM dbo.HistorialVersionesCargues
                    WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND ISNULL(IdEscenario,1) = @IdEscenario
                ) ranked
                WHERE rn > @MaxVersiones;

                DELETE FROM dbo.SnapshotsCargues
                WHERE IdHistorial IN (SELECT Id FROM @Sobrantes);

                DELETE FROM dbo.HistorialVersionesCargues
                WHERE Id IN (SELECT Id FROM @Sobrantes);

                DELETE s
                FROM dbo.SnapshotsCargues s
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.HistorialVersionesCargues h WHERE h.Id = s.IdHistorial
                );", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa",   idEmpresa);
                cmd.Parameters.AddWithValue("@Anio",        anio);
                cmd.Parameters.AddWithValue("@Modo",        modo);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cmd.Parameters.AddWithValue("@MaxVersiones", MaxVersionesPorLlaveCargue);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Compresión GZip (formato compatible con T-SQL COMPRESS/DECOMPRESS) ─

        private static byte[] Comprimir(string texto)
        {
            var bytes = Encoding.UTF8.GetBytes(texto ?? "[]");
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    gz.Write(bytes, 0, bytes.Length);
                return ms.ToArray();
            }
        }

        private static string Descomprimir(byte[] datos)
        {
            if (datos == null || datos.Length == 0) return "[]";
            using (var msIn = new MemoryStream(datos))
            using (var gz = new GZipStream(msIn, CompressionMode.Decompress))
            using (var msOut = new MemoryStream())
            {
                gz.CopyTo(msOut);
                return Encoding.UTF8.GetString(msOut.ToArray());
            }
        }

        private static HistorialVersiones MapHistorial(SqlDataReader r)
        {
            var version = new HistorialVersiones
            {
                Id                     = Convert.ToInt32(r["Id"]),
                IdEmpresa              = Convert.ToInt32(r["IdEmpresa"]),
                NombreEmpresa          = r["NombreEmpresa"].ToString(),
                Anio                   = Convert.ToInt32(r["Anio"]),
                Modo                   = Convert.ToByte(r["Modo"]),
                FechaCargue            = Convert.ToDateTime(r["FechaCargue"]),
                IdUsuario              = Convert.ToInt32(r["IdUsuario"]),
                NombreUsuario          = r["NombreUsuario"].ToString(),
                NombreArchivo          = r["NombreArchivo"].ToString(),
                TotalFilas             = Convert.ToInt32(r["TotalFilas"]),
                EsVersionActual        = Convert.ToBoolean(r["EsVersionActual"]),
                FechaReversion         = r["FechaReversion"] == DBNull.Value
                                            ? (DateTime?)null
                                            : Convert.ToDateTime(r["FechaReversion"]),
                IdUsuarioReversion     = r["IdUsuarioReversion"] == DBNull.Value
                                            ? (int?)null
                                            : Convert.ToInt32(r["IdUsuarioReversion"]),
                NombreUsuarioReversion = r["NombreUsuarioReversion"] == DBNull.Value
                                            ? null
                                            : r["NombreUsuarioReversion"].ToString(),
                NumeroVersionMes       = r["NumeroVersionMes"] != DBNull.Value
                                            ? Convert.ToInt32(r["NumeroVersionMes"])
                                            : 1
            };

            // Columna incremental (Sql/003_HistorialVersionesCargues_AddEscenario.sql) — patrón
            // de lectura retrocompatible: si la BD todavía no la tiene, queda en 1 (Escenario principal).
            try { version.IdEscenario = r["IdEscenario"] != DBNull.Value ? Convert.ToInt32(r["IdEscenario"]) : 1; }
            catch (IndexOutOfRangeException) { }

            return version;
        }
    }
}
