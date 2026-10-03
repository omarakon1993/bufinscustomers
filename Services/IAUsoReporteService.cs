using bufinscustomers.Helpers;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class UsoUsuarioFila
    {
        public string Usuario { get; set; }
        public int Consultas { get; set; }
        public long Tokens { get; set; }
    }

    public class UsoFuncionFila
    {
        public string Funcion { get; set; }
        public int Consultas { get; set; }
        public long Tokens { get; set; }
        public decimal Costo { get; set; }
    }

    public class UsoDetalleFila
    {
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string Funcion { get; set; }
        public string Modelo { get; set; }
        public int TokensPrompt { get; set; }
        public int TokensRespuesta { get; set; }
        public decimal? Costo { get; set; }
        public bool DesdeCache { get; set; }
        public bool Exitoso { get; set; }
        public string Error { get; set; }
        public bool Sobreconsumo { get; set; }
        public bool Degradado { get; set; }
    }

    /// <summary>
    /// Consultas de lectura sobre el uso de IA de una empresa: "Mi consumo de IA" (Admin de Empresa) y el
    /// reporte exportable a Excel. El detalle por función y por consulta sale de <c>IAUsoLog</c>; el
    /// consumo por usuario sale de <c>AuditoriaAnalisisIA</c> (existe desde antes del gateway). Todas
    /// toleran que las tablas nuevas aún no existan (devuelven listas vacías).
    /// </summary>
    public class IAUsoReporteService : BaseService
    {
        /// <summary>Consultas y tokens por usuario en [desde, hasta].</summary>
        public List<UsoUsuarioFila> PorUsuario(int idEmpresa, DateTime desde, DateTime hasta)
        {
            var lista = new List<UsoUsuarioFila>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT NombreUsuario, COUNT(*) AS Consultas, SUM(CAST(ISNULL(TokensTotal, 0) AS BIGINT)) AS Tokens
                    FROM AuditoriaAnalisisIA
                    WHERE IdEmpresa = @IdEmpresa AND FechaPregunta >= @Desde AND FechaPregunta < @Hasta
                    GROUP BY IdUsuario, NombreUsuario
                    ORDER BY Tokens DESC", cn))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Desde", desde.Date);
                    cmd.Parameters.AddWithValue("@Hasta", hasta.Date.AddDays(1));
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add(new UsoUsuarioFila
                            {
                                Usuario = r["NombreUsuario"] as string,
                                Consultas = Convert.ToInt32(r["Consultas"]),
                                Tokens = Convert.ToInt64(r["Tokens"])
                            });
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoReporteService.PorUsuario"); }
            return lista;
        }

        /// <summary>Consultas atendidas, tokens y costo por función en [desde, hasta].</summary>
        public List<UsoFuncionFila> PorFuncion(int idEmpresa, DateTime desde, DateTime hasta)
        {
            var lista = new List<UsoFuncionFila>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT Funcion, COUNT(*) AS Consultas,
                           SUM(CAST(TokensPrompt + TokensRespuesta AS BIGINT)) AS Tokens,
                           SUM(ISNULL(CostoUSD, 0)) AS Costo
                    FROM dbo.IAUsoLog
                    WHERE IdEmpresa = @IdEmpresa AND Exitoso = 1 AND Fecha >= @Desde AND Fecha < @Hasta
                    GROUP BY Funcion
                    ORDER BY Tokens DESC", cn))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Desde", desde.Date);
                    cmd.Parameters.AddWithValue("@Hasta", hasta.Date.AddDays(1));
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add(new UsoFuncionFila
                            {
                                Funcion = r["Funcion"] as string,
                                Consultas = Convert.ToInt32(r["Consultas"]),
                                Tokens = Convert.ToInt64(r["Tokens"]),
                                Costo = Convert.ToDecimal(r["Costo"])
                            });
                }
            }
            catch (SqlException ex) when (ex.Number == 208) { /* IAUsoLog aún no existe (Sql/013) */ }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoReporteService.PorFuncion"); }
            return lista;
        }

        /// <summary>Una fila por llamada (también las rechazadas) en [desde, hasta], más reciente primero.</summary>
        public List<UsoDetalleFila> Detalle(int idEmpresa, DateTime desde, DateTime hasta)
        {
            try { return LeerDetalle(idEmpresa, desde, hasta, true); }
            catch (SqlException ex) when (ex.Number == 207) // sin Sql/015: columnas Sobreconsumo/Degradado
            {
                try { return LeerDetalle(idEmpresa, desde, hasta, false); }
                catch (Exception ex2) { AppLogger.Error(ex2, "IAUsoReporteService.Detalle"); }
            }
            catch (SqlException ex) when (ex.Number == 208) { /* IAUsoLog aún no existe */ }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoReporteService.Detalle"); }
            return new List<UsoDetalleFila>();
        }

        private List<UsoDetalleFila> LeerDetalle(int idEmpresa, DateTime desde, DateTime hasta, bool conFase2)
        {
            var lista = new List<UsoDetalleFila>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand($@"
                SELECT TOP (20000) l.Fecha, LTRIM(RTRIM(ISNULL(u.Nombre, '') + ' ' + ISNULL(u.Apellidos, ''))) AS Usuario,
                       l.Funcion, l.Modelo, l.TokensPrompt, l.TokensRespuesta, l.CostoUSD, l.DesdeCache, l.Exitoso, l.Error
                       {(conFase2 ? ", l.Sobreconsumo, l.Degradado" : "")}
                FROM dbo.IAUsoLog l
                LEFT JOIN Usuarios u ON u.Id = l.IdUsuario
                WHERE l.IdEmpresa = @IdEmpresa AND l.Fecha >= @Desde AND l.Fecha < @Hasta
                ORDER BY l.Fecha DESC", cn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date.AddDays(1));
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(new UsoDetalleFila
                        {
                            Fecha = Convert.ToDateTime(r["Fecha"]),
                            Usuario = r["Usuario"] as string,
                            Funcion = r["Funcion"] as string,
                            Modelo = r["Modelo"] as string,
                            TokensPrompt = Convert.ToInt32(r["TokensPrompt"]),
                            TokensRespuesta = Convert.ToInt32(r["TokensRespuesta"]),
                            Costo = r["CostoUSD"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["CostoUSD"]),
                            DesdeCache = Convert.ToBoolean(r["DesdeCache"]),
                            Exitoso = Convert.ToBoolean(r["Exitoso"]),
                            Error = r["Error"] as string,
                            Sobreconsumo = conFase2 && Convert.ToBoolean(r["Sobreconsumo"]),
                            Degradado = conFase2 && Convert.ToBoolean(r["Degradado"])
                        });
            }
            return lista;
        }

        /// <summary>
        /// Borra TODO el registro de uso (<c>IAUsoLog</c>): es lo que alimenta "Uso y costos" y el reporte de uso.
        /// NO toca <c>IAUsoMensual</c> ni <c>AuditoriaAnalisisIA</c>, que son la base del presupuesto mensual y
        /// de la auditoría. Devuelve las filas eliminadas (-1 si la tabla aún no existe).
        /// </summary>
        public int LimpiarRegistroUso()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM dbo.IAUsoLog", cn) { CommandTimeout = 120 })
                {
                    cn.Open();
                    return cmd.ExecuteNonQuery();
                }
            }
            catch (SqlException ex) when (ex.Number == 208) { return -1; }
        }

        // ── Observabilidad (Super Admin): uso, costo, caché, latencia y errores sobre IAUsoLog ──

        public class ObsTotales
        {
            public int Consultas { get; set; }
            public int Exitosas { get; set; }
            public int Rechazadas { get; set; }
            public int DesdeCache { get; set; }
            public long Tokens { get; set; }
            public decimal Costo { get; set; }
            public double? LatenciaP50 { get; set; }
            public double? LatenciaP95 { get; set; }
        }

        public class ObsDia
        {
            public string Dia { get; set; }
            public int Consultas { get; set; }
            public long Tokens { get; set; }
            public decimal Costo { get; set; }
        }

        public class ObsGrupo
        {
            public string Clave { get; set; }
            public int Consultas { get; set; }
            public long Tokens { get; set; }
            public decimal Costo { get; set; }
            public int Errores { get; set; }
        }

        public class ObsError
        {
            public string Fecha { get; set; }
            public int IdEmpresa { get; set; }
            public string Funcion { get; set; }
            public string Error { get; set; }
        }

        public class ObservabilidadIA
        {
            public ObsTotales Totales { get; set; } = new ObsTotales();
            public List<ObsDia> PorDia { get; set; } = new List<ObsDia>();
            public List<ObsGrupo> PorFuncion { get; set; } = new List<ObsGrupo>();
            public List<ObsGrupo> PorModelo { get; set; } = new List<ObsGrupo>();
            /// <summary>La Clave es el Id de la empresa (el controlador lo reemplaza por su nombre).</summary>
            public List<ObsGrupo> PorEmpresa { get; set; } = new List<ObsGrupo>();
            public List<ObsError> Errores { get; set; } = new List<ObsError>();
            /// <summary>false si IAUsoLog aún no existe (Sql/013).</summary>
            public bool Disponible { get; set; } = true;
        }

        /// <summary>Métricas globales de uso de IA entre dos fechas (inclusive). Nunca lanza.</summary>
        public ObservabilidadIA Observabilidad(DateTime desde, DateTime hasta)
        {
            var o = new ObservabilidadIA();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    Func<string, SqlCommand> cmd = sql =>
                    {
                        var c = new SqlCommand(sql, cn) { CommandTimeout = 60 };
                        c.Parameters.AddWithValue("@Desde", desde.Date);
                        c.Parameters.AddWithValue("@Hasta", hasta.Date.AddDays(1));
                        return c;
                    };
                    const string rango = "FROM dbo.IAUsoLog WHERE Fecha >= @Desde AND Fecha < @Hasta";

                    using (var c = cmd($@"SELECT COUNT(*) AS N,
                            SUM(CASE WHEN Exitoso = 1 THEN 1 ELSE 0 END) AS Ok,
                            SUM(CASE WHEN Exitoso = 0 THEN 1 ELSE 0 END) AS Fallos,
                            SUM(CASE WHEN DesdeCache = 1 THEN 1 ELSE 0 END) AS Cache,
                            SUM(CAST(TokensPrompt + TokensRespuesta AS BIGINT)) AS Tokens,
                            SUM(ISNULL(CostoUSD, 0)) AS Costo {rango}"))
                    using (var r = c.ExecuteReader())
                        if (r.Read())
                        {
                            o.Totales.Consultas = Convert.ToInt32(r["N"]);
                            o.Totales.Exitosas = r["Ok"] == DBNull.Value ? 0 : Convert.ToInt32(r["Ok"]);
                            o.Totales.Rechazadas = r["Fallos"] == DBNull.Value ? 0 : Convert.ToInt32(r["Fallos"]);
                            o.Totales.DesdeCache = r["Cache"] == DBNull.Value ? 0 : Convert.ToInt32(r["Cache"]);
                            o.Totales.Tokens = r["Tokens"] == DBNull.Value ? 0 : Convert.ToInt64(r["Tokens"]);
                            o.Totales.Costo = r["Costo"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Costo"]);
                        }

                    // Latencia solo de llamadas reales al modelo (sin caché ni rechazos).
                    using (var c = cmd($@"SELECT TOP 1
                            PERCENTILE_CONT(0.5)  WITHIN GROUP (ORDER BY LatenciaMs) OVER () AS P50,
                            PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY LatenciaMs) OVER () AS P95
                            {rango} AND Exitoso = 1 AND DesdeCache = 0 AND LatenciaMs IS NOT NULL"))
                    using (var r = c.ExecuteReader())
                        if (r.Read())
                        {
                            o.Totales.LatenciaP50 = r["P50"] == DBNull.Value ? (double?)null : Convert.ToDouble(r["P50"]);
                            o.Totales.LatenciaP95 = r["P95"] == DBNull.Value ? (double?)null : Convert.ToDouble(r["P95"]);
                        }

                    using (var c = cmd($@"SELECT CONVERT(VARCHAR(10), Fecha, 23) AS Dia, COUNT(*) AS N,
                            SUM(CAST(TokensPrompt + TokensRespuesta AS BIGINT)) AS Tokens, SUM(ISNULL(CostoUSD, 0)) AS Costo
                            {rango} AND Exitoso = 1 GROUP BY CONVERT(VARCHAR(10), Fecha, 23) ORDER BY Dia"))
                    using (var r = c.ExecuteReader())
                        while (r.Read())
                            o.PorDia.Add(new ObsDia
                            {
                                Dia = r["Dia"] as string,
                                Consultas = Convert.ToInt32(r["N"]),
                                Tokens = Convert.ToInt64(r["Tokens"]),
                                Costo = Convert.ToDecimal(r["Costo"])
                            });

                    o.PorFuncion = LeerGrupo(cmd, rango, "Funcion", null);
                    o.PorModelo = LeerGrupo(cmd, rango, "ISNULL(Modelo, '—')", null);
                    o.PorEmpresa = LeerGrupo(cmd, rango, "CAST(IdEmpresa AS VARCHAR(20))", 15);

                    using (var c = cmd($@"SELECT TOP 15 CONVERT(VARCHAR(16), Fecha, 120) AS F, IdEmpresa, Funcion, Error
                            {rango} AND Exitoso = 0 ORDER BY Fecha DESC"))
                    using (var r = c.ExecuteReader())
                        while (r.Read())
                            o.Errores.Add(new ObsError
                            {
                                Fecha = r["F"] as string,
                                IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
                                Funcion = r["Funcion"] as string,
                                Error = r["Error"] as string
                            });
                }
            }
            catch (SqlException ex) when (ex.Number == 208) { o.Disponible = false; }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoReporteService.Observabilidad"); }
            return o;
        }

        private static List<ObsGrupo> LeerGrupo(Func<string, SqlCommand> cmd, string rango, string expr, int? top)
        {
            var lista = new List<ObsGrupo>();
            using (var c = cmd($@"SELECT {(top.HasValue ? "TOP " + top.Value : "")} {expr} AS Clave, COUNT(*) AS N,
                    SUM(CAST(TokensPrompt + TokensRespuesta AS BIGINT)) AS Tokens, SUM(ISNULL(CostoUSD, 0)) AS Costo,
                    SUM(CASE WHEN Exitoso = 0 THEN 1 ELSE 0 END) AS Errores
                    {rango} GROUP BY {expr} ORDER BY Tokens DESC"))
            using (var r = c.ExecuteReader())
                while (r.Read())
                    lista.Add(new ObsGrupo
                    {
                        Clave = r["Clave"] as string,
                        Consultas = Convert.ToInt32(r["N"]),
                        Tokens = Convert.ToInt64(r["Tokens"]),
                        Costo = Convert.ToDecimal(r["Costo"]),
                        Errores = Convert.ToInt32(r["Errores"])
                    });
            return lista;
        }
    }
}
