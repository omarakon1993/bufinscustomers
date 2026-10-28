using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Umbrales propios por empresa del semáforo de indicadores (dbo.IndicadoresUmbrales, Sql/042). Sin fila, la empresa usa los
    /// cortes generales del catálogo; si la tabla no existe todo sigue con los generales y <c>disponible</c> sale en false.
    /// Solo se pueden personalizar los indicadores con dirección (más es mejor / menos es mejor), no monetarios y sin regla propia.
    /// </summary>
    public class IndicadoresUmbralesService : BaseService
    {
        private const int ErrorTablaNoExiste = 208;

        /// <summary>true si el indicador admite umbrales propios.</summary>
        public static bool EsEditable(IndicadorDef d)
        {
            return d != null && d.MasEsMejor.HasValue && d.NivelPersonalizado == null && d.Cortes != null && d.Cortes.Length == 3 && d.Formato != "moneda";
        }

        public Dictionary<string, decimal[]> Obtener(int idEmpresa, out bool disponible)
        {
            disponible = true;
            var r = new Dictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("SELECT Codigo, Corte1, Corte2, Corte3 FROM dbo.IndicadoresUmbrales WHERE IdEmpresa = @IdEmpresa", cn);
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cn.Open();
                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read())
                            r[(string)rd["Codigo"]] = new[] { Convert.ToDecimal(rd["Corte1"]), Convert.ToDecimal(rd["Corte2"]), Convert.ToDecimal(rd["Corte3"]) };
                }
            }
            catch (SqlException ex) when (ex.Number == ErrorTablaNoExiste) { disponible = false; }
            return r;
        }

        /// <summary>Valida los 3 cortes según la dirección del indicador. Devuelve la clave de recurso del error o null si son válidos.</summary>
        public static string Validar(IndicadorDef d, decimal[] c)
        {
            if (!EsEditable(d)) return "Ind_Umb_NoEditable";
            if (c == null || c.Length != 3) return "Ind_Umb_Invalido";
            bool ok = d.MasEsMejor.Value ? (c[0] >= c[1] && c[1] >= c[2]) : (c[0] <= c[1] && c[1] <= c[2]);
            return ok ? null : (d.MasEsMejor.Value ? "Ind_Umb_OrdenMas" : "Ind_Umb_OrdenMenos");
        }

        public void Guardar(int idEmpresa, string codigo, decimal[] c, int idUsuario)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(@"
                    UPDATE dbo.IndicadoresUmbrales SET Corte1 = @C1, Corte2 = @C2, Corte3 = @C3, IdUsuario = @U, Fecha = GETDATE()
                    WHERE IdEmpresa = @E AND Codigo = @K;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.IndicadoresUmbrales (IdEmpresa, Codigo, Corte1, Corte2, Corte3, IdUsuario) VALUES (@E, @K, @C1, @C2, @C3, @U);", cn);
                cmd.Parameters.AddWithValue("@E", idEmpresa);
                cmd.Parameters.AddWithValue("@K", codigo);
                cmd.Parameters.AddWithValue("@C1", c[0]); cmd.Parameters.AddWithValue("@C2", c[1]); cmd.Parameters.AddWithValue("@C3", c[2]);
                cmd.Parameters.AddWithValue("@U", idUsuario);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void Restablecer(int idEmpresa, string codigo)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("DELETE FROM dbo.IndicadoresUmbrales WHERE IdEmpresa = @E AND Codigo = @K", cn);
                cmd.Parameters.AddWithValue("@E", idEmpresa);
                cmd.Parameters.AddWithValue("@K", codigo);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Copia de la definición con otros cortes (el catálogo es estático y compartido: nunca se modifica).</summary>
        public static IndicadorDef ConCortes(IndicadorDef d, decimal[] cortes)
        {
            return new IndicadorDef
            {
                Codigo = d.Codigo, Familia = d.Familia, Formato = d.Formato, MasEsMejor = d.MasEsMejor, Cortes = cortes,
                NecesitaLtmCompleto = d.NecesitaLtmCompleto, UsaFlujoCaja = d.UsaFlujoCaja, UsaBalance = d.UsaBalance,
                Calc = d.Calc, NivelPersonalizado = d.NivelPersonalizado
            };
        }
    }
}
