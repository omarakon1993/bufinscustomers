using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    public class WidgetsService : BaseService
    {
        private const string SelectCols = @"
            SELECT Id, Nombre, Tipo, Icono, ColorIcono, Orden, Activo,
                   ConsultaSQL, UnidadValor, TipoGrafico, FondoOscuro
            FROM DashboardTarjetas";

        public List<WidgetTarjeta> ObtenerTodas()
        {
            var list = new List<WidgetTarjeta>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(SelectCols + " ORDER BY Orden, Id", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(Map(r));
            }
            return list;
        }

        public List<WidgetTarjeta> ObtenerActivas()
        {
            var list = new List<WidgetTarjeta>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(SelectCols + " WHERE Activo = 1 ORDER BY Orden, Id", cn);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(Map(r));
            }
            return list;
        }

        public bool Crear(WidgetTarjeta t)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        INSERT INTO DashboardTarjetas
                            (Nombre, Tipo, Icono, ColorIcono, Orden, Activo,
                             ConsultaSQL, UnidadValor, TipoGrafico, FondoOscuro)
                        VALUES
                            (@Nombre, @Tipo, @Icono, @ColorIcono, @Orden, @Activo,
                             @ConsultaSQL, @UnidadValor, @TipoGrafico, @FondoOscuro)", cn);
                    AddParams(cmd, t);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public bool Editar(WidgetTarjeta t)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        UPDATE DashboardTarjetas SET
                            Nombre=@Nombre, Tipo=@Tipo, Icono=@Icono, ColorIcono=@ColorIcono,
                            Orden=@Orden, Activo=@Activo,
                            ConsultaSQL=@ConsultaSQL, UnidadValor=@UnidadValor,
                            TipoGrafico=@TipoGrafico, FondoOscuro=@FondoOscuro
                        WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", t.Id);
                    AddParams(cmd, t);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public bool Eliminar(int id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("DELETE FROM DashboardTarjetas WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch { return false; }
        }

        public List<WidgetKpiResultado> EjecutarKpi(string sql, int? idEmpresaFiltro)
        {
            var list = new List<WidgetKpiResultado>();
            if (!EsSelectValido(sql)) return list;

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int idEmp = 0;
                            try { idEmp = Convert.ToInt32(r["IdEmpresa"]); } catch { }

                            if (idEmpresaFiltro.HasValue && idEmp != idEmpresaFiltro.Value) continue;

                            var kpi = new WidgetKpiResultado
                            {
                                IdEmpresa = idEmp,
                                Valor     = r["Valor"]?.ToString() ?? ""
                            };
                            try { kpi.NombreEmpresa = r["NombreEmpresa"]?.ToString(); } catch { }
                            try { kpi.Etiqueta      = r["Etiqueta"]?.ToString(); }      catch { }
                            list.Add(kpi);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public List<WidgetGraficoResultado> EjecutarGrafico(string sql, int? idEmpresaFiltro)
        {
            var list = new List<WidgetGraficoResultado>();
            if (!EsSelectValido(sql)) return list;

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int idEmp = 0;
                            try { idEmp = Convert.ToInt32(r["IdEmpresa"]); } catch { }

                            if (idEmpresaFiltro.HasValue && idEmp != idEmpresaFiltro.Value) continue;

                            decimal valor = 0m;
                            try
                            {
                                var raw = r["Valor"];
                                if (raw != null && raw != DBNull.Value)
                                    valor = Convert.ToDecimal(raw);
                            }
                            catch { }

                            var item = new WidgetGraficoResultado
                            {
                                IdEmpresa = idEmp,
                                Etiqueta  = r["Etiqueta"]?.ToString() ?? "",
                                Valor     = valor
                            };
                            try { item.NombreEmpresa = r["NombreEmpresa"]?.ToString(); } catch { }
                            try { item.Serie         = r["Serie"]?.ToString(); }         catch { }
                            list.Add(item);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private static bool EsSelectValido(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;
            var t = sql.Trim();
            return t.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase);
        }

        private static WidgetTarjeta Map(SqlDataReader r) => new WidgetTarjeta
        {
            Id          = Convert.ToInt32(r["Id"]),
            Nombre      = r["Nombre"]?.ToString(),
            Tipo        = Convert.ToByte(r["Tipo"]),
            Icono       = r["Icono"]?.ToString(),
            ColorIcono  = r["ColorIcono"]?.ToString(),
            Orden       = Convert.ToInt32(r["Orden"]),
            Activo      = Convert.ToBoolean(r["Activo"]),
            ConsultaSQL = r["ConsultaSQL"] == DBNull.Value ? null : r["ConsultaSQL"].ToString(),
            UnidadValor = r["UnidadValor"] == DBNull.Value ? null : r["UnidadValor"].ToString(),
            TipoGrafico = r["TipoGrafico"] == DBNull.Value ? null : r["TipoGrafico"].ToString(),
            FondoOscuro = r["FondoOscuro"] != DBNull.Value && Convert.ToBoolean(r["FondoOscuro"])
        };

        private static void AddParams(SqlCommand cmd, WidgetTarjeta t)
        {
            cmd.Parameters.AddWithValue("@Nombre",      t.Nombre     ?? "");
            cmd.Parameters.AddWithValue("@Tipo",        t.Tipo);
            cmd.Parameters.AddWithValue("@Icono",       t.Icono      ?? "fas fa-chart-bar");
            cmd.Parameters.AddWithValue("@ColorIcono",  t.ColorIcono ?? "#583AFF");
            cmd.Parameters.AddWithValue("@Orden",       t.Orden);
            cmd.Parameters.AddWithValue("@Activo",      t.Activo);
            cmd.Parameters.AddWithValue("@ConsultaSQL", (object)t.ConsultaSQL ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UnidadValor", (object)t.UnidadValor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TipoGrafico", (object)t.TipoGrafico ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FondoOscuro", t.FondoOscuro);
        }
    }
}
