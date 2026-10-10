using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Orquesta la validación en dos pasos del cargue de Excel: crea/consulta el lote de
    /// staging (dbo.CarguesLotes), invoca dbo.sp_ValidarCargueStaging contra las tablas
    /// dbo.Staging_Ini_* (ver Sql/007_CarguesStaging_CreateTables.sql), y mueve un lote ya
    /// validado a las tablas reales Ini_* vía dbo.sp_ConfirmarCargueStaging. Los datos reales
    /// nunca se tocan hasta <see cref="ConfirmarLote"/>.
    /// </summary>
    public class CargueStagingService : BaseService
    {
        /// <summary>
        /// Todas las Staging_Ini_* del cargue: las estándar + las de TODOS los paquetes de personalización
        /// (TablasCargueHelper.MapeoCompleto). Es seguro incluirlas siempre porque toda operación sobre ellas
        /// va filtrada por IdLote. Los nombres salen del código, nunca del usuario.
        /// </summary>
        private static readonly string[] TablasStaging =
            TablasCargueHelper.MapeoCompleto.Values.Distinct().Select(TablasCargueHelper.NombreStaging).ToArray();

        /// <summary>
        /// Envuelve una sentencia sobre una tabla de staging para que no falle si la tabla aún no existe
        /// (p. ej. la de una hoja personalizada cuyo script todavía no se ejecutó en esa BD).
        /// </summary>
        private static string SiExiste(string tabla, string sentencia, string siNo = null) =>
            $"IF OBJECT_ID(N'dbo.{tabla}', N'U') IS NOT NULL {sentencia}" + (siNo != null ? $" ELSE {siNo}" : "");

        private static DateTime _ultimaPurga = DateTime.MinValue;
        private static readonly object _purgaLock = new object();

        /// <summary>Un lote que lleva más de esto en revisión (sin confirmar ni descartar) se da por abandonado.</summary>
        public const int HorasVencimientoRevision = 2;

        /// <summary>
        /// Un lote que sigue "EnValidacion" pasado este tiempo es un cargue que murió a mitad (reciclaje del
        /// app pool, timeout, caída): ninguna petición real dura tanto (executionTimeout = 60 min).
        /// </summary>
        public const int MinutosVencimientoValidacion = 70;

        /// <summary>
        /// Condición SQL (sobre dbo.CarguesLotes) de "lote vencido": abandonado en revisión, cargue caído a
        /// mitad, o confirmado cuya limpieza no se completó (su staging ya no sirve para nada). Fechas con
        /// GETDATE() del servidor SQL, no del web server, para no depender de relojes distintos.
        /// </summary>
        private static readonly string CondicionVencido =
            "(Estado = 'Confirmado'" +
            $" OR (Estado = 'EnValidacion' AND FechaCreacion < DATEADD(MINUTE, -{MinutosVencimientoValidacion}, GETDATE()))" +
            $" OR FechaCreacion < DATEADD(HOUR, -{HorasVencimientoRevision}, GETDATE()))";

        // ── Cabecera de lote ────────────────────────────────────────────────────────────

        private const string MensajeLoteActivoExistente =
            "Ya hay un cargue en revisión pendiente para esta empresa, año, modo y escenario. Confírmelo o descártelo antes de subir uno nuevo.";

        /// <summary>
        /// Crea un lote nuevo, a menos que ya exista uno "activo" (no confirmado ni descartado)
        /// para la misma llave (IdEmpresa, Anio, Modo, IdEscenario) — evita que dos cargues
        /// concurrentes para el mismo destino se pisen entre sí al confirmar. El candado real es
        /// el índice único filtrado UX_CarguesLotes_ActivoPorLlave (Sql/008_...): el pre-check de
        /// aquí solo da un mensaje amable en el caso normal; si aun así hay una carrera real, se
        /// atrapa la violación del índice y se responde igual de amable.
        /// </summary>
        public (bool exito, long idLote, string mensaje) CrearLote(int idEmpresa, string nombreEmpresa, int anio, byte modo, int idEscenario,
            int idUsuario, string nombreUsuario, string nombreArchivo)
        {
            // Antes de mirar si la llave está ocupada, se liberan los lotes VENCIDOS de esa misma llave
            // (cargue caído a mitad, revisión abandonada): así un lote muerto nunca bloquea el siguiente
            // cargue de la empresa esperando a la purga general.
            EjecutarBorradoDeLotes(
                "IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND IdEscenario = @IdEscenario AND " + CondicionVencido,
                c =>
                {
                    c.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    c.Parameters.AddWithValue("@Anio", anio);
                    c.Parameters.AddWithValue("@Modo", modo);
                    c.Parameters.AddWithValue("@IdEscenario", idEscenario);
                });

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                long? idLoteActivo = BuscarLoteActivo(cn, null, idEmpresa, anio, modo, idEscenario);
                if (idLoteActivo.HasValue)
                {
                    // Subir de nuevo reemplaza el lote pendiente (su staging y hallazgos se borran) cuando no hay
                    // nada que proteger: es del mismo usuario (típico "subir archivo corregido") o ya estaba
                    // bloqueado por errores y no se podía confirmar. Si es un lote de OTRO usuario que sí se puede
                    // confirmar, se respeta su revisión y se avisa como antes.
                    var previo = ObtenerLote(idLoteActivo.Value);
                    bool reemplazable = previo == null
                        || previo.IdUsuario == idUsuario
                        || previo.Estado == CargueLoteEstado.ConErrores;
                    if (!reemplazable)
                        return (false, idLoteActivo.Value, MensajeLoteActivoExistente);

                    if (previo != null) DescartarLote(previo.IdLote);
                }

                try
                {
                    using (var cmd = new SqlCommand(@"
                        INSERT INTO dbo.CarguesLotes
                            (IdEmpresa, NombreEmpresa, Anio, Modo, IdEscenario, IdUsuario, NombreUsuario, NombreArchivo, Estado)
                        VALUES
                            (@IdEmpresa, @NombreEmpresa, @Anio, @Modo, @IdEscenario, @IdUsuario, @NombreUsuario, @NombreArchivo, @Estado);
                        SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        cmd.Parameters.AddWithValue("@NombreEmpresa", (object)nombreEmpresa ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Anio", anio);
                        cmd.Parameters.AddWithValue("@Modo", modo);
                        cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                        cmd.Parameters.AddWithValue("@NombreUsuario", (object)nombreUsuario ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@NombreArchivo", (object)nombreArchivo ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Estado", CargueLoteEstado.EnValidacion);
                        return (true, (long)cmd.ExecuteScalar(), null);
                    }
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    // Carrera real: otro cargue creó el lote activo justo entre el pre-check y el INSERT.
                    long idGanador = BuscarLoteActivo(cn, null, idEmpresa, anio, modo, idEscenario) ?? 0;
                    return (false, idGanador, MensajeLoteActivoExistente);
                }
            }
        }

        private static long? BuscarLoteActivo(SqlConnection cn, SqlTransaction tx, int idEmpresa, int anio, byte modo, int idEscenario)
        {
            using (var cmd = new SqlCommand(@"
                SELECT TOP 1 IdLote FROM dbo.CarguesLotes
                WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo AND IdEscenario = @IdEscenario
                  AND Estado NOT IN ('Confirmado', 'Descartado')", cn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                var r = cmd.ExecuteScalar();
                return r == null ? (long?)null : Convert.ToInt64(r);
            }
        }

        public void ActualizarTotalFilas(long idLote, int totalFilas)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("UPDATE dbo.CarguesLotes SET TotalFilas = @TotalFilas WHERE IdLote = @IdLote", cn))
            {
                cmd.Parameters.AddWithValue("@TotalFilas", totalFilas);
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public CargueLote ObtenerLote(long idLote)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT * FROM dbo.CarguesLotes WHERE IdLote = @IdLote", cn))
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return MapLote(r);
                }
            }
        }

        public List<CargueLoteHallazgo> ObtenerHallazgos(long idLote)
        {
            var list = new List<CargueLoteHallazgo>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                SELECT * FROM dbo.CarguesLotesErrores
                WHERE IdLote = @IdLote
                ORDER BY CASE Severidad WHEN 'Error' THEN 0 ELSE 1 END, NombreHoja, NumeroFilaExcel", cn))
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var h = new CargueLoteHallazgo
                        {
                            Id = Convert.ToInt64(r["Id"]),
                            IdLote = Convert.ToInt64(r["IdLote"]),
                            NombreHoja = r["NombreHoja"] as string,
                            NombreTabla = r["NombreTabla"] as string,
                            NumeroFilaExcel = r["NumeroFilaExcel"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["NumeroFilaExcel"]),
                            Columna = r["Columna"] as string,
                            Severidad = r["Severidad"] as string,
                            CodigoRegla = r["CodigoRegla"] as string,
                            Mensaje = r["Mensaje"] as string,
                            MensajeEn = r["MensajeEn"] as string
                        };
                        // Titulo/TituloEn los escribe sp_ValidarCargueStaging; columnas incrementales (Sql/011).
                        try { h.Titulo = r["Titulo"] as string; } catch (IndexOutOfRangeException) { }
                        try { h.TituloEn = r["TituloEn"] as string; } catch (IndexOutOfRangeException) { }
                        list.Add(h);
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Cuenta cuántas filas de staging tiene el lote en cada tabla Ini_/Staging_Ini_ — para
        /// mostrar el detalle "por hoja" en el resumen de un cargue ya confirmado. Usa la conexión/
        /// transacción de la confirmación y debe llamarse ANTES de <see cref="LimpiarLoteEnTransaccion"/>.
        /// </summary>
        public List<(string NombreHoja, string NombreTabla, int Filas)> ObtenerConteoPorHoja(SqlConnection conn, SqlTransaction tx, long idLote)
        {
            var resultado = new List<(string, string, int)>();
            foreach (var kv in TablasCargueHelper.MapeoCompleto)
            {
                string nombreStaging = TablasCargueHelper.NombreStaging(kv.Value);
                if (Array.IndexOf(TablasStaging, nombreStaging) < 0) continue;
                using (var cmd = new SqlCommand(SiExiste(nombreStaging, $"SELECT COUNT(*) FROM dbo.[{nombreStaging}] WHERE IdLote = @IdLote", "SELECT 0"), conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdLote", idLote);
                    int filas = (int)cmd.ExecuteScalar();
                    if (filas > 0) resultado.Add((kv.Key, kv.Value, filas));
                }
            }
            return resultado;
        }

        /// <summary>
        /// Borra staging + hallazgos + cabecera de un lote DENTRO de la transacción de confirmación
        /// (después de sp_ConfirmarCargueStaging). Así la copia a Ini_* y la limpieza del staging son
        /// atómicas: o queda todo confirmado y limpio, o no cambia nada — nunca un lote "Confirmado"
        /// con su staging colgado.
        /// </summary>
        public void LimpiarLoteEnTransaccion(SqlConnection conn, SqlTransaction tx, long idLote)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in TablasStaging)
                sb.AppendLine(SiExiste(t, $"DELETE FROM dbo.{t} WHERE IdLote = @IdLote;"));
            sb.AppendLine("DELETE FROM dbo.CarguesLotesErrores WHERE IdLote = @IdLote;");
            sb.AppendLine("DELETE FROM dbo.CarguesLotes WHERE IdLote = @IdLote;");
            using (var cmd = new SqlCommand(sb.ToString(), conn, tx) { CommandTimeout = 120 })
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Validación ──────────────────────────────────────────────────────────────────

        /// <summary>Llama a dbo.sp_ValidarCargueStaging para el lote indicado.</summary>
        public (bool exito, string mensaje, int totalErrores, int totalAdvertencias) ValidarLote(long idLote)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("dbo.sp_ValidarCargueStaging", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 180 })
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote);

                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read())
                        return (false, "sp_ValidarCargueStaging no devolvió resultado.", 0, 0);

                    int cod = Convert.ToInt32(r["CodMessage"]);
                    if (cod != 1)
                        return (false, r["ErrorMessage"] as string ?? "Error desconocido validando el cargue.", 0, 0);

                    // El SP siempre devuelve TotalErrores/TotalAdvertencias junto con CodMessage=1.
                    int totalErrores = Convert.ToInt32(r["TotalErrores"]);
                    int totalAdvertencias = Convert.ToInt32(r["TotalAdvertencias"]);
                    return (true, null, totalErrores, totalAdvertencias);
                }
            }
        }

        // ── Confirmación ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ejecuta dbo.sp_ConfirmarCargueStaging usando la conexión/transacción del llamador
        /// (misma transacción donde ya se hizo CrearSnapshotEnTransaccion + Eliminar*DeIni),
        /// para que el paso de staging→real quede atómico con el resto del cargue.
        /// </summary>
        public (bool exito, string mensaje) ConfirmarLote(SqlConnection conn, SqlTransaction tx, long idLote, int? idHistorialGenerado)
        {
            using (var cmd = new SqlCommand("dbo.sp_ConfirmarCargueStaging", conn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 180 })
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                cmd.Parameters.AddWithValue("@IdHistorialGenerado", (object)idHistorialGenerado ?? DBNull.Value);

                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read())
                        return (false, "sp_ConfirmarCargueStaging no devolvió resultado.");

                    int cod = Convert.ToInt32(r["CodMessage"]);
                    return cod == 1 ? (true, (string)null) : (false, r["ErrorMessage"] as string ?? "Error desconocido confirmando el cargue.");
                }
            }
        }

        // ── Descarte / limpieza de staging ──────────────────────────────────────────────

        /// <summary>
        /// Borra el staging de un lote (y su cabecera) sin tocar nada real. Todo o nada. Nunca toca un
        /// lote que ya esté Confirmado (si una confirmación está en curso, espera a que termine y lo omite).
        /// </summary>
        public void DescartarLote(long idLote)
        {
            EjecutarBorradoDeLotes("IdLote = @IdLote AND Estado <> 'Confirmado'", cmd => cmd.Parameters.AddWithValue("@IdLote", idLote));
        }

        /// <summary>
        /// Descarta los lotes pendientes (no confirmados) de un usuario PARA UNA EMPRESA. Se usa al subir
        /// un archivo nuevo: cualquier lote suyo de esa misma empresa que siga abierto quedó abandonado
        /// (salió sin descartar, cerró el navegador, subió el corregido cambiando año/modo/escenario…).
        /// Nunca toca lotes de otra empresa — ni siquiera los del mismo usuario. Devuelve cuántos borró.
        /// </summary>
        public int DescartarLotesPendientesDeUsuario(int idUsuario, int idEmpresa)
        {
            if (idUsuario <= 0 || idEmpresa <= 0) return 0;
            return EjecutarBorradoDeLotes(
                "IdUsuario = @IdUsuario AND IdEmpresa = @IdEmpresa AND Estado <> 'Confirmado'",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                });
        }

        /// <summary>
        /// Llamar DENTRO de la transacción de confirmación, ANTES de borrar nada real: toma un candado de
        /// actualización (UPDLOCK) sobre la cabecera del lote, que se mantiene hasta el commit/rollback.
        /// Así ningún descarte/purga/abandono puede borrar el staging de ese lote a mitad de la
        /// confirmación (esperan a que termine y, ya Confirmado, lo omiten); y si el descarte ganó la
        /// carrera, aquí se detecta y se aborta sin haber borrado los datos reales de la empresa.
        /// </summary>
        public (bool ok, string mensaje) BloquearLoteParaConfirmar(SqlConnection conn, SqlTransaction tx, long idLote, int idEmpresa)
        {
            string estado;
            int idEmpresaLote, totalFilas;
            using (var cmd = new SqlCommand(@"
                SELECT Estado, IdEmpresa, TotalFilas FROM dbo.CarguesLotes WITH (UPDLOCK, ROWLOCK)
                WHERE IdLote = @IdLote", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return (false, "El cargue en revisión ya no existe (se descartó o venció). Vuelva a subir el archivo.");
                    estado = r["Estado"] as string;
                    idEmpresaLote = Convert.ToInt32(r["IdEmpresa"]);
                    totalFilas = Convert.ToInt32(r["TotalFilas"]);
                }
            }

            if (idEmpresaLote != idEmpresa)
                return (false, "El cargue en revisión no corresponde a la empresa indicada.");
            if (estado != CargueLoteEstado.ValidadoOk && estado != CargueLoteEstado.ConAdvertencias)
                return (false, $"El cargue no está en un estado confirmable (estado actual: {estado}).");

            // Red de seguridad: si el lote dice tener filas pero su staging está vacío, no se confirma
            // (confirmar borraría los datos reales de la empresa y no insertaría nada).
            if (totalFilas > 0)
            {
                int filasStaging = 0;
                foreach (var t in TablasStaging)
                {
                    using (var cmd = new SqlCommand(SiExiste(t, $"SELECT COUNT(*) FROM dbo.{t} WHERE IdLote = @IdLote", "SELECT 0"), conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@IdLote", idLote);
                        filasStaging += (int)cmd.ExecuteScalar();
                    }
                }
                if (filasStaging == 0)
                    return (false, "El cargue en revisión ya no tiene datos en staging. Vuelva a subir el archivo.");
            }

            return (true, null);
        }

        /// <summary>
        /// Borra, en UNA transacción (XACT_ABORT), el staging + hallazgos + cabecera de los lotes de
        /// CarguesLotes que cumplan <paramref name="whereLotes"/>. La selección toma UPDLOCK sobre las
        /// cabeceras: si un lote se está confirmando (ver <see cref="BloquearLoteParaConfirmar"/>), espera
        /// a que termine y vuelve a evaluar el filtro, así nunca se borra staging que otra petición está
        /// usando. Todo se filtra por IdLote: ningún borrado alcanza filas de otro lote/empresa.
        /// </summary>
        private int EjecutarBorradoDeLotes(string whereLotes, Action<SqlCommand> parametros)
        {
            var sb = new System.Text.StringBuilder(@"
                SET NOCOUNT ON;
                SET XACT_ABORT ON;
                DECLARE @Lotes TABLE (IdLote BIGINT PRIMARY KEY);
                BEGIN TRANSACTION;
                INSERT INTO @Lotes (IdLote)
                SELECT IdLote FROM dbo.CarguesLotes WITH (UPDLOCK, ROWLOCK) WHERE ");
            sb.AppendLine(whereLotes + ";");
            foreach (var t in TablasStaging)
                sb.AppendLine(SiExiste(t, $"DELETE FROM dbo.{t} WHERE IdLote IN (SELECT IdLote FROM @Lotes);"));
            sb.AppendLine("DELETE FROM dbo.CarguesLotesErrores WHERE IdLote IN (SELECT IdLote FROM @Lotes);");
            sb.AppendLine("DELETE FROM dbo.CarguesLotes WHERE IdLote IN (SELECT IdLote FROM @Lotes);");
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine("SELECT COUNT(*) FROM @Lotes;");

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sb.ToString(), cn))
            {
                cmd.CommandTimeout = 120;
                parametros(cmd);
                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Descarta (no confirmados) los lotes indicados. Lo usa el cierre de sesión — logout o expiración
        /// por inactividad — con los lotes que ESA sesión abrió (ver CargueLoteSesionHelper).
        /// </summary>
        public int DescartarLotes(IEnumerable<long> idsLote)
        {
            var ids = new List<long>(idsLote ?? new long[0]);
            if (ids.Count == 0) return 0;
            // Lista de BIGINT generada aquí (no viene del usuario): sin riesgo de inyección.
            return EjecutarBorradoDeLotes(
                $"IdLote IN ({string.Join(",", ids)}) AND Estado <> 'Confirmado'", cmd => { });
        }

        /// <summary>
        /// Purga oportunista (como mucho 1 vez cada 15 min) — red de seguridad para lo que los descartes
        /// automáticos (salir de la pantalla, subir otro archivo, cerrar/expirar la sesión) no alcanzaron a
        /// limpiar: navegador cerrado de golpe, caída de red, app pool reciclado en pleno cargue…
        /// Borra los lotes vencidos (<see cref="CondicionVencido"/>) y las filas de staging/hallazgos
        /// huérfanas (cuyo IdLote ya no existe en CarguesLotes, p. ej. por un borrado manual a medias).
        /// </summary>
        public void PurgarLotesVencidosSiToca()
        {
            if ((DateTime.Now - _ultimaPurga).TotalMinutes < 15) return;
            lock (_purgaLock)
            {
                if ((DateTime.Now - _ultimaPurga).TotalMinutes < 15) return;
                _ultimaPurga = DateTime.Now;
            }

            // En segundo plano: la página que la dispara no espera (la purga puede tener que aguardar a que
            // termine el cargue de otra empresa que tiene filas bloqueadas).
            if (System.Web.Hosting.HostingEnvironment.IsHosted)
                System.Web.Hosting.HostingEnvironment.QueueBackgroundWorkItem(ct => EjecutarPurga());
            else
                EjecutarPurga();
        }

        private void EjecutarPurga()
        {
            try
            {
                int vencidos = EjecutarBorradoDeLotes(CondicionVencido, cmd => { });

                // READPAST solo sobre el staging: salta filas que otro cargue está insertando (aún sin commit)
                // en vez de esperarlo. NO sobre CarguesLotes: saltarse una cabecera bloqueada (p. ej. en plena
                // validación) haría ver su staging como huérfano y lo borraría.
                var sb = new System.Text.StringBuilder("SET NOCOUNT ON; DECLARE @n INT = 0;\n");
                foreach (var t in TablasStaging)
                    sb.AppendLine(SiExiste(t, $"BEGIN DELETE s FROM dbo.{t} s WITH (READPAST) WHERE NOT EXISTS (SELECT 1 FROM dbo.CarguesLotes l WHERE l.IdLote = s.IdLote); SET @n += @@ROWCOUNT; END"));
                sb.AppendLine("DELETE e FROM dbo.CarguesLotesErrores e WITH (READPAST) WHERE NOT EXISTS (SELECT 1 FROM dbo.CarguesLotes l WHERE l.IdLote = e.IdLote); SET @n += @@ROWCOUNT;");
                sb.AppendLine("SELECT @n;");
                int huerfanas;
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(sb.ToString(), cn) { CommandTimeout = 120 })
                {
                    cn.Open();
                    huerfanas = Convert.ToInt32(cmd.ExecuteScalar());
                }

                if (vencidos > 0 || huerfanas > 0)
                    AppLogger.Info($"[CargueStagingService.Purga] Lotes vencidos borrados: {vencidos}; filas huérfanas borradas: {huerfanas}.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[CargueStagingService.PurgarLotesVencidosSiToca] {ex.Message}");
            }
        }

        private static CargueLote MapLote(SqlDataReader r) => new CargueLote
        {
            IdLote = Convert.ToInt64(r["IdLote"]),
            IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
            NombreEmpresa = r["NombreEmpresa"] as string,
            Anio = Convert.ToInt32(r["Anio"]),
            Modo = Convert.ToByte(r["Modo"]),
            IdEscenario = Convert.ToInt32(r["IdEscenario"]),
            IdUsuario = Convert.ToInt32(r["IdUsuario"]),
            NombreUsuario = r["NombreUsuario"] as string,
            NombreArchivo = r["NombreArchivo"] as string,
            FechaCreacion = Convert.ToDateTime(r["FechaCreacion"]),
            FechaValidacion = r["FechaValidacion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaValidacion"]),
            FechaConfirmacion = r["FechaConfirmacion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaConfirmacion"]),
            Estado = r["Estado"] as string,
            TotalFilas = Convert.ToInt32(r["TotalFilas"]),
            TotalErrores = Convert.ToInt32(r["TotalErrores"]),
            TotalAdvertencias = Convert.ToInt32(r["TotalAdvertencias"]),
            IdHistorialGenerado = r["IdHistorialGenerado"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["IdHistorialGenerado"])
        };
    }
}
