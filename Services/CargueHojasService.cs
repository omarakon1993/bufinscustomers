using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Catálogo de hojas del cargue de Excel (dbo.CargueHojas / dbo.CargueHojasEmpresas, ver
    /// Sql/021_GestorHojasCargue.sql) y operaciones de estructura (DDL) sobre sus tablas
    /// dbo.Ini_X / dbo.Staging_Ini_X. Lo usa el Gestor de hojas de cargue (Super Admin) y lo
    /// cachea <see cref="Helpers.CargueHojasCacheHelper"/> para el resto de la aplicación.
    ///
    /// Las columnas de una hoja NO se guardan en el catálogo: la fuente de verdad es el esquema
    /// físico (INFORMATION_SCHEMA), el mismo que usa el cargue (DatosController.GuardarEnIni).
    ///
    /// Seguridad del DDL: todo identificador pasa por las expresiones regulares de este archivo y
    /// se escribe con <see cref="Q"/> (corchetes escapados); los tipos salen de una lista blanca y
    /// los valores por defecto se convierten a literales según el tipo. Ningún texto del usuario
    /// llega al SQL sin validar. T-SQL compatible con SQL Server 2012.
    /// </summary>
    public class CargueHojasService : BaseService
    {
        // ── Columnas de sistema (convención de Sql/007 y Sql/020) ───────────────────────────────

        /// <summary>Columnas que el cargue llena solo en las tablas Ini_ (no van en el Excel).</summary>
        public static readonly string[] ColumnasSistemaIni =
        {
            "Llave", "IdEmpresa_Log", "IdUsuarioCargue_Log", "FechaCargue_Log", "Historico_Log",
            "IdUsuarioEjecucion_Log", "FechaEjecucion_Log", "Observacion_Log", "IdEscenario"
        };

        /// <summary>Columnas de ejecución que existen en Ini_ pero no en su espejo de staging.</summary>
        private static readonly string[] ColumnasSoloIni = { "IdUsuarioEjecucion_Log", "FechaEjecucion_Log", "Observacion_Log" };

        /// <summary>Columnas propias de las tablas de staging.</summary>
        public static readonly string[] ColumnasSistemaStaging = { "IdLote", "NumeroFilaExcel" };

        private static readonly HashSet<string> _sistema =
            new HashSet<string>(ColumnasSistemaIni.Concat(ColumnasSistemaStaging), StringComparer.OrdinalIgnoreCase);

        public static bool EsColumnaSistema(string nombre) => nombre != null && _sistema.Contains(nombre);

        /// <summary>Columna obligatoria en toda hoja: el borrado previo, el snapshot y la plantilla con datos filtran por año.</summary>
        public const string ColumnaAnio = "Año";

        /// <summary>Tipos permitidos al diseñar columnas (lista blanca).</summary>
        public static readonly string[] TiposPermitidos = { "varchar", "nvarchar", "int", "bigint", "decimal", "money", "date", "datetime", "bit" };

        private static readonly Regex RxHoja = new Regex(@"^Z_[A-Za-z0-9_]{1,29}$", RegexOptions.Compiled);           // Excel: máx. 31 caracteres
        private static readonly Regex RxTabla = new Regex(@"^Ini_[A-Za-z0-9_]{1,96}$", RegexOptions.Compiled);        // + "Staging_" ≤ 128
        private static readonly Regex RxColumna = new Regex(@"^[A-Za-zÁÉÍÓÚáéíóúÑñÜü_][A-Za-z0-9ÁÉÍÓÚáéíóúÑñÜü_]{0,63}$", RegexOptions.Compiled);

        public static bool NombreHojaValido(string n) => n != null && RxHoja.IsMatch(n);
        public static bool NombreTablaValido(string n) => n != null && RxTabla.IsMatch(n);
        public static bool NombreColumnaValido(string n) => n != null && RxColumna.IsMatch(n) && !EsColumnaSistema(n);

        /// <summary>Identificador entre corchetes con escape (equivale a QUOTENAME).</summary>
        private static string Q(string identificador) => "[" + identificador.Replace("]", "]]") + "]";

        /// <summary>Error de validación del gestor: <see cref="Exception.Message"/> es una clave de recurso (HojasCargue_Err_*).</summary>
        public class GestorHojasException : Exception
        {
            public string Detalle { get; }
            public GestorHojasException(string clave, string detalle = null) : base(clave) { Detalle = detalle; }
        }

        // ══════════════════════════════════════════════════════════════════════════════════════
        //  CATÁLOGO
        // ══════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Todas las hojas (activas e inactivas) con sus empresas. <c>null</c> si el script 021 no se ha ejecutado.</summary>
        public List<HojaCargue> ObtenerCatalogo()
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                IF OBJECT_ID('dbo.CargueHojas', 'U') IS NULL OR OBJECT_ID('dbo.CargueHojasEmpresas', 'U') IS NULL
                BEGIN
                    SELECT CAST(0 AS BIT) AS Existe;
                    RETURN;
                END
                SELECT CAST(1 AS BIT) AS Existe;
                SELECT IdHoja, NombreHoja, TablaIni, Descripcion, Alcance, Activo, Orden, Origen,
                       FechaCreacion, VigenteDesde, FechaModificacion
                FROM dbo.CargueHojas
                ORDER BY Orden, NombreHoja;
                SELECT IdHoja, IdEmpresa, FechaAsignacion FROM dbo.CargueHojasEmpresas WHERE Activo = 1;", cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read() || !Convert.ToBoolean(r["Existe"])) return null;

                    var hojas = new List<HojaCargue>();
                    r.NextResult();
                    while (r.Read())
                    {
                        hojas.Add(new HojaCargue
                        {
                            IdHoja = Convert.ToInt32(r["IdHoja"]),
                            NombreHoja = Convert.ToString(r["NombreHoja"]).Trim(),
                            TablaIni = Convert.ToString(r["TablaIni"]).Trim(),
                            Descripcion = r["Descripcion"] as string,
                            Alcance = Convert.ToString(r["Alcance"]).Trim(),
                            Activo = Convert.ToBoolean(r["Activo"]),
                            Orden = Convert.ToInt32(r["Orden"]),
                            Origen = Convert.ToString(r["Origen"]).Trim(),
                            FechaCreacion = Convert.ToDateTime(r["FechaCreacion"]),
                            VigenteDesde = Convert.ToDateTime(r["VigenteDesde"]),
                            FechaModificacion = r["FechaModificacion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaModificacion"])
                        });
                    }

                    var porId = hojas.ToDictionary(h => h.IdHoja);
                    r.NextResult();
                    while (r.Read())
                    {
                        if (porId.TryGetValue(Convert.ToInt32(r["IdHoja"]), out var hoja))
                            hoja.Empresas[Convert.ToInt32(r["IdEmpresa"])] = Convert.ToDateTime(r["FechaAsignacion"]);
                    }
                    return hojas;
                }
            }
        }

        /// <summary>
        /// Respaldo previo al script 021: empresas (y fecha) del paquete CHURIDO_HIST_PRECIOS en
        /// dbo.EmpresaPersonalizaciones (Sql/020). Vacío si esa tabla tampoco existe.
        /// </summary>
        public Dictionary<int, DateTime> ObtenerAsignacionesLegadoChurido()
        {
            var resultado = new Dictionary<int, DateTime>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                IF OBJECT_ID('dbo.EmpresaPersonalizaciones', 'U') IS NOT NULL
                    SELECT IdEmpresa, FechaCreacion FROM dbo.EmpresaPersonalizaciones
                    WHERE Activo = 1 AND Codigo = 'CHURIDO_HIST_PRECIOS';", cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        resultado[Convert.ToInt32(r["IdEmpresa"])] = Convert.ToDateTime(r["FechaCreacion"]);
            }
            return resultado;
        }

        // ══════════════════════════════════════════════════════════════════════════════════════
        //  ESQUEMA FÍSICO
        // ══════════════════════════════════════════════════════════════════════════════════════

        public bool ExisteTabla(string tabla)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                return ExisteTabla(cn, null, tabla);
            }
        }

        private static bool ExisteTabla(SqlConnection cn, SqlTransaction tx, string tabla)
        {
            using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@t, 'U') IS NULL THEN 0 ELSE 1 END", cn, tx))
            {
                cmd.Parameters.AddWithValue("@t", "dbo." + Q(tabla));
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        /// <summary>Columnas de la tabla (de negocio y de sistema), en orden físico.</summary>
        public List<ColumnaHoja> ObtenerColumnas(string tabla)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                return ObtenerColumnas(cn, null, tabla);
            }
        }

        private static List<ColumnaHoja> ObtenerColumnas(SqlConnection cn, SqlTransaction tx, string tabla)
        {
            var cols = new List<ColumnaHoja>();
            using (var cmd = new SqlCommand(@"
                SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE,
                       IS_NULLABLE, COLUMN_DEFAULT, ORDINAL_POSITION
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t
                ORDER BY ORDINAL_POSITION", cn, tx))
            {
                cmd.Parameters.AddWithValue("@t", tabla);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string tipo = Convert.ToString(r["DATA_TYPE"]).ToLowerInvariant();
                        bool esDecimal = tipo == "decimal" || tipo == "numeric";
                        string nombre = Convert.ToString(r["COLUMN_NAME"]);
                        cols.Add(new ColumnaHoja
                        {
                            Nombre = nombre,
                            Tipo = tipo,
                            Longitud = r["CHARACTER_MAXIMUM_LENGTH"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["CHARACTER_MAXIMUM_LENGTH"]),
                            Precision = esDecimal && r["NUMERIC_PRECISION"] != DBNull.Value ? Convert.ToInt32(r["NUMERIC_PRECISION"]) : (int?)null,
                            Escala = esDecimal && r["NUMERIC_SCALE"] != DBNull.Value ? Convert.ToInt32(r["NUMERIC_SCALE"]) : (int?)null,
                            PermiteNulos = string.Equals(Convert.ToString(r["IS_NULLABLE"]), "YES", StringComparison.OrdinalIgnoreCase),
                            ValorDefecto = DefaultLegible(r["COLUMN_DEFAULT"] as string),
                            Orden = Convert.ToInt32(r["ORDINAL_POSITION"]),
                            EsSistema = EsColumnaSistema(nombre)
                        });
                    }
                }
            }
            return cols;
        }

        /// <summary>"((0))" → "0", "(N'abc')" → "abc", "(getdate())" → "GETDATE()".</summary>
        private static string DefaultLegible(string def)
        {
            if (string.IsNullOrWhiteSpace(def)) return null;
            string s = def.Trim();
            while (s.Length >= 2 && s[0] == '(' && s[s.Length - 1] == ')')
                s = s.Substring(1, s.Length - 2).Trim();
            if (s.StartsWith("N'", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                s = s.Substring(1, s.Length - 2).Replace("''", "'");
            if (string.Equals(s, "getdate()", StringComparison.OrdinalIgnoreCase)) s = "GETDATE()";
            return s;
        }

        /// <summary>Filas totales de la tabla (metadatos de particiones: no recorre la tabla). 0 si no existe.</summary>
        private static int ContarFilas(SqlConnection cn, SqlTransaction tx, string tabla)
        {
            using (var cmd = new SqlCommand(@"
                SELECT ISNULL(SUM(p.rows), 0) FROM sys.partitions p
                WHERE p.object_id = OBJECT_ID(@t, 'U') AND p.index_id IN (0, 1)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@t", "dbo." + Q(tabla));
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>Módulos SQL (SP, vistas, funciones) cuyo texto menciona la tabla Ini_X o su Staging_Ini_X.</summary>
        private static List<string> ObtenerUsadaPor(SqlConnection cn, SqlTransaction tx, string tablaIni)
        {
            // El nombre debe ir seguido de un carácter que no sea de identificador, para que
            // "Ini_PYG" no coincida con "Ini_PYGDetalladoConAjuste".
            string patron = "%" + tablaIni.Replace("[", "[[]").Replace("_", "[_]") + "[^A-Za-z0-9_]%";
            var lista = new List<string>();
            using (var cmd = new SqlCommand(@"
                SELECT OBJECT_SCHEMA_NAME(m.object_id) + '.' + OBJECT_NAME(m.object_id)
                FROM sys.sql_modules m
                WHERE m.definition LIKE @p
                ORDER BY 1", cn, tx))
            {
                cmd.Parameters.AddWithValue("@p", patron);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(r.GetString(0));
            }
            return lista;
        }

        /// <summary>Detalle de una hoja para el panel del gestor: columnas, uso, filas y diagnóstico.</summary>
        public HojaCargueDetalle ObtenerDetalle(HojaCargue hoja)
        {
            var d = new HojaCargueDetalle { Hoja = hoja };
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                d.ExisteIni = ExisteTabla(cn, null, hoja.TablaIni);
                d.ExisteStaging = ExisteTabla(cn, null, hoja.TablaStaging);

                var colsIni = d.ExisteIni ? ObtenerColumnas(cn, null, hoja.TablaIni) : new List<ColumnaHoja>();
                var colsStg = d.ExisteStaging ? ObtenerColumnas(cn, null, hoja.TablaStaging) : new List<ColumnaHoja>();
                d.Columnas = colsIni.Where(c => !c.EsSistema).ToList();
                d.ColumnasSistema = colsIni.Where(c => c.EsSistema).ToList();

                if (d.ExisteIni)
                {
                    d.FilasIni = ContarFilas(cn, null, hoja.TablaIni);
                    if (d.FilasIni > 0 && colsIni.Any(c => c.Nombre.Equals("IdEmpresa_Log", StringComparison.OrdinalIgnoreCase)))
                    {
                        using (var cmd = new SqlCommand(
                            $"SELECT IdEmpresa_Log, COUNT(*) FROM dbo.{Q(hoja.TablaIni)} WHERE IdEmpresa_Log IS NOT NULL GROUP BY IdEmpresa_Log", cn))
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                                d.FilasPorEmpresa[Convert.ToInt32(r[0])] = Convert.ToInt32(r[1]);
                    }
                }
                if (d.ExisteStaging) d.FilasStaging = ContarFilas(cn, null, hoja.TablaStaging);
                d.UsadaPor = ObtenerUsadaPor(cn, null, hoja.TablaIni);
                d.Diagnostico = Diagnosticar(d.ExisteIni, d.ExisteStaging, colsIni, colsStg);
            }
            return d;
        }

        /// <summary>Verifica que Ini_X y Staging_Ini_X existan, tengan las columnas de sistema y coincidan entre sí.</summary>
        private static List<HallazgoDiagnostico> Diagnosticar(bool existeIni, bool existeStg, List<ColumnaHoja> colsIni, List<ColumnaHoja> colsStg)
        {
            var h = new List<HallazgoDiagnostico>();
            void Add(string nivel, string codigo, string detalle = null) => h.Add(new HallazgoDiagnostico { Nivel = nivel, Codigo = codigo, Detalle = detalle });

            if (!existeIni) Add("error", "IniNoExiste");
            if (!existeStg) Add("error", "StagingNoExiste");
            if (!existeIni || !existeStg) return h;

            var nombresIni = new HashSet<string>(colsIni.Select(c => c.Nombre), StringComparer.OrdinalIgnoreCase);
            var nombresStg = new HashSet<string>(colsStg.Select(c => c.Nombre), StringComparer.OrdinalIgnoreCase);

            foreach (var c in ColumnasSistemaIni.Where(c => !nombresIni.Contains(c)))
                Add("error", "SistemaFaltaIni", c);
            foreach (var c in ColumnasSistemaIni.Except(ColumnasSoloIni).Concat(ColumnasSistemaStaging).Where(c => !nombresStg.Contains(c)))
                Add("error", "SistemaFaltaStaging", c);

            var negocioIni = colsIni.Where(c => !c.EsSistema).ToList();
            if (negocioIni.Count == 0) Add("error", "SinColumnas");
            if (!nombresIni.Contains(ColumnaAnio)) Add("error", "SinAnio");
            if (!nombresIni.Contains("Mes")) Add("warn", "SinMes");

            var stgPorNombre = colsStg.ToDictionary(c => c.Nombre, StringComparer.OrdinalIgnoreCase);
            foreach (var c in negocioIni)
            {
                if (!stgPorNombre.TryGetValue(c.Nombre, out var s)) { Add("error", "ColumnaFaltaStaging", c.Nombre); continue; }
                if (!string.Equals(c.TipoCompleto, s.TipoCompleto, StringComparison.OrdinalIgnoreCase))
                    Add("warn", "TipoDistinto", $"{c.Nombre}: {c.TipoCompleto} ≠ {s.TipoCompleto}");
            }
            foreach (var s in colsStg.Where(c => !c.EsSistema && !nombresIni.Contains(c.Nombre)))
                Add("warn", "ColumnaSoloStaging", s.Nombre);

            if (h.Count == 0) Add("ok", "Ok");
            return h;
        }

        // ══════════════════════════════════════════════════════════════════════════════════════
        //  VALIDACIÓN Y GENERACIÓN DE DDL
        // ══════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Normaliza y valida una columna diseñada en el gestor. Lanza <see cref="GestorHojasException"/>.</summary>
        public static void ValidarColumna(ColumnaHoja c)
        {
            c.Nombre = (c.Nombre ?? "").Trim();
            c.Tipo = (c.Tipo ?? "").Trim().ToLowerInvariant();
            if (EsColumnaSistema(c.Nombre)) throw new GestorHojasException("HojasCargue_Err_ColumnaSistema", c.Nombre);
            if (!NombreColumnaValido(c.Nombre)) throw new GestorHojasException("HojasCargue_Err_ColumnaNombre", c.Nombre);
            if (!TiposPermitidos.Contains(c.Tipo)) throw new GestorHojasException("HojasCargue_Err_Tipo", c.Nombre);

            if (c.Tipo == "varchar" || c.Tipo == "nvarchar")
            {
                int max = c.Tipo == "varchar" ? 8000 : 4000;
                if (!c.Longitud.HasValue || c.Longitud == 0 || c.Longitud < -1 || c.Longitud > max)
                    throw new GestorHojasException("HojasCargue_Err_Longitud", c.Nombre);
                c.Precision = null; c.Escala = null;
            }
            else if (c.Tipo == "decimal")
            {
                if (!c.Precision.HasValue || c.Precision < 1 || c.Precision > 38) throw new GestorHojasException("HojasCargue_Err_Precision", c.Nombre);
                c.Escala = c.Escala ?? 0;
                if (c.Escala < 0 || c.Escala > c.Precision) throw new GestorHojasException("HojasCargue_Err_Precision", c.Nombre);
                c.Longitud = null;
            }
            else { c.Longitud = null; c.Precision = null; c.Escala = null; }

            c.ValorDefecto = string.IsNullOrWhiteSpace(c.ValorDefecto) ? null : c.ValorDefecto.Trim();
            if (c.ValorDefecto != null) LiteralDefecto(c);   // valida
            if (!c.PermiteNulos && c.ValorDefecto == null && c.Nombre != ColumnaAnio)
            {
                // Una columna NOT NULL sin default rechazaría cualquier celda vacía del Excel con un
                // error de SQL crudo en el cargue: se exige un valor por defecto.
                throw new GestorHojasException("HojasCargue_Err_NoNuloSinDefecto", c.Nombre);
            }
        }

        /// <summary>Tipo T-SQL de una columna ya validada.</summary>
        private static string TipoSql(ColumnaHoja c)
        {
            switch (c.Tipo)
            {
                case "varchar":
                case "nvarchar":
                    return c.Tipo.ToUpperInvariant() + "(" + (c.Longitud == -1 ? "MAX" : c.Longitud.Value.ToString(CultureInfo.InvariantCulture)) + ")";
                case "decimal":
                    return $"DECIMAL({c.Precision.Value},{c.Escala ?? 0})";
                default:
                    return c.Tipo.ToUpperInvariant();
            }
        }

        /// <summary>Literal T-SQL del valor por defecto (validado según el tipo).</summary>
        private static string LiteralDefecto(ColumnaHoja c)
        {
            string v = c.ValorDefecto;
            var inv = CultureInfo.InvariantCulture;
            switch (c.Tipo)
            {
                case "int":
                case "bigint":
                    if (!long.TryParse(v, NumberStyles.Integer, inv, out long l) || (c.Tipo == "int" && (l < int.MinValue || l > int.MaxValue)))
                        throw new GestorHojasException("HojasCargue_Err_Defecto", c.Nombre);
                    return l.ToString(inv);
                case "decimal":
                case "money":
                    if (!decimal.TryParse(v.Replace(',', '.'), NumberStyles.Number, inv, out decimal d))
                        throw new GestorHojasException("HojasCargue_Err_Defecto", c.Nombre);
                    return d.ToString(inv);
                case "bit":
                    string b = v.ToLowerInvariant();
                    if (b == "1" || b == "true" || b == "si" || b == "sí") return "1";
                    if (b == "0" || b == "false" || b == "no") return "0";
                    throw new GestorHojasException("HojasCargue_Err_Defecto", c.Nombre);
                case "date":
                case "datetime":
                    if (string.Equals(v, "GETDATE()", StringComparison.OrdinalIgnoreCase)) return "GETDATE()";
                    if (!DateTime.TryParseExact(v, new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss" }, inv, DateTimeStyles.None, out DateTime f))
                        throw new GestorHojasException("HojasCargue_Err_Defecto", c.Nombre);
                    return c.Tipo == "date" ? "'" + f.ToString("yyyyMMdd", inv) + "'" : "'" + f.ToString("yyyy-MM-ddTHH:mm:ss", inv) + "'";
                default: // varchar / nvarchar
                    if (c.Longitud.HasValue && c.Longitud > 0 && v.Length > c.Longitud)
                        throw new GestorHojasException("HojasCargue_Err_Defecto", c.Nombre);
                    return "N'" + v.Replace("'", "''") + "'";
            }
        }

        /// <summary>Nombre de constraint seguro para el default de una columna.</summary>
        private static string NombreConstraintDefecto(string tabla, string columna)
        {
            string limpio = Regex.Replace(columna, "[^A-Za-z0-9_]", "_");
            string nombre = $"DF_{tabla}_{limpio}";
            return nombre.Length > 120 ? nombre.Substring(0, 120) : nombre;
        }

        private static string DefinicionColumna(string tabla, ColumnaHoja c) =>
            $"{Q(c.Nombre)} {TipoSql(c)} {(c.PermiteNulos ? "NULL" : "NOT NULL")}" +
            (c.ValorDefecto != null ? $" CONSTRAINT {Q(NombreConstraintDefecto(tabla, c.Nombre))} DEFAULT ({LiteralDefecto(c)})" : "");

        /// <summary>Valida los datos de una hoja nueva (nombres, columnas, alcance). Lanza <see cref="GestorHojasException"/>.</summary>
        public static void ValidarNueva(HojaCargueNueva n, IEnumerable<HojaCargue> catalogo, bool tablaExiste)
        {
            n.NombreHoja = (n.NombreHoja ?? "").Trim();
            n.TablaIni = (n.TablaIni ?? "").Trim();
            n.Alcance = string.Equals(n.Alcance, HojaCargue.AlcancePersonalizada, StringComparison.OrdinalIgnoreCase)
                ? HojaCargue.AlcancePersonalizada : HojaCargue.AlcanceEstandar;
            n.Descripcion = string.IsNullOrWhiteSpace(n.Descripcion) ? null : n.Descripcion.Trim();
            if (n.Descripcion != null && n.Descripcion.Length > 500) n.Descripcion = n.Descripcion.Substring(0, 500);

            if (!NombreHojaValido(n.NombreHoja)) throw new GestorHojasException("HojasCargue_Err_NombreHoja");
            if (!NombreTablaValido(n.TablaIni)) throw new GestorHojasException("HojasCargue_Err_NombreTabla");

            var lista = catalogo?.ToList() ?? new List<HojaCargue>();
            if (lista.Any(h => h.NombreHoja.Equals(n.NombreHoja, StringComparison.OrdinalIgnoreCase)))
                throw new GestorHojasException("HojasCargue_Err_HojaDuplicada", n.NombreHoja);
            if (lista.Any(h => h.TablaIni.Equals(n.TablaIni, StringComparison.OrdinalIgnoreCase)))
                throw new GestorHojasException("HojasCargue_Err_TablaDuplicada", n.TablaIni);

            if (n.Alcance == HojaCargue.AlcancePersonalizada && (n.Empresas == null || n.Empresas.Count == 0))
                throw new GestorHojasException("HojasCargue_Err_SinEmpresas");

            if (tablaExiste) return;   // se registra con la estructura que ya tiene

            n.Columnas = n.Columnas ?? new List<ColumnaHoja>();
            if (n.Columnas.Count == 0) throw new GestorHojasException("HojasCargue_Err_SinColumnas");
            foreach (var c in n.Columnas) ValidarColumna(c);

            var duplicada = n.Columnas.GroupBy(c => c.Nombre, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicada != null) throw new GestorHojasException("HojasCargue_Err_ColumnaDuplicada", duplicada.Key);
            if (!n.Columnas.Any(c => c.Nombre.Equals(ColumnaAnio, StringComparison.OrdinalIgnoreCase)))
                throw new GestorHojasException("HojasCargue_Err_SinAnio");
        }

        /// <summary>
        /// Sentencias DDL para crear Ini_X + Staging_Ini_X con la convención de Sql/020
        /// (columnas de sistema, FK a Escenarios y a CarguesLotes, índices).
        /// </summary>
        public static List<string> SentenciasCrearTablas(string tablaIni, IList<ColumnaHoja> columnas)
        {
            string stg = "Staging_" + tablaIni;
            var negocioIni = string.Join(",\n    ", columnas.Select(c => DefinicionColumna(tablaIni, c)));
            var negocioStg = string.Join(",\n    ", columnas.Select(c => DefinicionColumna(stg, c)));

            return new List<string>
            {
$@"CREATE TABLE dbo.{Q(tablaIni)} (
    {negocioIni},
    [Llave] VARCHAR(500) NULL,
    [IdEmpresa_Log] INT NULL,
    [IdUsuarioCargue_Log] INT NULL,
    [FechaCargue_Log] DATETIME NULL,
    [Historico_Log] TINYINT NOT NULL CONSTRAINT {Q("DF_" + tablaIni + "_Historico")} DEFAULT (0),
    [IdUsuarioEjecucion_Log] INT NULL,
    [FechaEjecucion_Log] DATETIME NULL,
    [Observacion_Log] VARCHAR(500) NULL,
    [IdEscenario] TINYINT NOT NULL CONSTRAINT {Q("DF_" + tablaIni + "_Escenario")} DEFAULT (1),
    CONSTRAINT {Q("FK_" + tablaIni + "_Escenario")} FOREIGN KEY ([IdEscenario]) REFERENCES dbo.[Escenarios] ([Id])
);",
$@"CREATE NONCLUSTERED INDEX {Q("IX_" + tablaIni + "_Empresa_Anio_Escenario")}
    ON dbo.{Q(tablaIni)} ([IdEmpresa_Log], {Q(ColumnaAnio)}, [IdEscenario], [Historico_Log]);",
$@"CREATE TABLE dbo.{Q(stg)} (
    {negocioStg},
    [Llave] VARCHAR(500) NULL,
    [IdEmpresa_Log] INT NULL,
    [IdUsuarioCargue_Log] INT NULL,
    [FechaCargue_Log] DATETIME NULL,
    [Historico_Log] TINYINT NOT NULL,
    [IdEscenario] TINYINT NOT NULL,
    [IdLote] BIGINT NOT NULL CONSTRAINT {Q("DF_" + stg + "_IdLote")} DEFAULT (0),
    [NumeroFilaExcel] INT NULL,
    CONSTRAINT {Q("FK_" + stg + "_Lote")} FOREIGN KEY ([IdLote]) REFERENCES dbo.[CarguesLotes] ([IdLote]) ON DELETE CASCADE
);",
$@"CREATE NONCLUSTERED INDEX {Q("IX_" + stg + "_Lote")} ON dbo.{Q(stg)} ([IdLote]);"
            };
        }

        /// <summary>Script legible (vista previa en el gestor) de las sentencias indicadas.</summary>
        public static string ComoScript(IEnumerable<string> sentencias) =>
            string.Join("\nGO\n\n", sentencias) + "\nGO";

        /// <summary>
        /// Sentencias para crear el Staging_Ini_X de una tabla Ini_X que ya existe (registro de tablas
        /// creadas fuera del gestor): copia sus columnas de negocio con el mismo tipo.
        /// </summary>
        private static List<string> SentenciasCrearStagingDesdeIni(string tablaIni, List<ColumnaHoja> colsIni)
        {
            var negocio = colsIni.Where(c => !c.EsSistema).Select(c =>
            {
                var copia = new ColumnaHoja { Nombre = c.Nombre, Tipo = c.Tipo, Longitud = c.Longitud, Precision = c.Precision, Escala = c.Escala, PermiteNulos = true };
                return copia;
            }).ToList();
            return SentenciasCrearTablas(tablaIni, negocio).Skip(2).ToList();   // solo las 2 de staging
        }

        // ══════════════════════════════════════════════════════════════════════════════════════
        //  OPERACIONES (cada una en UNA transacción: DDL + catálogo, o nada)
        // ══════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Crea la hoja: si Ini_X no existe, crea Ini_X + Staging_Ini_X con las columnas diseñadas; si ya
        /// existe, la registra con su estructura actual (y crea el Staging_Ini_X si faltara). Devuelve el
        /// Id de la hoja y las sentencias DDL ejecutadas (para la auditoría).
        /// </summary>
        public (int idHoja, List<string> sql) Crear(HojaCargueNueva n, IEnumerable<HojaCargue> catalogo, int idUsuario)
        {
            var ejecutadas = new List<string>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                bool existeIni = ExisteTabla(cn, null, n.TablaIni);
                ValidarNueva(n, catalogo, existeIni);

                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        string origen = HojaCargue.OrigenGestor;
                        if (!existeIni)
                        {
                            if (ExisteTabla(cn, tx, "Staging_" + n.TablaIni))
                                throw new GestorHojasException("HojasCargue_Err_StagingHuerfana", "Staging_" + n.TablaIni);
                            ejecutadas.AddRange(SentenciasCrearTablas(n.TablaIni, n.Columnas));
                        }
                        else
                        {
                            origen = HojaCargue.OrigenExistente;
                            var colsIni = ObtenerColumnas(cn, tx, n.TablaIni);
                            if (!colsIni.Any(c => c.Nombre.Equals(ColumnaAnio, StringComparison.OrdinalIgnoreCase)))
                                throw new GestorHojasException("HojasCargue_Err_SinAnio");
                            if (!ExisteTabla(cn, tx, "Staging_" + n.TablaIni))
                                ejecutadas.AddRange(SentenciasCrearStagingDesdeIni(n.TablaIni, colsIni));
                        }

                        foreach (var sql in ejecutadas) Ejecutar(cn, tx, sql);

                        int idHoja;
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO dbo.CargueHojas (NombreHoja, TablaIni, Descripcion, Alcance, Activo, Orden, Origen,
                                                         FechaCreacion, VigenteDesde, IdUsuarioCreacion)
                            VALUES (@Hoja, @Tabla, @Desc, @Alcance, 1, @Orden, @Origen, GETDATE(), GETDATE(), @Usr);
                            SELECT CAST(SCOPE_IDENTITY() AS INT);", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Hoja", n.NombreHoja);
                            cmd.Parameters.AddWithValue("@Tabla", n.TablaIni);
                            cmd.Parameters.AddWithValue("@Desc", (object)n.Descripcion ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Alcance", n.Alcance);
                            cmd.Parameters.AddWithValue("@Orden", n.Orden);
                            cmd.Parameters.AddWithValue("@Origen", origen);
                            cmd.Parameters.AddWithValue("@Usr", idUsuario);
                            idHoja = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        if (n.Alcance == HojaCargue.AlcancePersonalizada)
                            GuardarEmpresas(cn, tx, idHoja, n.Empresas);

                        tx.Commit();
                        return (idHoja, ejecutadas);
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        /// <summary>Nombre de hoja, descripción, orden y activo (solo metadatos). Activarla renueva su vigencia.</summary>
        public void GuardarGeneral(HojaCargue actual, string nombreHoja, string descripcion, int orden, bool activo,
            IEnumerable<HojaCargue> catalogo, int idUsuario)
        {
            nombreHoja = (nombreHoja ?? "").Trim();
            if (!NombreHojaValido(nombreHoja)) throw new GestorHojasException("HojasCargue_Err_NombreHoja");
            if (catalogo.Any(h => h.IdHoja != actual.IdHoja && h.NombreHoja.Equals(nombreHoja, StringComparison.OrdinalIgnoreCase)))
                throw new GestorHojasException("HojasCargue_Err_HojaDuplicada", nombreHoja);

            descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
            if (descripcion != null && descripcion.Length > 500) descripcion = descripcion.Substring(0, 500);

            if (activo && !actual.Activo)
            {
                // No se activa una hoja cuya estructura está rota: el cargue fallaría al escribir en ella.
                var detalle = ObtenerDetalle(actual);
                if (detalle.Diagnostico.Any(x => x.Nivel == "error"))
                    throw new GestorHojasException("HojasCargue_Err_DiagnosticoConErrores");
            }

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                UPDATE dbo.CargueHojas
                SET NombreHoja = @Hoja, Descripcion = @Desc, Orden = @Orden, Activo = @Activo,
                    VigenteDesde = CASE WHEN @Activo = 1 AND Activo = 0 THEN GETDATE() ELSE VigenteDesde END,
                    FechaModificacion = GETDATE(), IdUsuarioModificacion = @Usr
                WHERE IdHoja = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Hoja", nombreHoja);
                cmd.Parameters.AddWithValue("@Desc", (object)descripcion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Orden", orden);
                cmd.Parameters.AddWithValue("@Activo", activo);
                cmd.Parameters.AddWithValue("@Usr", idUsuario);
                cmd.Parameters.AddWithValue("@Id", actual.IdHoja);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Alcance Estándar/Personalizada + empresas. Volverla Estándar renueva su vigencia; una empresa
        /// nueva o reactivada se asigna con fecha de hoy (el rollback no exige su snapshot hacia atrás).
        /// </summary>
        public void GuardarAlcance(HojaCargue actual, string alcance, List<int> empresas, int idUsuario)
        {
            alcance = string.Equals(alcance, HojaCargue.AlcancePersonalizada, StringComparison.OrdinalIgnoreCase)
                ? HojaCargue.AlcancePersonalizada : HojaCargue.AlcanceEstandar;
            empresas = (empresas ?? new List<int>()).Where(e => e > 0).Distinct().ToList();
            if (alcance == HojaCargue.AlcancePersonalizada && empresas.Count == 0)
                throw new GestorHojasException("HojasCargue_Err_SinEmpresas");

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = new SqlCommand(@"
                            UPDATE dbo.CargueHojas
                            SET VigenteDesde = CASE WHEN @Alcance = 'ESTANDAR' AND Alcance <> 'ESTANDAR' THEN GETDATE() ELSE VigenteDesde END,
                                Alcance = @Alcance, FechaModificacion = GETDATE(), IdUsuarioModificacion = @Usr
                            WHERE IdHoja = @Id", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Alcance", alcance);
                            cmd.Parameters.AddWithValue("@Usr", idUsuario);
                            cmd.Parameters.AddWithValue("@Id", actual.IdHoja);
                            cmd.ExecuteNonQuery();
                        }
                        GuardarEmpresas(cn, tx, actual.IdHoja, alcance == HojaCargue.AlcancePersonalizada ? empresas : new List<int>());
                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        /// <summary>Reemplazo completo de las empresas activas de la hoja (las que salen quedan Activo = 0).</summary>
        private static void GuardarEmpresas(SqlConnection cn, SqlTransaction tx, int idHoja, List<int> empresas)
        {
            string csv = string.Join(",", (empresas ?? new List<int>()).Distinct().Select(e => e.ToString(CultureInfo.InvariantCulture)));
            using (var cmd = new SqlCommand(@"
                DECLARE @ids TABLE (IdEmpresa INT PRIMARY KEY);
                IF LEN(@Csv) > 0
                    INSERT INTO @ids (IdEmpresa)
                    SELECT DISTINCT CAST(LTRIM(RTRIM(x.i.value('.', 'VARCHAR(20)'))) AS INT)
                    FROM (SELECT CAST('<i>' + REPLACE(@Csv, ',', '</i><i>') + '</i>' AS XML) AS xm) t
                    CROSS APPLY t.xm.nodes('/i') x(i);

                UPDATE e SET Activo = 0
                FROM dbo.CargueHojasEmpresas e
                WHERE e.IdHoja = @IdHoja AND e.Activo = 1 AND NOT EXISTS (SELECT 1 FROM @ids i WHERE i.IdEmpresa = e.IdEmpresa);

                UPDATE e SET Activo = 1, FechaAsignacion = GETDATE()
                FROM dbo.CargueHojasEmpresas e
                INNER JOIN @ids i ON i.IdEmpresa = e.IdEmpresa
                WHERE e.IdHoja = @IdHoja AND e.Activo = 0;

                INSERT INTO dbo.CargueHojasEmpresas (IdHoja, IdEmpresa, Activo, FechaAsignacion)
                SELECT @IdHoja, i.IdEmpresa, 1, GETDATE()
                FROM @ids i
                INNER JOIN dbo.Empresas emp ON emp.EmpId = i.IdEmpresa
                WHERE NOT EXISTS (SELECT 1 FROM dbo.CargueHojasEmpresas e WHERE e.IdHoja = @IdHoja AND e.IdEmpresa = i.IdEmpresa);", cn, tx))
            {
                cmd.Parameters.AddWithValue("@Csv", csv);
                cmd.Parameters.AddWithValue("@IdHoja", idHoja);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Agrega una columna de negocio a Ini_X y Staging_Ini_X (permitido aunque haya datos).</summary>
        public List<string> AgregarColumna(HojaCargue hoja, ColumnaHoja col)
        {
            ValidarColumna(col);
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                var existentes = ObtenerColumnas(cn, null, hoja.TablaIni);
                if (existentes.Any(c => c.Nombre.Equals(col.Nombre, StringComparison.OrdinalIgnoreCase)))
                    throw new GestorHojasException("HojasCargue_Err_ColumnaDuplicada", col.Nombre);
                if (!col.PermiteNulos && col.ValorDefecto == null)
                    throw new GestorHojasException("HojasCargue_Err_NoNuloSinDefecto", col.Nombre);

                var sql = new List<string>
                {
                    $"ALTER TABLE dbo.{Q(hoja.TablaIni)} ADD {DefinicionColumna(hoja.TablaIni, col)};",
                    $"ALTER TABLE dbo.{Q(hoja.TablaStaging)} ADD {DefinicionColumna(hoja.TablaStaging, col)};"
                };
                EjecutarEnTransaccion(cn, sql);
                return sql;
            }
        }

        /// <summary>
        /// Modifica una columna existente (nombre, tipo, nulos, default). Solo con tablas vacías y sin
        /// módulos SQL que las usen: cambiar una columna que lee un SP lo rompería.
        /// </summary>
        public List<string> ModificarColumna(HojaCargue hoja, string nombreOriginal, ColumnaHoja col)
        {
            ValidarColumna(col);
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                ExigirEstructuraEditable(cn, hoja);
                var existentes = ObtenerColumnas(cn, null, hoja.TablaIni);
                var original = existentes.FirstOrDefault(c => !c.EsSistema && c.Nombre.Equals(nombreOriginal ?? "", StringComparison.OrdinalIgnoreCase));
                if (original == null) throw new GestorHojasException("HojasCargue_Err_ColumnaNoExiste", nombreOriginal);
                if (!col.Nombre.Equals(original.Nombre, StringComparison.OrdinalIgnoreCase)
                    && existentes.Any(c => c.Nombre.Equals(col.Nombre, StringComparison.OrdinalIgnoreCase)))
                    throw new GestorHojasException("HojasCargue_Err_ColumnaDuplicada", col.Nombre);
                if (original.Nombre.Equals(ColumnaAnio, StringComparison.OrdinalIgnoreCase) && !col.Nombre.Equals(ColumnaAnio, StringComparison.OrdinalIgnoreCase))
                    throw new GestorHojasException("HojasCargue_Err_AnioObligatorio");

                var sql = new List<string>();
                foreach (var tabla in new[] { hoja.TablaIni, hoja.TablaStaging })
                {
                    sql.Add(SentenciaQuitarDefecto(tabla, original.Nombre));
                    string nombreActual = original.Nombre;
                    if (!col.Nombre.Equals(original.Nombre, StringComparison.Ordinal))
                    {
                        sql.Add($"EXEC sp_rename N'dbo.{Q(tabla)}.{Q(original.Nombre)}', N'{col.Nombre.Replace("'", "''")}', 'COLUMN';");
                        nombreActual = col.Nombre;
                    }
                    sql.Add($"ALTER TABLE dbo.{Q(tabla)} ALTER COLUMN {Q(nombreActual)} {TipoSql(col)} {(col.PermiteNulos ? "NULL" : "NOT NULL")};");
                    if (col.ValorDefecto != null)
                        sql.Add($"ALTER TABLE dbo.{Q(tabla)} ADD CONSTRAINT {Q(NombreConstraintDefecto(tabla, col.Nombre))} DEFAULT ({LiteralDefecto(col)}) FOR {Q(nombreActual)};");
                }
                EjecutarEnTransaccion(cn, sql);
                return sql;
            }
        }

        /// <summary>Elimina una columna de negocio (tablas vacías y sin módulos SQL que las usen). Año no se puede quitar.</summary>
        public List<string> EliminarColumna(HojaCargue hoja, string nombre)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                ExigirEstructuraEditable(cn, hoja);
                var existentes = ObtenerColumnas(cn, null, hoja.TablaIni);
                var col = existentes.FirstOrDefault(c => !c.EsSistema && c.Nombre.Equals(nombre ?? "", StringComparison.OrdinalIgnoreCase));
                if (col == null) throw new GestorHojasException("HojasCargue_Err_ColumnaNoExiste", nombre);
                if (col.Nombre.Equals(ColumnaAnio, StringComparison.OrdinalIgnoreCase)) throw new GestorHojasException("HojasCargue_Err_AnioObligatorio");
                if (existentes.Count(c => !c.EsSistema) <= 1) throw new GestorHojasException("HojasCargue_Err_SinColumnas");

                var sql = new List<string>();
                foreach (var tabla in new[] { hoja.TablaIni, hoja.TablaStaging })
                {
                    sql.Add(SentenciaQuitarDefecto(tabla, col.Nombre));
                    sql.Add($"IF COL_LENGTH(N'dbo.{Q(tabla)}', N'{col.Nombre.Replace("'", "''")}') IS NOT NULL ALTER TABLE dbo.{Q(tabla)} DROP COLUMN {Q(col.Nombre)};");
                }
                EjecutarEnTransaccion(cn, sql);
                return sql;
            }
        }

        /// <summary>
        /// Elimina la hoja del catálogo. Si sus dos tablas están vacías y ningún módulo SQL las usa,
        /// también las borra (DROP); si no, las conserva. Devuelve las sentencias DDL ejecutadas.
        /// </summary>
        public List<string> Eliminar(HojaCargue hoja, bool borrarTablas)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                var sql = new List<string>();
                if (borrarTablas)
                {
                    ExigirEstructuraEditable(cn, hoja);
                    // La staging primero: tiene la FK hacia CarguesLotes, no al revés.
                    if (ExisteTabla(cn, null, hoja.TablaStaging)) sql.Add($"DROP TABLE dbo.{Q(hoja.TablaStaging)};");
                    if (ExisteTabla(cn, null, hoja.TablaIni)) sql.Add($"DROP TABLE dbo.{Q(hoja.TablaIni)};");
                }
                sql.Add("DELETE FROM dbo.CargueHojas WHERE IdHoja = " + hoja.IdHoja.ToString(CultureInfo.InvariantCulture) + ";");
                EjecutarEnTransaccion(cn, sql);
                return sql.Take(sql.Count - 1).ToList();
            }
        }

        /// <summary>Lanza si la hoja tiene datos o la usa algún módulo SQL.</summary>
        private static void ExigirEstructuraEditable(SqlConnection cn, HojaCargue hoja)
        {
            if (ContarFilas(cn, null, hoja.TablaIni) > 0 || ContarFilas(cn, null, hoja.TablaStaging) > 0)
                throw new GestorHojasException("HojasCargue_Err_TieneDatos");
            var usadaPor = ObtenerUsadaPor(cn, null, hoja.TablaIni);
            if (usadaPor.Count > 0)
                throw new GestorHojasException("HojasCargue_Err_UsadaPorModulos", string.Join(", ", usadaPor));
        }

        /// <summary>Quita el DEFAULT de una columna, si tiene (nombre del constraint resuelto en el servidor).</summary>
        private static string SentenciaQuitarDefecto(string tabla, string columna) =>
$@"DECLARE @df SYSNAME;
SELECT @df = dc.name FROM sys.default_constraints dc
INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'dbo.{Q(tabla)}') AND c.name = N'{columna.Replace("'", "''")}';
IF @df IS NOT NULL EXEC (N'ALTER TABLE dbo.{Q(tabla).Replace("'", "''")} DROP CONSTRAINT ' + QUOTENAME(@df));";

        private static void Ejecutar(SqlConnection cn, SqlTransaction tx, string sql)
        {
            using (var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 })
                cmd.ExecuteNonQuery();
        }

        private static void EjecutarEnTransaccion(SqlConnection cn, IEnumerable<string> sentencias)
        {
            using (var tx = cn.BeginTransaction())
            {
                try
                {
                    foreach (var s in sentencias) Ejecutar(cn, tx, s);
                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }
            }
        }

        /// <summary>
        /// Reordena las hojas (orden de las hojas en la plantilla) en una sola transacción. Solo metadatos.
        /// Los Id que no existan se ignoran.
        /// </summary>
        public void ActualizarOrden(IList<int> ids, IList<int> ordenes, int idUsuario)
        {
            if (ids == null || ordenes == null || ids.Count != ordenes.Count || ids.Count == 0)
                throw new GestorHojasException("HojasCargue_Err_Orden");

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        for (int i = 0; i < ids.Count; i++)
                        {
                            using (var cmd = new SqlCommand(@"
                                UPDATE dbo.CargueHojas
                                SET Orden = @Orden, FechaModificacion = GETDATE(), IdUsuarioModificacion = @Usr
                                WHERE IdHoja = @Id AND Orden <> @Orden", cn, tx))
                            {
                                cmd.Parameters.AddWithValue("@Orden", ordenes[i]);
                                cmd.Parameters.AddWithValue("@Usr", idUsuario);
                                cmd.Parameters.AddWithValue("@Id", ids[i]);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        /// <summary>Marca de modificación de la hoja (tras un cambio de columnas).</summary>
        public void TocarHoja(int idHoja, int idUsuario)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "UPDATE dbo.CargueHojas SET FechaModificacion = GETDATE(), IdUsuarioModificacion = @Usr WHERE IdHoja = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Usr", idUsuario);
                cmd.Parameters.AddWithValue("@Id", idHoja);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
