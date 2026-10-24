using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>Paleta corporativa de una empresa (hasta 2 por empresa: Slot 1 y 2).</summary>
    public class EmpresaPaleta
    {
        public int IdEmpresa { get; set; }
        public byte Slot { get; set; }
        public string Nombre { get; set; }
        public string Primario { get; set; }
        public string Secundario { get; set; }
        public string Acento { get; set; }
    }

    /// <summary>
    /// Persistencia de los colores corporativos (<c>dbo.EmpresaPaletas</c>, <c>Sql/037_EmpresaPaletas.sql</c>, script manual).
    /// Si la tabla aún no existe (error 208) nada se rompe: no hay paletas y <see cref="Disponible"/> lo dice.
    /// </summary>
    public class EmpresaPaletaService : BaseService
    {
        private const int TablaInexistente = 208;

        public bool Disponible()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.EmpresaPaletas','U') IS NULL THEN 0 ELSE 1 END", cn))
                {
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch (SqlException) { return false; }
        }

        /// <summary>Las paletas de la empresa (0, 1 o 2), ordenadas por Slot.</summary>
        public List<EmpresaPaleta> Obtener(int idEmpresa)
        {
            var l = new List<EmpresaPaleta>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"SELECT IdEmpresa, Slot, Nombre, Primario, Secundario, Acento
                      FROM dbo.EmpresaPaletas WHERE IdEmpresa = @id ORDER BY Slot", cn))
                {
                    cmd.Parameters.AddWithValue("@id", idEmpresa);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            l.Add(new EmpresaPaleta
                            {
                                IdEmpresa = (int)r["IdEmpresa"],
                                Slot = (byte)r["Slot"],
                                Nombre = r["Nombre"] == DBNull.Value ? null : (string)r["Nombre"],
                                Primario = ((string)r["Primario"]).Trim(),
                                Secundario = ((string)r["Secundario"]).Trim(),
                                Acento = r["Acento"] == DBNull.Value ? null : ((string)r["Acento"]).Trim()
                            });
                }
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { /* sin script 037: sin paletas */ }
            return l;
        }

        public EmpresaPaleta Obtener(int idEmpresa, byte slot)
        {
            return Obtener(idEmpresa).Find(p => p.Slot == slot);
        }

        /// <summary>Crea o reemplaza la paleta del Slot (los colores ya llegan validados y normalizados a #RRGGBB).</summary>
        public void Guardar(EmpresaPaleta p, int? idUsuario)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                @"UPDATE dbo.EmpresaPaletas
                     SET Nombre = @n, Primario = @p, Secundario = @s, Acento = @a, FechaActualizacion = GETDATE(), IdUsuarioActualizo = @u
                   WHERE IdEmpresa = @id AND Slot = @slot;
                  IF @@ROWCOUNT = 0
                      INSERT INTO dbo.EmpresaPaletas (IdEmpresa, Slot, Nombre, Primario, Secundario, Acento, IdUsuarioActualizo)
                      VALUES (@id, @slot, @n, @p, @s, @a, @u);", cn))
            {
                cmd.Parameters.AddWithValue("@id", p.IdEmpresa);
                cmd.Parameters.AddWithValue("@slot", p.Slot);
                cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(p.Nombre) ? (object)DBNull.Value : p.Nombre.Trim());
                cmd.Parameters.AddWithValue("@p", p.Primario);
                cmd.Parameters.AddWithValue("@s", p.Secundario);
                cmd.Parameters.AddWithValue("@a", string.IsNullOrWhiteSpace(p.Acento) ? (object)DBNull.Value : p.Acento);
                cmd.Parameters.AddWithValue("@u", idUsuario.HasValue ? (object)idUsuario.Value : DBNull.Value);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Quita la paleta del Slot. true si existía.</summary>
        public bool Eliminar(int idEmpresa, byte slot)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM dbo.EmpresaPaletas WHERE IdEmpresa = @id AND Slot = @slot", cn))
                {
                    cmd.Parameters.AddWithValue("@id", idEmpresa);
                    cmd.Parameters.AddWithValue("@slot", slot);
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { return false; }
        }
    }
}
