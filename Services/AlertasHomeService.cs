using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Alertas del Home. Ejecuta <c>dbo.sp_ObtenerAlertasHome</c>, cuyas reglas (un BLOQUE por alerta) viven
    /// íntegramente en el SP — igual que <c>sp_ValidarCargueStaging</c>: agregar una alerta = un bloque más
    /// ahí, sin tocar C#. Ver <c>Sql/StoredProcedures/sp_ObtenerAlertasHome.sql</c>.
    /// </summary>
    public class AlertasHomeService : BaseService
    {
        /// <param name="idsEmpresas">Empresas visibles para el usuario; <c>null</c> = todas (Super Admin).
        /// Lista vacía = ninguna (no se consulta).</param>
        public List<AlertaHome> Obtener(List<int> idsEmpresas)
        {
            var lista = new List<AlertaHome>();
            if (idsEmpresas != null && idsEmpresas.Count == 0) return lista;

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("sp_ObtenerAlertasHome", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@IdsEmpresas",
                    idsEmpresas == null ? (object)DBNull.Value : string.Join(",", idsEmpresas.Distinct()));
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    // 1.er result set: CodMessage / ErrorMessage / totales (mismo cierre que sp_ValidarCargueStaging).
                    if (!r.Read() || Convert.ToInt32(r["CodMessage"]) != 1)
                        throw new InvalidOperationException(
                            "sp_ObtenerAlertasHome: " + (r.HasRows && r["ErrorMessage"] != DBNull.Value ? r["ErrorMessage"].ToString() : "sin respuesta"));

                    // 2.º result set: un renglón por alerta.
                    if (r.NextResult())
                        while (r.Read())
                            lista.Add(new AlertaHome
                            {
                                IdEmpresa     = Convert.ToInt32(r["IdEmpresa"]),
                                NombreEmpresa = r["NombreEmpresa"] == DBNull.Value ? null : r["NombreEmpresa"].ToString(),
                                Severidad     = r["Severidad"].ToString(),
                                CodigoRegla   = r["CodigoRegla"] == DBNull.Value ? null : r["CodigoRegla"].ToString(),
                                Titulo        = r["Titulo"] == DBNull.Value ? null : r["Titulo"].ToString(),
                                TituloEn      = r["TituloEn"] == DBNull.Value ? null : r["TituloEn"].ToString(),
                                Mensaje       = r["Mensaje"] == DBNull.Value ? null : r["Mensaje"].ToString(),
                                MensajeEn     = r["MensajeEn"] == DBNull.Value ? null : r["MensajeEn"].ToString()
                            });
                }
            }
            return lista;
        }
    }
}
