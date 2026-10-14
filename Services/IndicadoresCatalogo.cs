using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>Datos mensuales ya indexados (año*12 + mes-1) que usan los indicadores: PYG y Flujo de caja (flujos del mes) y
    /// Balance (saldo del mes). Las llaves son "descripción|índice" — una misma descripción se suma si el modelo trae varias filas.</summary>
    public class IndicadoresDatos
    {
        public Dictionary<string, decimal> Pyg = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> Bal = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, decimal> Fcj = new Dictionary<int, decimal>();      // Flujo de caja libre operativo por mes
        public HashSet<int> MesesPyg = new HashSet<int>();                        // meses con ingresos en el PYG
        public HashSet<int> MesesBal = new HashSet<int>();                        // meses con activo en el Balance
        public HashSet<int> MesesFcj = new HashSet<int>();                        // meses con flujo libre en el Flujo de caja

        public static int Indice(int año, int mes) { return año * 12 + (mes - 1); }
    }

    /// <summary>Resultado de calcular un indicador en un mes. Si Valor es null, Motivo explica por qué (y NivelForzado permite
    /// marcar como alerta un caso que no tiene número, p. ej. deuda neta sobre un EBITDA negativo).</summary>
    public class IndRes
    {
        public decimal? Valor;
        public string Motivo;
        public string NivelForzado;

        public static IndRes Ok(decimal? v) { return v.HasValue ? new IndRes { Valor = v } : new IndRes { Motivo = "divisor_cero" }; }
        public static IndRes No(string motivo) { return new IndRes { Motivo = motivo }; }
        public static IndRes NoConNivel(string motivo, string nivel) { return new IndRes { Motivo = motivo, NivelForzado = nivel }; }
    }

    /// <summary>Contexto de cálculo de un mes (corte o mes histórico): ventana de 12 meses de flujos y saldos del mes.</summary>
    public class IndCtx
    {
        private readonly IndicadoresDatos _d;
        public readonly int Idx;

        /// <summary>Meses con datos de PYG dentro de la ventana de 12 meses que termina en este mes.</summary>
        public readonly int MesesPyg;
        public readonly int MesesFcj;

        public IndCtx(IndicadoresDatos datos, int idx)
        {
            _d = datos; Idx = idx;
            for (int i = idx - 11; i <= idx; i++)
            {
                if (datos.MesesPyg.Contains(i)) MesesPyg++;
                if (datos.MesesFcj.Contains(i)) MesesFcj++;
            }
        }

        public bool HayBalance { get { return _d.MesesBal.Contains(Idx); } }
        public IndCtx Previo { get { return new IndCtx(_d, Idx - 12); } }

        /// <summary>Suma de los 12 meses que terminan en este mes (null si no hay ningún mes con datos).</summary>
        public decimal? Ltm(string desc)
        {
            if (MesesPyg == 0) return null;
            decimal s = 0m;
            for (int i = Idx - 11; i <= Idx; i++) { decimal v; if (_d.Pyg.TryGetValue(desc + "|" + i, out v)) s += v; }
            return s;
        }

        public decimal? Ltm(params string[] descs)
        {
            decimal? s = null;
            foreach (var d in descs) { var x = Ltm(d); if (x.HasValue) s = (s ?? 0m) + x.Value; }
            return s;
        }

        /// <summary>EBITDA: Utilidad operacional + depreciaciones y amortizaciones (no existe una línea EBITDA en el modelo; misma
        /// regla que el informe de PYG).</summary>
        public decimal? Ebitda()
        {
            var op = Ltm(IndicadoresCatalogo.UtilOperacional);
            if (!op.HasValue) return null;
            return op.Value + (Ltm(IndicadoresCatalogo.DeprecCosto, IndicadoresCatalogo.AmortCosto, IndicadoresCatalogo.DeprecGasto, IndicadoresCatalogo.AmortGasto) ?? 0m);
        }

        public decimal? FlujoLibre()
        {
            if (MesesFcj == 0) return null;
            decimal s = 0m;
            for (int i = Idx - 11; i <= Idx; i++) { decimal v; if (_d.Fcj.TryGetValue(i, out v)) s += v; }
            return s;
        }

        /// <summary>Saldo del Balance en este mes (null si el mes no tiene balance).</summary>
        public decimal? Saldo(string desc)
        {
            if (!HayBalance) return null;
            decimal v;
            return _d.Bal.TryGetValue(desc + "|" + Idx, out v) ? v : 0m;
        }

        public decimal? DeudaFinanciera()
        {
            var a = Saldo(IndicadoresCatalogo.ObligFinCorriente);
            var b = Saldo(IndicadoresCatalogo.ObligFinNoCorriente);
            if (!a.HasValue || !b.HasValue) return null;
            return a.Value + b.Value;
        }

        public decimal? CapitalTrabajo()
        {
            var ac = Saldo(IndicadoresCatalogo.ActivoCorriente); var pc = Saldo(IndicadoresCatalogo.PasivoCorriente);
            return ac.HasValue && pc.HasValue ? ac.Value - pc.Value : (decimal?)null;
        }
    }

    public class IndicadorDef
    {
        public string Codigo;
        public string Familia;
        public string Formato;                    // pct | x | dias | moneda
        public bool? MasEsMejor;                  // null = sin semáforo por rango
        public decimal[] Cortes;                  // 3 cortes: verde / amarillo / naranja
        public bool NecesitaLtmCompleto;          // días, rotaciones, retornos, crecimiento: no se calculan con menos de 12 meses
        public bool UsaFlujoCaja;
        public bool UsaBalance;
        public Func<IndCtx, IndRes> Calc;
        public Func<decimal, string> NivelPersonalizado;   // p. ej. capital de trabajo: positivo = verde, negativo = rojo
    }

    /// <summary>Catálogo de los indicadores v1 (26): fórmula, formato, dirección y cortes del semáforo. Los cortes son referencias
    /// GENERALES de mercado (dependen de la industria y del país) y están pensados para ajustarse con datos colombianos; para
    /// los de "más es mejor": ≥ corte1 verde, ≥ corte2 amarillo, ≥ corte3 naranja, menos = rojo (al revés si "menos es mejor").
    /// Los textos de cada indicador viven en los .resx (claves Ind_{Codigo}_*).</summary>
    public static class IndicadoresCatalogo
    {
        // Líneas del PYG
        public const string Ingresos = "Ingresos";
        public const string Cmv = "CMV";
        public const string UtilBruta = "Utilidad bruta";
        public const string GastosOperacionales = "Gastos operacionales";
        public const string UtilOperacional = "Utilidad operacional";
        public const string GastosFinancieros = "Gastos financieros";
        public const string UtilNeta = "Utilidad neta";
        public const string DeprecCosto = "Depreciación costo", AmortCosto = "Amortización costo",
                            DeprecGasto = "Depreciación gasto", AmortGasto = "Amortización gasto";
        // Líneas del Balance
        public const string Caja = "Caja y bancos";
        public const string CuentasPorCobrar = "Cuentas por cobrar";
        public const string Inventarios = "Inventarios";
        public const string Proveedores = "Proveedores corrientes";
        public const string ActivoCorriente = "Total activo corriente";
        public const string PasivoCorriente = "Total pasivo corriente";
        public const string ActivoTotal = "Total Activo";
        public const string PasivoTotal = "Total pasivo";
        public const string Patrimonio = "Total patrimonio";
        public const string ObligFinCorriente = "Total obligaciones financieras corrientes";
        public const string ObligFinNoCorriente = "Obligaciones financieras no corrientes";
        // Flujo de caja (Ord = Id de dbo.Rel_FlujoCaja): 19 = Subtotal Flujo de caja libre Operativo
        public const int FlujoCajaLibreOperativoOrd = 19;

        private const string Rent = "Rentabilidad", Ret = "Retornos", Efi = "Eficiencia", Liq = "Liquidez", Sol = "Solvencia", Cre = "Crecimiento";
        public static readonly string[] Familias = { Rent, Ret, Efi, Liq, Sol, Cre };

        private static decimal? Div(decimal? a, decimal? b)
        {
            if (!a.HasValue || !b.HasValue || b.Value == 0m) return null;
            return a.Value / b.Value;
        }

        private static decimal? DivPos(decimal? a, decimal? b)
        {
            if (!a.HasValue || !b.HasValue || b.Value <= 0m) return null;
            return a.Value / b.Value;
        }

        private static IndicadorDef D(string cod, string fam, string fmt, bool? masEsMejor, decimal c1, decimal c2, decimal c3,
            Func<IndCtx, IndRes> calc, bool ltmCompleto = false, bool flujoCaja = false, bool balance = false)
        {
            return new IndicadorDef
            {
                Codigo = cod, Familia = fam, Formato = fmt, MasEsMejor = masEsMejor, Cortes = new[] { c1, c2, c3 },
                Calc = calc, NecesitaLtmCompleto = ltmCompleto, UsaFlujoCaja = flujoCaja, UsaBalance = balance
            };
        }

        private static IndicadorDef WithNivel(IndicadorDef d, Func<decimal, string> nivel) { d.NivelPersonalizado = nivel; return d; }

        public static readonly List<IndicadorDef> Todos = new List<IndicadorDef>
        {
            // ===== Rentabilidad (12 meses móviles) =====
            D("MargenBruto", Rent, "pct", true, 0.35m, 0.25m, 0.15m, c => IndRes.Ok(Div(c.Ltm(UtilBruta), c.Ltm(Ingresos)))),
            D("MargenEbitda", Rent, "pct", true, 0.15m, 0.10m, 0.05m, c => IndRes.Ok(Div(c.Ebitda(), c.Ltm(Ingresos)))),
            D("MargenOperacional", Rent, "pct", true, 0.12m, 0.08m, 0.03m, c => IndRes.Ok(Div(c.Ltm(UtilOperacional), c.Ltm(Ingresos)))),
            D("MargenNeto", Rent, "pct", true, 0.08m, 0.04m, 0.01m, c => IndRes.Ok(Div(c.Ltm(UtilNeta), c.Ltm(Ingresos)))),
            D("MargenFlujoLibre", Rent, "pct", true, 0.10m, 0.05m, 0.0m, c => IndRes.Ok(Div(c.FlujoLibre(), c.Ltm(Ingresos))), flujoCaja: true),
            D("ConversionCaja", Rent, "pct", true, 0.70m, 0.50m, 0.25m, c =>
            {
                var e = c.Ebitda();
                if (e.HasValue && e.Value <= 0m) return IndRes.No("ebitda_no_positivo");
                return IndRes.Ok(Div(c.FlujoLibre(), e));
            }, flujoCaja: true),

            // ===== Retornos =====
            D("Roa", Ret, "pct", true, 0.08m, 0.05m, 0.02m, c => IndRes.Ok(Div(c.Ltm(UtilNeta), c.Saldo(ActivoTotal))), ltmCompleto: true, balance: true),
            D("Roe", Ret, "pct", true, 0.15m, 0.10m, 0.05m, c => IndRes.Ok(DivPos(c.Ltm(UtilNeta), c.Saldo(Patrimonio))), ltmCompleto: true, balance: true),

            // ===== Eficiencia =====
            D("Dso", Efi, "dias", false, 30m, 45m, 60m, c => IndRes.Ok(Div(c.Saldo(CuentasPorCobrar), c.Ltm(Ingresos)) * 365m), ltmCompleto: true, balance: true),
            D("Dio", Efi, "dias", false, 45m, 75m, 120m, c => IndRes.Ok(Div(c.Saldo(Inventarios), c.Ltm(Cmv)) * 365m), ltmCompleto: true, balance: true),
            D("Dpo", Efi, "dias", true, 60m, 45m, 30m, c => IndRes.Ok(Div(c.Saldo(Proveedores), c.Ltm(Cmv)) * 365m), ltmCompleto: true, balance: true),
            D("CicloEfectivo", Efi, "dias", false, 45m, 90m, 150m, c =>
            {
                var dso = Div(c.Saldo(CuentasPorCobrar), c.Ltm(Ingresos)) * 365m;
                var dio = Div(c.Saldo(Inventarios), c.Ltm(Cmv)) * 365m;
                var dpo = Div(c.Saldo(Proveedores), c.Ltm(Cmv)) * 365m;
                if (!dso.HasValue || !dio.HasValue || !dpo.HasValue) return IndRes.No("divisor_cero");
                return IndRes.Ok(dso.Value + dio.Value - dpo.Value);
            }, ltmCompleto: true, balance: true),
            D("RotacionActivos", Efi, "x", true, 1.5m, 1.0m, 0.5m, c => IndRes.Ok(Div(c.Ltm(Ingresos), c.Saldo(ActivoTotal))), ltmCompleto: true, balance: true),
            D("RotacionInventario", Efi, "x", true, 8m, 5m, 3m, c => IndRes.Ok(Div(c.Ltm(Cmv), c.Saldo(Inventarios))), ltmCompleto: true, balance: true),
            D("GastosOperacionales", Efi, "pct", false, 0.15m, 0.25m, 0.35m, c => IndRes.Ok(Div(c.Ltm(GastosOperacionales), c.Ltm(Ingresos)))),
            WithNivel(D("CapitalTrabajoIngresos", Efi, "pct", false, 0.10m, 0.20m, 0.30m, c => IndRes.Ok(Div(c.CapitalTrabajo(), c.Ltm(Ingresos))), ltmCompleto: true, balance: true),
                v => v < 0m ? IndicadorNivel.Rojo : v <= 0.10m ? IndicadorNivel.Verde : v <= 0.20m ? IndicadorNivel.Amarillo : v <= 0.30m ? IndicadorNivel.Naranja : IndicadorNivel.Rojo),   // negativo = el corto plazo no está cubierto

            // ===== Liquidez (saldos del mes de corte) =====
            D("RazonCorriente", Liq, "x", true, 1.5m, 1.2m, 1.0m, c => IndRes.Ok(Div(c.Saldo(ActivoCorriente), c.Saldo(PasivoCorriente))), balance: true),
            D("PruebaAcida", Liq, "x", true, 1.0m, 0.8m, 0.6m, c =>
            {
                var ac = c.Saldo(ActivoCorriente); var inv = c.Saldo(Inventarios);
                return IndRes.Ok(Div(ac.HasValue && inv.HasValue ? ac.Value - inv.Value : (decimal?)null, c.Saldo(PasivoCorriente)));
            }, balance: true),
            D("RazonEfectivo", Liq, "x", true, 0.30m, 0.15m, 0.05m, c => IndRes.Ok(Div(c.Saldo(Caja), c.Saldo(PasivoCorriente))), balance: true),
            new IndicadorDef
            {
                Codigo = "CapitalTrabajo", Familia = Liq, Formato = "moneda", MasEsMejor = true, Cortes = new[] { 0m, 0m, 0m }, UsaBalance = true,
                Calc = c => { var v = c.CapitalTrabajo(); return v.HasValue ? IndRes.Ok(v) : IndRes.No("sin_dato"); },
                NivelPersonalizado = v => v > 0m ? IndicadorNivel.Verde : IndicadorNivel.Rojo
            },

            // ===== Solvencia =====
            D("Endeudamiento", Sol, "pct", false, 0.40m, 0.55m, 0.70m, c => IndRes.Ok(Div(c.Saldo(PasivoTotal), c.Saldo(ActivoTotal))), balance: true),
            D("DeudaNetaEbitda", Sol, "x", false, 2.0m, 3.0m, 4.0m, c =>
            {
                var e = c.Ebitda(); var deuda = c.DeudaFinanciera(); var caja = c.Saldo(Caja);
                if (!e.HasValue || !deuda.HasValue || !caja.HasValue) return IndRes.No("sin_dato");
                decimal neta = deuda.Value - caja.Value;
                if (neta <= 0m) return IndRes.Ok(0m);                                   // más caja que deuda
                if (e.Value <= 0m) return IndRes.NoConNivel("ebitda_no_positivo", IndicadorNivel.Rojo);   // debe y no genera EBITDA
                return IndRes.Ok(neta / e.Value);
            }, ltmCompleto: true, balance: true),
            D("CoberturaIntereses", Sol, "x", true, 4.0m, 2.5m, 1.5m, c =>
            {
                var gf = c.Ltm(GastosFinancieros); var e = c.Ebitda();
                if (!gf.HasValue || !e.HasValue) return IndRes.No("sin_dato");
                if (gf.Value <= 0m) return IndRes.NoConNivel("sin_intereses", IndicadorNivel.Verde);
                return IndRes.Ok(e.Value / gf.Value);
            }),
            D("CostoDeuda", Sol, "pct", false, 0.10m, 0.14m, 0.18m, c =>
            {
                var deuda = c.DeudaFinanciera();
                if (!deuda.HasValue || deuda.Value <= 0m) return IndRes.No("sin_dato");
                var gf = c.Ltm(GastosFinancieros);
                return gf.HasValue ? IndRes.Ok(gf.Value / deuda.Value) : IndRes.No("sin_dato");
            }, ltmCompleto: true, balance: true),
            D("ApalancamientoFinanciero", Sol, "x", false, 2.0m, 3.0m, 4.0m, c => IndRes.Ok(DivPos(c.Saldo(ActivoTotal), c.Saldo(Patrimonio))), balance: true),

            // ===== Crecimiento (12 meses contra los 12 meses anteriores) =====
            D("CrecimientoIngresos", Cre, "pct", true, 0.10m, 0.03m, 0.0m, c =>
            {
                var act = c.Ltm(Ingresos); var prev = c.Previo;
                if (prev.MesesPyg < 12) return IndRes.No("sin_comparativo");
                var ant = prev.Ltm(Ingresos);
                if (!act.HasValue || !ant.HasValue || ant.Value <= 0m) return IndRes.No("divisor_cero");
                return IndRes.Ok(act.Value / ant.Value - 1m);
            }, ltmCompleto: true),
            D("CrecimientoEbitda", Cre, "pct", true, 0.10m, 0.0m, -0.10m, c =>
            {
                var act = c.Ebitda(); var prev = c.Previo;
                if (prev.MesesPyg < 12) return IndRes.No("sin_comparativo");
                var ant = prev.Ebitda();
                if (!act.HasValue || !ant.HasValue || ant.Value <= 0m) return IndRes.No("divisor_cero");
                return IndRes.Ok(act.Value / ant.Value - 1m);
            }, ltmCompleto: true),
        };

        /// <summary>Nivel del semáforo para un valor (verde, amarillo, naranja o rojo; gris si no hay dirección).</summary>
        public static string Nivel(IndicadorDef d, decimal v)
        {
            if (d.NivelPersonalizado != null) return d.NivelPersonalizado(v);
            if (!d.MasEsMejor.HasValue) return IndicadorNivel.Gris;
            var c = d.Cortes;
            if (d.MasEsMejor.Value)
                return v >= c[0] ? IndicadorNivel.Verde : v >= c[1] ? IndicadorNivel.Amarillo : v >= c[2] ? IndicadorNivel.Naranja : IndicadorNivel.Rojo;
            return v <= c[0] ? IndicadorNivel.Verde : v <= c[1] ? IndicadorNivel.Amarillo : v <= c[2] ? IndicadorNivel.Naranja : IndicadorNivel.Rojo;
        }

        public static int Severidad(string nivel)
        {
            switch (nivel) { case IndicadorNivel.Rojo: return 3; case IndicadorNivel.Naranja: return 2; case IndicadorNivel.Amarillo: return 1; default: return 0; }
        }
    }
}
