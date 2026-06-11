using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class CuentasCorreoService : BaseService
    {
        public List<CuentaCorreo> ObtenerTodas()
        {
            var lista = new List<CuentaCorreo>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"SELECT Id, Nombre, Host, Puerto, Ssl, IgnorarCertificado,
                             Usuario, Contrasena, Remitente, NombreRemitente,
                             Predeterminada, Activa, FechaCreacion, FechaModificacion
                        FROM ConfiguracionCuentasCorreo
                       ORDER BY Predeterminada DESC, Activa DESC, Nombre", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) lista.Add(Map(r));
            }
            return lista;
        }

        public CuentaCorreo ObtenerPredeterminada()
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"SELECT TOP 1 Id, Nombre, Host, Puerto, Ssl, IgnorarCertificado,
                                   Usuario, Contrasena, Remitente, NombreRemitente,
                                   Predeterminada, Activa, FechaCreacion, FechaModificacion
                        FROM ConfiguracionCuentasCorreo
                       WHERE Predeterminada = 1 AND Activa = 1", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? Map(r) : null;
            }
        }

        public CuentaCorreo ObtenerPorId(int id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    @"SELECT Id, Nombre, Host, Puerto, Ssl, IgnorarCertificado,
                             Usuario, Contrasena, Remitente, NombreRemitente,
                             Predeterminada, Activa, FechaCreacion, FechaModificacion
                        FROM ConfiguracionCuentasCorreo WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? Map(r) : null;
            }
        }

        public bool Crear(CuentaCorreo c)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        // Si esta va a ser predeterminada, quitar la marca a las demás
                        if (c.Predeterminada) QuitarPredeterminada(cn, tx);

                        var cmd = new SqlCommand(
                            @"INSERT INTO ConfiguracionCuentasCorreo
                                (Nombre, Host, Puerto, Ssl, IgnorarCertificado,
                                 Usuario, Contrasena, Remitente, NombreRemitente,
                                 Predeterminada, Activa)
                              VALUES
                                (@Nombre, @Host, @Puerto, @Ssl, @IgnorarCert,
                                 @Usuario, @Contrasena, @Remitente, @NombreRemitente,
                                 @Predeterminada, @Activa)", cn, tx);
                        BindParams(cmd, c);
                        cmd.ExecuteNonQuery();
                        tx.Commit();
                        return true;
                    }
                    catch { tx.Rollback(); throw; }
                }
            }
        }

        public bool Editar(CuentaCorreo c)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        if (c.Predeterminada) QuitarPredeterminada(cn, tx, c.Id);

                        bool mantenerPass = string.IsNullOrWhiteSpace(c.Contrasena);
                        string sql = mantenerPass
                            ? @"UPDATE ConfiguracionCuentasCorreo
                                   SET Nombre=@Nombre, Host=@Host, Puerto=@Puerto, Ssl=@Ssl,
                                       IgnorarCertificado=@IgnorarCert, Usuario=@Usuario,
                                       Remitente=@Remitente, NombreRemitente=@NombreRemitente,
                                       Predeterminada=@Predeterminada, Activa=@Activa,
                                       FechaModificacion=GETDATE()
                                 WHERE Id=@Id"
                            : @"UPDATE ConfiguracionCuentasCorreo
                                   SET Nombre=@Nombre, Host=@Host, Puerto=@Puerto, Ssl=@Ssl,
                                       IgnorarCertificado=@IgnorarCert, Usuario=@Usuario,
                                       Contrasena=@Contrasena,
                                       Remitente=@Remitente, NombreRemitente=@NombreRemitente,
                                       Predeterminada=@Predeterminada, Activa=@Activa,
                                       FechaModificacion=GETDATE()
                                 WHERE Id=@Id";

                        var cmd = new SqlCommand(sql, cn, tx);
                        BindParams(cmd, c, incluirContrasena: !mantenerPass);
                        cmd.Parameters.AddWithValue("@Id", c.Id);
                        cmd.ExecuteNonQuery();
                        tx.Commit();
                        return true;
                    }
                    catch { tx.Rollback(); throw; }
                }
            }
        }

        public bool Eliminar(int id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(
                    "DELETE FROM ConfiguracionCuentasCorreo WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool EstablecerPredeterminada(int id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        QuitarPredeterminada(cn, tx, id);
                        var cmd = new SqlCommand(
                            "UPDATE ConfiguracionCuentasCorreo SET Predeterminada=1 WHERE Id=@Id",
                            cn, tx);
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.ExecuteNonQuery();
                        tx.Commit();
                        return true;
                    }
                    catch { tx.Rollback(); throw; }
                }
            }
        }

        public bool ToggleActiva(int id, out bool nuevaActiva)
        {
            nuevaActiva = false;
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                // Leer estado actual
                var read = new SqlCommand("SELECT Activa FROM ConfiguracionCuentasCorreo WHERE Id=@Id", cn);
                read.Parameters.AddWithValue("@Id", id);
                var actual = read.ExecuteScalar();
                if (actual == null) return false;

                nuevaActiva = !Convert.ToBoolean(actual);
                var upd = new SqlCommand(
                    "UPDATE ConfiguracionCuentasCorreo SET Activa=@Activa, FechaModificacion=GETDATE() WHERE Id=@Id", cn);
                upd.Parameters.AddWithValue("@Activa", nuevaActiva);
                upd.Parameters.AddWithValue("@Id", id);
                upd.ExecuteNonQuery();
                return true;
            }
        }

        // ────── privados ──────

        private static void QuitarPredeterminada(SqlConnection cn, SqlTransaction tx, int exceptoId = 0)
        {
            var cmd = new SqlCommand(
                "UPDATE ConfiguracionCuentasCorreo SET Predeterminada=0 WHERE Id <> @ExceptoId",
                cn, tx);
            cmd.Parameters.AddWithValue("@ExceptoId", exceptoId);
            cmd.ExecuteNonQuery();
        }

        private static void BindParams(SqlCommand cmd, CuentaCorreo c, bool incluirContrasena = true)
        {
            cmd.Parameters.AddWithValue("@Nombre",          c.Nombre?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@Host",            c.Host?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@Puerto",          c.Puerto);
            cmd.Parameters.AddWithValue("@Ssl",             c.Ssl);
            cmd.Parameters.AddWithValue("@IgnorarCert",     c.IgnorarCertificado);
            cmd.Parameters.AddWithValue("@Usuario",         c.Usuario?.Trim() ?? "");
            if (incluirContrasena)
                cmd.Parameters.AddWithValue("@Contrasena",  c.Contrasena?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@Remitente",       c.Remitente?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@NombreRemitente", c.NombreRemitente?.Trim() ?? "Bufins");
            cmd.Parameters.AddWithValue("@Predeterminada",  c.Predeterminada);
            cmd.Parameters.AddWithValue("@Activa",          c.Activa);
        }

        private static CuentaCorreo Map(SqlDataReader r) => new CuentaCorreo
        {
            Id                 = Convert.ToInt32(r["Id"]),
            Nombre             = r["Nombre"].ToString(),
            Host               = r["Host"].ToString(),
            Puerto             = Convert.ToInt32(r["Puerto"]),
            Ssl                = Convert.ToBoolean(r["Ssl"]),
            IgnorarCertificado = Convert.ToBoolean(r["IgnorarCertificado"]),
            Usuario            = r["Usuario"].ToString(),
            Contrasena         = r["Contrasena"].ToString(),
            Remitente          = r["Remitente"].ToString(),
            NombreRemitente    = r["NombreRemitente"].ToString(),
            Predeterminada     = Convert.ToBoolean(r["Predeterminada"]),
            Activa             = Convert.ToBoolean(r["Activa"]),
            FechaCreacion      = Convert.ToDateTime(r["FechaCreacion"]),
            FechaModificacion  = Convert.ToDateTime(r["FechaModificacion"]),
        };
    }
}
