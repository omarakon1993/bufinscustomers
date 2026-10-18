using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Conserva los últimos N archivos Excel de cargue CONFIRMADOS por empresa (N = parámetro
    /// <c>Cargue_ArchivosConservados</c>, 5 por defecto), comprimidos con GZip en dbo.CargueArchivos (Sql/033).
    /// El archivo se guarda como «Pendiente» al subirlo y pasa a «Confirmado» en la transacción de confirmación;
    /// los pendientes cuyo lote ya no existe (descartado, vencido) se barren en <see cref="Purgar"/>.
    /// Nada de aquí debe tumbar un cargue: si falla, se loguea y el cargue sigue.
    /// </summary>
    public class CargueArchivosService : BaseService
    {
        /// <summary>Guarda el archivo recién subido ligado a su lote. Devuelve false si no se guardó (sin tabla, N=0, error).</summary>
        public bool GuardarPendiente(CargueLote lote, byte[] contenido)
        {
            try
            {
                if (contenido == null || contenido.Length == 0) return false;
                if (CargueImpactoService.LeerArchivosConservados() <= 0) return false;

                byte[] gz;
                using (var ms = new MemoryStream())
                {
                    using (var z = new GZipStream(ms, CompressionMode.Compress, true)) z.Write(contenido, 0, contenido.Length);
                    gz = ms.ToArray();
                }

                var sb = new StringBuilder();
                using (var sha = SHA256.Create())
                    foreach (byte b in sha.ComputeHash(contenido)) sb.Append(b.ToString("x2"));

                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.CargueArchivos', N'U') IS NOT NULL
                    INSERT INTO dbo.CargueArchivos
                        (IdLote, Estado, IdEmpresa, NombreEmpresa, Anio, Modo, IdEscenario, IdUsuario, NombreUsuario,
                         NombreArchivo, FechaCarga, TamanoBytes, Sha256, DatosGzip)
                    VALUES
                        (@IdLote, 'Pendiente', @IdEmpresa, @NombreEmpresa, @Anio, @Modo, @IdEscenario, @IdUsuario, @NombreUsuario,
                         @NombreArchivo, GETDATE(), @Tamano, @Sha, @Datos)", cn) { CommandTimeout = 120 })
                {
                    cmd.Parameters.AddWithValue("@IdLote", lote.IdLote);
                    cmd.Parameters.AddWithValue("@IdEmpresa", lote.IdEmpresa);
                    cmd.Parameters.AddWithValue("@NombreEmpresa", (object)lote.NombreEmpresa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Anio", lote.Anio);
                    cmd.Parameters.AddWithValue("@Modo", lote.Modo);
                    cmd.Parameters.AddWithValue("@IdEscenario", lote.IdEscenario);
                    cmd.Parameters.AddWithValue("@IdUsuario", lote.IdUsuario);
                    cmd.Parameters.AddWithValue("@NombreUsuario", (object)lote.NombreUsuario ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@NombreArchivo", (object)lote.NombreArchivo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Tamano", contenido.Length);
                    cmd.Parameters.AddWithValue("@Sha", sb.ToString());
                    cmd.Parameters.Add("@Datos", System.Data.SqlDbType.VarBinary, -1).Value = gz;
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "CargueArchivosService.GuardarPendiente");
                return false;
            }
        }

        /// <summary>Dentro de la transacción de confirmación: el archivo del lote pasa a «Confirmado» y queda ligado a su auditoría.</summary>
        public void MarcarConfirmado(SqlConnection conn, SqlTransaction tx, long idLote, int? idAuditoriaCargue)
        {
            try
            {
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.CargueArchivos', N'U') IS NOT NULL
                        UPDATE dbo.CargueArchivos
                           SET Estado = 'Confirmado', IdAuditoriaCargue = @IdAud, IdLote = NULL
                         WHERE IdLote = @IdLote AND Estado = 'Pendiente'", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdAud", (object)idAuditoriaCargue ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdLote", idLote);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (SqlException ex) { AppLogger.Error(ex, "CargueArchivosService.MarcarConfirmado"); }
        }

        /// <summary>
        /// Deja solo los N confirmados más recientes de la empresa y barre los pendientes huérfanos (su lote ya no
        /// existe). Se llama después del commit de una confirmación.
        /// </summary>
        public void Purgar(int idEmpresa)
        {
            try
            {
                int n = CargueImpactoService.LeerArchivosConservados();
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.CargueArchivos', N'U') IS NOT NULL
                    BEGIN
                        DELETE a FROM dbo.CargueArchivos a
                         WHERE a.Estado = 'Pendiente'
                           AND (a.IdLote IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.CarguesLotes l WHERE l.IdLote = a.IdLote));

                        DELETE FROM dbo.CargueArchivos
                         WHERE Id IN (SELECT Id FROM (
                                        SELECT Id, ROW_NUMBER() OVER (ORDER BY FechaCarga DESC, Id DESC) rn
                                          FROM dbo.CargueArchivos
                                         WHERE Estado = 'Confirmado' AND IdEmpresa = @IdEmpresa) x
                                      WHERE x.rn > @N);
                    END", cn) { CommandTimeout = 60 })
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@N", n);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "CargueArchivosService.Purgar"); }
        }

        /// <summary>Metadatos del archivo ligado a un cargue auditado, o null si no se conservó (o ya se purgó).</summary>
        public CargueArchivoInfo ObtenerInfoPorAuditoria(int idAuditoriaCargue)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID(N'dbo.CargueArchivos', N'U') IS NOT NULL
                        SELECT TOP 1 Id, IdEmpresa, NombreArchivo, FechaCarga, TamanoBytes, Sha256
                          FROM dbo.CargueArchivos WHERE IdAuditoriaCargue = @Id AND Estado = 'Confirmado'
                         ORDER BY Id DESC", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", idAuditoriaCargue);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        return r != null && r.Read() ? Mapear(r) : null;
                }
            }
            catch (SqlException ex) { AppLogger.Error(ex, "CargueArchivosService.ObtenerInfoPorAuditoria"); return null; }
        }

        /// <summary>Contenido (descomprimido) de un archivo conservado, con su metadato. null si no existe.</summary>
        public (CargueArchivoInfo info, byte[] contenido)? Obtener(long id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT Id, IdEmpresa, NombreArchivo, FechaCarga, TamanoBytes, Sha256, DatosGzip
                      FROM dbo.CargueArchivos WHERE Id = @Id AND Estado = 'Confirmado'", cn) { CommandTimeout = 120 })
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        var info = Mapear(r);
                        var gz = (byte[])r["DatosGzip"];
                        using (var origen = new MemoryStream(gz))
                        using (var z = new GZipStream(origen, CompressionMode.Decompress))
                        using (var destino = new MemoryStream())
                        {
                            z.CopyTo(destino);
                            return (info, destino.ToArray());
                        }
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "CargueArchivosService.Obtener"); return null; }
        }

        private static CargueArchivoInfo Mapear(SqlDataReader r) => new CargueArchivoInfo
        {
            Id = Convert.ToInt64(r["Id"]),
            IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
            NombreArchivo = r["NombreArchivo"] as string,
            FechaCarga = Convert.ToDateTime(r["FechaCarga"]),
            TamanoBytes = Convert.ToInt32(r["TamanoBytes"]),
            Sha256 = (r["Sha256"] as string)?.Trim()
        };
    }
}
