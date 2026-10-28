using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe de rentabilidad por línea de negocio. Lee dbo.ModeloLineasNegocio (materializada; no ejecuta el SP).
    /// La tabla trae un renglón por mes, concepto contable (CuentaPUC) y línea, y la LÍNEA NO ES UNA COLUMNA: va como
    /// sufijo de Descripcion ("Ingresos Exp", "CMV Nal"). Las líneas son dinámicas por empresa, así que se descubren
    /// quitando el nombre del concepto (fijo por CuentaPUC) de la Descripcion. Todos los valores son magnitudes positivas
    /// (igual que ModeloPYG): el estado de resultados se arma restando costos y gastos.
    /// </summary>
    public class InformeLineasNegocioService : BaseService
    {
        // Concepto de cada CuentaPUC (el texto es el prefijo de Descripcion; lo que sobra es la línea). Confirmado contra las
        // empresas 1, 6, 7 y 8 de la BD (16 conceptos por línea).
        private static readonly Dictionary<string, string> ConceptoPorPuc = new Dictionary<string, string>
        {
            { "410505", "Ingresos" }, { "420505", "Otros ingresos no operacionales" }, { "421000", "Ingresos financieros" },
            { "510503", "Gastos de administración" }, { "516000", "Depreciación gasto" }, { "516500", "Amortización gasto" },
            { "519900", "Provisión de cartera" }, { "520503", "Gastos de venta" }, { "529915", "Provisión inventarios" },
            { "530505", "Otros gastos no operacionales" }, { "530520", "Gastos financieros" }, { "530525", "Gasto diferencia en cambio" },
            { "540000", "Impuesto renta" }, { "610505", "CMV" }, { "736000", "Depreciación costo" }, { "736500", "Amortización costo" }
        };

        private const string LineaGeneral = "General";
        // Cuadre contra ModeloPYG: tolerancia en pesos (redondeos de la cadena de subtotales).
        private const decimal ToleranciaCuadre = 1m;

        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        private static int Clave(int año, int mes) => año * 12 + (mes - 1);

        // ===== Lectura =====

        public List<LnFilaModelo> ObtenerFilas(int idEmpresa, byte idEscenario, int añoDesde, int añoHasta)
        {
            var filas = new List<LnFilaModelo>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT Año, Mes, CuentaPUC, Descripcion, Valor
                    FROM dbo.ModeloLineasNegocio
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario AND Año BETWEEN @AñoDesde AND @AñoHasta";
                var cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cmd.Parameters.AddWithValue("@AñoDesde", añoDesde);
                cmd.Parameters.AddWithValue("@AñoHasta", añoHasta);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string puc = (reader["CuentaPUC"] as string ?? "").Trim();
                        if (!ConceptoPorPuc.TryGetValue(puc, out string concepto)) continue; // concepto no reconocido: se ignora
                        int mes = Convert.ToInt32(reader["Mes"]);
                        if (mes < 1 || mes > 12) continue;
                        filas.Add(new LnFilaModelo
                        {
                            Año = Convert.ToInt32(reader["Año"]),
                            Mes = mes,
                            CuentaPUC = puc,
                            Linea = ExtraerLinea(reader["Descripcion"] as string, concepto),
                            Valor = reader["Valor"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["Valor"])
                        });
                    }
                }
            }
            return filas;
        }

        /// <summary>Quita el nombre del concepto de la Descripcion; lo que queda es la línea ("Ingresos Exp" → "Exp").</summary>
        internal static string ExtraerLinea(string descripcion, string concepto)
        {
            string d = (descripcion ?? "").Trim();
            if (d.StartsWith(concepto, StringComparison.OrdinalIgnoreCase))
                d = d.Substring(concepto.Length).Trim();
            return d.Length == 0 ? LineaGeneral : d;
        }

        public List<LnMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            var meses = new List<LnMesDisponible>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand(@"SELECT DISTINCT Año, Mes FROM dbo.ModeloLineasNegocio
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario AND Año IS NOT NULL AND Mes IS NOT NULL", cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int año = Convert.ToInt32(reader["Año"]), mes = Convert.ToInt32(reader["Mes"]);
                        if (mes < 1 || mes > 12) continue;
                        meses.Add(new LnMesDisponible { Anio = año, Mes = mes, Etiqueta = MesesAbrev[mes - 1] + " " + año });
                    }
                }
            }
            return meses.OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        /// <summary>Suma de ModeloPYG en el rango para el cuadre (null si no hay datos o la línea no existe).</summary>
        private decimal? SumaPyg(int idEmpresa, byte idEscenario, int kDesde, int kHasta, string descripcion)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        SELECT SUM(Valor), COUNT(*) FROM dbo.ModeloPYG
                        WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario
                          AND (Año * 12 + Mes - 1) BETWEEN @KDesde AND @KHasta
                          AND LTRIM(RTRIM(Descripcion)) = @Descripcion", cn);
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.Parameters.AddWithValue("@KDesde", kDesde);
                    cmd.Parameters.AddWithValue("@KHasta", kHasta);
                    cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                    cn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read() || reader.GetInt32(1) == 0 || reader.IsDBNull(0)) return null;
                        return Convert.ToDecimal(reader[0]);
                    }
                }
            }
            catch (Exception ex)
            {
                bufinscustomers.Helpers.AppLogger.Error(ex, "InformeLineasNegocioService.SumaPyg");
                return null;
            }
        }

        // ===== Cálculo =====

        /// <summary>Estado de resultados de una línea a partir de los acumulados por CuentaPUC.</summary>
        private static LnLineaResumen Calcular(string nombre, Func<string, decimal> g)
        {
            var r = new LnLineaResumen { Nombre = nombre };
            r.Ingresos = g("410505");
            r.CostoVentas = g("610505") + g("736000") + g("736500");
            r.UtilidadBruta = r.Ingresos - r.CostoVentas;
            r.GastosOperacionales = g("510503") + g("516000") + g("516500") + g("519900") + g("520503") + g("529915");
            r.UtilidadOperacional = r.UtilidadBruta - r.GastosOperacionales;
            r.Ebitda = r.UtilidadOperacional + g("736000") + g("736500") + g("516000") + g("516500");
            r.ResultadoNoOperacional = g("420505") + g("421000") - g("530505") - g("530520") - g("530525");
            r.UtilidadAntesImpuestos = r.UtilidadOperacional + r.ResultadoNoOperacional;
            r.Impuesto = g("540000");
            r.UtilidadNeta = r.UtilidadAntesImpuestos - r.Impuesto;
            r.MargenBruto = Razon(r.UtilidadBruta, r.Ingresos);
            r.MargenOperacional = Razon(r.UtilidadOperacional, r.Ingresos);
            r.MargenEbitda = Razon(r.Ebitda, r.Ingresos);
            r.MargenNeto = Razon(r.UtilidadNeta, r.Ingresos);
            return r;
        }

        private static decimal? Razon(decimal parte, decimal total) => total != 0 ? parte / total : (decimal?)null;

        private static decimal? Variacion(decimal actual, decimal? baseComparacion)
            => (baseComparacion.HasValue && baseComparacion.Value != 0) ? (actual - baseComparacion.Value) / Math.Abs(baseComparacion.Value) : (decimal?)null;

        private static Dictionary<string, Dictionary<string, decimal>> Acumular(IEnumerable<LnFilaModelo> filas)
        {
            var r = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in filas)
            {
                if (!r.TryGetValue(f.Linea, out var porPuc)) r[f.Linea] = porPuc = new Dictionary<string, decimal>();
                porPuc.TryGetValue(f.CuentaPUC, out decimal actual);
                porPuc[f.CuentaPUC] = actual + f.Valor;
            }
            return r;
        }

        public LineasNegocioReporteViewModel ConstruirReporte(FiltrosLineasNegocio filtros)
        {
            int mesD = Math.Min(12, Math.Max(1, filtros.MesDesde)), mesH = Math.Min(12, Math.Max(1, filtros.MesHasta));
            int kD = Clave(filtros.AnioDesde, mesD), kH = Clave(filtros.AnioHasta, mesH);
            if (kD > kH) { int t = kD; kD = kH; kH = t; }

            var res = new LineasNegocioReporteViewModel
            {
                IdEmpresa = filtros.IdEmpresa, IdEscenario = filtros.IdEscenario,
                AnioDesde = kD / 12, MesDesde = kD % 12 + 1, AnioHasta = kH / 12, MesHasta = kH % 12 + 1
            };

            var todas = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenario, res.AnioDesde - 1, res.AnioHasta);
            var enRango = todas.Where(f => { int k = Clave(f.Año, f.Mes); return k >= kD && k <= kH; }).ToList();
            var anterior = todas.Where(f => { int k = Clave(f.Año, f.Mes); return k >= kD - 12 && k <= kH - 12; }).ToList();

            var acum = Acumular(enRango);
            // Líneas con movimiento en el rango (algún valor distinto de cero), de mayor a menor ingreso.
            var nombres = acum.Where(p => p.Value.Values.Any(v => v != 0m)).Select(p => p.Key).ToList();
            if (nombres.Count == 0) { res.SinDatos = true; return res; }

            Func<Dictionary<string, decimal>, string, decimal> leer = (d, puc) => d != null && d.TryGetValue(puc, out decimal v) ? v : 0m;
            var resumenes = nombres.Select(n => { var d = acum[n]; return Calcular(n, puc => leer(d, puc)); })
                .OrderByDescending(x => x.Ingresos).ThenBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
            res.Lineas = resumenes.Select(x => x.Nombre).ToList();

            // Total = suma de las líneas por concepto
            var sumaTotal = new Dictionary<string, decimal>();
            foreach (var d in acum.Where(p => res.Lineas.Contains(p.Key, StringComparer.OrdinalIgnoreCase)).Select(p => p.Value))
                foreach (var kv in d) { sumaTotal.TryGetValue(kv.Key, out decimal a); sumaTotal[kv.Key] = a + kv.Value; }
            var total = Calcular("Total", puc => leer(sumaTotal, puc));

            // Comparativo: mismo rango del año anterior
            var acumAnt = Acumular(anterior);
            res.HayAnioAnterior = anterior.Any(f => f.Valor != 0m);
            foreach (var r in resumenes)
            {
                r.PctIngresos = Razon(r.Ingresos, total.Ingresos);
                // Con una utilidad operacional total negativa o cero, "qué parte aporta cada línea" no tiene sentido (daría porcentajes negativos o enormes).
                r.PctUtilidadOperacional = total.UtilidadOperacional > 0m ? Razon(r.UtilidadOperacional, total.UtilidadOperacional) : null;
                if (res.HayAnioAnterior)
                {
                    acumAnt.TryGetValue(r.Nombre, out var dAnt);
                    var ant = Calcular(r.Nombre, puc => leer(dAnt, puc));
                    r.IngresosAnterior = ant.Ingresos;
                    r.VariacionIngresos = Variacion(r.Ingresos, ant.Ingresos);
                    r.UtilidadOperacionalAnterior = ant.UtilidadOperacional;
                    r.VariacionUtilidadOperacional = Variacion(r.UtilidadOperacional, ant.UtilidadOperacional);
                }
            }
            total.PctIngresos = total.Ingresos != 0 ? 1m : (decimal?)null;
            if (res.HayAnioAnterior)
            {
                var sumaAnt = new Dictionary<string, decimal>();
                foreach (var kv in anterior.Where(f => res.Lineas.Contains(f.Linea, StringComparer.OrdinalIgnoreCase)).GroupBy(f => f.CuentaPUC))
                    sumaAnt[kv.Key] = kv.Sum(f => f.Valor);
                var antTotal = Calcular("Total", puc => leer(sumaAnt, puc));
                total.IngresosAnterior = antTotal.Ingresos;
                total.VariacionIngresos = Variacion(total.Ingresos, antTotal.Ingresos);
                total.UtilidadOperacionalAnterior = antTotal.UtilidadOperacional;
                total.VariacionUtilidadOperacional = Variacion(total.UtilidadOperacional, antTotal.UtilidadOperacional);
            }
            res.Resumen = resumenes;
            res.Total = total;

            // Matriz del estado de resultados: filas = concepto, columnas = línea (+ total)
            Action<string, bool, Func<LnLineaResumen, decimal>> fila = (codigo, sub, sel) =>
            {
                var f = new LnFilaEstado { Codigo = codigo, EsSubtotal = sub, Total = sel(total) };
                foreach (var r in resumenes) f.Valores.Add(sel(r));
                res.Filas.Add(f);
            };
            fila("Ingresos", true, x => x.Ingresos);
            fila("CostoVentas", false, x => x.CostoVentas);
            fila("UtilidadBruta", true, x => x.UtilidadBruta);
            fila("GastosOperacionales", false, x => x.GastosOperacionales);
            fila("UtilidadOperacional", true, x => x.UtilidadOperacional);
            fila("Ebitda", false, x => x.Ebitda);
            fila("ResultadoNoOperacional", false, x => x.ResultadoNoOperacional);
            fila("UtilidadAntesImpuestos", true, x => x.UtilidadAntesImpuestos);
            fila("Impuesto", false, x => x.Impuesto);
            fila("UtilidadNeta", true, x => x.UtilidadNeta);

            // Mezcla mensual de ingresos por línea
            var mesesRango = new List<int>();
            for (int k = kD; k <= kH; k++) mesesRango.Add(k);
            bool variosAños = res.AnioDesde != res.AnioHasta;
            res.Etiquetas = mesesRango.Select(k => MesesAbrev[k % 12] + (variosAños ? " " + (k / 12 % 100).ToString("00") : "")).ToList();
            foreach (var nombre in res.Lineas)
            {
                var serie = new LnSerieMensual { Linea = nombre };
                var ingresosMes = enRango.Where(f => f.CuentaPUC == "410505" && string.Equals(f.Linea, nombre, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => Clave(f.Año, f.Mes)).ToDictionary(g => g.Key, g => g.Sum(f => f.Valor));
                foreach (int k in mesesRango) serie.Ingresos.Add(ingresosMes.TryGetValue(k, out decimal v) ? v : 0m);
                res.Series.Add(serie);
            }

            // Cuadre contra el PYG del mismo rango (ingresos y utilidad neta)
            var ingPyg = SumaPyg(filtros.IdEmpresa, filtros.IdEscenario, kD, kH, "Ingresos");
            var netaPyg = SumaPyg(filtros.IdEmpresa, filtros.IdEscenario, kD, kH, "Utilidad neta");
            if (ingPyg.HasValue || netaPyg.HasValue)
            {
                res.HayComparacionPYG = true;
                res.DiferenciaIngresosPYG = ingPyg.HasValue ? total.Ingresos - ingPyg.Value : 0m;
                res.CuadraIngresosPYG = Math.Abs(res.DiferenciaIngresosPYG) <= ToleranciaCuadre;
                res.DiferenciaUtilidadPYG = netaPyg.HasValue ? total.UtilidadNeta - netaPyg.Value : 0m;
                res.CuadraUtilidadPYG = Math.Abs(res.DiferenciaUtilidadPYG) <= ToleranciaCuadre;
            }
            return res;
        }
    }
}
