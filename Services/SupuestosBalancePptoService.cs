using bufinscustomers.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Supuestos del balance del ppto: dbo.Rel_BalancePpto administrada POR EMPRESA
    /// (script Sql/038). Solo la columna Valor es editable; las filas con TipoUnidad = CALCULO
    /// (subtotales) son informativas. Tolera que el script 038 aún no se haya ejecutado
    /// (<see cref="Disponible"/> = false) en vez de romper.
    /// </summary>
    public class SupuestosBalancePptoService : BaseService
    {
        public const string CodigoMenu = "ADMIN_CONFIG_BALANCE_PPTO";
        public const string NombreHoja = "Rel_BalancePpto";

        private static readonly string[] ColumnasAuditoria =
            { "IdEmpresa", "IdUsuarioCarga", "FechaCarga", "IdUsuarioModificacion", "FechaModificacion" };

        // ───────────────────────── Disponibilidad ─────────────────────────

        /// <summary>true si la tabla ya tiene IdEmpresa (script 038 ejecutado).</summary>
        public bool Disponible()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Rel_BalancePpto','IdEmpresa') IS NULL THEN 0 ELSE 1 END", cn))
                {
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch { return false; }
        }

        private bool TieneAuditoria(SqlConnection cn)
        {
            using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Rel_BalancePpto','FechaModificacion') IS NULL THEN 0 ELSE 1 END", cn))
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        // ───────────────────────── Lectura ─────────────────────────

        public List<SupuestoBalancePpto> Obtener(int idEmpresa)
        {
            var lista = new List<SupuestoBalancePpto>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT * FROM dbo.Rel_BalancePpto WHERE IdEmpresa = @e ORDER BY Id, Mes", cn))
            {
                cmd.Parameters.AddWithValue("@e", idEmpresa);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    var campos = new HashSet<string>(Enumerable.Range(0, r.FieldCount).Select(r.GetName), StringComparer.OrdinalIgnoreCase);
                    while (r.Read())
                    {
                        var s = new SupuestoBalancePpto
                        {
                            Id = campos.Contains("Id") && r["Id"] != DBNull.Value ? Convert.ToInt32(r["Id"]) : 0,
                            Descripcion = Texto(r, campos, "Descripcion"),
                            DescripcionBase = Texto(r, campos, "DescripcionBase"),
                            Formula = Texto(r, campos, "Formula"),
                            TipoUnidad = Texto(r, campos, "TipoUnidad"),
                            Mes = campos.Contains("Mes") && r["Mes"] != DBNull.Value ? Convert.ToInt32(r["Mes"]) : 0,
                            MesBase = campos.Contains("MesBase") && r["MesBase"] != DBNull.Value ? (int?)Convert.ToInt32(r["MesBase"]) : null,
                            Valor = campos.Contains("Valor") && r["Valor"] != DBNull.Value ? (decimal?)Convert.ToDecimal(r["Valor"]) : null,
                            FechaModificacion = campos.Contains("FechaModificacion") && r["FechaModificacion"] != DBNull.Value
                                ? (DateTime?)Convert.ToDateTime(r["FechaModificacion"]) : null
                        };
                        s.EsCalculo = string.Equals((s.TipoUnidad ?? "").Trim(), "CALCULO", StringComparison.OrdinalIgnoreCase);
                        lista.Add(s);
                    }
                }
            }
            return lista;
        }

        private static string Texto(IDataRecord r, HashSet<string> campos, string nombre)
            => campos.Contains(nombre) && r[nombre] != DBNull.Value ? Convert.ToString(r[nombre]) : null;

        /// <summary>Completitud: pendientes = filas que no son CALCULO con Valor NULL.</summary>
        public ResumenSupuestos ObtenerResumen(int idEmpresa)
        {
            var res = new ResumenSupuestos();
            if (!Disponible()) return res;
            res.Disponible = true;

            var filas = Obtener(idEmpresa);
            res.TieneFilas = filas.Count > 0;
            res.Calculo = filas.Count(f => f.EsCalculo);
            var editables = filas.Where(f => !f.EsCalculo).ToList();
            res.Total = editables.Count;
            res.Diligenciados = editables.Count(f => f.Valor.HasValue);
            res.Pendientes = res.Total - res.Diligenciados;

            // Una sola entrada por línea (descripción): los 12 meses son columnas, no líneas distintas
            foreach (var g in editables.GroupBy(f => (f.Descripcion ?? "").Trim().ToLowerInvariant()).OrderBy(g => g.Min(x => x.Id)))
            {
                int pend = g.Count(f => !f.Valor.HasValue);
                if (pend == 0) continue;
                var l = new LineaPendienteSupuesto { Id = g.Min(x => x.Id), Descripcion = (g.First().Descripcion ?? "").Trim(), Pendientes = pend, Total = g.Select(x => x.Mes).Distinct().Count() };
                l.MesesPendientes.AddRange(g.Where(f => !f.Valor.HasValue).Select(f => f.Mes).Distinct().OrderBy(m => m));
                res.Lineas.Add(l);
            }
            res.LineasConPendientes = res.Lineas.Count;

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();
                    if (TieneAuditoria(cn))
                    {
                        using (var cmd = new SqlCommand(@"
                            SELECT MAX(r.FechaCarga), MAX(r.FechaModificacion) FROM dbo.Rel_BalancePpto r WHERE r.IdEmpresa = @e;
                            SELECT TOP 1 LTRIM(RTRIM(ISNULL(u.Nombre,'') + ' ' + ISNULL(u.Apellidos,'')))
                            FROM   dbo.Rel_BalancePpto r JOIN dbo.Usuarios u ON u.Id = r.IdUsuarioModificacion
                            WHERE  r.IdEmpresa = @e AND r.FechaModificacion IS NOT NULL
                            ORDER BY r.FechaModificacion DESC;", cn))
                        {
                            cmd.Parameters.AddWithValue("@e", idEmpresa);
                            using (var rd = cmd.ExecuteReader())
                            {
                                if (rd.Read())
                                {
                                    res.UltimaCarga = rd.IsDBNull(0) ? (DateTime?)null : rd.GetDateTime(0);
                                    res.UltimaModificacion = rd.IsDBNull(1) ? (DateTime?)null : rd.GetDateTime(1);
                                }
                                if (rd.NextResult() && rd.Read() && !rd.IsDBNull(0)) res.UltimoUsuario = rd.GetString(0);
                            }
                        }
                    }
                }
            }
            catch { /* la trazabilidad es informativa */ }
            return res;
        }

        /// <summary>Cantidad de supuestos pendientes (para el aviso de Ejecución de Modelos). -1 = no disponible / sin filas.</summary>
        public int ContarPendientes(int idEmpresa)
        {
            try
            {
                if (!Disponible()) return -1;
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT CASE WHEN COUNT(*) = 0 THEN -1
                                ELSE SUM(CASE WHEN UPPER(ISNULL(TipoUnidad,'')) <> 'CALCULO' AND Valor IS NULL THEN 1 ELSE 0 END) END
                    FROM dbo.Rel_BalancePpto WHERE IdEmpresa = @e", cn))
                {
                    cmd.Parameters.AddWithValue("@e", idEmpresa);
                    cn.Open();
                    var o = cmd.ExecuteScalar();
                    return o == null || o == DBNull.Value ? -1 : Convert.ToInt32(o);
                }
            }
            catch { return -1; }
        }

        /// <summary>Nombre y URL ACTUALES de la opción de menú (para enlazar sin escribir el nombre a mano).</summary>
        public void ObtenerMenu(bool ingles, out string nombre, out string controller, out string action)
        {
            nombre = null; controller = "SupuestosBalancePpto"; action = "Index";
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT TOP 1 * FROM dbo.MenuOpciones WHERE Codigo = @c AND Activo = 1", cn))
                {
                    cmd.Parameters.AddWithValue("@c", CodigoMenu);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return;
                        var campos = new HashSet<string>(Enumerable.Range(0, r.FieldCount).Select(r.GetName), StringComparer.OrdinalIgnoreCase);
                        string es = Texto(r, campos, "Nombre"), en = Texto(r, campos, "NombreEN");
                        nombre = ingles && !string.IsNullOrWhiteSpace(en) ? en : es;
                        controller = Texto(r, campos, "Controller") ?? controller;
                        action = Texto(r, campos, "Action") ?? action;
                    }
                }
            }
            catch { /* sin menú: el aviso usa el texto genérico */ }
        }

        // ───────────────────────── Escritura ─────────────────────────

        /// <summary>Crea la estructura (Valor NULL) de una empresa. Devuelve filas creadas (0 si ya tenía o no hay plantilla).</summary>
        public int Inicializar(int idEmpresa, int? idUsuario)
        {
            if (!Disponible()) return 0;
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("dbo.sp_InicializarSupuestosBalancePpto", cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 120;
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdUsuario", (object)idUsuario ?? DBNull.Value);
                var filas = new SqlParameter("@Filas", SqlDbType.Int) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(filas);
                cn.Open();
                cmd.ExecuteNonQuery();
                return filas.Value == DBNull.Value ? 0 : Convert.ToInt32(filas.Value);
            }
        }

        /// <summary>Guarda cambios de Valor (solo filas que no son CALCULO). Devuelve filas actualizadas.</summary>
        public int GuardarValores(int idEmpresa, int idUsuario, IEnumerable<CambioSupuesto> cambios)
        {
            int total = 0;
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                bool aud = TieneAuditoria(cn);
                using (var tx = cn.BeginTransaction())
                {
                    foreach (var c in cambios)
                    {
                        using (var cmd = new SqlCommand(@"
                            UPDATE dbo.Rel_BalancePpto SET Valor = @v" + (aud ? ", IdUsuarioModificacion = @u, FechaModificacion = GETDATE()" : "") + @"
                            WHERE IdEmpresa = @e AND Id = @id AND Mes = @m AND UPPER(ISNULL(TipoUnidad,'')) <> 'CALCULO'", cn, tx))
                        {
                            cmd.Parameters.Add("@v", SqlDbType.Decimal).Value = c.Valor.HasValue ? (object)c.Valor.Value : DBNull.Value;
                            cmd.Parameters["@v"].Precision = 19; cmd.Parameters["@v"].Scale = 6;
                            cmd.Parameters.AddWithValue("@u", idUsuario);
                            cmd.Parameters.AddWithValue("@e", idEmpresa);
                            cmd.Parameters.AddWithValue("@id", c.Id);
                            cmd.Parameters.AddWithValue("@m", c.Mes);
                            total += cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
            return total;
        }

        // ───────────────────────── Excel ─────────────────────────

        private List<string> ColumnasExcel(SqlConnection cn)
        {
            var cols = new List<string>();
            using (var cmd = new SqlCommand(@"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                                              WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Rel_BalancePpto' ORDER BY ORDINAL_POSITION", cn))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    string c = r.GetString(0);
                    if (!ColumnasAuditoria.Contains(c, StringComparer.OrdinalIgnoreCase)) cols.Add(c);
                }
            return cols;
        }

        /// <summary>Excel con los supuestos de la empresa (misma hoja y encabezados que usaba Relacionamientos).</summary>
        public byte[] GenerarExcel(int idEmpresa, bool soloEstructura)
        {
            using (var wb = new XLWorkbook())
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                var cols = ColumnasExcel(cn);
                var ws = wb.Worksheets.Add(NombreHoja);

                for (int i = 0; i < cols.Count; i++)
                {
                    var h = ws.Cell(1, i + 1);
                    h.Value = cols[i];
                    h.Style.Font.Bold = true;
                    h.Style.Font.FontColor = XLColor.White;
                    h.Style.Fill.BackgroundColor = XLColor.FromHtml("#583AFF");
                }

                string select = "SELECT " + string.Join(", ", cols.Select(c => "[" + c + "]")) +
                                " FROM dbo.Rel_BalancePpto WHERE IdEmpresa = @e ORDER BY Id, Mes";
                int fila = 2;
                using (var cmd = new SqlCommand(select, cn))
                {
                    cmd.Parameters.AddWithValue("@e", idEmpresa);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            for (int i = 0; i < cols.Count; i++)
                            {
                                var celda = ws.Cell(fila, i + 1);
                                bool esValor = cols[i].Equals("Valor", StringComparison.OrdinalIgnoreCase);
                                if (r.IsDBNull(i) || (soloEstructura && esValor)) continue;
                                Helpers.ExcelCellHelper.SetValue(celda, r.GetValue(i));
                            }
                            // Valor (columna editable) resaltada; el resto es informativo
                            int cv = cols.FindIndex(c => c.Equals("Valor", StringComparison.OrdinalIgnoreCase));
                            if (cv >= 0) ws.Cell(fila, cv + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFECFF");
                            fila++;
                        }
                }
                ws.SheetView.Freeze(1, 0);
                ws.Columns().AdjustToContents(1, Math.Min(fila, 200));
                if (ws.LastRowUsed() != null) ws.RangeUsed().SetAutoFilter();

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Valida y aplica un Excel: SOLO actualiza Valor por clave (Id, Mes) de la empresa; nunca borra ni crea estructura.
        /// Si hay algún Error no se escribe nada. Las advertencias no bloquean.
        /// </summary>
        public ResultadoCargaSupuestos CargarDesdeExcel(int idEmpresa, int idUsuario, XLWorkbook wb)
        {
            var res = new ResultadoCargaSupuestos();
            var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals(NombreHoja, StringComparison.OrdinalIgnoreCase))
                     ?? (wb.Worksheets.Count == 1 ? wb.Worksheets.First() : null);
            if (ws == null || ws.LastRowUsed() == null)
            {
                res.Hallazgos.Add(new HallazgoSupuesto { Fila = 0, Severidad = "Error", Codigo = "SinHoja", Dato = NombreHoja });
                res.Mensaje = "SinHoja";
                return res;
            }

            // Encabezados (fila 1) → posición
            var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int cc = 1;
            while (true)
            {
                string h = ws.Cell(1, cc).GetFormattedString()?.Trim();
                if (string.IsNullOrEmpty(h)) break;
                if (!pos.ContainsKey(h)) pos[h] = cc;
                cc++;
            }
            foreach (var req in new[] { "Id", "Mes", "Valor" })
                if (!pos.ContainsKey(req))
                    res.Hallazgos.Add(new HallazgoSupuesto { Fila = 1, Columna = req, Severidad = "Error", Codigo = "SinColumna", Dato = req });
            if (res.Hallazgos.Any(h => h.Severidad == "Error")) { res.Mensaje = "Errores"; return res; }

            // Estructura vigente de la empresa
            var vigente = Obtener(idEmpresa).ToDictionary(f => f.Id + "|" + f.Mes, f => f);
            if (vigente.Count == 0)
            {
                res.Hallazgos.Add(new HallazgoSupuesto { Fila = 0, Severidad = "Error", Codigo = "EmpresaSinSupuestos" });
                res.Mensaje = "EmpresaSinSupuestos";
                return res;
            }

            var cambios = new Dictionary<string, CambioSupuesto>();
            int ultima = ws.LastRowUsed().RowNumber();
            int vacias = 0;
            for (int f = 2; f <= ultima; f++)
            {
                var cId = ws.Cell(f, pos["Id"]); var cMes = ws.Cell(f, pos["Mes"]); var cVal = ws.Cell(f, pos["Valor"]);
                if (cId.IsEmpty() && cMes.IsEmpty() && cVal.IsEmpty()) continue;
                res.FilasLeidas++;

                if (!EsEntero(cId, out int id)) { Hall(res, f, "Id", "Error", "IdInvalido", cId.GetFormattedString()); continue; }
                if (!EsEntero(cMes, out int mes) || mes < 1 || mes > 12) { Hall(res, f, "Mes", "Error", "MesInvalido", cMes.GetFormattedString()); continue; }

                string clave = id + "|" + mes;
                if (!vigente.TryGetValue(clave, out SupuestoBalancePpto actual)) { Hall(res, f, "Id", "Error", "FilaNoExiste", id + " / " + mes); continue; }
                if (cambios.ContainsKey(clave)) { Hall(res, f, "Id", "Error", "Duplicada", id + " / " + mes); continue; }

                if (cVal.IsEmpty() || string.IsNullOrWhiteSpace(cVal.GetFormattedString()))
                {
                    if (!actual.EsCalculo) { vacias++; Hall(res, f, "Valor", "Advertencia", "ValorVacio", actual.Descripcion); }
                    continue;   // vacío = no se modifica
                }

                if (!TryDecimal(cVal, out decimal v)) { Hall(res, f, "Valor", "Error", "ValorNoNumerico", cVal.GetFormattedString()); continue; }
                if (actual.EsCalculo) { Hall(res, f, "Valor", "Advertencia", "CalculoIgnorado", actual.Descripcion); continue; }

                cambios[clave] = new CambioSupuesto { Id = id, Mes = mes, Valor = v };
            }

            if (res.Hallazgos.Any(h => h.Severidad == "Error"))
            {
                res.Mensaje = "Errores";   // todo o nada
                return res;
            }

            // Solo se escribe lo que realmente cambia
            var aplicar = cambios.Values.Where(c => !vigente[c.Id + "|" + c.Mes].Valor.HasValue || vigente[c.Id + "|" + c.Mes].Valor.Value != c.Valor.Value).ToList();
            res.FilasActualizadas = aplicar.Count == 0 ? 0 : GuardarValores(idEmpresa, idUsuario, aplicar);
            res.Exito = true;
            res.Mensaje = "Ok";
            return res;
        }

        private static void Hall(ResultadoCargaSupuestos r, int fila, string col, string sev, string codigo, string dato)
            => r.Hallazgos.Add(new HallazgoSupuesto { Fila = fila, Columna = col, Severidad = sev, Codigo = codigo, Dato = dato });

        private static bool EsEntero(IXLCell c, out int n)
        {
            n = 0;
            if (c.Value.IsNumber) { double d = c.Value.GetNumber(); if (d != Math.Floor(d)) return false; n = (int)d; return true; }
            return int.TryParse((c.GetFormattedString() ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        }

        private static bool TryDecimal(IXLCell c, out decimal v)
        {
            v = 0;
            if (c.Value.IsNumber) { v = (decimal)c.Value.GetNumber(); return true; }
            string t = (c.GetFormattedString() ?? "").Trim().Replace("%", "").Replace("$", "").Trim();
            return decimal.TryParse(t, NumberStyles.Any, new CultureInfo("es-CO"), out v)
                || decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
        }
    }

    public class CambioSupuesto
    {
        public int Id { get; set; }
        public int Mes { get; set; }
        public decimal? Valor { get; set; }
    }
}
