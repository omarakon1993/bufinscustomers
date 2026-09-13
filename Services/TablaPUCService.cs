using bufinscustomers.Models;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class TablaPUCService : BaseService
    {
        /// <summary>
        /// Devuelve los hijos directos de una cuenta (o el nivel raíz si <paramref name="cuenta"/> es
        /// nulo/vacío), calculando el siguiente "Largo" dinámicamente a partir de los datos ya cargados
        /// (sin asumir que la jerarquía es siempre Clase/Grupo/Cuenta/Subcuenta). TieneHijos indica si,
        /// a su vez, existe al menos un nivel más por debajo de cada hijo.
        /// </summary>
        public List<TablaPUC> ObtenerHijos(string cuenta)
        {
            var lista = new List<TablaPUC>();
            var sql = @"
DECLARE @LargoActual INT = NULL;
IF (@Cuenta IS NOT NULL AND @Cuenta <> '')
    SELECT @LargoActual = TRY_CAST(Largo AS INT) FROM dbo.TablaPUC WHERE Cuenta = @Cuenta;

DECLARE @LargoHijos INT;
SELECT @LargoHijos = MIN(TRY_CAST(Largo AS INT)) FROM dbo.TablaPUC
WHERE (@LargoActual IS NULL OR TRY_CAST(Largo AS INT) > @LargoActual);

DECLARE @LargoNietos INT;
IF (@LargoHijos IS NOT NULL)
    SELECT @LargoNietos = MIN(TRY_CAST(Largo AS INT)) FROM dbo.TablaPUC WHERE TRY_CAST(Largo AS INT) > @LargoHijos;

SELECT h.Cuenta, h.Nombre, h.Largo, h.Tipo, h.Descripcion,
       CASE WHEN @LargoNietos IS NOT NULL AND EXISTS (
           SELECT 1 FROM dbo.TablaPUC n
           WHERE TRY_CAST(n.Largo AS INT) = @LargoNietos AND n.Cuenta LIKE h.Cuenta + '%'
       ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS TieneHijos
FROM dbo.TablaPUC h
WHERE @LargoHijos IS NOT NULL
  AND TRY_CAST(h.Largo AS INT) = @LargoHijos
  AND (@Cuenta IS NULL OR @Cuenta = '' OR h.Cuenta LIKE @Cuenta + '%')
ORDER BY h.Cuenta;";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Cuenta", (object)cuenta ?? System.DBNull.Value);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        lista.Add(new TablaPUC
                        {
                            Cuenta = r["Cuenta"]?.ToString(),
                            Nombre = r["Nombre"]?.ToString(),
                            Largo = r["Largo"]?.ToString(),
                            Tipo = r["Tipo"]?.ToString(),
                            Descripcion = r["Descripcion"] == System.DBNull.Value ? null : r["Descripcion"].ToString(),
                            TieneHijos = r["TieneHijos"] != System.DBNull.Value && (bool)r["TieneHijos"]
                        });
                    }
                }
            }
            return lista;
        }

        /// <summary>
        /// Devuelve la cadena de ancestros de una cuenta (incluida ella misma), ordenada del nivel más
        /// alto (Clase) al más bajo — para reconstruir el breadcrumb tras una búsqueda o un enlace directo.
        /// </summary>
        public List<TablaPUC> ObtenerRuta(string cuenta)
        {
            var lista = new List<TablaPUC>();
            if (string.IsNullOrWhiteSpace(cuenta)) return lista;

            const string sql = @"
SELECT Cuenta, Nombre, Largo, Tipo, Descripcion
FROM dbo.TablaPUC
WHERE Cuenta = @Cuenta OR (@Cuenta LIKE Cuenta + '%' AND Cuenta <> @Cuenta)
ORDER BY TRY_CAST(Largo AS INT);";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Cuenta", cuenta);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        lista.Add(new TablaPUC
                        {
                            Cuenta = r["Cuenta"]?.ToString(),
                            Nombre = r["Nombre"]?.ToString(),
                            Largo = r["Largo"]?.ToString(),
                            Tipo = r["Tipo"]?.ToString(),
                            Descripcion = r["Descripcion"] == System.DBNull.Value ? null : r["Descripcion"].ToString()
                        });
                    }
                }
            }
            return lista;
        }

        /// <summary>
        /// Busca por código (si el texto es numérico) o por nombre, con el match exacto/prefijo de
        /// código primero. Usado por el buscador de salto directo del explorador.
        /// </summary>
        public List<TablaPUC> Buscar(string texto)
        {
            var lista = new List<TablaPUC>();
            texto = (texto ?? "").Trim();
            if (texto.Length < 2) return lista;

            bool esNumerico = true;
            foreach (var ch in texto)
                if (!char.IsDigit(ch)) { esNumerico = false; break; }

            const string sql = @"
SELECT TOP 25 Cuenta, Nombre, Largo, Tipo
FROM dbo.TablaPUC
WHERE (@EsNumerico = 1 AND Cuenta LIKE @Texto + '%') OR Nombre LIKE '%' + @Texto + '%'
ORDER BY
    CASE WHEN Cuenta = @Texto THEN 0 WHEN Cuenta LIKE @Texto + '%' THEN 1 ELSE 2 END,
    TRY_CAST(Largo AS INT), Cuenta;";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Texto", texto);
                cmd.Parameters.AddWithValue("@EsNumerico", esNumerico ? 1 : 0);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        lista.Add(new TablaPUC
                        {
                            Cuenta = r["Cuenta"]?.ToString(),
                            Nombre = r["Nombre"]?.ToString(),
                            Largo = r["Largo"]?.ToString(),
                            Tipo = r["Tipo"]?.ToString()
                        });
                    }
                }
            }
            return lista;
        }

        /// <summary>
        /// Devuelve una cuenta y todas sus descendientes (o el catálogo completo si <paramref name="cuenta"/>
        /// es nulo/vacío). Usado exclusivamente por la exportación a Excel.
        /// </summary>
        public List<TablaPUC> ObtenerSubarbol(string cuenta)
        {
            var lista = new List<TablaPUC>();
            const string sql = @"
SELECT Cuenta, Nombre, Largo, Tipo, Descripcion
FROM dbo.TablaPUC
WHERE @Cuenta IS NULL OR @Cuenta = '' OR Cuenta LIKE @Cuenta + '%'
ORDER BY Cuenta;";

            using (var conn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Cuenta", (object)cuenta ?? System.DBNull.Value);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        lista.Add(new TablaPUC
                        {
                            Cuenta = r["Cuenta"]?.ToString(),
                            Nombre = r["Nombre"]?.ToString(),
                            Largo = r["Largo"]?.ToString(),
                            Tipo = r["Tipo"]?.ToString(),
                            Descripcion = r["Descripcion"] == System.DBNull.Value ? null : r["Descripcion"].ToString()
                        });
                    }
                }
            }
            return lista;
        }
    }
}
