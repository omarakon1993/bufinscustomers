using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>Comentario de gestión de un informe, anclado a empresa + informe + año/mes de cierre (+ una clave opcional, p. ej. la línea de negocio).</summary>
    public class ComentarioGestion
    {
        public long Id { get; set; }
        public int IdEmpresa { get; set; }
        public string Informe { get; set; }
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string Clave { get; set; }
        public string Texto { get; set; }
        public int IdUsuario { get; set; }
        public string NombreUsuario { get; set; }
        public string Fecha { get; set; }
        public string FechaEdicion { get; set; }
    }

    /// <summary>
    /// Comentarios de gestión de los informes (dbo.ComentariosGestion, Sql/040). Cualquier usuario con acceso a la empresa puede
    /// leer y escribir; el borrado es lógico (Activo = 0). Si la tabla aún no existe (script sin ejecutar) <c>Disponible</c> queda en
    /// false y los informes siguen funcionando sin la tarjeta de comentarios.
    /// </summary>
    public class ComentariosGestionService : BaseService
    {
        /// <summary>Informes que pueden llevar comentarios (clave corta que viaja en la petición; nunca texto libre).</summary>
        public static readonly string[] InformesValidos =
            { "LineasNegocio", "PYG", "Balance", "Tesoreria", "FlujoCaja", "FlujoEfectivo", "Indicadores", "GerenciaMensual" };

        public const int MaxTexto = 1000;
        public const int MaxClave = 150;

        private const int ErrorTablaNoExiste = 208;

        public bool InformeValido(string informe)
        {
            foreach (var i in InformesValidos) if (string.Equals(i, informe, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Comentarios del cierre (año/mes) de una empresa; <paramref name="informe"/> null = de todos los informes.</summary>
        public List<ComentarioGestion> Listar(int idEmpresa, int anio, int mes, string informe, out bool disponible)
        {
            disponible = true;
            var lista = new List<ComentarioGestion>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        SELECT Id, IdEmpresa, Informe, Anio, Mes, Clave, Texto, IdUsuario, NombreUsuario, FechaCreacion, FechaEdicion
                        FROM dbo.ComentariosGestion
                        WHERE IdEmpresa = @IdEmpresa AND Anio = @Anio AND Mes = @Mes AND Activo = 1
                          AND (@Informe IS NULL OR Informe = @Informe)
                        ORDER BY FechaCreacion DESC", cn);
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    cmd.Parameters.AddWithValue("@Mes", mes);
                    cmd.Parameters.AddWithValue("@Informe", (object)informe ?? DBNull.Value);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            lista.Add(new ComentarioGestion
                            {
                                Id = Convert.ToInt64(r["Id"]), IdEmpresa = Convert.ToInt32(r["IdEmpresa"]), Informe = r["Informe"] as string,
                                Anio = Convert.ToInt32(r["Anio"]), Mes = Convert.ToInt32(r["Mes"]), Clave = r["Clave"] as string,
                                Texto = r["Texto"] as string, IdUsuario = Convert.ToInt32(r["IdUsuario"]), NombreUsuario = r["NombreUsuario"] as string,
                                Fecha = Convert.ToDateTime(r["FechaCreacion"]).ToString("yyyy-MM-dd HH:mm"),
                                FechaEdicion = r["FechaEdicion"] == DBNull.Value ? null : Convert.ToDateTime(r["FechaEdicion"]).ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == ErrorTablaNoExiste)
            {
                disponible = false;
            }
            return lista;
        }

        public ComentarioGestion Obtener(long id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(@"SELECT Id, IdEmpresa, Informe, Anio, Mes, Clave, Texto, IdUsuario, NombreUsuario
                    FROM dbo.ComentariosGestion WHERE Id = @Id AND Activo = 1", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new ComentarioGestion
                    {
                        Id = Convert.ToInt64(r["Id"]), IdEmpresa = Convert.ToInt32(r["IdEmpresa"]), Informe = r["Informe"] as string,
                        Anio = Convert.ToInt32(r["Anio"]), Mes = Convert.ToInt32(r["Mes"]), Clave = r["Clave"] as string,
                        Texto = r["Texto"] as string, IdUsuario = Convert.ToInt32(r["IdUsuario"]), NombreUsuario = r["NombreUsuario"] as string
                    };
                }
            }
        }

        public long Crear(int idEmpresa, string informe, int anio, int mes, string clave, string texto, int idUsuario, string nombreUsuario)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO dbo.ComentariosGestion (IdEmpresa, Informe, Anio, Mes, Clave, Texto, IdUsuario, NombreUsuario)
                    VALUES (@IdEmpresa, @Informe, @Anio, @Mes, @Clave, @Texto, @IdUsuario, @NombreUsuario);
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Informe", informe);
                cmd.Parameters.AddWithValue("@Anio", anio);
                cmd.Parameters.AddWithValue("@Mes", mes);
                cmd.Parameters.AddWithValue("@Clave", string.IsNullOrWhiteSpace(clave) ? (object)DBNull.Value : clave);
                cmd.Parameters.AddWithValue("@Texto", texto);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@NombreUsuario", nombreUsuario ?? "");
                cn.Open();
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        public void Editar(long id, string texto)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("UPDATE dbo.ComentariosGestion SET Texto = @Texto, FechaEdicion = GETDATE() WHERE Id = @Id AND Activo = 1", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Texto", texto);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void Eliminar(long id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("UPDATE dbo.ComentariosGestion SET Activo = 0, FechaEdicion = GETDATE() WHERE Id = @Id", cn);
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
