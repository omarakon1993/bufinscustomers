using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>Logo guardado de una empresa (ya normalizado a PNG por <c>EmpresaLogoHelper</c>).</summary>
    public class EmpresaLogoDatos
    {
        public int IdEmpresa { get; set; }
        public byte[] Imagen { get; set; }
        public string TipoImagen { get; set; }
        public int Ancho { get; set; }
        public int Alto { get; set; }
        public string Version { get; set; }
        public DateTime FechaActualizacion { get; set; }
    }

    /// <summary>
    /// Persistencia del logo de la empresa (<c>dbo.EmpresaLogos</c>, <c>Sql/036_EmpresaLogos.sql</c>, script manual).
    /// Si la tabla aún no existe (error 208) nada se rompe: no hay logos y <see cref="Disponible"/> lo dice, para que el
    /// gestor avise en vez de dar por guardado algo que no se almacenó.
    /// </summary>
    public class EmpresaLogoService : BaseService
    {
        private const int TablaInexistente = 208;

        /// <summary>false si la tabla no existe en esta BD (script 036 sin ejecutar).</summary>
        public bool Disponible()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.EmpresaLogos','U') IS NULL THEN 0 ELSE 1 END", cn))
                {
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch (SqlException) { return false; }
        }

        /// <summary>Logo completo (con imagen) o null si la empresa no tiene.</summary>
        public EmpresaLogoDatos Obtener(int idEmpresa)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"SELECT IdEmpresa, Imagen, TipoImagen, Ancho, Alto, Version, FechaActualizacion
                      FROM dbo.EmpresaLogos WHERE IdEmpresa = @id", cn))
                {
                    cmd.Parameters.AddWithValue("@id", idEmpresa);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        return new EmpresaLogoDatos
                        {
                            IdEmpresa = (int)r["IdEmpresa"],
                            Imagen = (byte[])r["Imagen"],
                            TipoImagen = (string)r["TipoImagen"],
                            Ancho = (int)r["Ancho"],
                            Alto = (int)r["Alto"],
                            Version = (string)r["Version"],
                            FechaActualizacion = (DateTime)r["FechaActualizacion"]
                        };
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { return null; }
        }

        /// <summary>Versión (hash) del logo de cada empresa que tiene uno; las demás no aparecen. Sin leer las imágenes.</summary>
        public Dictionary<int, string> ObtenerVersiones()
        {
            var d = new Dictionary<int, string>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT IdEmpresa, Version FROM dbo.EmpresaLogos", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) d[(int)r["IdEmpresa"]] = (string)r["Version"];
                }
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { /* sin script 036: sin logos */ }
            return d;
        }

        /// <summary>Crea o reemplaza el logo de la empresa (un único registro por empresa).</summary>
        public void Guardar(int idEmpresa, byte[] png, int ancho, int alto, string version, string nombreOriginal, int? idUsuario)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                @"UPDATE dbo.EmpresaLogos
                     SET Imagen = @img, TipoImagen = 'image/png', Ancho = @w, Alto = @h, Bytes = @b, Version = @v,
                         NombreOriginal = @n, FechaActualizacion = GETDATE(), IdUsuarioActualizo = @u
                   WHERE IdEmpresa = @id;
                  IF @@ROWCOUNT = 0
                      INSERT INTO dbo.EmpresaLogos (IdEmpresa, Imagen, TipoImagen, Ancho, Alto, Bytes, Version, NombreOriginal, IdUsuarioActualizo)
                      VALUES (@id, @img, 'image/png', @w, @h, @b, @v, @n, @u);", cn))
            {
                cmd.Parameters.AddWithValue("@id", idEmpresa);
                cmd.Parameters.Add("@img", SqlDbType.VarBinary, -1).Value = png;
                cmd.Parameters.AddWithValue("@w", ancho);
                cmd.Parameters.AddWithValue("@h", alto);
                cmd.Parameters.AddWithValue("@b", png.Length);
                cmd.Parameters.AddWithValue("@v", version);
                cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(nombreOriginal) ? (object)DBNull.Value : Truncar(nombreOriginal, 260));
                cmd.Parameters.AddWithValue("@u", idUsuario.HasValue ? (object)idUsuario.Value : DBNull.Value);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Quita el logo. true si había uno.</summary>
        public bool Eliminar(int idEmpresa)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM dbo.EmpresaLogos WHERE IdEmpresa = @id", cn))
                {
                    cmd.Parameters.AddWithValue("@id", idEmpresa);
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { return false; }
        }

        private static string Truncar(string s, int max)
        {
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
