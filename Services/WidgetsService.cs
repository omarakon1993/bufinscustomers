using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    public class WidgetsService : BaseService
    {
        // Palabras clave que nunca deben aparecer en una consulta de solo lectura.
        // Se buscan como palabras completas (word boundary) para evitar falsos positivos.
        private static readonly Regex _patronPeligroso = new Regex(
            @"(;|\bEXEC\b|\bEXECUTE\b|\bINSERT\b|\bUPDATE\b|\bDELETE\b|" +
            @"\bDROP\b|\bCREATE\b|\bALTER\b|\bTRUNCATE\b|\bBULK\b|\bxp_|\bsp_)" ,
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string SelectCols = @"
            SELECT Id, Nombre, Tipo, Icono, ColorIcono, Orden, Activo,
                   ConsultaSQL, UnidadValor, TipoGrafico, FondoOscuro
            FROM DashboardTarjetas";

        public async Task<List<WidgetTarjeta>> ObtenerTodasAsync()
        {
            var list = new List<WidgetTarjeta>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(SelectCols + " ORDER BY Orden, Id", cn);
                await cn.OpenAsync().ConfigureAwait(false);
                using (var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await r.ReadAsync().ConfigureAwait(false)) list.Add(Map(r));
            }
            return list;
        }

        public async Task<List<WidgetTarjeta>> ObtenerActivasAsync()
        {
            var list = new List<WidgetTarjeta>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(SelectCols + " WHERE Activo = 1 ORDER BY Orden, Id", cn);
                await cn.OpenAsync().ConfigureAwait(false);
                using (var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await r.ReadAsync().ConfigureAwait(false)) list.Add(Map(r));
            }
            return list;
        }

        public async Task<bool> CrearAsync(WidgetTarjeta t)
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
                    await cn.OpenAsync().ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> EditarAsync(WidgetTarjeta t)
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
                    await cn.OpenAsync().ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> EliminarAsync(int id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand("DELETE FROM DashboardTarjetas WHERE Id = @Id", cn);
                    cmd.Parameters.AddWithValue("@Id", id);
                    await cn.OpenAsync().ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                return true;
            }
            catch { return false; }
        }

        public async Task<List<WidgetKpiResultado>> EjecutarKpiAsync(string sql, int? idEmpresaFiltro)
        {
            var list = new List<WidgetKpiResultado>();
            if (!EsSelectValido(sql)) return list;

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
                    cmd.Parameters.AddWithValue("@IdEmpresa",
                        idEmpresaFiltro.HasValue ? (object)idEmpresaFiltro.Value : DBNull.Value);
                    await cn.OpenAsync().ConfigureAwait(false);
                    using (var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await r.ReadAsync().ConfigureAwait(false))
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

        public async Task<List<WidgetGraficoResultado>> EjecutarGraficoAsync(string sql, int? idEmpresaFiltro)
        {
            var list = new List<WidgetGraficoResultado>();
            if (!EsSelectValido(sql)) return list;

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(sql, cn) { CommandTimeout = 15 };
                    cmd.Parameters.AddWithValue("@IdEmpresa",
                        idEmpresaFiltro.HasValue ? (object)idEmpresaFiltro.Value : DBNull.Value);
                    await cn.OpenAsync().ConfigureAwait(false);
                    using (var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await r.ReadAsync().ConfigureAwait(false))
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

            // Solo se permiten consultas SELECT o CTEs (WITH ... SELECT)
            if (!t.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase))
                return false;

            // Bloquear cualquier keyword peligroso aunque esté dentro de una cadena SELECT válida
            if (_patronPeligroso.IsMatch(t))
                return false;

            return true;
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
