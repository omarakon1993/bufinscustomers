using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe gerencial de tesorería del presupuesto. Lee de la tabla ya materializada dbo.ModeloTesoreriaPpto
    /// (resultado persistido de sp_ModeloTesoreriaPpto) — no ejecuta el SP en vivo. La tabla solo guarda el año de
    /// ejecución de cada empresa, con 38 líneas por mes (Mes es entero 1-12): Valor = presupuesto, ValorFuturo =
    /// proyección. Los saldos corridos (caja neta acumulada, excedentes, deuda nueva) NO se leen de la tabla: se
    /// calculan aquí a partir de la caja neta mensual, porque ValorForecast/ValorFuturo de esas 3 líneas no son
    /// confiables (ver CLAUDE.md, "Informe de tesorería del presupuesto").
    /// </summary>
    public class InformeTesoreriaPptoService : BaseService
    {
        // Literales de dbo.ModeloTesoreriaPpto.Descripcion CONFIRMADOS contra la BD real
        // (SELECT … FROM dbo.Rel_TesoreriaPpto ORDER BY Id). La comparación es case-insensitive.
        private const string DescCajaBruta = "Sub total Caja bruta";
        private const string DescCajaAimp = "Sub Total Caja AIMP";
        private const string DescGastosOperacionales = "Gastos operacionales";
        private const string DescImpuestoRenta = "Impuesto renta";
        private const string DescCambiosPpe = "Cambios en Propiedad, planta y equipo";
        private const string DescInversionesOperacionales = "Inversiones operacionales";
        private const string PrefijoFlujoOperacion = "Subtotal Flujo de efectivo neto generado en actividades de operación";
        private const string DescCajaNeta = "Caja neta";
        private const string DescCajaNetaAcum = "Caja neta acumulada";
        private const string DescExcedentes = "Excedentes de tesorería";
        private const string DescDeudaNueva = "Deuda nueva (Déficit de tesorería)";

        // Líneas cuyo aumento es desfavorable para la caja (se pintan en rojo al subir).
        private static readonly HashSet<string> Costos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CMV", "Gastos operacionales", "Intereses", "Otros gastos no operacionales", "Gasto diferencia en cambio",
            "Impuesto renta", DescDeudaNueva
        };

        // Líneas que son subtotales/saldos (se resaltan en la tabla).
        private static readonly HashSet<string> Subtotales = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DescCajaBruta, DescCajaAimp, DescCajaNeta, DescCajaNetaAcum, DescExcedentes, DescDeudaNueva
        };

        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        // ===== Lectura =====

        public List<TesoreriaFilaModelo> ObtenerFilas(int idEmpresa, byte idEscenario, int añoDesde, int añoHasta)
        {
            var filas = new List<TesoreriaFilaModelo>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT Año, Mes, Ord, Descripcion, Valor, ValorFuturo
                    FROM dbo.ModeloTesoreriaPpto
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario
                      AND Año BETWEEN @AñoDesde AND @AñoHasta";

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
                        int mes = Convert.ToInt32(reader["Mes"]);
                        if (mes < 1 || mes > 12) continue;
                        filas.Add(new TesoreriaFilaModelo
                        {
                            Año = Convert.ToInt32(reader["Año"]),
                            Mes = mes,
                            Ord = Convert.ToInt32(reader["Ord"]),
                            Descripcion = (reader["Descripcion"] as string ?? "").Trim(),
                            Valor = LeerDecimal(reader, "Valor"),
                            ValorFuturo = LeerDecimal(reader, "ValorFuturo")
                        });
                    }
                }
            }

            return filas.OrderBy(f => f.Ord).ThenBy(f => f.Año).ThenBy(f => f.Mes).ToList();
        }

        private static decimal LeerDecimal(IDataRecord reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? 0m : Convert.ToDecimal(valor);
        }

        /// <summary>Meses con datos para la empresa/escenario, en orden cronológico (alimenta el slider).</summary>
        public List<TesoreriaMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            var meses = new List<TesoreriaMesDisponible>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT DISTINCT Año, Mes FROM dbo.ModeloTesoreriaPpto
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario
                      AND Año IS NOT NULL AND Mes IS NOT NULL";

                var cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int año = Convert.ToInt32(reader["Año"]);
                        int mes = Convert.ToInt32(reader["Mes"]);
                        if (mes < 1 || mes > 12) continue;
                        meses.Add(new TesoreriaMesDisponible { Anio = año, Mes = mes, Etiqueta = MesesAbrev[mes - 1] + " " + año });
                    }
                }
            }

            return meses.OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        public List<byte> ObtenerEscenariosConDatos(int idEmpresa)
        {
            var ids = new List<byte>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("SELECT DISTINCT IdEscenario FROM dbo.ModeloTesoreriaPpto WHERE IdEmpresa = @IdEmpresa AND IdEscenario IS NOT NULL", cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        ids.Add(Convert.ToByte(reader[0]));
                }
            }

            return ids;
        }

        // ===== Construcción del reporte =====

        private static int Clave(int año, int mes) { return año * 12 + mes - 1; }

        /// <summary>Quita el ruido numérico del modelo (valores del orden de 0,001) y redondea a pesos.</summary>
        private static decimal Limpio(decimal v)
        {
            return Math.Abs(v) < 0.5m ? 0m : Math.Round(v, 0);
        }

        private static decimal? Variacion(decimal actual, decimal baseValor)
        {
            if (baseValor == 0m) return null;
            return (actual - baseValor) / Math.Abs(baseValor);
        }

        /// <summary>Datos de una medida/escenario: valor mensual por línea (Ord) y caja neta mensual con su acumulado.</summary>
        private class Serie
        {
            public Dictionary<int, Dictionary<int, decimal>> PorOrd = new Dictionary<int, Dictionary<int, decimal>>();
            public Dictionary<int, decimal> CajaNeta = new Dictionary<int, decimal>();      // clave mes → valor
            public Dictionary<int, decimal> CajaNetaAcum = new Dictionary<int, decimal>();  // acumulado del año
            public bool Vacia { get { return PorOrd.Count == 0; } }

            public decimal Linea(int ord, int clave)
            {
                Dictionary<int, decimal> m;
                decimal v;
                return PorOrd.TryGetValue(ord, out m) && m.TryGetValue(clave, out v) ? v : 0m;
            }
        }

        private static Serie ArmarSerie(List<TesoreriaFilaModelo> filas, bool proyeccion, int ordCajaNeta)
        {
            var s = new Serie();
            foreach (var f in filas)
            {
                Dictionary<int, decimal> m;
                if (!s.PorOrd.TryGetValue(f.Ord, out m)) { m = new Dictionary<int, decimal>(); s.PorOrd[f.Ord] = m; }
                int k = Clave(f.Año, f.Mes);
                decimal v = Limpio(proyeccion ? f.ValorFuturo : f.Valor);
                decimal previo;
                m[k] = m.TryGetValue(k, out previo) ? previo + v : v;
            }

            if (ordCajaNeta >= 0 && s.PorOrd.ContainsKey(ordCajaNeta))
            {
                int añoActual = -1;
                decimal acum = 0m;
                foreach (var kv in s.PorOrd[ordCajaNeta].OrderBy(x => x.Key))
                {
                    int año = kv.Key / 12;
                    if (año != añoActual) { añoActual = año; acum = 0m; }   // el saldo corrido reinicia cada año
                    acum += kv.Value;
                    s.CajaNeta[kv.Key] = kv.Value;
                    s.CajaNetaAcum[kv.Key] = acum;
                }
            }
            return s;
        }

        /// <summary>Valor mensual de una línea; las 3 líneas de saldo se derivan de la caja neta acumulada.</summary>
        private static decimal ValorLinea(Serie s, string descripcion, int ord, int clave)
        {
            decimal acum;
            if (string.Equals(descripcion, DescCajaNetaAcum, StringComparison.OrdinalIgnoreCase))
                return s.CajaNetaAcum.TryGetValue(clave, out acum) ? acum : 0m;
            if (string.Equals(descripcion, DescExcedentes, StringComparison.OrdinalIgnoreCase))
                return s.CajaNetaAcum.TryGetValue(clave, out acum) ? Math.Max(acum, 0m) : 0m;
            if (string.Equals(descripcion, DescDeudaNueva, StringComparison.OrdinalIgnoreCase))
                return s.CajaNetaAcum.TryGetValue(clave, out acum) ? Math.Max(-acum, 0m) : 0m;
            return s.Linea(ord, clave);
        }

        private static bool EsSaldo(string descripcion)
        {
            return string.Equals(descripcion, DescCajaNetaAcum, StringComparison.OrdinalIgnoreCase)
                || string.Equals(descripcion, DescExcedentes, StringComparison.OrdinalIgnoreCase)
                || string.Equals(descripcion, DescDeudaNueva, StringComparison.OrdinalIgnoreCase);
        }

        public TesoreriaReporteViewModel ConstruirReporte(FiltrosTesoreria filtros)
        {
            string medida = TesoreriaMedida.Normalizar(filtros.Medida);
            string comparar = TesoreriaComparar.Normalizar(filtros.Comparar);

            var reporte = new TesoreriaReporteViewModel
            {
                IdEmpresa = filtros.IdEmpresa,
                IdEscenario = filtros.IdEscenario,
                AnioDesde = filtros.AnioDesde, MesDesde = filtros.MesDesde,
                AnioHasta = filtros.AnioHasta, MesHasta = filtros.MesHasta,
                Medida = medida,
                Comparar = comparar,
                IdEscenarioComparar = filtros.IdEscenarioComparar
            };

            int kD = Clave(filtros.AnioDesde, Math.Max(1, Math.Min(12, filtros.MesDesde)));
            int kH = Clave(filtros.AnioHasta, Math.Max(1, Math.Min(12, filtros.MesHasta)));
            if (kD > kH) { int t = kD; kD = kH; kH = t; }
            reporte.AnioDesde = kD / 12; reporte.MesDesde = kD % 12 + 1;
            reporte.AnioHasta = kH / 12; reporte.MesHasta = kH % 12 + 1;

            var filas = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenario, reporte.AnioDesde, reporte.AnioHasta);
            if (filas.Count == 0) { reporte.SinDatos = true; return reporte; }

            // Descripción por línea (Ord) y ord de las líneas clave.
            var desc = new Dictionary<int, string>();
            foreach (var f in filas) if (!desc.ContainsKey(f.Ord)) desc[f.Ord] = f.Descripcion;
            Func<string, int> ordDe = d =>
            {
                var hit = desc.FirstOrDefault(kv => string.Equals(kv.Value, d, StringComparison.OrdinalIgnoreCase));
                return hit.Value == null ? -1 : hit.Key;
            };
            int ordCajaNeta = ordDe(DescCajaNeta);
            if (ordCajaNeta < 0) { reporte.SinDatos = true; return reporte; }

            bool proy = medida == TesoreriaMedida.Proyeccion;
            var principal = ArmarSerie(filas, proy, ordCajaNeta);
            var otra = ArmarSerie(filas, !proy, ordCajaNeta);

            // Sin cifras útiles en la medida elegida (p. ej. una empresa con el presupuesto en 0): no hay nada que mostrar.
            // El usuario puede cambiar de medida (presupuesto ↔ proyección) y volver a consultar.
            if (!principal.PorOrd.Values.Any(m => m.Values.Any(v => v != 0m)))
            { reporte.SinDatos = true; return reporte; }

            var presupuesto = proy ? otra : principal;
            var proyeccion = proy ? principal : otra;
            reporte.HayProyeccion = proyeccion.PorOrd.Values.Any(m => m.Values.Any(v => v != 0m));
            reporte.HayComparativo = comparar == TesoreriaComparar.Medida;

            // Escenario de comparación (misma medida); si no tiene datos queda desactivado.
            Serie esc = null;
            byte? idEscComp = filtros.IdEscenarioComparar;
            if (idEscComp.HasValue && idEscComp.Value != filtros.IdEscenario)
            {
                var filasEsc = ObtenerFilas(filtros.IdEmpresa, idEscComp.Value, reporte.AnioDesde, reporte.AnioHasta);
                if (filasEsc.Count > 0)
                {
                    var descEsc = filasEsc.FirstOrDefault(f => string.Equals(f.Descripcion, DescCajaNeta, StringComparison.OrdinalIgnoreCase));
                    if (descEsc != null)
                    {
                        var serieEsc = ArmarSerie(filasEsc, proy, descEsc.Ord);
                        if (!serieEsc.Vacia) { esc = serieEsc; reporte.HayEscenarioComparar = true; }
                    }
                }
            }

            // Meses: todos los del año (gráfico/mapa de calor) y los del rango (tabla/KPIs).
            var clavesTodas = principal.PorOrd.Values.SelectMany(m => m.Keys).Distinct().OrderBy(k => k).ToList();
            var clavesRango = clavesTodas.Where(k => k >= kD && k <= kH).ToList();
            if (clavesRango.Count == 0) { reporte.SinDatos = true; return reporte; }
            reporte.MesesRango = clavesRango.Select(k => new TesoreriaMesRef { Anio = k / 12, Mes = k % 12 + 1 }).ToList();

            foreach (var k in clavesTodas)
            {
                decimal v;
                reporte.Meses.Add(new TesoreriaPuntoMes
                {
                    Anio = k / 12, Mes = k % 12 + 1, EnRango = k >= kD && k <= kH,
                    CajaNeta = principal.CajaNeta.TryGetValue(k, out v) ? v : 0m,
                    CajaNetaAcum = principal.CajaNetaAcum.TryGetValue(k, out v) ? v : 0m,
                    CajaNetaComp = otra.CajaNeta.TryGetValue(k, out v) ? v : 0m,
                    CajaNetaAcumComp = otra.CajaNetaAcum.TryGetValue(k, out v) ? v : 0m,
                    CajaNetaEsc = esc != null && esc.CajaNeta.TryGetValue(k, out v) ? v : 0m,
                    CajaNetaAcumEsc = esc != null && esc.CajaNetaAcum.TryGetValue(k, out v) ? v : 0m
                });
            }

            // Filas de la tabla: todas las líneas, salvo las que valen 0 en todos los meses (no aportan).
            foreach (var kv in desc.OrderBy(x => x.Key))
            {
                int ord = kv.Key;
                string d = kv.Value;
                bool saldo = EsSaldo(d);
                var fila = new TesoreriaFilaReporte
                {
                    Ord = ord, Descripcion = d, EsSaldo = saldo,
                    EsSubtotal = Subtotales.Contains(d) || d.StartsWith("Subtotal", StringComparison.OrdinalIgnoreCase),
                    EsGastoOCosto = Costos.Contains(d)
                };
                foreach (var k in clavesRango)
                {
                    fila.Valores.Add(ValorLinea(principal, d, ord, k));
                    fila.ValoresComp.Add(ValorLinea(otra, d, ord, k));
                    fila.ValoresEsc.Add(esc != null ? ValorLinea(esc, d, ord, k) : 0m);
                }
                fila.Total = saldo ? fila.Valores.Last() : fila.Valores.Sum();
                fila.TotalComp = saldo ? fila.ValoresComp.Last() : fila.ValoresComp.Sum();
                fila.TotalEsc = saldo ? fila.ValoresEsc.Last() : fila.ValoresEsc.Sum();
                fila.VariacionAbsoluta = fila.Total - fila.TotalComp;
                fila.VariacionPorcentual = Variacion(fila.Total, fila.TotalComp);
                fila.VariacionEscenarioPorcentual = esc != null ? Variacion(fila.Total, fila.TotalEsc) : null;

                bool todoCero = fila.Valores.All(v => v == 0m) && fila.ValoresComp.All(v => v == 0m) && fila.ValoresEsc.All(v => v == 0m);
                if (todoCero && !fila.EsSubtotal) continue;
                reporte.Filas.Add(fila);
            }

            reporte.Kpis = ArmarKpis(reporte, principal, otra, esc, clavesRango);
            reporte.Cascada = ArmarCascada(reporte, principal, ordDe, clavesRango, desc);
            return reporte;
        }

        private static List<TesoreriaKpi> ArmarKpis(TesoreriaReporteViewModel r, Serie principal, Serie otra, Serie esc, List<int> clavesRango)
        {
            Func<Serie, Func<int, decimal>> netaDe = s => k => { decimal v; return s.CajaNeta.TryGetValue(k, out v) ? v : 0m; };
            Func<Serie, Func<int, decimal>> acumDe = s => k => { decimal v; return s.CajaNetaAcum.TryGetValue(k, out v) ? v : 0m; };
            int ultimo = clavesRango.Last();

            Func<Serie, decimal> periodo = s => clavesRango.Sum(k => netaDe(s)(k));
            Func<Serie, decimal> acumFinal = s => acumDe(s)(ultimo);
            Func<Serie, decimal> minimo = s => clavesRango.Min(k => acumDe(s)(k));
            Func<Serie, int> mesesDeficit = s => clavesRango.Count(k => acumDe(s)(k) < 0m);
            Func<Serie, decimal> deudaNueva = s => Math.Max(0m, -minimo(s));

            int kMin = clavesRango.OrderBy(k => acumDe(principal)(k)).First();

            var kpis = new List<TesoreriaKpi>();
            Func<string, Func<Serie, decimal>, TesoreriaKpi> crear = (codigo, f) => new TesoreriaKpi
            {
                Codigo = codigo,
                Valor = f(principal),
                ValorComp = f(otra),
                ValorEsc = esc != null ? f(esc) : 0m,
                VariacionPorcentual = Variacion(f(principal), f(otra))
            };

            kpis.Add(crear("CajaNetaPeriodo", periodo));
            kpis.Add(crear("CajaAcumFinal", acumFinal));
            var kMinimo = crear("SaldoMinimo", minimo);
            kMinimo.Anio = kMin / 12; kMinimo.Mes = kMin % 12 + 1;
            kpis.Add(kMinimo);
            var kMeses = crear("MesesDeficit", s => mesesDeficit(s));
            kMeses.Total = clavesRango.Count;
            kpis.Add(kMeses);
            kpis.Add(crear("DeudaNueva", deudaNueva));
            return kpis;
        }

        /// <summary>Puente de caja del rango: Caja bruta → gastos operacionales → financieros y otros → impuestos →
        /// inversiones → capital de trabajo → otros movimientos → Caja neta. Cada tramo sale de diferencias entre
        /// subtotales que ya calcula el modelo y el último tramo ("otros") absorbe el resto, así que SIEMPRE cuadra
        /// con la caja neta real del rango.</summary>
        private static List<TesoreriaCascadaBarra> ArmarCascada(TesoreriaReporteViewModel r, Serie s, Func<string, int> ordDe,
            List<int> clavesRango, Dictionary<int, string> desc)
        {
            Func<int, decimal> total = ord => ord < 0 ? 0m : clavesRango.Sum(k => s.Linea(ord, k));
            Func<string, decimal> t = d => total(ordDe(d));

            int ordFlujoOp = desc.Where(kv => kv.Value.StartsWith(PrefijoFlujoOperacion, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).DefaultIfEmpty(-1).First();

            decimal bruta = t(DescCajaBruta), gastos = t(DescGastosOperacionales), aimp = t(DescCajaAimp), impuesto = t(DescImpuestoRenta);
            decimal inversiones = t(DescCambiosPpe) + t(DescInversionesOperacionales);
            decimal capitalTrabajo = total(ordFlujoOp);
            decimal cajaNeta = clavesRango.Sum(k => { decimal v; return s.CajaNeta.TryGetValue(k, out v) ? v : 0m; });

            decimal financieros = (aimp - bruta) + gastos;
            decimal otros = cajaNeta - (aimp - impuesto + inversiones + capitalTrabajo);

            var barras = new List<TesoreriaCascadaBarra>();
            barras.Add(new TesoreriaCascadaBarra { Etiqueta = "CajaBruta", Tipo = "total", Desde = 0m, Hasta = bruta });
            decimal nivel = bruta;
            Action<string, decimal> tramo = (etiqueta, delta) =>
            {
                if (Math.Abs(delta) < 0.5m) return;
                decimal despues = nivel + delta;
                barras.Add(new TesoreriaCascadaBarra
                {
                    Etiqueta = etiqueta, Delta = delta,
                    Tipo = delta >= 0 ? "positivo" : "negativo",
                    Desde = delta >= 0 ? nivel : despues,
                    Hasta = delta >= 0 ? despues : nivel
                });
                nivel = despues;
            };
            tramo("GastosOperacionales", -gastos);
            tramo("FinancierosOtros", financieros);
            tramo("Impuestos", -impuesto);
            tramo("Inversiones", inversiones);
            tramo("CapitalTrabajo", capitalTrabajo);
            tramo("OtrosMovimientos", otros);
            barras.Add(new TesoreriaCascadaBarra { Etiqueta = "CajaNeta", Tipo = "total", Desde = 0m, Hasta = cajaNeta });
            return barras;
        }
    }
}
