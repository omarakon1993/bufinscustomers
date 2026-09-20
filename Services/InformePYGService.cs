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

        /// <summary>Lee todas las filas (todos los meses) de dbo.ModeloPYG para una empresa/escenario/año,
        /// con el flag de subtotal ya resuelto vía JOIN a dbo.Rel_PYG (Tipo &lt;&gt; 'Cuentas').</summary>
        public List<PygFilaModelo> ObtenerFilas(int idEmpresa, byte idEscenario, int año)
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
                    WHERE m.IdEmpresa = @IdEmpresa AND m.IdEscenario = @IdEscenario AND m.Año = @Año";

                var cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cmd.Parameters.AddWithValue("@Año", año);
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

            return filas.OrderBy(f => f.Ord).ThenBy(f => f.Mes).ToList();
        }

        private static decimal LeerDecimal(IDataRecord reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? 0m : Convert.ToDecimal(valor);
        }

        public List<int> ObtenerAñosDisponibles(int idEmpresa, byte idEscenario)
        {
            var años = new List<int>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT DISTINCT Año FROM dbo.ModeloPYG
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario
                    ORDER BY Año DESC";

                var cmd = new SqlCommand(query, cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                cn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader[0] != DBNull.Value)
                            años.Add(Convert.ToInt32(reader[0]));
                    }
                }
            }

            return años;
        }

        /// <summary>Arma el reporte completo (filas Periodo+Acumulado, KPIs y series de tendencia) para
        /// la empresa/escenario/año pedidos, sumando el rango [mesDesde, mesHasta] elegido en el slider.
        /// El bloque "Acumulado" siempre se toma en mesHasta (ValorAcumulado ya es un corrido hasta ese mes).</summary>
        public PygReporteViewModel ConstruirReporte(int idEmpresa, byte idEscenario, int año, int mesDesde, int mesHasta)
        {
            if (mesDesde > mesHasta)
            {
                int tmp = mesDesde; mesDesde = mesHasta; mesHasta = tmp;
            }

            var resultado = new PygReporteViewModel
            {
                IdEmpresa = idEmpresa,
                IdEscenario = idEscenario,
                Año = año,
                MesDesde = mesDesde,
                MesHasta = mesHasta
            };

            var todasLasFilas = ObtenerFilas(idEmpresa, idEscenario, año);
            if (todasLasFilas.Count == 0)
            {
                resultado.SinDatos = true;
                return resultado;
            }

            var filasDelRango = todasLasFilas.Where(f => f.Mes >= mesDesde && f.Mes <= mesHasta).ToList();
            var filasHasta = todasLasFilas.Where(f => f.Mes == mesHasta).OrderBy(f => f.Ord).ToList();
            if (filasDelRango.Count == 0 || filasHasta.Count == 0)
            {
                resultado.SinDatos = true;
                return resultado;
            }

            var ingresos = ValoresLinea(filasDelRango, filasHasta, new[] { DescIngresos });

            foreach (var f in filasHasta)
            {
                var v = ValoresLinea(filasDelRango, filasHasta, new[] { f.Descripcion });
                resultado.Filas.Add(ArmarFilaReporte(f.Ord, f.Descripcion, f.EsSubtotal,
                    v.periodoReal, v.periodoPpto, v.acumReal, v.acumPpto,
                    ingresos.periodoReal, ingresos.acumReal));

                // EBITDA no es una línea real de Rel_PYG — se inserta calculada justo después de
                // "Utilidad operacional" (Utilidad operacional + Depreciación/Amortización costo+gasto).
                if (string.Equals(f.Descripcion, DescUtilidadOperacional, StringComparison.OrdinalIgnoreCase))
                {
                    var e = ValoresLinea(filasDelRango, filasHasta, DescComponentesEbitda);
                    resultado.Filas.Add(ArmarFilaReporte(f.Ord, DescEbitdaSintetico, true,
                        e.periodoReal, e.periodoPpto, e.acumReal, e.acumPpto,
                        ingresos.periodoReal, ingresos.acumReal));
                }
            }

            var bruta = ValoresLinea(filasDelRango, filasHasta, new[] { DescUtilidadBruta });
            resultado.Kpis.Add(ArmarKpiDesdeValores("MargenBruto", bruta, ingresos));

            var ebitdaKpi = ValoresLinea(filasDelRango, filasHasta, DescComponentesEbitda);
            resultado.Kpis.Add(ArmarKpiDesdeValores("Ebitda", ebitdaKpi, ingresos));

            var neta = ValoresLinea(filasDelRango, filasHasta, new[] { DescUtilidadNeta });
            resultado.Kpis.Add(ArmarKpiDesdeValores("MargenNeto", neta, ingresos));

            resultado.TendenciaIngresos = ArmarTendencia(todasLasFilas, new[] { DescIngresos }, año);
            resultado.TendenciaEbitda = ArmarTendencia(todasLasFilas, DescComponentesEbitda, año);
            resultado.TendenciaUtilidadNeta = ArmarTendencia(todasLasFilas, new[] { DescUtilidadNeta }, año);

            return resultado;
        }

        private static PygFilaReporte ArmarFilaReporte(int orden, string descripcion, bool esSubtotal,
            decimal periodoReal, decimal periodoPpto, decimal acumReal, decimal acumPpto,
            decimal ingresosPeriodoReal, decimal ingresosAcumReal)
        {
            var fila = new PygFilaReporte
            {
                Orden = orden,
                Descripcion = descripcion,
                EsSubtotal = esSubtotal,
                EsGastoOCosto = DescripcionesGastoOCosto.Contains(descripcion),
                PeriodoReal = periodoReal,
                PeriodoPresupuesto = periodoPpto,
                AcumReal = acumReal,
                AcumPresupuesto = acumPpto
            };

            fila.PeriodoVariacionPorcentual = periodoPpto != 0 ? (periodoReal - periodoPpto) / Math.Abs(periodoPpto) : (decimal?)null;
            fila.AcumVariacionPorcentual = acumPpto != 0 ? (acumReal - acumPpto) / Math.Abs(acumPpto) : (decimal?)null;
            fila.PeriodoMargen = ingresosPeriodoReal != 0 ? periodoReal / ingresosPeriodoReal : (decimal?)null;
            fila.AcumMargen = ingresosAcumReal != 0 ? acumReal / ingresosAcumReal : (decimal?)null;

            return fila;
        }

        /// <summary>Real/Presupuesto de una línea (o suma de varias, para EBITDA), sumado en el rango
        /// elegido para el bloque "Periodo" y tomado en mesHasta para el bloque "Acumulado".</summary>
        private static (decimal periodoReal, decimal periodoPpto, decimal acumReal, decimal acumPpto) ValoresLinea(
            List<PygFilaModelo> filasDelRango, List<PygFilaModelo> filasHasta, string[] descripciones)
        {
            return (
                SumarComponentes(filasDelRango, descripciones, f => f.Valor),
                SumarComponentes(filasDelRango, descripciones, f => f.ValorPresupuesto),
                SumarComponentes(filasHasta, descripciones, f => f.ValorAcumulado),
                SumarComponentes(filasHasta, descripciones, f => f.ValorPresupuestoAcumulado)
            );
        }

        private static decimal SumarComponentes(List<PygFilaModelo> filas, string[] descripciones, Func<PygFilaModelo, decimal> selector)
        {
            return filas
                .Where(f => descripciones.Any(d => string.Equals(f.Descripcion, d, StringComparison.OrdinalIgnoreCase)))
                .Sum(selector);
        }

        private static PygKpiMargen ArmarKpiDesdeValores(string codigo,
            (decimal periodoReal, decimal periodoPpto, decimal acumReal, decimal acumPpto) valores,
            (decimal periodoReal, decimal periodoPpto, decimal acumReal, decimal acumPpto) ingresos)
        {
            return new PygKpiMargen
            {
                Codigo = codigo,
                PeriodoRealPct = ingresos.periodoReal != 0 ? valores.periodoReal / ingresos.periodoReal : 0m,
                PeriodoPresupuestoPct = ingresos.periodoPpto != 0 ? valores.periodoPpto / ingresos.periodoPpto : 0m,
                AcumRealPct = ingresos.acumReal != 0 ? valores.acumReal / ingresos.acumReal : 0m,
                AcumPresupuestoPct = ingresos.acumPpto != 0 ? valores.acumPpto / ingresos.acumPpto : 0m
            };
        }

        /// <summary>Serie de tendencia sumando, mes a mes, una o varias descripciones (EBITDA es la
        /// suma de 5 líneas; Ingresos/Utilidad neta son una sola).</summary>
        private static List<PygPuntoTendencia> ArmarTendencia(List<PygFilaModelo> todasLasFilas, string[] descripciones, int año)
        {
            return todasLasFilas
                .Where(f => descripciones.Any(d => string.Equals(f.Descripcion, d, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(f => f.Mes)
                .OrderBy(g => g.Key)
                .Select(g => new PygPuntoTendencia
                {
                    Año = año,
                    Mes = g.Key,
                    Etiqueta = g.Key >= 1 && g.Key <= 12 ? MesesAbrev[g.Key - 1] : string.Empty,
                    Real = g.Sum(f => f.Valor),
                    Presupuesto = g.Sum(f => f.ValorPresupuesto)
                })
                .ToList();
        }
    }
}
