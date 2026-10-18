using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Detalle de auditoría de cargues: una fila por tabla afectada (filas antes / filas nuevas) en cada cargue
    /// confirmado, cierre de año o rollback (dbo.CargueDetalleAuditoria, Sql/033). Si la tabla aún no existe,
    /// todo sigue funcionando sin detalle.
    /// </summary>
    public class CargueAuditoriaDetalleService : BaseService
    {
        private const string SqlInsert = @"
            IF OBJECT_ID(N'dbo.CargueDetalleAuditoria', N'U') IS NOT NULL
            INSERT INTO dbo.CargueDetalleAuditoria
                (IdAuditoriaCargue, Origen, Fecha, IdEmpresa, NombreEmpresa, Anio, Modo, IdEscenario, IdUsuario, NombreUsuario,
                 NombreArchivo, IdLote, IdHistorial, NombreHoja, NombreTabla, FilasAntes, FilasNuevas, Nota)
            VALUES
                (@IdAud, @Origen, GETDATE(), @IdEmpresa, @NombreEmpresa, @Anio, @Modo, @IdEscenario, @IdUsuario, @NombreUsuario,
                 @NombreArchivo, @IdLote, @IdHistorial, @NombreHoja, @NombreTabla, @FilasAntes, @FilasNuevas, @Nota)";

        private static object V(object o) => o ?? DBNull.Value;

        /// <summary>
        /// Registra las filas dentro de la transacción del llamador (así el detalle queda atómico con el cambio).
        /// Un fallo del detalle nunca debe tumbar el cargue: se loguea y se sigue.
        /// </summary>
        public void Registrar(SqlConnection conn, SqlTransaction tx, IEnumerable<CargueDetalleFila> filas)
        {
            try
            {
                foreach (var f in filas)
                {
                    using (var cmd = new SqlCommand(SqlInsert, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@IdAud", V(f.IdAuditoriaCargue));
                        cmd.Parameters.AddWithValue("@Origen", f.Origen);
                        cmd.Parameters.AddWithValue("@IdEmpresa", f.IdEmpresa);
                        cmd.Parameters.AddWithValue("@NombreEmpresa", V(f.NombreEmpresa));
                        cmd.Parameters.AddWithValue("@Anio", V(f.Anio));
                        cmd.Parameters.AddWithValue("@Modo", V(f.Modo));
                        cmd.Parameters.AddWithValue("@IdEscenario", V(f.IdEscenario));
                        cmd.Parameters.AddWithValue("@IdUsuario", V(f.IdUsuario));
                        cmd.Parameters.AddWithValue("@NombreUsuario", V(f.NombreUsuario));
                        cmd.Parameters.AddWithValue("@NombreArchivo", V(f.NombreArchivo));
                        cmd.Parameters.AddWithValue("@IdLote", V(f.IdLote));
                        cmd.Parameters.AddWithValue("@IdHistorial", V(f.IdHistorial));
                        cmd.Parameters.AddWithValue("@NombreHoja", V(f.NombreHoja));
                        cmd.Parameters.AddWithValue("@NombreTabla", f.NombreTabla);
                        cmd.Parameters.AddWithValue("@FilasAntes", f.FilasAntes);
                        cmd.Parameters.AddWithValue("@FilasNuevas", f.FilasNuevas);
                        cmd.Parameters.AddWithValue("@Nota", V(f.Nota));
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException ex)
            {
                AppLogger.Error(ex, "CargueAuditoriaDetalleService.Registrar");
            }
        }

        /// <summary>Detalle de un cargue (fila de AuditoriaCargues). Vacío si no hay detalle o la tabla no existe.</summary>
        public List<CargueDetalleFila> ObtenerPorAuditoria(int idAuditoriaCargue)
        {
            var lista = new List<CargueDetalleFila>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.CargueDetalleAuditoria', N'U') IS NOT NULL
                        SELECT * FROM dbo.CargueDetalleAuditoria WHERE IdAuditoriaCargue = @Id
                        ORDER BY FilasAntes DESC, NombreTabla", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idAuditoriaCargue);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r != null && r.Read())
                        {
                            lista.Add(new CargueDetalleFila
                            {
                                Id = Convert.ToInt64(r["Id"]),
                                IdAuditoriaCargue = r["IdAuditoriaCargue"] as int?,
                                Origen = r["Origen"] as string,
                                Fecha = Convert.ToDateTime(r["Fecha"]),
                                IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
                                NombreEmpresa = r["NombreEmpresa"] as string,
                                Anio = r["Anio"] as int?,
                                Modo = r["Modo"] == DBNull.Value ? (byte?)null : Convert.ToByte(r["Modo"]),
                                IdEscenario = r["IdEscenario"] as int?,
                                IdHistorial = r["IdHistorial"] as int?,
                                NombreHoja = r["NombreHoja"] as string,
                                NombreTabla = r["NombreTabla"] as string,
                                FilasAntes = Convert.ToInt32(r["FilasAntes"]),
                                FilasNuevas = Convert.ToInt32(r["FilasNuevas"]),
                                Nota = r["Nota"] as string
                            });
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                AppLogger.Error(ex, "CargueAuditoriaDetalleService.ObtenerPorAuditoria");
            }
            return lista;
        }
    }
}
