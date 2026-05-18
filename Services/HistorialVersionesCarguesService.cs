using bufinscustomers.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class HistorialVersionesCarguesService : BaseService
    {
        public const int MaxVersionesPorEscenario = 3;

        private static readonly string[] _tablasIni = {
            "Ini_BalancePrueba", "Ini_CteYnoCte", "Ini_EjecPCH", "Ini_PCH",
            "Ini_PptoPYG", "Ini_PptoPYGConAjuste", "Ini_PresupuestoBalance",
            "Ini_PYG", "Ini_PYGDetalladoConAjuste"
        };

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
            string nombreArchivo)
        {
            using (var cmd = new SqlCommand(@"
                UPDATE dbo.HistorialVersionesCargues
                SET EsVersionActual = 0
                WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                cmd.ExecuteNonQuery();
            }

            int totalFilas = ContarFilasActuales(conn, tx, idEmpresa, anio, modo);

            // Número global inmutable: siempre crece, nunca reinicia por mes
            int numeroVersionMes;
            using (var cmd = new SqlCommand(@"
                SELECT ISNULL(MAX(NumeroVersionMes), 0) + 1
                FROM dbo.HistorialVersionesCargues
                WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Modo", modo);
                numeroVersionMes = Convert.ToInt32(cmd.ExecuteScalar());
            }

            int idHistorial;
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

            string whereClause = modo == 0
                ? "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio AND Historico_Log = 0"
                : "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio";

            foreach (var tabla in _tablasIni)
            {
                string json = SerializarTabla(conn, tx, tabla, whereClause, idEmpresa, anio);
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.SnapshotsCargues (IdHistorial, NombreTabla, DatosJson)
                    VALUES (@IdHistorial, @NombreTabla, @DatosJson)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdHistorial", idHistorial);
                    cmd.Parameters.AddWithValue("@NombreTabla", tabla);
                    cmd.Parameters.AddWithValue("@DatosJson", json);
                    cmd.ExecuteNonQuery();
                }
            }

            PurgarVersionesAntiguas(conn, tx, idEmpresa, anio, modo);
            return idHistorial;
        }

        // ── Consultas ─────────────────────────────────────────────────────────

        public List<HistorialVersiones> ObtenerHistorial(int? idEmpresa, int? anio, byte? modo)
        {
            var list = new List<HistorialVersiones>();
            string sql = @"
                SELECT *
                FROM dbo.HistorialVersionesCargues
                WHERE (@IdEmpresa IS NULL OR IdEmpresa = @IdEmpresa)
                  AND (@Anio     IS NULL OR Anio      = @Anio)
                  AND (@Modo     IS NULL OR Modo      = @Modo)
                ORDER BY IdEmpresa, Anio, Modo, FechaCargue DESC";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", (object)idEmpresa ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Anio",      (object)anio     ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Modo",      (object)modo     ?? DBNull.Value);
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

        public bool EjecutarRollback(int idHistorial, int idUsuarioReversion, string nombreUsuarioReversion)
        {
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
                            "SELECT NombreTabla, DatosJson FROM dbo.SnapshotsCargues WHERE IdHistorial = @IdHistorial",
                            conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@IdHistorial", idHistorial);
                            using (var r = cmd.ExecuteReader())
                                while (r.Read())
                                    snapshots[r["NombreTabla"].ToString()] = r["DatosJson"].ToString();
                        }

                        string deleteWhere = version.Modo == 0
                            ? "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio AND Historico_Log = 0"
                            : "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio";

                        foreach (var tabla in _tablasIni)
                        {
                            using (var cmd = new SqlCommand(
                                $"DELETE FROM dbo.[{tabla}] {deleteWhere}", conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@IdEmpresa", version.IdEmpresa);
                                cmd.Parameters.AddWithValue("@Anio", version.Anio);
                                cmd.ExecuteNonQuery();
                            }

                            if (snapshots.TryGetValue(tabla, out string json) &&
                                !string.IsNullOrWhiteSpace(json) && json != "[]")
                            {
                                ReinsertarDesdeJson(conn, tx, tabla, json);
                            }
                        }

                        using (var cmd = new SqlCommand(@"
                            UPDATE dbo.HistorialVersionesCargues
                            SET EsVersionActual = 0
                            WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo;

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
                            cmd.Parameters.AddWithValue("@IdUsrRev",     idUsuarioReversion);
                            cmd.Parameters.AddWithValue("@NombreUsrRev", nombreUsuarioReversion);
                            cmd.Parameters.AddWithValue("@IdHistorial",  idHistorial);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        return true;
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                        return false;
                    }
                }
            }
        }

        // ── Helpers privados ──────────────────────────────────────────────────

        private int ContarFilasActuales(SqlConnection conn, SqlTransaction tx, int idEmpresa, int anio, byte modo)
        {
            int total = 0;
            string where = modo == 0
                ? "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio AND Historico_Log = 0"
                : "WHERE IdEmpresa_Log = @IdEmpresa AND Año = @Anio";

            foreach (var tabla in _tablasIni)
            {
                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM dbo.[{tabla}] {where}", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    total += Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            return total;
        }

        private string SerializarTabla(SqlConnection conn, SqlTransaction tx,
            string tabla, string whereClause, int idEmpresa, int anio)
        {
            try
            {
                var rows = new List<Dictionary<string, object>>();
                using (var cmd = new SqlCommand($"SELECT * FROM dbo.[{tabla}] {whereClause}", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
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
            catch
            {
                return "[]";
            }
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
            int idEmpresa, int anio, byte modo)
        {
            using (var cmd = new SqlCommand(@"
                DELETE FROM dbo.HistorialVersionesCargues
                WHERE Id IN (
                    SELECT Id FROM (
                        SELECT Id,
                               ROW_NUMBER() OVER (ORDER BY FechaCargue DESC) AS rn
                        FROM dbo.HistorialVersionesCargues
                        WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Modo = @Modo
                    ) ranked
                    WHERE rn > @MaxVersiones
                )", conn, tx))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa",   idEmpresa);
                cmd.Parameters.AddWithValue("@Anio",        anio);
                cmd.Parameters.AddWithValue("@Modo",        modo);
                cmd.Parameters.AddWithValue("@MaxVersiones", MaxVersionesPorEscenario);
                cmd.ExecuteNonQuery();
            }
        }

        private static HistorialVersiones MapHistorial(SqlDataReader r)
        {
            return new HistorialVersiones
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
        }
    }
}
