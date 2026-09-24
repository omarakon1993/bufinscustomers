using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe gerencial de Estado de Resultados (PYG). Lee de la tabla ya materializada
    /// dbo.ModeloPYG (resultado persistido de sp_ModeloPYG) — no ejecuta el SP en vivo.
    /// </summary>
    public class InformePYGService : BaseService
    {
        // Estructura real confirmada contra dbo.Rel_PYG (22 líneas, en orden de Ord):
        // Ingresos, CMV, Depreciación costo, Amortización costo, Utilidad bruta [Calculo],
        // Gastos de administración, Gastos de venta, Gastos operacionales [Calculo],
        // Depreciación gasto, Amortización gasto, Provisión de cartera, Provisión inventarios,
        // Utilidad operacional [Calculo], Ingresos financieros, Gastos financieros,
        // Otros ingresos no operacionales, Otros gastos no operacionales, Gasto diferencia en cambio,
        // Utilidad antes de impuestos [Calculo], Impuesto renta, Utilidad neta [Calculo],
        // Efecto neto en el Ebitda [Calculo SQL]. No existe una línea "EBITDA" propiamente dicha —
        // se reconstruye como Utilidad operacional + Depreciación/Amortización (costo+gasto), ver
        // DescComponentesEbitda, y se inserta como fila calculada extra en el reporte.
        private const string DescIngresos = "Ingresos";
        private const string DescUtilidadBruta = "Utilidad bruta";
        private const string DescUtilidadOperacional = "Utilidad operacional";
        private const string DescUtilidadNeta = "Utilidad neta";
        private const string DescEbitdaSintetico = "EBITDA";
        private const string DescDeprecCosto = "Depreciación costo";
        private const string DescAmortCosto = "Amortización costo";
        private const string DescDeprecGasto = "Depreciación gasto";
        private const string DescAmortGasto = "Amortización gasto";

        private static readonly string[] DescComponentesEbitda =
            { DescUtilidadOperacional, DescDeprecCosto, DescAmortCosto, DescDeprecGasto, DescAmortGasto };

        // Líneas de costo/gasto/impuesto: los valores vienen como magnitud positiva, así que
        // "Real > Presupuesto" ahí es DESFAVORABLE (se gastó más de lo presupuestado) — se pinta en
        // rojo. Todo lo que no está en esta lista (ingresos, utilidades/subtotales) es lo contrario:
        // "Real > Presupuesto" es favorable (verde). Si aparecen líneas nuevas no listadas aquí,
        // ajustar este set.
        private static readonly HashSet<string> DescripcionesGastoOCosto = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CMV", "Depreciación costo", "Amortización costo",
            "Gastos de administración", "Gastos de venta", "Gastos operacionales",
            "Depreciación gasto", "Amortización gasto", "Provisión de cartera", "Provisión inventarios",
            "Gastos financieros", "Otros gastos no operacionales", "Gasto diferencia en cambio",
            "Impuesto renta"
        };

        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        /// <summary>Lee todas las filas (todos los meses) de dbo.ModeloPYG para una empresa/escenario y un
        /// rango de años (ambos inclusive), con el flag de subtotal ya resuelto vía JOIN a dbo.Rel_PYG
        /// (Tipo &lt;&gt; 'Cuentas').</summary>
        public List<PygFilaModelo> ObtenerFilas(int idEmpresa, byte idEscenario, int añoDesde, int añoHasta)
        {
            var filas = new List<PygFilaModelo>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT m.IdEscenario, m.Año, m.Mes, m.Ord, m.CuentaPUC, m.Descripcion, m.Valor,
                           m.ValorAcumulado, m.ValorForecast, m.ValorFuturo, m.ValorFuturoAcumulado,
                           m.ValorPresupuesto, m.ValorPresupuestoAcumulado, m.ValorPresupuestoConAjuste,
                           r.Tipo AS TipoLinea
                    FROM dbo.ModeloPYG m
                    LEFT JOIN dbo.Rel_PYG r ON r.Id = m.Ord
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
                        string tipoLinea = (reader["TipoLinea"] as string ?? "").Trim();

                        filas.Add(new PygFilaModelo
                        {
                            IdEscenario = Convert.ToByte(reader["IdEscenario"]),
                            Año = Convert.ToInt32(reader["Año"]),
                            Mes = Convert.ToInt32(reader["Mes"]),
                            Ord = Convert.ToInt32(reader["Ord"]),
                            CuentaPUC = reader["CuentaPUC"] as string,
                            Descripcion = (reader["Descripcion"] as string ?? "").Trim(),
                            Valor = LeerDecimal(reader, "Valor"),
                            ValorAcumulado = LeerDecimal(reader, "ValorAcumulado"),
                            ValorForecast = LeerDecimal(reader, "ValorForecast"),
                            ValorFuturo = LeerDecimal(reader, "ValorFuturo"),
                            ValorFuturoAcumulado = LeerDecimal(reader, "ValorFuturoAcumulado"),
                            ValorPresupuesto = LeerDecimal(reader, "ValorPresupuesto"),
                            ValorPresupuestoAcumulado = LeerDecimal(reader, "ValorPresupuestoAcumulado"),
                            ValorPresupuestoConAjuste = LeerDecimal(reader, "ValorPresupuestoConAjuste"),
                            EsSubtotal = tipoLinea.Length > 0 && !string.Equals(tipoLinea, "Cuentas", StringComparison.OrdinalIgnoreCase)
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

        /// <summary>Meses (año+mes) con datos en dbo.ModeloPYG para la empresa/escenario, en orden
        /// cronológico — alimenta el slider de rango continuo (igual que el Informe de Línea de Tiempo).</summary>
        public List<PygMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            var meses = new List<PygMesDisponible>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                // Validación y orden en C# (no en SQL), igual que ObtenerFilas: no depende del tipo de la columna Mes.
                const string query = @"
                    SELECT DISTINCT Año, Mes FROM dbo.ModeloPYG
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
                        meses.Add(new PygMesDisponible { Anio = año, Mes = mes, Etiqueta = MesesAbrev[mes - 1] + " " + año });
                    }
                }
            }

            return meses.OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        /// <summary>Escenarios con al menos una fila en dbo.ModeloPYG para la empresa — la vista deshabilita
        /// en "Comparar con escenario" los que no tienen datos.</summary>
        public List<byte> ObtenerEscenariosConDatos(int idEmpresa)
        {
            var ids = new List<byte>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("SELECT DISTINCT IdEscenario FROM dbo.ModeloPYG WHERE IdEmpresa = @IdEmpresa AND IdEscenario IS NOT NULL", cn);
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

        // ===== Cortes de datos por rango =====

        /// <summary>Filas de un rango ya recortadas: Rango = meses del período; Hasta = mes final (de ahí
        /// sale ValorAcumulado); YtdHasta = enero..mes final del año final (acumulado del comparativo
        /// cuando no hay una columna acumulada propia, p. ej. presupuesto con ajuste / forecast).</summary>
        private class Corte
        {
            public List<PygFilaModelo> Rango;
            public List<PygFilaModelo> Hasta;
            public List<PygFilaModelo> YtdHasta;
            public bool Vacio => Rango.Count == 0 || Hasta.Count == 0;
        }

        /// <summary>Real/comparativo de una línea (o suma de varias, para EBITDA) en un corte.</summary>
        private class Valores
        {
            public decimal PeriodoReal, PeriodoComp, AcumReal, AcumComp;
        }

        /// <summary>Índice absoluto de mes (año*12 + mes-1) — permite rangos que cruzan años.</summary>
        private static int Clave(int año, int mes) => año * 12 + (mes - 1);

        private static Corte Cortar(List<PygFilaModelo> filas, int kDesde, int kHasta)
        {
            int añoH = kHasta / 12, mesH = kHasta % 12 + 1;
            return new Corte
            {
                Rango = filas.Where(f => { int k = Clave(f.Año, f.Mes); return k >= kDesde && k <= kHasta; }).ToList(),
                Hasta = filas.Where(f => f.Año == añoH && f.Mes == mesH).OrderBy(f => f.Ord).ToList(),
                YtdHasta = filas.Where(f => f.Año == añoH && f.Mes <= mesH).ToList()
            };
        }

        private static Func<PygFilaModelo, decimal> SelectorComparativo(string comparar)
        {
            if (comparar == PygComparar.PresupuestoAjuste) return f => f.ValorPresupuestoConAjuste;
            if (comparar == PygComparar.Forecast) return f => f.ValorForecast;
            return f => f.ValorPresupuesto;
        }

        private static Valores ValoresLinea(Corte c, string[] descripciones, string comparar)
        {
            if (c == null) return null;
            var comp = SelectorComparativo(comparar);
            return new Valores
            {
                PeriodoReal = SumarComponentes(c.Rango, descripciones, f => f.Valor),
                PeriodoComp = SumarComponentes(c.Rango, descripciones, comp),
                AcumReal = SumarComponentes(c.Hasta, descripciones, f => f.ValorAcumulado),
                // El presupuesto trae su propio acumulado materializado; para los demás comparativos se
                // suman sus meses de enero al mes final del año final.
                AcumComp = comparar == PygComparar.Presupuesto
                    ? SumarComponentes(c.Hasta, descripciones, f => f.ValorPresupuestoAcumulado)
                    : SumarComponentes(c.YtdHasta, descripciones, comp)
            };
        }

        private static decimal SumarComponentes(List<PygFilaModelo> filas, string[] descripciones, Func<PygFilaModelo, decimal> selector)
        {
            return filas
                .Where(f => descripciones.Any(d => string.Equals(f.Descripcion, d, StringComparison.OrdinalIgnoreCase)))
                .Sum(selector);
        }

        private static string NormalizarAgrupar(string agrupar)
        {
            if (string.Equals(agrupar, "Trimestre", StringComparison.OrdinalIgnoreCase)) return "Trimestre";
            if (string.Equals(agrupar, "Anio", StringComparison.OrdinalIgnoreCase)) return "Anio";
            return "Mes";
        }

        /// <summary>Arma el reporte completo (filas Periodo+Acumulado, KPIs, cascada y tendencias) para el
        /// rango continuo [AnioDesde/MesDesde, AnioHasta/MesHasta] — puede cruzar años. "Periodo" suma los
        /// meses del rango; "Acumulado" es el acumulado del año final hasta el mes final; la comparación
        /// interanual usa el mismo rango desplazado 12 meses; el comparativo ("Presupuesto" en la vista)
        /// sale de la columna elegida en FiltrosPYG.Comparar.</summary>
        public PygReporteViewModel ConstruirReporte(FiltrosPYG filtros)
        {
            int mesD = Math.Min(12, Math.Max(1, filtros.MesDesde)), mesH = Math.Min(12, Math.Max(1, filtros.MesHasta));
            int kD = Clave(filtros.AnioDesde, mesD), kH = Clave(filtros.AnioHasta, mesH);
            if (kD > kH) { int tmp = kD; kD = kH; kH = tmp; }
            int añoD = kD / 12, añoH = kH / 12;
            string comparar = PygComparar.Normalizar(filtros.Comparar);
            string agrupar = NormalizarAgrupar(filtros.Agrupar);
            bool conEscenario = filtros.IdEscenarioComparar.HasValue && filtros.IdEscenarioComparar.Value != filtros.IdEscenario;

            var resultado = new PygReporteViewModel
            {
                IdEmpresa = filtros.IdEmpresa,
                IdEscenario = filtros.IdEscenario,
                Año = añoH,
                AnioDesde = añoD,
                MesDesde = kD % 12 + 1,
                AnioHasta = añoH,
                MesHasta = kH % 12 + 1,
                Comparar = comparar,
                Agrupar = agrupar,
                IdEscenarioComparar = conEscenario ? filtros.IdEscenarioComparar : null,
                AnioAnterior = añoH - 1
            };

            // Se lee también el año anterior al inicial: la comparación interanual (YoY) usa el mismo
            // rango desplazado 12 meses. Si no hay datos ahí (empresa nueva) la comparación queda vacía
            // (todos los campos *AnioAnterior/YoY en null), nunca rompe el resto del reporte.
            var filas = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenario, añoD - 1, añoH);
            var actual = Cortar(filas, kD, kH);
            if (actual.Vacio)
            {
                resultado.SinDatos = true;
                return resultado;
            }

            var anterior = Cortar(filas, kD - 12, kH - 12);
            bool hayAnterior = !anterior.Vacio;
            resultado.HayAnioAnterior = hayAnterior;
            if (!hayAnterior) anterior = null;

            List<PygFilaModelo> filasEsc = null;
            Corte escenario = null;
            if (conEscenario)
            {
                filasEsc = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenarioComparar.Value, añoD, añoH);
                var corteEsc = Cortar(filasEsc, kD, kH);
                if (!corteEsc.Vacio) escenario = corteEsc;
                else filasEsc = null;
            }
            resultado.HayEscenarioComparar = escenario != null;

            Func<string[], Valores> vAct = d => ValoresLinea(actual, d, comparar);
            Func<string[], Valores> vAnt = d => ValoresLinea(anterior, d, comparar);
            Func<string[], Valores> vEsc = d => ValoresLinea(escenario, d, comparar);

            var descIngresos = new[] { DescIngresos };
            var ingresos = vAct(descIngresos);
            var ingresosAnt = vAnt(descIngresos);
            var ingresosEsc = vEsc(descIngresos);

            foreach (var f in actual.Hasta)
            {
                var d = new[] { f.Descripcion };
                resultado.Filas.Add(ArmarFilaReporte(f.Ord, f.Descripcion, f.EsSubtotal,
                    vAct(d), ingresos, vAnt(d), ingresosAnt, vEsc(d), ingresosEsc));

                // EBITDA no es una línea real de Rel_PYG — se inserta calculada justo después de
                // "Utilidad operacional" (Utilidad operacional + Depreciación/Amortización costo+gasto).
                if (string.Equals(f.Descripcion, DescUtilidadOperacional, StringComparison.OrdinalIgnoreCase))
                {
                    resultado.Filas.Add(ArmarFilaReporte(f.Ord, DescEbitdaSintetico, true,
                        vAct(DescComponentesEbitda), ingresos, vAnt(DescComponentesEbitda), ingresosAnt,
                        vEsc(DescComponentesEbitda), ingresosEsc));
                }
            }

            resultado.IngresosFila = ArmarFilaReporte(0, DescIngresos, true, ingresos, ingresos, ingresosAnt, ingresosAnt, ingresosEsc, ingresosEsc);
            resultado.EbitdaFila = ArmarFilaReporte(0, DescEbitdaSintetico, true,
                vAct(DescComponentesEbitda), ingresos, vAnt(DescComponentesEbitda), ingresosAnt, vEsc(DescComponentesEbitda), ingresosEsc);
            var descNeta = new[] { DescUtilidadNeta };
            resultado.UtilidadNetaFila = ArmarFilaReporte(0, DescUtilidadNeta, true,
                vAct(descNeta), ingresos, vAnt(descNeta), ingresosAnt, vEsc(descNeta), ingresosEsc);

            var descBruta = new[] { DescUtilidadBruta };
            resultado.Kpis.Add(ArmarKpi("MargenBruto", vAct(descBruta), ingresos, vAnt(descBruta), ingresosAnt, vEsc(descBruta), ingresosEsc));
            resultado.Kpis.Add(ArmarKpi("Ebitda", vAct(DescComponentesEbitda), ingresos, vAnt(DescComponentesEbitda), ingresosAnt, vEsc(DescComponentesEbitda), ingresosEsc));
            resultado.Kpis.Add(ArmarKpi("MargenNeto", vAct(descNeta), ingresos, vAnt(descNeta), ingresosAnt, vEsc(descNeta), ingresosEsc));

            // Cascada (waterfall) Ingresos → Utilidad Neta del período seleccionado, con los 4 subtotales
            // que ya vienen calculados por sp_ModeloPYG (Ingresos/Utilidad bruta/Utilidad operacional/
            // Utilidad neta) como puntos de apoyo — así la suma de los tramos SIEMPRE cuadra exacto con
            // Ingresos-Utilidad Neta reales, sin depender de reconstruir el detalle línea a línea.
            var bruta = vAct(descBruta);
            var operacional = vAct(new[] { DescUtilidadOperacional });
            var neta = vAct(descNeta);
            resultado.CascadaPeriodo = ArmarCascada(ingresos.PeriodoReal, bruta.PeriodoReal, operacional.PeriodoReal, neta.PeriodoReal);
            resultado.CascadaPresupuestoUtilidadNeta = neta.PeriodoComp;

            // Tendencias: todos los meses de los años que toca el rango (contexto), marcando qué puntos
            // caen dentro del rango consultado (la vista los sombrea).
            var filasTendencia = filas.Where(f => f.Año >= añoD && f.Año <= añoH).ToList();
            bool variosAños = añoD != añoH;
            resultado.TendenciaIngresos = ArmarTendencia(filasTendencia, filasEsc, descIngresos, comparar, agrupar, kD, kH, variosAños);
            resultado.TendenciaEbitda = ArmarTendencia(filasTendencia, filasEsc, DescComponentesEbitda, comparar, agrupar, kD, kH, variosAños);
            resultado.TendenciaUtilidadNeta = ArmarTendencia(filasTendencia, filasEsc, descNeta, comparar, agrupar, kD, kH, variosAños);

            return resultado;
        }

        /// <summary>Arma los 5 tramos de la cascada Ingresos → Utilidad Neta (2 barras "total" en los
        /// extremos + 3 tramos intermedios cuyo delta es, por construcción, la diferencia exacta entre
        /// dos subtotales consecutivos ya calculados por el modelo — generico para cualquier empresa,
        /// no depende de qué cuentas puntuales componen cada bloque.</summary>
        private static List<PygCascadaBarra> ArmarCascada(decimal ingresos, decimal utilidadBruta, decimal utilidadOperacional, decimal utilidadNeta)
        {
            var barras = new List<PygCascadaBarra>
            {
                new PygCascadaBarra { Etiqueta = "Ingresos", Tipo = "total", Desde = 0, Hasta = ingresos }
            };

            barras.Add(TramoCascada("CostoVentas", ingresos, utilidadBruta));
            barras.Add(TramoCascada("GastosOperacionales", utilidadBruta, utilidadOperacional));
            barras.Add(TramoCascada("OtrosEImpuestos", utilidadOperacional, utilidadNeta));

            barras.Add(new PygCascadaBarra { Etiqueta = "UtilidadNeta", Tipo = "total", Desde = 0, Hasta = utilidadNeta });
            return barras;
        }

        private static PygCascadaBarra TramoCascada(string etiqueta, decimal desde, decimal hasta)
        {
            decimal delta = hasta - desde;
            return new PygCascadaBarra
            {
                Etiqueta = etiqueta,
                Tipo = delta >= 0 ? "positivo" : "negativo",
                Desde = Math.Min(desde, hasta),
                Hasta = Math.Max(desde, hasta),
                Delta = delta
            };
        }

        private static decimal? Variacion(decimal actual, decimal? baseComparacion)
        {
            return (baseComparacion.HasValue && baseComparacion.Value != 0)
                ? (actual - baseComparacion.Value) / Math.Abs(baseComparacion.Value) : (decimal?)null;
        }

        /// <param name="ant">null si el año anterior no tiene datos.</param>
        /// <param name="esc">null si no se pidió (o no hay datos de) escenario de comparación.</param>
        private static PygFilaReporte ArmarFilaReporte(int orden, string descripcion, bool esSubtotal,
            Valores v, Valores ingresos, Valores ant, Valores ingresosAnt, Valores esc, Valores ingresosEsc)
        {
            var fila = new PygFilaReporte
            {
                Orden = orden,
                Descripcion = descripcion,
                EsSubtotal = esSubtotal,
                EsGastoOCosto = DescripcionesGastoOCosto.Contains(descripcion),
                PeriodoReal = v.PeriodoReal,
                PeriodoPresupuesto = v.PeriodoComp,
                AcumReal = v.AcumReal,
                AcumPresupuesto = v.AcumComp
            };

            fila.PeriodoVariacionPorcentual = Variacion(v.PeriodoReal, v.PeriodoComp);
            fila.AcumVariacionPorcentual = Variacion(v.AcumReal, v.AcumComp);
            fila.PeriodoMargen = ingresos.PeriodoReal != 0 ? v.PeriodoReal / ingresos.PeriodoReal : (decimal?)null;
            fila.AcumMargen = ingresos.AcumReal != 0 ? v.AcumReal / ingresos.AcumReal : (decimal?)null;

            if (ant != null)
            {
                fila.PeriodoRealAnioAnterior = ant.PeriodoReal;
                fila.PeriodoVariacionYoYPorcentual = Variacion(v.PeriodoReal, ant.PeriodoReal);
                fila.AcumRealAnioAnterior = ant.AcumReal;
                fila.AcumVariacionYoYPorcentual = Variacion(v.AcumReal, ant.AcumReal);
            }

            if (esc != null)
            {
                fila.PeriodoRealEscenario = esc.PeriodoReal;
                fila.PeriodoVariacionEscenarioPorcentual = Variacion(v.PeriodoReal, esc.PeriodoReal);
                fila.AcumRealEscenario = esc.AcumReal;
                fila.AcumVariacionEscenarioPorcentual = Variacion(v.AcumReal, esc.AcumReal);
            }

            return fila;
        }

        private static PygKpiMargen ArmarKpi(string codigo, Valores v, Valores ingresos,
            Valores vAnt, Valores ingresosAnt, Valores vEsc, Valores ingresosEsc)
        {
            var kpi = new PygKpiMargen
            {
                Codigo = codigo,
                PeriodoRealPct = ingresos.PeriodoReal != 0 ? v.PeriodoReal / ingresos.PeriodoReal : 0m,
                PeriodoPresupuestoPct = ingresos.PeriodoComp != 0 ? v.PeriodoComp / ingresos.PeriodoComp : 0m,
                AcumRealPct = ingresos.AcumReal != 0 ? v.AcumReal / ingresos.AcumReal : 0m,
                AcumPresupuestoPct = ingresos.AcumComp != 0 ? v.AcumComp / ingresos.AcumComp : 0m
            };

            if (vAnt != null)
            {
                kpi.PeriodoRealPctAnioAnterior = ingresosAnt.PeriodoReal != 0 ? vAnt.PeriodoReal / ingresosAnt.PeriodoReal : (decimal?)null;
                kpi.AcumRealPctAnioAnterior = ingresosAnt.AcumReal != 0 ? vAnt.AcumReal / ingresosAnt.AcumReal : (decimal?)null;
            }

            if (vEsc != null)
            {
                kpi.PeriodoRealPctEscenario = ingresosEsc.PeriodoReal != 0 ? vEsc.PeriodoReal / ingresosEsc.PeriodoReal : (decimal?)null;
                kpi.AcumRealPctEscenario = ingresosEsc.AcumReal != 0 ? vEsc.AcumReal / ingresosEsc.AcumReal : (decimal?)null;
            }

            return kpi;
        }

        /// <summary>Serie de tendencia sumando una o varias descripciones (EBITDA es la suma de 5 líneas;
        /// Ingresos/Utilidad neta son una sola), agrupada por mes, trimestre o año.</summary>
        private static List<PygPuntoTendencia> ArmarTendencia(List<PygFilaModelo> filas, List<PygFilaModelo> filasEscenario,
            string[] descripciones, string comparar, string agrupar, int kDesde, int kHasta, bool variosAños)
        {
            var comp = SelectorComparativo(comparar);
            Func<PygFilaModelo, bool> esLinea = f => descripciones.Any(d => string.Equals(f.Descripcion, d, StringComparison.OrdinalIgnoreCase));
            Func<PygFilaModelo, int> grupo;
            if (agrupar == "Trimestre") grupo = f => f.Año * 4 + (f.Mes - 1) / 3;
            else if (agrupar == "Anio") grupo = f => f.Año;
            else grupo = f => Clave(f.Año, f.Mes);

            var escPorGrupo = filasEscenario?
                .Where(esLinea)
                .GroupBy(grupo)
                .ToDictionary(g => g.Key, g => g.Sum(f => f.Valor));

            return filas
                .Where(esLinea)
                .GroupBy(grupo)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var primera = g.First();
                    int año = primera.Año, mes, trimestre = (primera.Mes - 1) / 3 + 1;
                    string etiqueta;
                    if (agrupar == "Trimestre") { mes = trimestre; etiqueta = variosAños ? $"T{trimestre} {año}" : $"T{trimestre}"; }
                    else if (agrupar == "Anio") { mes = 0; etiqueta = año.ToString(); }
                    else
                    {
                        mes = primera.Mes;
                        string abrev = mes >= 1 && mes <= 12 ? MesesAbrev[mes - 1] : string.Empty;
                        etiqueta = variosAños ? $"{abrev} {año % 100:00}" : abrev;
                    }

                    decimal? realEsc = null;
                    if (escPorGrupo != null) realEsc = escPorGrupo.TryGetValue(g.Key, out var v) ? v : 0m;

                    return new PygPuntoTendencia
                    {
                        Año = año,
                        Mes = mes,
                        Etiqueta = etiqueta,
                        Real = g.Sum(f => f.Valor),
                        Presupuesto = g.Sum(comp),
                        RealEscenario = realEsc,
                        EnRango = g.Any(f => { int k = Clave(f.Año, f.Mes); return k >= kDesde && k <= kHasta; })
                    };
                })
                .ToList();
        }
    }
}
