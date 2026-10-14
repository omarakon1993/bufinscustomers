using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe de indicadores: calcula ~26 indicadores financieros (rentabilidad, retornos, eficiencia, liquidez, solvencia y
    /// crecimiento) con semáforo, a partir de las tablas ya materializadas dbo.ModeloPYG, dbo.ModeloBalance y
    /// dbo.ModeloFlujoCaja (Ord 19 = flujo de caja libre operativo). No ejecuta ningún SP.
    ///
    /// Los flujos (PYG, Flujo de caja) se miden en los 12 meses que terminan en el mes de corte (LTM) y los saldos (Balance)
    /// son los del mes de corte. Con menos de 12 meses de historia los indicadores que lo necesitan (días, rotaciones, retornos,
    /// crecimiento) quedan "parciales" — nunca se anualiza. Se lee desde 2 años antes del rango para poder calcular el LTM de
    /// los primeros meses de la tendencia y el crecimiento contra los 12 meses anteriores.
    /// </summary>
    public class InformeIndicadoresService : BaseService
    {
        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        /// <summary>Máximo de puntos de la mini-tendencia (los más recientes) para no inflar la respuesta.</summary>
        private const int MaxPuntosTendencia = 36;

        // ===== Meses con datos =====

        /// <summary>Meses con flujos reales (ingresos en el PYG), en orden cronológico: alimenta el slider. Se usa el PYG como base porque
        /// los indicadores de 12 meses necesitan sus flujos; un mes que solo tenga Balance dejaría casi todo "parcial".</summary>
        public List<IndicadorMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            var meses = new List<IndicadorMesDisponible>();
            using (var cn = new SqlConnection(CadenaConexion))
            {
                const string query = @"
                    SELECT DISTINCT Año, Mes FROM dbo.ModeloPYG
                    WHERE IdEmpresa = @IdEmpresa AND IdEscenario = @IdEscenario AND Descripcion = 'Ingresos' AND Valor <> 0";
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
                        meses.Add(new IndicadorMesDisponible { Anio = año, Mes = mes, Etiqueta = MesesAbrev[mes - 1] + " " + año });
                    }
                }
            }
            return meses.OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        // ===== Lectura de datos =====

        private IndicadoresDatos CargarDatos(int idEmpresa, byte idEscenario, int añoDesde, int añoHasta)
        {
            var d = new IndicadoresDatos();

            foreach (var f in new InformePYGService().ObtenerFilas(idEmpresa, idEscenario, añoDesde, añoHasta))
            {
                int idx = IndicadoresDatos.Indice(f.Año, f.Mes);
                string k = f.Descripcion + "|" + idx;
                decimal ya; d.Pyg.TryGetValue(k, out ya);
                d.Pyg[k] = ya + f.Valor;
                if (string.Equals(f.Descripcion, IndicadoresCatalogo.Ingresos, StringComparison.OrdinalIgnoreCase) && f.Valor != 0m)
                    d.MesesPyg.Add(idx);
            }

            foreach (var f in new InformeBalanceService().ObtenerFilas(idEmpresa, idEscenario, añoDesde, añoHasta))
            {
                int idx = IndicadoresDatos.Indice(f.Año, f.Mes);
                string k = f.Descripcion + "|" + idx;
                decimal ya; d.Bal.TryGetValue(k, out ya);
                d.Bal[k] = ya + f.Valor;
                if (string.Equals(f.Descripcion, IndicadoresCatalogo.ActivoTotal, StringComparison.OrdinalIgnoreCase) && f.Valor != 0m)
                    d.MesesBal.Add(idx);
            }

            foreach (var f in new InformeFlujoService(FlujoTipo.Caja).ObtenerFilas(idEmpresa, idEscenario, añoDesde, añoHasta))
            {
                if (f.Ord != IndicadoresCatalogo.FlujoCajaLibreOperativoOrd) continue;
                int idx = IndicadoresDatos.Indice(f.Año, f.Mes);
                decimal ya; d.Fcj.TryGetValue(idx, out ya);
                d.Fcj[idx] = ya + f.Valor;
                if (f.Valor != 0m) d.MesesFcj.Add(idx);
            }

            return d;
        }

        // ===== Reporte =====

        public IndicadoresReporteViewModel ConstruirReporte(FiltrosIndicadores filtros)
        {
            string comparar = IndicadoresComparar.Normalizar(filtros.Comparar);
            int idxCorte = IndicadoresDatos.Indice(filtros.AnioHasta, filtros.MesHasta);

            int idxComp;
            switch (comparar)
            {
                case IndicadoresComparar.MesAnterior: idxComp = idxCorte - 1; break;
                case IndicadoresComparar.MismoMesAnioAnterior: idxComp = idxCorte - 12; break;
                default: idxComp = IndicadoresDatos.Indice(filtros.AnioHasta - 1, 12); break;
            }

            int idxDesde = Math.Min(IndicadoresDatos.Indice(filtros.AnioDesde, filtros.MesDesde), idxCorte);
            int añoLectura = Math.Min(filtros.AnioDesde, Math.Min(filtros.AnioHasta, (idxComp / 12))) - 2;
            var datos = CargarDatos(filtros.IdEmpresa, filtros.IdEscenario, añoLectura, filtros.AnioHasta);

            var corte = new IndCtx(datos, idxCorte);
            var comp = new IndCtx(datos, idxComp);

            var r = new IndicadoresReporteViewModel
            {
                IdEmpresa = filtros.IdEmpresa, IdEscenario = filtros.IdEscenario,
                AnioDesde = idxDesde / 12, MesDesde = idxDesde % 12 + 1,
                AnioHasta = filtros.AnioHasta, MesHasta = filtros.MesHasta,
                Comparar = comparar, AnioComp = idxComp / 12, MesComp = idxComp % 12 + 1,
                HayBalance = corte.HayBalance,
                HayFlujoCaja = corte.MesesFcj > 0,
                MesesLtm = corte.MesesPyg,
                HayComparativo = datos.MesesPyg.Contains(idxComp) || datos.MesesBal.Contains(idxComp)
            };

            r.SinDatos = corte.MesesPyg == 0 && !corte.HayBalance;
            if (r.SinDatos) return r;

            // Meses de la tendencia: los que tienen datos dentro del rango (los más recientes si son muchos).
            var idxTendencia = new List<int>();
            for (int i = idxDesde; i <= idxCorte; i++)
                if (datos.MesesPyg.Contains(i) || datos.MesesBal.Contains(i)) idxTendencia.Add(i);
            if (idxTendencia.Count > MaxPuntosTendencia) idxTendencia = idxTendencia.Skip(idxTendencia.Count - MaxPuntosTendencia).ToList();

            foreach (var def in IndicadoresCatalogo.Todos)
            {
                var ev = Evaluar(def, corte);
                var iv = new IndicadorValor
                {
                    Codigo = def.Codigo, Familia = def.Familia, Formato = def.Formato, MasEsMejor = def.MasEsMejor, Cortes = def.Cortes,
                    Valor = ev.Valor, Nivel = ev.Nivel, NoAplica = ev.NoAplica, Parcial = ev.Parcial, Motivo = ev.Motivo, MesesLtm = ev.MesesLtm
                };

                if (r.HayComparativo)
                {
                    var evc = Evaluar(def, comp);
                    iv.NivelComp = evc.Nivel;
                    iv.ValorComp = evc.Valor;
                    if (ev.Valor.HasValue && evc.Valor.HasValue)
                    {
                        iv.Variacion = ev.Valor.Value - evc.Valor.Value;
                        if (def.MasEsMejor.HasValue && Math.Abs(iv.Variacion.Value) > 0.0000001m)
                            iv.Favorable = (iv.Variacion.Value > 0m) == def.MasEsMejor.Value;
                    }
                }

                foreach (int i in idxTendencia)
                {
                    var evt = Evaluar(def, new IndCtx(datos, i));
                    iv.Tendencia.Add(new IndicadorPunto { Anio = i / 12, Mes = i % 12 + 1, Valor = evt.Valor, Nivel = evt.Nivel });
                }

                r.Indicadores.Add(iv);
            }

            foreach (var iv in r.Indicadores)
            {
                switch (iv.Nivel)
                {
                    case IndicadorNivel.Verde: r.TotalVerde++; break;
                    case IndicadorNivel.Amarillo: r.TotalAmarillo++; break;
                    case IndicadorNivel.Naranja: r.TotalNaranja++; break;
                    case IndicadorNivel.Rojo: r.TotalRojo++; break;
                    default: r.TotalGris++; break;
                }
            }

            // Los que más requieren atención: rojos y naranjas, el más grave primero y, a igual gravedad, los que empeoraron.
            r.Atencion = r.Indicadores
                .Where(i => i.Nivel == IndicadorNivel.Rojo || i.Nivel == IndicadorNivel.Naranja)
                .OrderByDescending(i => IndicadoresCatalogo.Severidad(i.Nivel))
                .ThenBy(i => i.Favorable == false ? 0 : 1)
                .Take(3).Select(i => i.Codigo).ToList();

            return r;
        }

        private class Evaluacion
        {
            public decimal? Valor; public string Nivel = IndicadorNivel.Gris; public bool NoAplica, Parcial; public string Motivo; public int MesesLtm;
        }

        private static Evaluacion Evaluar(IndicadorDef def, IndCtx ctx)
        {
            var e = new Evaluacion();
            e.MesesLtm = def.UsaFlujoCaja ? Math.Min(ctx.MesesPyg, ctx.MesesFcj) : ctx.MesesPyg;

            // Sin los datos del modelo que necesita el indicador no hay nada que calcular.
            if (def.UsaBalance && !ctx.HayBalance) { e.NoAplica = true; e.Motivo = "sin_dato"; return e; }
            if (def.UsaFlujoCaja && ctx.MesesFcj == 0) { e.NoAplica = true; e.Motivo = "sin_dato"; return e; }

            // Con menos de 12 meses, los indicadores que anualizan no se calculan ("parcial").
            if (def.NecesitaLtmCompleto && e.MesesLtm < 12)
            {
                e.NoAplica = true; e.Parcial = true; e.Motivo = "ltm_parcial";
                return e;
            }
            if (!def.NecesitaLtmCompleto && e.MesesLtm > 0 && e.MesesLtm < 12 && DependeDeFlujos(def)) e.Parcial = true;

            var res = def.Calc(ctx);
            if (!res.Valor.HasValue)
            {
                e.NoAplica = true; e.Motivo = res.Motivo ?? "divisor_cero";
                if (e.Motivo == "divisor_cero" && ctx.MesesPyg == 0 && !def.UsaBalance) e.Motivo = "sin_dato";
                if (res.NivelForzado != null) e.Nivel = res.NivelForzado;
                return e;
            }

            e.Valor = res.Valor;
            // Un indicador con pocos meses de flujo se muestra pero sin semáforo (gris): el rango no es comparable.
            e.Nivel = e.Parcial ? IndicadorNivel.Gris : IndicadoresCatalogo.Nivel(def, res.Valor.Value);
            if (e.Parcial) e.Motivo = "ltm_parcial";
            return e;
        }

        /// <summary>Indicadores que mezclan flujos de varios meses: con menos de 12 meses se muestran como "parciales".</summary>
        private static bool DependeDeFlujos(IndicadorDef def)
        {
            return def.Familia == "Rentabilidad" || def.Codigo == "GastosOperacionales" || def.Codigo == "CoberturaIntereses";
        }
    }
}
