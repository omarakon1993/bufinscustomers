using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Lee dbo.EmpresaPersonalizaciones (Sql/020_PersonalizacionesEmpresa_HistPreciosChurido.sql):
    /// qué paquetes de personalización (definidos en Helpers/PersonalizacionesEmpresaHelper.cs)
    /// tiene activos cada empresa. Lo cachea <see cref="Helpers.PersonalizacionesEmpresaHelper"/>.
    /// </summary>
    public class PersonalizacionEmpresaService : BaseService
    {
        /// <summary>
        /// IdEmpresa → códigos de personalización activos. Si la tabla aún no existe en la BD
        /// (script 020 sin ejecutar) devuelve un diccionario vacío: ninguna empresa personalizada.
        /// </summary>
        public Dictionary<int, HashSet<string>> ObtenerAsignacionesActivas()
        {
            var resultado = new Dictionary<int, HashSet<string>>();

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                IF OBJECT_ID('dbo.EmpresaPersonalizaciones', 'U') IS NOT NULL
                    SELECT IdEmpresa, Codigo FROM dbo.EmpresaPersonalizaciones WHERE Activo = 1;", cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int idEmpresa = Convert.ToInt32(r["IdEmpresa"]);
                        string codigo = Convert.ToString(r["Codigo"])?.Trim();
                        if (string.IsNullOrEmpty(codigo)) continue;

                        if (!resultado.TryGetValue(idEmpresa, out var codigos))
                            resultado[idEmpresa] = codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        codigos.Add(codigo);
                    }
                }
            }
            return resultado;
        }
    }
}
