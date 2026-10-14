using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informes gerenciales de Flujo de caja (dbo.ModeloFlujoCaja) y Flujo de efectivo (dbo.ModeloFlujoEfectivo). Leen las
    /// tablas ya materializadas — no ejecutan los SP en vivo. Ambos modelos tienen la misma forma: por Año/Mes/línea (Ord =
    /// Id de dbo.Rel_FlujoCaja / dbo.Rel_FlujoEfectivo; Mes es entero 1-12) traen Valor (real, solo meses ejecutados),
    /// ValorFuturo (proyección) y ValorPresupuesto. Son flujos del mes (se suman); el acumulado se calcula aquí.
    ///
    /// Relación verificada contra la BD: Flujo de caja libre operativo + Flujo de caja financiero + Flujo de caja no
    /// operacional (FlujoCaja) = Aumento neto del efectivo (FlujoEfectivo), mes a mes.
    ///
    /// Medida "proy" (cierre proyectado): real hasta el último mes ejecutado del año y, desde ahí, ValorFuturo. Se arma así
    /// (y no con ValorFuturo a secas) porque en los años ya cerrados ValorFuturo trae basura en enero (arrastre del saldo
    /// inicial) y en los meses ejecutados no aporta nada.
    /// </summary>
    public class InformeFlujoService : BaseService
    {
        private readonly FlujoTipo _tipo;

        public InformeFlujoService(FlujoTipo tipo) { _tipo = tipo; }

        public FlujoTipo Tipo { get { return _tipo; } }

        // Nombre de tabla/Rel fijos por enum (nunca viene del cliente → seguro para concatenar en el SQL).
        private string Tabla { get { return _tipo == FlujoTipo.Caja ? "dbo.ModeloFlujoCaja" : "dbo.ModeloFlujoEfectivo"; } }
        private string RelTabla { get { return _tipo == FlujoTipo.Caja ? "dbo.Rel_FlujoCaja" : "dbo.Rel_FlujoEfectivo"; } }
        private string RelNombre { get { return _tipo == FlujoTipo.Caja ? "Rel_FlujoCaja" : "Rel_FlujoEfectivo"; } }

        // ===== Ord de las líneas clave (= Id de la tabla Rel_ del modelo, CONFIRMADOS contra la BD real) =====
        // Flujo de caja:    1 Utilidad operacional · 6 Subtotal Flujo de caja bruto antes de impuestos · 7 Impuesto renta ·
        //                   8 Subtotal Flujo de caja bruto después de impuestos · 15 Subtotal … operación K de W (capital de
        //                   trabajo) · 19 Subtotal Flujo de caja libre Operativo · 25 Subtotal Flujo de caja financiero ·
        //                   29 Subtotal Flujo de caja no operacional.
        // Flujo de efectivo: 1 Utilidad neta · 8 Subtotal Efectivo generado en operación · 15 Subtotal … operación K de W ·
        //                   16 Subtotal Flujo de efectivo neto generado en actividades de operación · 28 Flujo de efectivo
        //                   neto generado en actividades de inversión · 29 Cambio en deuda neta · 30 Aumento neto (o
        //                   disminución) del efectivo.
        private const int CjUtilOper = 1, CjBrutoAntes = 6, CjImpuesto = 7, CjBrutoDespues = 8, CjCapTrabajo = 15, CjFcl = 19, CjFinanciero = 25, CjNoOp = 29;
        private const int EfUtilNeta = 1, EfOperacionGenerado = 8, EfCapTrabajo = 15, EfFlujoOperacion = 16, EfInversion = 28, EfDeudaNeta = 29, EfAumentoNeto = 30;

        // Líneas de Flujo de caja cuyo valor es una MAGNITUD que resta (subir = desfavorable para la caja).
        private static readonly HashSet<string> CostosCaja = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Impuesto renta", "Gastos financieros", "Gasto diferencia en cambio", "Otros gastos no operacionales"
        };

        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        // ===== Lectura =====

        public List<FlujoFilaModelo> ObtenerFilas(int idEmpresa, byte idEscenario, int añoDesde, int añoHasta)
        {
            var filas = new List<FlujoFilaModelo>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                string query = @"
                    SELECT m.Año, m.Mes, m.Ord, m.Descripcion, m.Valor, m.ValorFuturo, m.ValorPresupuesto,
                           r.Tipo AS TipoLinea, r.Tabla AS TablaLinea
                    FROM " + Tabla + @" m
                    LEFT JOIN " + RelTabla + @" r ON r.Id = m.Ord
                    WHERE m.IdEmpresa = @IdEmpresa AND m.IdEscenario = @IdEscenario
                      AND m.Año BETWEEN @AñoDesde AND @AñoHasta";

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
                        string tipoLinea = (reader["TipoLinea"] as string ?? "").Trim();
                        string tablaLinea = (reader["TablaLinea"] as string ?? "").Trim();
                        filas.Add(new FlujoFilaModelo
                        {
                            Año = Convert.ToInt32(reader["Año"]),
                            Mes = mes,
                            Ord = Convert.ToInt32(reader["Ord"]),
                            Descripcion = (reader["Descripcion"] as string ?? "").Trim(),
                            Valor = LeerDecimal(reader, "Valor"),
                            ValorFuturo = LeerDecimal(reader, "ValorFuturo"),
                            ValorPresupuesto = LeerDecimal(reader, "ValorPresupuesto"),
                            // Subtotal = línea calculada a partir de OTRAS líneas del mismo modelo (no un agregado de balance/PYG).
                            EsSubtotal = tipoLinea.Length > 0 && string.Equals(tablaLinea, RelNombre, StringComparison.OrdinalIgnoreCase)
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

        public List<FlujoMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            var meses = new List<FlujoMesDisponible>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                string query = @"
                    SELECT DISTINCT Año, Mes FROM " + Tabla + @"
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
                        meses.Add(new FlujoMesDisponible { Anio = año, Mes = mes, Etiqueta = MesesAbrev[mes - 1] + " " + año });
                    }
                }
            }

            return meses.OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        /// <summary>Último mes con cifras reales (Valor distinto de 0) de la empresa/escenario, o null si no hay ninguno: la vista lo usa
        /// para proponer como rango inicial lo realmente ejecutado.</summary>
        public FlujoMesRef ObtenerUltimoMesReal(int idEmpresa, byte idEscenario)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            {
                string query = @"SELECT TOP 1 Año, Mes FROM " + Tabla + @"
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario AND ABS(Valor) >= 0.5
                    ORDER BY Año DESC, Mes DESC";
                var cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new FlujoMesRef { Anio = Convert.ToInt32(reader["Año"]), Mes = Convert.ToInt32(reader["Mes"]) };
                }
            }
        }

        public List<byte> ObtenerEscenariosConDatos(int idEmpresa)
        {
            var ids = new List<byte>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("SELECT DISTINCT IdEscenario FROM " + Tabla + " WHERE IdEmpresa = @IdEmpresa AND IdEscenario IS NOT NULL", cn);
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

        /// <summary>Quita el ruido numérico del modelo (|v| &lt; 0,5) y redondea a pesos.</summary>
        private static decimal Limpio(decimal v) { return Math.Abs(v) < 0.5m ? 0m : Math.Round(v, 0); }

        private static decimal? Variacion(decimal actual, decimal baseValor)
        {
            if (baseValor == 0m) return null;
            return (actual - baseValor) / Math.Abs(baseValor);
        }

        /// <summary>Valor mensual por línea (Ord) → clave de mes → valor.</summary>
        private class Serie
        {
            public readonly Dictionary<int, Dictionary<int, decimal>> PorOrd = new Dictionary<int, Dictionary<int, decimal>>();
            public bool TieneCifras { get { return PorOrd.Values.Any(m => m.Values.Any(v => v != 0m)); } }

            public void Poner(int ord, int clave, decimal valor)
            {
                Dictionary<int, decimal> m;
                if (!PorOrd.TryGetValue(ord, out m)) { m = new Dictionary<int, decimal>(); PorOrd[ord] = m; }
                decimal previo;
                m[clave] = m.TryGetValue(clave, out previo) ? previo + valor : valor;
            }

            public decimal Linea(int ord, int clave)
            {
                Dictionary<int, decimal> m;
                decimal v;
                return PorOrd.TryGetValue(ord, out m) && m.TryGetValue(clave, out v) ? v : 0m;
            }

            public decimal Suma(int ord, IEnumerable<int> claves) { return claves.Sum(k => Linea(ord, k)); }
        }

        /// <summary>Último mes ejecutado (con algún Valor real) de cada año.</summary>
        private static Dictionary<int, int> UltimoMesRealPorAño(List<FlujoFilaModelo> filas)
        {
            var d = new Dictionary<int, int>();
            foreach (var f in filas)
            {
                if (Limpio(f.Valor) == 0m) continue;
                int actual;
                if (!d.TryGetValue(f.Año, out actual) || f.Mes > actual) d[f.Año] = f.Mes;
            }
            return d;
        }

        private static Serie ArmarSerie(List<FlujoFilaModelo> filas, string medida, Dictionary<int, int> ultimoReal)
        {
            var s = new Serie();
            foreach (var f in filas)
            {
                decimal v;
                if (medida == FlujoMedida.Proyectado)
                {
                    int ult;
                    ultimoReal.TryGetValue(f.Año, out ult);
                    v = f.Mes <= ult ? f.Valor : f.ValorFuturo;
                }
                else v = f.Valor;
                s.Poner(f.Ord, Clave(f.Año, f.Mes), Limpio(v));
            }
            return s;
        }

        private static Serie ArmarSeriePpto(List<FlujoFilaModelo> filas)
        {
            var s = new Serie();
            foreach (var f in filas) s.Poner(f.Ord, Clave(f.Año, f.Mes), Limpio(f.ValorPresupuesto));
            return s;
        }

        /// <summary>Flujo neto de un mes: en Flujo de caja = FCL operativo + financiero + no operacional (no existe como línea
        /// del modelo); en Flujo de efectivo = Aumento neto del efectivo (línea 30).</summary>
        private decimal Neto(Serie s, int clave)
        {
            if (_tipo == FlujoTipo.Caja) return s.Linea(CjFcl, clave) + s.Linea(CjFinanciero, clave) + s.Linea(CjNoOp, clave);
            return s.Linea(EfAumentoNeto, clave);
        }

        private decimal NetoTotal(Serie s, List<int> claves) { return claves.Sum(k => Neto(s, k)); }

        public FlujoReporteViewModel ConstruirReporte(FiltrosFlujo filtros)
        {
            string medida = FlujoMedida.Normalizar(filtros.Medida);

            int kD = Clave(filtros.AnioDesde, Math.Max(1, Math.Min(12, filtros.MesDesde)));
            int kH = Clave(filtros.AnioHasta, Math.Max(1, Math.Min(12, filtros.MesHasta)));
            if (kD > kH) { int t = kD; kD = kH; kH = t; }

            var reporte = new FlujoReporteViewModel
            {
                Tipo = _tipo.ToString(),
                IdEmpresa = filtros.IdEmpresa, IdEscenario = filtros.IdEscenario,
                AnioDesde = kD / 12, MesDesde = kD % 12 + 1, AnioHasta = kH / 12, MesHasta = kH % 12 + 1,
                Medida = medida, IdEscenarioComparar = filtros.IdEscenarioComparar
            };

            // Se lee también el año anterior (para la comparación interanual).
            var filas = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenario, reporte.AnioDesde - 1, reporte.AnioHasta);
            if (filas.Count == 0) { reporte.SinDatos = true; return reporte; }

            var ultimoReal = UltimoMesRealPorAño(filas);
            int ultimoRealFinal;
            ultimoReal.TryGetValue(reporte.AnioHasta, out ultimoRealFinal);
            reporte.UltimoMesReal = ultimoRealFinal;

            var principal = ArmarSerie(filas, medida, ultimoReal);
            var ppto = ArmarSeriePpto(filas);

            // Descripción/subtotal por línea (Ord).
            var desc = new Dictionary<int, string>();
            var subtotal = new Dictionary<int, bool>();
            foreach (var f in filas)
            {
                if (!desc.ContainsKey(f.Ord)) { desc[f.Ord] = f.Descripcion; subtotal[f.Ord] = f.EsSubtotal; }
            }

            // Meses del rango que existen en el modelo.
            var clavesRango = filas.Select(f => Clave(f.Año, f.Mes)).Distinct().Where(k => k >= kD && k <= kH).OrderBy(k => k).ToList();
            if (clavesRango.Count == 0) { reporte.SinDatos = true; return reporte; }

            // Sin cifras en la medida elegida dentro del rango (p. ej. "real" de meses aún no ejecutados): nada que mostrar.
            // El usuario puede cambiar a "proyectado" y volver a consultar.
            bool hayCifras = principal.PorOrd.Values.Any(m => clavesRango.Any(k => m.ContainsKey(k) && m[k] != 0m));
            if (!hayCifras) { reporte.SinDatos = true; return reporte; }

            reporte.HayPresupuesto = ppto.PorOrd.Values.Any(m => clavesRango.Any(k => m.ContainsKey(k) && m[k] != 0m));
            reporte.HayAnioAnterior = principal.PorOrd.Values.Any(m => clavesRango.Any(k => m.ContainsKey(k - 12) && m[k - 12] != 0m));
            reporte.HayMesesProyectados = medida == FlujoMedida.Proyectado
                && clavesRango.Any(k => { int ult; ultimoReal.TryGetValue(k / 12, out ult); return (k % 12 + 1) > ult; });
            reporte.MesesRango = clavesRango.Select(k =>
            {
                int ult; ultimoReal.TryGetValue(k / 12, out ult);
                return new FlujoMesRef { Anio = k / 12, Mes = k % 12 + 1, Proyectado = medida == FlujoMedida.Proyectado && (k % 12 + 1) > ult };
            }).ToList();

            // Escenario de comparación (misma medida); si no tiene datos queda desactivado.
            Serie esc = null;
            byte? idEscComp = filtros.IdEscenarioComparar;
            if (idEscComp.HasValue && idEscComp.Value != filtros.IdEscenario)
            {
                var filasEsc = ObtenerFilas(filtros.IdEmpresa, idEscComp.Value, reporte.AnioDesde, reporte.AnioHasta);
                if (filasEsc.Count > 0)
                {
                    var serieEsc = ArmarSerie(filasEsc, medida, UltimoMesRealPorAño(filasEsc));
                    if (serieEsc.PorOrd.Values.Any(m => clavesRango.Any(k => m.ContainsKey(k) && m[k] != 0m)))
                    { esc = serieEsc; reporte.HayEscenarioComparar = true; }
                }
            }

            // Puntos mensuales (gráfico principal): flujo neto del mes y acumulado DEL RANGO.
            decimal aP = 0m, aPp = 0m, aA = 0m, aE = 0m;
            foreach (var k in clavesRango)
            {
                decimal n = Neto(principal, k), np = Neto(ppto, k), na = Neto(principal, k - 12), ne = esc != null ? Neto(esc, k) : 0m;
                aP += n; aPp += np; aA += na; aE += ne;
                int ult; ultimoReal.TryGetValue(k / 12, out ult);
                reporte.Meses.Add(new FlujoPuntoMes
                {
                    Anio = k / 12, Mes = k % 12 + 1, Proyectado = medida == FlujoMedida.Proyectado && (k % 12 + 1) > ult,
                    Neto = n, NetoAcum = aP, NetoPpto = np, NetoPptoAcum = aPp, NetoAnt = na, NetoAntAcum = aA, NetoEsc = ne, NetoEscAcum = aE
                });
            }

            // Filas de la tabla (todas las líneas del modelo; se ocultan las que valen 0 en todas las series, salvo subtotales).
            foreach (var kv in desc.OrderBy(x => x.Key))
            {
                var fila = ConstruirFila(kv.Key, kv.Value, subtotal[kv.Key], principal, ppto, esc, clavesRango);
                if (fila == null) continue;
                reporte.Filas.Add(fila);
            }
            if (_tipo == FlujoTipo.Caja)
            {
                // Línea que el informe agrega: Flujo neto de caja = FCL operativo + financiero + no operacional.
                var total = new FlujoFilaReporte { Ord = 999, Codigo = "FlujoNeto", Descripcion = "", EsSubtotal = true, EsTotal = true };
                foreach (var k in clavesRango)
                {
                    total.Valores.Add(Neto(principal, k)); total.ValoresPpto.Add(Neto(ppto, k));
                    total.ValoresAnt.Add(Neto(principal, k - 12)); total.ValoresEsc.Add(esc != null ? Neto(esc, k) : 0m);
                }
                CompletarTotales(total, esc != null);
                reporte.Filas.Add(total);
            }
            else
            {
                var ultima = reporte.Filas.FirstOrDefault(f => f.Ord == EfAumentoNeto);
                if (ultima != null) ultima.EsTotal = true;
            }

            reporte.Kpis = ArmarKpis(principal, ppto, esc, clavesRango);
            reporte.Cascada = ArmarCascada(principal, clavesRango);
            return reporte;
        }

        private FlujoFilaReporte ConstruirFila(int ord, string descripcion, bool esSubtotal, Serie principal, Serie ppto, Serie esc, List<int> claves)
        {
            var fila = new FlujoFilaReporte
            {
                Ord = ord, Descripcion = descripcion, EsSubtotal = esSubtotal,
                EsGastoOCosto = _tipo == FlujoTipo.Caja && CostosCaja.Contains(descripcion)
            };
            foreach (var k in claves)
            {
                fila.Valores.Add(principal.Linea(ord, k));
                fila.ValoresPpto.Add(ppto.Linea(ord, k));
                fila.ValoresAnt.Add(principal.Linea(ord, k - 12));
                fila.ValoresEsc.Add(esc != null ? esc.Linea(ord, k) : 0m);
            }
            bool todoCero = fila.Valores.All(v => v == 0m) && fila.ValoresPpto.All(v => v == 0m)
                && fila.ValoresAnt.All(v => v == 0m) && fila.ValoresEsc.All(v => v == 0m);
            if (todoCero && !esSubtotal) return null;
            CompletarTotales(fila, esc != null);
            return fila;
        }

        private static void CompletarTotales(FlujoFilaReporte f, bool hayEsc)
        {
            f.Total = f.Valores.Sum();
            f.TotalPpto = f.ValoresPpto.Sum();
            f.TotalAnt = f.ValoresAnt.Sum();
            f.TotalEsc = f.ValoresEsc.Sum();
            f.VariacionAbsoluta = f.Total - f.TotalPpto;
            f.VariacionPorcentual = Variacion(f.Total, f.TotalPpto);
            f.VariacionAnioPorcentual = Variacion(f.Total, f.TotalAnt);
            f.VariacionEscenarioPorcentual = hayEsc ? Variacion(f.Total, f.TotalEsc) : null;
        }

        // ===== KPIs =====

        private List<FlujoKpi> ArmarKpis(Serie principal, Serie ppto, Serie esc, List<int> claves)
        {
            var kpis = new List<FlujoKpi>();
            Func<string, string, bool, Func<Serie, decimal>, FlujoKpi> crear = (codigo, formato, neutral, f) =>
            {
                decimal v = f(principal), p = f(ppto), a = 0m, e = esc != null ? f(esc) : 0m;
                // El año anterior es el mismo rango desplazado 12 meses.
                a = FuncionAnterior(principal, claves, f);
                bool pct = formato == "pct";
                var k = new FlujoKpi { Codigo = codigo, Formato = formato, Neutral = neutral, Valor = v, ValorPpto = p, ValorAnt = a, ValorEsc = e };
                // En porcentajes la variación es la diferencia en puntos (fracción); en moneda, relativa al comparativo.
                k.VariacionPptoPorcentual = pct ? (decimal?)(v - p) : Variacion(v, p);
                k.VariacionAnioPorcentual = pct ? (decimal?)(v - a) : Variacion(v, a);
                k.VariacionEscenarioPorcentual = esc != null ? (pct ? (decimal?)(v - e) : Variacion(v, e)) : null;
                return k;
            };

            if (_tipo == FlujoTipo.Caja)
            {
                kpis.Add(crear("BrutoDespuesImpuestos", "moneda", false, s => s.Suma(CjBrutoDespues, claves)));
                kpis.Add(crear("CapitalTrabajo", "moneda", false, s => s.Suma(CjCapTrabajo, claves)));
                kpis.Add(crear("FclOperativo", "moneda", false, s => s.Suma(CjFcl, claves)));
                kpis.Add(crear("FlujoNeto", "moneda", false, s => NetoTotal(s, claves)));
                var conv = crear("ConversionCaja", "pct", false, s => { decimal u = s.Suma(CjUtilOper, claves); return u > 0m ? s.Suma(CjFcl, claves) / u : 0m; });
                // La conversión solo tiene sentido con utilidad operacional positiva.
                conv.NoAplica = principal.Suma(CjUtilOper, claves) <= 0m;
                kpis.Add(conv);
            }
            else
            {
                kpis.Add(crear("EfectivoOperacion", "moneda", false, s => s.Suma(EfOperacionGenerado, claves)));
                kpis.Add(crear("FlujoOperacion", "moneda", false, s => s.Suma(EfFlujoOperacion, claves)));
                kpis.Add(crear("FlujoInversion", "moneda", true, s => s.Suma(EfInversion, claves)));
                kpis.Add(crear("DeudaNeta", "moneda", true, s => s.Suma(EfDeudaNeta, claves)));
                kpis.Add(crear("AumentoNeto", "moneda", false, s => s.Suma(EfAumentoNeto, claves)));
            }
            return kpis;
        }

        /// <summary>Evalúa un KPI sobre el mismo rango un año antes: se desplaza cada clave 12 meses.</summary>
        private static decimal FuncionAnterior(Serie principal, List<int> claves, Func<Serie, decimal> f)
        {
            var corrida = new Serie();
            foreach (var kv in principal.PorOrd)
                foreach (var m in kv.Value)
                    corrida.Poner(kv.Key, m.Key + 12, m.Value);   // lo de hace un año "cae" en el mes actual
            return f(corrida);
        }

        // ===== Puente de caja =====

        /// <summary>Puente del rango: parte del resultado (utilidad operacional en Flujo de caja; utilidad neta en Flujo de efectivo)
        /// y llega al flujo neto. Cada tramo sale de diferencias entre subtotales que ya calcula el modelo, y la inversión (en caja)
        /// absorbe el resto, así SIEMPRE cuadra con el flujo neto real del rango.</summary>
        private List<FlujoCascadaBarra> ArmarCascada(Serie s, List<int> claves)
        {
            var barras = new List<FlujoCascadaBarra>();
            Func<int, decimal> t = ord => s.Suma(ord, claves);

            decimal inicio, neto;
            var tramos = new List<KeyValuePair<string, decimal>>();
            if (_tipo == FlujoTipo.Caja)
            {
                inicio = t(CjUtilOper);
                tramos.Add(new KeyValuePair<string, decimal>("AjustesNoMonetarios", t(CjBrutoAntes) - t(CjUtilOper)));
                tramos.Add(new KeyValuePair<string, decimal>("Impuestos", t(CjBrutoDespues) - t(CjBrutoAntes)));
                tramos.Add(new KeyValuePair<string, decimal>("CapitalTrabajo", t(CjCapTrabajo)));
                tramos.Add(new KeyValuePair<string, decimal>("Inversion", t(CjFcl) - t(CjBrutoDespues) - t(CjCapTrabajo)));
                tramos.Add(new KeyValuePair<string, decimal>("Financiero", t(CjFinanciero)));
                tramos.Add(new KeyValuePair<string, decimal>("NoOperacional", t(CjNoOp)));
                neto = NetoTotal(s, claves);
            }
            else
            {
                inicio = t(EfUtilNeta);
                tramos.Add(new KeyValuePair<string, decimal>("AjustesNoMonetarios", t(EfOperacionGenerado) - t(EfUtilNeta)));
                tramos.Add(new KeyValuePair<string, decimal>("CapitalTrabajo", t(EfCapTrabajo)));
                tramos.Add(new KeyValuePair<string, decimal>("Inversion", t(EfInversion)));
                tramos.Add(new KeyValuePair<string, decimal>("DeudaNeta", t(EfDeudaNeta)));
                neto = NetoTotal(s, claves);
            }

            barras.Add(new FlujoCascadaBarra { Etiqueta = "Inicio", Tipo = "total", Desde = 0m, Hasta = inicio });
            decimal nivel = inicio;
            foreach (var tr in tramos)
            {
                if (Math.Abs(tr.Value) <= 1m) continue;   // sin ruido de redondeo (±1 peso)
                decimal despues = nivel + tr.Value;
                barras.Add(new FlujoCascadaBarra
                {
                    Etiqueta = tr.Key, Delta = tr.Value, Tipo = tr.Value >= 0 ? "positivo" : "negativo",
                    Desde = tr.Value >= 0 ? nivel : despues, Hasta = tr.Value >= 0 ? despues : nivel
                });
                nivel = despues;
            }
            barras.Add(new FlujoCascadaBarra { Etiqueta = "Final", Tipo = "total", Desde = 0m, Hasta = neto });
            return barras;
        }
    }
}
