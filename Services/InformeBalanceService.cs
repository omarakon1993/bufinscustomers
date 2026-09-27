using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe gerencial de Balance General. Lee de la tabla ya materializada dbo.ModeloBalance
    /// (resultado persistido de sp_ModeloBalance) — no ejecuta el SP en vivo. A diferencia del PYG
    /// (InformePYGService), el balance es una FOTO a una fecha de corte, no un flujo acumulable: no
    /// existe un "Período" que se pueda sumar. El corte para KPIs/tabla/cuadre es siempre el mes final
    /// del rango elegido (AnioHasta/MesHasta); el rango completo solo alimenta las 3 tendencias mensuales.
    /// </summary>
    public class InformeBalanceService : BaseService
    {
        // Literales candidatos de dbo.REL_Balance.Descripcion con Tipo='CALCULO', SIN CONFIRMAR contra la
        // BD en vivo (mismo riesgo ya documentado en InformePYGService para Rel_PYG): si no calzan
        // exacto, la tabla completa sigue mostrándose bien (viene de Ord/Descripcion reales) — solo los
        // KPIs, la estructura (2 barras) y las tendencias quedarían en 0/"No aplica" hasta ajustar estas
        // constantes tras ver el primer reporte con datos reales.
        private const string DescActivoTotal = "Total Activo";
        private const string DescActivoCorriente = "Activo Corriente";
        private const string DescActivoNoCorriente = "Activo No Corriente";
        private const string DescPasivoTotal = "Total Pasivo";
        private const string DescPasivoCorriente = "Pasivo Corriente";
        private const string DescPasivoNoCorriente = "Pasivo No Corriente";
        private const string DescPatrimonioTotal = "Total Patrimonio";
        // Para el cruce opcional con dbo.ModeloPYG (utilidad neta acumulada vs. resultado del ejercicio
        // del balance) — igualmente sin confirmar; si no se encuentra, HayComparacionPYG queda en false
        // y el badge de cuadre cruzado simplemente no se muestra (no rompe el resto del informe).
        private const string DescResultadoEjercicio = "Resultado del Ejercicio";
        private const string DescUtilidadNetaPYG = "Utilidad neta";

        // Tolerancia para el chequeo de cuadre (Activo = Pasivo + Patrimonio) y el cruce con el PYG:
        // 1 peso, para absorber redondeos de la cadena de subtotales sin marcar como "descuadrado" un
        // balance que en la práctica cuadra.
        private const decimal ToleranciaCuadre = 1m;

        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        /// <summary>Lee todas las filas (todos los meses) de dbo.ModeloBalance para una empresa/escenario
        /// y un rango de años (ambos inclusive), con el flag de subtotal ya resuelto vía JOIN a
        /// dbo.REL_Balance (Tipo = 'CALCULO').</summary>
        public List<BalanceFilaModelo> ObtenerFilas(int idEmpresa, byte idEscenario, int añoDesde, int añoHasta)
        {
            var filas = new List<BalanceFilaModelo>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT m.IdEscenario, m.Año, m.Mes, m.Ord, m.CuentaPUC, m.Descripcion, m.Valor,
                           r.Tipo AS TipoLinea
                    FROM dbo.ModeloBalance m
                    LEFT JOIN dbo.REL_Balance r ON r.Id = m.Ord
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

                        filas.Add(new BalanceFilaModelo
                        {
                            IdEscenario = Convert.ToByte(reader["IdEscenario"]),
                            Año = Convert.ToInt32(reader["Año"]),
                            Mes = Convert.ToInt32(reader["Mes"]),
                            Ord = Convert.ToInt32(reader["Ord"]),
                            CuentaPUC = reader["CuentaPUC"] as string,
                            Descripcion = (reader["Descripcion"] as string ?? "").Trim(),
                            Valor = LeerDecimal(reader, "Valor"),
                            EsSubtotal = string.Equals(tipoLinea, "CALCULO", StringComparison.OrdinalIgnoreCase)
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

        /// <summary>Meses (año+mes) con datos en dbo.ModeloBalance para la empresa/escenario, en orden
        /// cronológico — alimenta el slider de rango continuo (igual que PYG/Línea de Tiempo).</summary>
        public List<BalanceMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            var meses = new List<BalanceMesDisponible>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT DISTINCT Año, Mes FROM dbo.ModeloBalance
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
                        meses.Add(new BalanceMesDisponible { Anio = año, Mes = mes, Etiqueta = MesesAbrev[mes - 1] + " " + año });
                    }
                }
            }

            return meses.OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        /// <summary>Escenarios con al menos una fila en dbo.ModeloBalance para la empresa — la vista
        /// deshabilita en "Comparar con escenario" los que no tienen datos.</summary>
        public List<byte> ObtenerEscenariosConDatos(int idEmpresa)
        {
            var ids = new List<byte>();

            using (var cn = new SqlConnection(CadenaConexion))
            {
                var cmd = new SqlCommand("SELECT DISTINCT IdEscenario FROM dbo.ModeloBalance WHERE IdEmpresa = @IdEmpresa AND IdEscenario IS NOT NULL", cn);
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

        /// <summary>Utilidad neta acumulada de dbo.ModeloPYG a una fecha de corte (para el cruce opcional
        /// de cuadre con el balance) — null si la empresa/escenario no tiene datos del PYG en ese mes.</summary>
        private decimal? ObtenerUtilidadNetaAcumuladaPYG(int idEmpresa, byte idEscenario, int año, int mes)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    const string query = @"
                        SELECT SUM(ValorAcumulado) FROM dbo.ModeloPYG
                        WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario
                          AND Año = @Año AND Mes = @Mes AND Descripcion = @Descripcion";
                    var cmd = new SqlCommand(query, cn);
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.Parameters.AddWithValue("@Año", año);
                    cmd.Parameters.AddWithValue("@Mes", mes);
                    cmd.Parameters.AddWithValue("@Descripcion", DescUtilidadNetaPYG);
                    cn.Open();
                    var resultado = cmd.ExecuteScalar();
                    return resultado == null || resultado == DBNull.Value ? (decimal?)null : Convert.ToDecimal(resultado);
                }
            }
            catch { return null; }
        }

        // ===== "Fotos" (saldo de todas las cuentas en un mes exacto) — el balance no admite "rango sumado" =====

        private class Foto
        {
            public List<BalanceFilaModelo> Filas;
            public bool Vacio => Filas.Count == 0;
        }

        private static Foto FotoDe(List<BalanceFilaModelo> filas, int año, int mes)
        {
            return new Foto { Filas = filas.Where(f => f.Año == año && f.Mes == mes).OrderBy(f => f.Ord).ToList() };
        }

        /// <summary>Índice absoluto de mes (año*12 + mes-1) — permite rangos que cruzan años.</summary>
        private static int Clave(int año, int mes) => año * 12 + (mes - 1);

        private static decimal SumaLinea(List<BalanceFilaModelo> filas, string descripcion)
        {
            return filas.Where(f => string.Equals(f.Descripcion, descripcion, StringComparison.OrdinalIgnoreCase)).Sum(f => f.Valor);
        }

        /// <summary>Ver BalanceFilaReporte.EsPasivo: clasifica por el primer dígito del PUC cuando la
        /// línea tiene cuenta propia; para subtotales sin cuenta propia (Total Pasivo, Pasivo Corriente…)
        /// cae a buscar "pasivo" en la descripción, cuidando no confundir con "patrimonio".</summary>
        private static bool EsLineaDePasivo(string cuentaPUC, string descripcion)
        {
            if (!string.IsNullOrWhiteSpace(cuentaPUC)) return cuentaPUC.TrimStart().StartsWith("2");
            return descripcion.IndexOf("pasivo", StringComparison.OrdinalIgnoreCase) >= 0
                && descripcion.IndexOf("patrimonio", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static decimal? Variacion(decimal actual, decimal? baseComparacion)
        {
            return (baseComparacion.HasValue && baseComparacion.Value != 0)
                ? (actual - baseComparacion.Value) / Math.Abs(baseComparacion.Value) : (decimal?)null;
        }

        /// <summary>(Año,Mes) del comparativo elegido, a partir del mes de corte.</summary>
        private static (int año, int mes) ComparativoAnioMes(string comparar, int añoCorte, int mesCorte)
        {
            if (comparar == BalanceComparar.MesAnterior)
            {
                int k = Clave(añoCorte, mesCorte) - 1;
                return (k / 12, k % 12 + 1);
            }
            if (comparar == BalanceComparar.MismoMesAnioAnterior)
                return (añoCorte - 1, mesCorte);

            return (añoCorte - 1, 12); // CierreAnterior (default)
        }

        private static string NormalizarAgrupar(string agrupar)
        {
            if (string.Equals(agrupar, "Trimestre", StringComparison.OrdinalIgnoreCase)) return "Trimestre";
            if (string.Equals(agrupar, "Anio", StringComparison.OrdinalIgnoreCase)) return "Anio";
            return "Mes";
        }

        /// <summary>Arma el reporte completo (filas, KPIs, estructura, tendencias, cuadre) para el rango
        /// continuo [AnioDesde/MesDesde, AnioHasta/MesHasta] — puede cruzar años, pero el corte real para
        /// KPIs/tabla/cuadre es siempre AnioHasta/MesHasta; el rango solo alimenta las tendencias.</summary>
        public BalanceReporteViewModel ConstruirReporte(FiltrosBalance filtros)
        {
            int mesD = Math.Min(12, Math.Max(1, filtros.MesDesde)), mesH = Math.Min(12, Math.Max(1, filtros.MesHasta));
            int kD = Clave(filtros.AnioDesde, mesD), kH = Clave(filtros.AnioHasta, mesH);
            if (kD > kH) { int tmp = kD; kD = kH; kH = tmp; }
            int añoD = kD / 12, añoH = kH / 12, mesHasta = kH % 12 + 1, mesDesde = kD % 12 + 1;
            string comparar = BalanceComparar.Normalizar(filtros.Comparar);
            string agrupar = NormalizarAgrupar(filtros.Agrupar);
            bool conEscenario = filtros.IdEscenarioComparar.HasValue && filtros.IdEscenarioComparar.Value != filtros.IdEscenario;

            var resultado = new BalanceReporteViewModel
            {
                IdEmpresa = filtros.IdEmpresa,
                IdEscenario = filtros.IdEscenario,
                AnioDesde = añoD,
                MesDesde = mesDesde,
                AnioHasta = añoH,
                MesHasta = mesHasta,
                Comparar = comparar,
                Agrupar = agrupar,
                IdEscenarioComparar = conEscenario ? filtros.IdEscenarioComparar : null
            };

            var (añoComp, mesComp) = ComparativoAnioMes(comparar, añoH, mesHasta);
            int añoMinFetch = Math.Min(añoD, añoComp);

            var filas = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenario, añoMinFetch, añoH);
            var fotoCorte = FotoDe(filas, añoH, mesHasta);
            if (fotoCorte.Vacio)
            {
                resultado.SinDatos = true;
                return resultado;
            }

            var fotoComp = FotoDe(filas, añoComp, mesComp);
            resultado.HayComparativo = !fotoComp.Vacio;

            List<BalanceFilaModelo> filasEsc = null;
            Foto fotoEsc = null;
            if (conEscenario)
            {
                filasEsc = ObtenerFilas(filtros.IdEmpresa, filtros.IdEscenarioComparar.Value, añoD, añoH);
                var f = FotoDe(filasEsc, añoH, mesHasta);
                if (!f.Vacio) fotoEsc = f;
                else filasEsc = null;
            }
            resultado.HayEscenarioComparar = fotoEsc != null;

            decimal activoTotal = SumaLinea(fotoCorte.Filas, DescActivoTotal);
            decimal pasivoTotal = SumaLinea(fotoCorte.Filas, DescPasivoTotal);
            decimal patrimonio = SumaLinea(fotoCorte.Filas, DescPatrimonioTotal);

            resultado.ActivoCorriente = SumaLinea(fotoCorte.Filas, DescActivoCorriente);
            resultado.ActivoNoCorriente = SumaLinea(fotoCorte.Filas, DescActivoNoCorriente);
            resultado.PasivoCorriente = SumaLinea(fotoCorte.Filas, DescPasivoCorriente);
            resultado.PasivoNoCorriente = SumaLinea(fotoCorte.Filas, DescPasivoNoCorriente);
            resultado.Patrimonio = patrimonio;

            resultado.DiferenciaCuadre = activoTotal - (pasivoTotal + patrimonio);
            resultado.CuadraBalance = Math.Abs(resultado.DiferenciaCuadre) <= ToleranciaCuadre;

            // Cruce opcional con el PYG: solo se muestra si el balance trae una línea reconocible para
            // el resultado del ejercicio Y el PYG tiene datos en el mismo mes — ver constantes al inicio.
            decimal? resultadoEjercicioBalance = fotoCorte.Filas.Any(f => string.Equals(f.Descripcion, DescResultadoEjercicio, StringComparison.OrdinalIgnoreCase))
                ? SumaLinea(fotoCorte.Filas, DescResultadoEjercicio) : (decimal?)null;
            decimal? utilidadNetaPYG = resultadoEjercicioBalance.HasValue
                ? ObtenerUtilidadNetaAcumuladaPYG(filtros.IdEmpresa, filtros.IdEscenario, añoH, mesHasta) : null;
            if (resultadoEjercicioBalance.HasValue && utilidadNetaPYG.HasValue)
            {
                resultado.HayComparacionPYG = true;
                resultado.DiferenciaConPYG = utilidadNetaPYG.Value - resultadoEjercicioBalance.Value;
                resultado.CuadraConPYG = Math.Abs(resultado.DiferenciaConPYG) <= ToleranciaCuadre;
            }

            // ===== Filas de la tabla (análisis vertical + horizontal) =====
            foreach (var f in fotoCorte.Filas)
            {
                var fila = new BalanceFilaReporte
                {
                    Orden = f.Ord,
                    Descripcion = f.Descripcion,
                    EsSubtotal = f.EsSubtotal,
                    EsPasivo = EsLineaDePasivo(f.CuentaPUC, f.Descripcion),
                    SaldoCorte = f.Valor,
                    PctVertical = activoTotal != 0 ? f.Valor / activoTotal : (decimal?)null
                };

                if (resultado.HayComparativo)
                {
                    fila.SaldoComparativo = SumaLinea(fotoComp.Filas, f.Descripcion);
                    fila.VariacionPorcentual = Variacion(f.Valor, fila.SaldoComparativo);
                }

                if (fotoEsc != null)
                {
                    decimal saldoEsc = SumaLinea(fotoEsc.Filas, f.Descripcion);
                    fila.SaldoEscenario = saldoEsc;
                    fila.VariacionEscenarioPorcentual = Variacion(f.Valor, saldoEsc);
                }

                resultado.Filas.Add(fila);
            }

            // ===== KPIs =====
            decimal? activoTotalComp = resultado.HayComparativo ? SumaLinea(fotoComp.Filas, DescActivoTotal) : (decimal?)null;
            resultado.Kpis.Add(ArmarKpi("ActivoTotal", activoTotal, activoTotalComp, false));
            resultado.Kpis.Add(ArmarKpiCalculado("CapitalTrabajo", fotoCorte, resultado.HayComparativo ? fotoComp : null, CalcCapitalTrabajo));
            resultado.Kpis.Add(ArmarKpiCalculado("RazonCorriente", fotoCorte, resultado.HayComparativo ? fotoComp : null, CalcRazonCorriente));
            resultado.Kpis.Add(ArmarKpiCalculado("Endeudamiento", fotoCorte, resultado.HayComparativo ? fotoComp : null, CalcEndeudamiento));

            // ===== Tendencias (foto de fin de cada mes/trimestre/año dentro del rango elegido) =====
            var filasTendencia = filas.Where(f => f.Año >= añoD && f.Año <= añoH).ToList();
            bool variosAños = añoD != añoH;
            resultado.TendenciaCapitalTrabajo = ArmarTendencia(filasTendencia, filasEsc, agrupar, kD, kH, variosAños, CalcCapitalTrabajo);
            resultado.TendenciaRazonCorriente = ArmarTendencia(filasTendencia, filasEsc, agrupar, kD, kH, variosAños, CalcRazonCorriente);
            resultado.TendenciaEndeudamiento = ArmarTendencia(filasTendencia, filasEsc, agrupar, kD, kH, variosAños, CalcEndeudamiento);

            return resultado;
        }

        // ===== Fórmulas de los 3 indicadores usados en KPIs y tendencias (null = "No aplica") =====

        private static decimal? CalcCapitalTrabajo(List<BalanceFilaModelo> foto)
        {
            return SumaLinea(foto, DescActivoCorriente) - SumaLinea(foto, DescPasivoCorriente);
        }

        private static decimal? CalcRazonCorriente(List<BalanceFilaModelo> foto)
        {
            decimal pasivoCorriente = SumaLinea(foto, DescPasivoCorriente);
            return pasivoCorriente != 0 ? SumaLinea(foto, DescActivoCorriente) / pasivoCorriente : (decimal?)null;
        }

        private static decimal? CalcEndeudamiento(List<BalanceFilaModelo> foto)
        {
            decimal activoTotal = SumaLinea(foto, DescActivoTotal);
            return activoTotal != 0 ? SumaLinea(foto, DescPasivoTotal) / activoTotal : (decimal?)null;
        }

        private static BalanceKpi ArmarKpi(string codigo, decimal valor, decimal? comparativo, bool noAplica)
        {
            var kpi = new BalanceKpi { Codigo = codigo, Valor = valor, NoAplica = noAplica };
            if (comparativo.HasValue)
            {
                kpi.ValorComparativo = comparativo.Value;
                kpi.VariacionPorcentual = Variacion(valor, comparativo);
            }
            return kpi;
        }

        private static BalanceKpi ArmarKpiCalculado(string codigo, Foto corte, Foto comparativo, Func<List<BalanceFilaModelo>, decimal?> calculador)
        {
            decimal? valor = calculador(corte.Filas);
            var kpi = new BalanceKpi { Codigo = codigo };
            if (!valor.HasValue) { kpi.NoAplica = true; return kpi; }

            kpi.Valor = valor.Value;
            if (comparativo != null)
            {
                decimal? comp = calculador(comparativo.Filas);
                if (comp.HasValue)
                {
                    kpi.ValorComparativo = comp.Value;
                    kpi.VariacionPorcentual = Variacion(valor.Value, comp);
                }
            }
            return kpi;
        }

        /// <summary>Serie de tendencia de un indicador (Capital de trabajo/Razón corriente/Endeudamiento):
        /// a diferencia de PYG (que SUMA los meses del grupo) cada punto es la FOTO del último mes con
        /// datos dentro del grupo (mes/trimestre/año) — el balance no se puede sumar mes a mes.</summary>
        private static List<BalancePuntoTendencia> ArmarTendencia(List<BalanceFilaModelo> filas, List<BalanceFilaModelo> filasEscenario,
            string agrupar, int kDesde, int kHasta, bool variosAños, Func<List<BalanceFilaModelo>, decimal?> calculador)
        {
            Func<BalanceFilaModelo, int> grupo;
            if (agrupar == "Trimestre") grupo = f => f.Año * 4 + (f.Mes - 1) / 3;
            else if (agrupar == "Anio") grupo = f => f.Año;
            else grupo = f => Clave(f.Año, f.Mes);

            var gruposEsc = filasEscenario?.GroupBy(grupo).ToDictionary(g => g.Key, g => g.ToList());

            return filas
                .GroupBy(grupo)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var lista = g.ToList();
                    int mesRepresentativo = lista.Max(f => Clave(f.Año, f.Mes));
                    var foto = lista.Where(f => Clave(f.Año, f.Mes) == mesRepresentativo).ToList();
                    var primera = foto.First();
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

                    decimal? valorEsc = null;
                    if (gruposEsc != null && gruposEsc.TryGetValue(g.Key, out var listaEsc) && listaEsc.Count > 0)
                    {
                        int mesRepEsc = listaEsc.Max(f => Clave(f.Año, f.Mes));
                        valorEsc = calculador(listaEsc.Where(f => Clave(f.Año, f.Mes) == mesRepEsc).ToList());
                    }

                    return new BalancePuntoTendencia
                    {
                        Año = año,
                        Mes = mes,
                        Etiqueta = etiqueta,
                        Valor = calculador(foto) ?? 0m,
                        ValorEscenario = valorEsc,
                        EnRango = g.Any(f => { int k = Clave(f.Año, f.Mes); return k >= kDesde && k <= kHasta; })
                    };
                })
                .ToList();
        }
    }
}
