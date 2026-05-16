using bufinscustomers.Models;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;

namespace bufinscustomers.Services
{
    public class TablaPUCService : BaseService
    {
        public List<string> ObtenerTipos()
        {
            var lista = new List<string>();
            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "SELECT DISTINCT Tipo FROM dbo.TablaPUC WHERE Tipo IS NOT NULL AND Tipo <> '' ORDER BY Tipo", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) lista.Add(r["Tipo"].ToString());
            }
            return lista;
        }

        public List<string> ObtenerLargos()
        {
            var lista = new List<string>();
            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "SELECT DISTINCT Largo FROM dbo.TablaPUC WHERE Largo IS NOT NULL AND Largo <> '' ORDER BY Largo", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) lista.Add(r["Largo"].ToString());
            }
            return lista;
        }

        public (List<TablaPUC> filas, int total) ConsultarDatos(FiltrosTablaPUC filtros)
        {
            var lista = new List<TablaPUC>();
            var sb = new StringBuilder("SELECT Cuenta, Nombre, Largo, Tipo FROM dbo.TablaPUC WHERE 1=1");

            if (!string.IsNullOrWhiteSpace(filtros.Tipo))   sb.Append(" AND Tipo = @Tipo");
            if (!string.IsNullOrWhiteSpace(filtros.Largo))  sb.Append(" AND Largo = @Largo");
            if (!string.IsNullOrWhiteSpace(filtros.Cuenta)) sb.Append(" AND Cuenta LIKE @Cuenta");
            if (!string.IsNullOrWhiteSpace(filtros.Nombre)) sb.Append(" AND Nombre LIKE @Nombre");

            sb.Append(" ORDER BY Cuenta");

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sb.ToString(), conn))
            {
                if (!string.IsNullOrWhiteSpace(filtros.Tipo))   cmd.Parameters.AddWithValue("@Tipo",   filtros.Tipo);
                if (!string.IsNullOrWhiteSpace(filtros.Largo))  cmd.Parameters.AddWithValue("@Largo",  filtros.Largo);
                if (!string.IsNullOrWhiteSpace(filtros.Cuenta)) cmd.Parameters.AddWithValue("@Cuenta", filtros.Cuenta + "%");
                if (!string.IsNullOrWhiteSpace(filtros.Nombre)) cmd.Parameters.AddWithValue("@Nombre", "%" + filtros.Nombre + "%");

                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        lista.Add(new TablaPUC
                        {
                            Cuenta = r["Cuenta"]?.ToString(),
                            Nombre = r["Nombre"]?.ToString(),
                            Largo  = r["Largo"]?.ToString(),
                            Tipo   = r["Tipo"]?.ToString()
                        });
                    }
                }
            }
            return (lista, lista.Count);
        }
    }
}
