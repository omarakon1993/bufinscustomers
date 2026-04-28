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
                   ConsultaSQL, UnidadValor,
                   InfoTitulo, InfoSubtitulo, InfoCuerpo, InfoUrlAccion, InfoTextoAccion
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
                             ConsultaSQL, UnidadValor,
                             InfoTitulo, InfoSubtitulo, InfoCuerpo, InfoUrlAccion, InfoTextoAccion)
                        VALUES
                            (@Nombre, @Tipo, @Icono, @ColorIcono, @Orden, @Activo,
                             @ConsultaSQL, @UnidadValor,
                             @InfoTitulo, @InfoSubtitulo, @InfoCuerpo, @InfoUrlAccion, @InfoTextoAccion)", cn);
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
                            InfoTitulo=@InfoTitulo, InfoSubtitulo=@InfoSubtitulo,
                            InfoCuerpo=@InfoCuerpo, InfoUrlAccion=@InfoUrlAccion, InfoTextoAccion=@InfoTextoAccion
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
            if (string.IsNullOrWhiteSpace(sql)) return list;

            var trimmed = sql.Trim();
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase))
                return list;

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

        private static WidgetTarjeta Map(SqlDataReader r) => new WidgetTarjeta
        {
            Id            = Convert.ToInt32(r["Id"]),
            Nombre        = r["Nombre"]?.ToString(),
            Tipo          = Convert.ToByte(r["Tipo"]),
            Icono         = r["Icono"]?.ToString(),
            ColorIcono    = r["ColorIcono"]?.ToString(),
            Orden         = Convert.ToInt32(r["Orden"]),
            Activo        = Convert.ToBoolean(r["Activo"]),
            ConsultaSQL   = r["ConsultaSQL"]   == DBNull.Value ? null : r["ConsultaSQL"].ToString(),
            UnidadValor   = r["UnidadValor"]   == DBNull.Value ? null : r["UnidadValor"].ToString(),
            InfoTitulo    = r["InfoTitulo"]    == DBNull.Value ? null : r["InfoTitulo"].ToString(),
            InfoSubtitulo = r["InfoSubtitulo"] == DBNull.Value ? null : r["InfoSubtitulo"].ToString(),
            InfoCuerpo    = r["InfoCuerpo"]    == DBNull.Value ? null : r["InfoCuerpo"].ToString(),
            InfoUrlAccion = r["InfoUrlAccion"] == DBNull.Value ? null : r["InfoUrlAccion"].ToString(),
            InfoTextoAccion = r["InfoTextoAccion"] == DBNull.Value ? null : r["InfoTextoAccion"].ToString()
        };

        private static void AddParams(SqlCommand cmd, WidgetTarjeta t)
        {
            cmd.Parameters.AddWithValue("@Nombre",        t.Nombre        ?? "");
            cmd.Parameters.AddWithValue("@Tipo",          t.Tipo);
            cmd.Parameters.AddWithValue("@Icono",         t.Icono         ?? "fas fa-chart-bar");
            cmd.Parameters.AddWithValue("@ColorIcono",    t.ColorIcono    ?? "#583AFF");
            cmd.Parameters.AddWithValue("@Orden",         t.Orden);
            cmd.Parameters.AddWithValue("@Activo",        t.Activo);
            cmd.Parameters.AddWithValue("@ConsultaSQL",   (object)t.ConsultaSQL    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UnidadValor",   (object)t.UnidadValor    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InfoTitulo",    (object)t.InfoTitulo     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InfoSubtitulo", (object)t.InfoSubtitulo  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InfoCuerpo",    (object)t.InfoCuerpo     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InfoUrlAccion", (object)t.InfoUrlAccion  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InfoTextoAccion",(object)t.InfoTextoAccion ?? DBNull.Value);
        }
    }
}
