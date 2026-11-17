using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace bufinscustomers.Services
{
    // Auditoria: parte de CargueOrquestadorService (misma clase; solo se separa el codigo por responsabilidad).
    public partial class CargueOrquestadorService
    {
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
