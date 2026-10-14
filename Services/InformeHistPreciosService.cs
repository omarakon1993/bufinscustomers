using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe histórico de precios (Churido &amp; Ucrania). Fuente: <c>dbo.Ini_HistPrecios_Churido</c>
    /// (hoja de cargue personalizada Z_HistPrecios_Churido), una fila por empresa / tipo de fruta / año / SEMESTRE
    /// (1 = ene-jun, 2 = jul-dic; es la cadencia con la que el cliente entrega los precios).
    /// Replica el Excel original: el precio anual de un tipo es el PROMEDIO de sus semestres del año y el
    /// "Total general" es el promedio de todos los registros del periodo (igual que la tabla dinámica
    /// "Promedio de precios" del Excel). Por si una carga se repite, antes se promedian los registros que
    /// coinciden en tipo/año/semestre, de modo que un duplicado no pesa doble. Se ignoran los registros sin semestre
    /// válido y los de precio 0 o vacío ("sin precio": un 0 bajaría los promedios y no es un precio real).
    /// </summary>
    public class InformeHistPreciosService : BaseService
    {
        public const string CodigoMenu = "INFORMES_HIST_PRECIOS";
        // Tabla fija (no viene del usuario): hoja personalizada del Gestor de hojas de cargue.
        private const string Tabla = "dbo.Ini_HistPrecios_Churido";

        private class Registro
        {
            public string Tipo;
            public int Anio;
            public int Sem;
            public decimal Valor;
        }

        // ---------- Lectura ----------

        private List<Registro> LeerRegistros(int idEmpresa, byte idEscenario)
        {
            var crudos = new List<Registro>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "SELECT TipoFruta, [Año] AS Anio, Semestre, Valor FROM " + Tabla +
                " WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario, 1) = @IdEscenario" +
                "   AND Valor > 0 AND NULLIF(LTRIM(RTRIM(TipoFruta)), '') IS NOT NULL", cn))   // 0 = sin precio (el Excel original deja la celda vacía)
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (!int.TryParse(Convert.ToString(r["Anio"])?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int anio))
                            continue;
                        crudos.Add(new Registro
                        {
                            Tipo = Convert.ToString(r["TipoFruta"]).Trim(),
                            Anio = anio,
                            Sem = ParsearSemestre(Convert.ToString(r["Semestre"])),
                            Valor = Convert.ToDecimal(r["Valor"])
                        });
                    }
                }
            }

            // Una fila por (tipo, año, semestre): los repetidos se promedian.
            return crudos
                .Where(x => x.Sem >= 1)
                .GroupBy(x => new { Tipo = x.Tipo.ToUpperInvariant(), x.Anio, x.Sem })
                .Select(g => new Registro { Tipo = g.First().Tipo, Anio = g.Key.Anio, Sem = g.Key.Sem, Valor = g.Average(x => x.Valor) })
                .ToList();
        }

        /// <summary>Semestre 1 o 2 desde "1", "2", "S1", "1 Sem", "2do semestre"…; 0 si no se reconoce.</summary>
        private static int ParsearSemestre(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(texto, @"\d+");
            if (!m.Success || !int.TryParse(m.Value, out int n)) return 0;
            return n == 1 || n == 2 ? n : 0;   // cualquier otro número (3, 7, 10…) no es un semestre
        }

        public HpOpciones ObtenerOpciones(int idEmpresa, byte idEscenario)
        {
            var regs = LeerRegistros(idEmpresa, idEscenario);
            return new HpOpciones
            {
                HayDatos = regs.Count > 0,
                Semestres = regs.Select(x => new { x.Anio, x.Sem }).Distinct()
                    .OrderBy(m => m.Anio).ThenBy(m => m.Sem)
                    .Select(m => new HpSemestre { Anio = m.Anio, Sem = m.Sem }).ToList(),
                Tipos = regs.Select(x => x.Tipo).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList()
            };
        }

        // ---------- Reporte ----------

        private static int SubPeriodo(string agrupar, int sem) { return agrupar == "Semestre" ? sem : 0; }

        private static int ClaveSem(int anio, int sem) { return anio * 2 + (sem - 1); }

        /// <summary>Promedio de los registros de cada periodo (año, sub-periodo).</summary>
        private static Dictionary<(int, int), decimal> Medias(IEnumerable<Registro> regs, string agrupar)
        {
            return regs.GroupBy(x => (x.Anio, SubPeriodo(agrupar, x.Sem)))
                .ToDictionary(g => g.Key, g => g.Average(x => x.Valor));
        }

        public HpReporte Construir(FiltrosHistPrecios f)
        {
            return ConstruirConRegistros(f, LeerRegistros(f.IdEmpresa, f.IdEscenario));
        }

        /// <summary>Todo el cálculo del informe a partir de los registros ya leídos (separado para poder probarlo sin BD).</summary>
        private HpReporte ConstruirConRegistros(FiltrosHistPrecios f, List<Registro> registros)
        {
            string agrupar = f.Agrupar == "Semestre" ? "Semestre" : "Anio";
            int ini = ClaveSem(f.AnioDesde, Math.Min(2, Math.Max(1, f.SemDesde)));
            int fin = ClaveSem(f.AnioHasta, Math.Min(2, Math.Max(1, f.SemHasta)));
            if (fin < ini) { int t = ini; ini = fin; fin = t; }

            var reporte = new HpReporte
            {
                Agrupar = agrupar,
                AnioDesde = ini / 2, SemDesde = (ini % 2) + 1,
                AnioHasta = fin / 2, SemHasta = (fin % 2) + 1
            };

            var tiposFiltro = (f.Tipos ?? new List<string>()).Select(t => (t ?? "").Trim().ToUpperInvariant()).Where(t => t.Length > 0).ToList();
            var todos = registros
                .Where(x => tiposFiltro.Count == 0 || tiposFiltro.Contains(x.Tipo.ToUpperInvariant()))
                .ToList();

            var regs = todos.Where(x => { int k = ClaveSem(x.Anio, x.Sem); return k >= ini && k <= fin; }).ToList();
            if (regs.Count == 0)
            {
                reporte.SinDatos = true;
                return reporte;
            }

            // Comparable: los MISMOS semestres del año anterior (rango desplazado 2 semestres), reubicados en el año
            // actual para que caigan en el mismo periodo. Un rango parcial (p. ej. solo S1) se compara contra el S1
            // del año anterior y no contra el año completo.
            var previos = todos.Where(x => { int k = ClaveSem(x.Anio, x.Sem); return k >= ini - 2 && k <= fin - 2; })
                .Select(x => new Registro { Tipo = x.Tipo, Anio = x.Anio + 1, Sem = x.Sem, Valor = x.Valor })
                .ToList();

            var periodos = regs.Select(x => new { x.Anio, Sub = SubPeriodo(agrupar, x.Sem) })
                .Distinct().OrderBy(p => p.Anio).ThenBy(p => p.Sub).ToList();
            reporte.Periodos = periodos.Select(p => new HpPeriodo { Anio = p.Anio, Sub = p.Sub }).ToList();
            var indice = new Dictionary<(int, int), int>();
            for (int i = 0; i < periodos.Count; i++) indice[(periodos[i].Anio, periodos[i].Sub)] = i;

            var series = new List<HpSerie>();
            foreach (var g in regs.GroupBy(x => x.Tipo.ToUpperInvariant()))
            {
                var prevTipo = previos.Where(x => x.Tipo.ToUpperInvariant() == g.Key).ToList();
                series.Add(ConstruirSerie(reporte, g.First().Tipo, g.ToList(), prevTipo, agrupar, indice));
            }
            reporte.Total = ConstruirSerie(reporte, null, regs, previos, agrupar, indice);
            reporte.Series = series.OrderByDescending(x => x.Promedio ?? 0).ThenBy(x => x.Tipo, StringComparer.CurrentCultureIgnoreCase).ToList();

            CalcularDestacados(reporte);
            CalcularCrecimientoCompuesto(reporte, regs);
            return reporte;
        }

        private static HpSerie ConstruirSerie(HpReporte reporte, string tipo, List<Registro> regs, List<Registro> previos,
            string agrupar, Dictionary<(int, int), int> indice)
        {
            int n = indice.Count;
            var valores = new decimal?[n];
            var variaciones = new decimal?[n];

            foreach (var kv in Medias(regs, agrupar)) valores[indice[kv.Key]] = kv.Value;
            foreach (var kv in Medias(previos, agrupar))
            {
                if (!indice.TryGetValue(kv.Key, out int i)) continue;
                if (valores[i].HasValue && kv.Value != 0m)
                {
                    variaciones[i] = valores[i].Value / kv.Value - 1m;
                    reporte.HayComparable = true;
                }
            }

            return new HpSerie
            {
                Tipo = tipo,
                Valores = valores,
                Variaciones = variaciones,
                Promedio = regs.Average(x => x.Valor),
                Minimo = regs.Min(x => x.Valor),
                Maximo = regs.Max(x => x.Valor)
            };
        }

        private static void CalcularDestacados(HpReporte r)
        {
            for (int i = r.Periodos.Count - 1; i >= 0; i--)
                if (r.Total.Valores[i].HasValue) { r.IdxUltimo = i; break; }
            int u = r.IdxUltimo;
            if (u < 0) return;

            var caro = r.Series.Where(s => s.Valores[u].HasValue).OrderByDescending(s => s.Valores[u].Value).FirstOrDefault();
            if (caro != null) r.MasCaro = new HpDestacado { Tipo = caro.Tipo, Valor = caro.Valores[u].Value };

            var conVar = r.Series.Where(s => s.Variaciones[u].HasValue).ToList();
            var alza = conVar.OrderByDescending(s => s.Variaciones[u].Value).FirstOrDefault();
            if (alza != null) r.MayorAlza = new HpDestacado { Tipo = alza.Tipo, Valor = alza.Variaciones[u].Value };
            var baja = conVar.OrderBy(s => s.Variaciones[u].Value).FirstOrDefault();
            if (baja != null && conVar.Count > 1) r.MenorVariacion = new HpDestacado { Tipo = baja.Tipo, Valor = baja.Variaciones[u].Value };
        }

        /// <summary>CAGR del promedio anual general entre el primer y el último año con datos del rango.</summary>
        private static void CalcularCrecimientoCompuesto(HpReporte r, List<Registro> regs)
        {
            var anual = regs.GroupBy(x => x.Anio).OrderBy(g => g.Key)
                .Select(g => new { Anio = g.Key, Prom = g.Average(x => x.Valor), Sems = g.Select(x => x.Sem).Distinct().Count() })
                .ToList();
            var ultimo = anual.Last();
            r.SemestresUltimoAnio = ultimo.Sems;
            if (anual.Count < 2) return;

            var primero = anual.First();
            int n = ultimo.Anio - primero.Anio;
            r.CagrAnioInicial = primero.Anio;
            r.CagrAnioFinal = ultimo.Anio;
            if (n >= 1 && primero.Prom > 0m)
                r.Cagr = (decimal)(Math.Pow((double)(ultimo.Prom / primero.Prom), 1.0 / n) - 1.0);
        }
    }
}
