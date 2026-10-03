using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

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
        private static readonly string[] TablasStaging =
        {
            "Staging_Ini_BalancePrueba", "Staging_Ini_CteYnoCte", "Staging_Ini_EjecPCH", "Staging_Ini_PCH",
            "Staging_Ini_PptoPYG", "Staging_Ini_PptoPYGConAjuste", "Staging_Ini_PresupuestoBalance",
            "Staging_Ini_PYG", "Staging_Ini_PYGDetalladoConAjuste"
        };

        private static DateTime _ultimaPurga = DateTime.MinValue;
        private static readonly object _purgaLock = new object();

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
        /// mostrar el detalle "por hoja" en el resumen de un cargue ya confirmado. Debe llamarse
        /// ANTES de <see cref="LimpiarStagingDeLote"/> (que borra el staging).
        /// </summary>
        public List<(string NombreHoja, string NombreTabla, int Filas)> ObtenerConteoPorHoja(long idLote)
        {
            var resultado = new List<(string, string, int)>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                foreach (var kv in TablasCargueHelper.MapeoZaIni)
                {
                    string nombreStaging = TablasCargueHelper.NombreStaging(kv.Value);
                    try
                    {
                        using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM dbo.[{nombreStaging}] WHERE IdLote = @IdLote", cn))
                        {
                            cmd.Parameters.AddWithValue("@IdLote", idLote);
                            int filas = (int)cmd.ExecuteScalar();
                            if (filas > 0) resultado.Add((kv.Key, kv.Value, filas));
                        }
                    }
                    catch (SqlException) { /* tabla de staging no existe en esta BD: se omite */ }
                }
            }
            return resultado;
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

        /// <summary>Borra el staging de un lote (y su cabecera) sin tocar nada real.</summary>
        public void DescartarLote(long idLote)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(ConstruirBorradoPorLote(), cn))
            {
                cmd.CommandTimeout = 120;
                cmd.Parameters.AddWithValue("@IdLote", idLote);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Igual que <see cref="DescartarLote"/> pero llamado tras confirmar (limpia el staging ya movido a real).</summary>
        public void LimpiarStagingDeLote(long idLote) => DescartarLote(idLote);

        private static string ConstruirBorradoPorLote()
        {
            var sb = new System.Text.StringBuilder("SET NOCOUNT ON;\n");
            foreach (var t in TablasStaging)
                sb.AppendLine($"DELETE FROM dbo.{t} WHERE IdLote = @IdLote;");
            sb.AppendLine("DELETE FROM dbo.CarguesLotesErrores WHERE IdLote = @IdLote;");
            sb.AppendLine("DELETE FROM dbo.CarguesLotes WHERE IdLote = @IdLote;");
            return sb.ToString();
        }

        /// <summary>Purga oportunista (como mucho 1 vez cada 24 h) de lotes nunca confirmados y vencidos.</summary>
        public void PurgarLotesVencidosSiToca(int horasAntiguedad = 48)
        {
            if ((DateTime.Now - _ultimaPurga).TotalHours < 24) return;
            lock (_purgaLock)
            {
                if ((DateTime.Now - _ultimaPurga).TotalHours < 24) return;
                _ultimaPurga = DateTime.Now;
            }

            try
            {
                var sb = new System.Text.StringBuilder(@"
                    SET NOCOUNT ON;
                    DECLARE @Vencidos TABLE (IdLote BIGINT PRIMARY KEY);
                    INSERT INTO @Vencidos (IdLote)
                    SELECT IdLote FROM dbo.CarguesLotes
                    WHERE Estado <> 'Confirmado' AND FechaCreacion < DATEADD(HOUR, -@Horas, GETDATE());
                ");
                foreach (var t in TablasStaging)
                    sb.AppendLine($"DELETE FROM dbo.{t} WHERE IdLote IN (SELECT IdLote FROM @Vencidos);");
                sb.AppendLine("DELETE FROM dbo.CarguesLotesErrores WHERE IdLote IN (SELECT IdLote FROM @Vencidos);");
                sb.AppendLine("DELETE FROM dbo.CarguesLotes WHERE IdLote IN (SELECT IdLote FROM @Vencidos);");

                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(sb.ToString(), cn))
                {
                    cmd.CommandTimeout = 120;
                    cmd.Parameters.AddWithValue("@Horas", horasAntiguedad);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[CargueStagingService.PurgarLotesVencidosSiToca] {0}", ex.Message);
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
