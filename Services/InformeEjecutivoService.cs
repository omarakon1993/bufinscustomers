using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Helpers;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informes ejecutivos de un mes de corte: «PYG en una hoja», «¿Cuánta plata me queda?» (caja disponible) e «Informe para comité».
    /// No calcula cifras propias del modelo: reutiliza los servicios de PYG, Flujo de caja y Gerencia mensual (así coinciden con sus
    /// pantallas) y solo las reorganiza. Todo sale de las tablas Modelo*. Cada sección es independiente: si una falla, las demás se muestran.
    /// </summary>
    public class InformeEjecutivoService : BaseService
    {
        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        private static readonly string[] ComponentesEbitda =
            { "Utilidad operacional", "Depreciación costo", "Amortización costo", "Depreciación gasto", "Amortización gasto" };

        // ===== Meses de corte =====

        /// <summary>Meses con Ingresos en el PYG (PYG en una hoja e Informe para comité).</summary>
        public List<EjMesDisponible> MesesPyg(int idEmpresa, byte idEscenario)
        {
            return new InformePYGService().ObtenerMesesDisponibles(idEmpresa, idEscenario)
                .Select(m => new EjMesDisponible { Anio = m.Anio, Mes = m.Mes, Etiqueta = MesesAbrev[m.Mes - 1] + " " + m.Anio })
                .OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        /// <summary>Meses ya ejecutados del Flujo de caja (hasta el último mes real): la caja disponible solo tiene sentido con lo real.</summary>
        public List<EjMesDisponible> MesesCaja(int idEmpresa, byte idEscenario)
        {
            var svc = new InformeFlujoService(FlujoTipo.Caja);
            var ult = svc.ObtenerUltimoMesReal(idEmpresa, idEscenario);
            if (ult == null) return new List<EjMesDisponible>();
            int kUlt = ult.Anio * 12 + ult.Mes;
            return svc.ObtenerMesesDisponibles(idEmpresa, idEscenario)
                .Where(m => m.Anio * 12 + m.Mes <= kUlt)
                .Select(m => new EjMesDisponible { Anio = m.Anio, Mes = m.Mes, Etiqueta = m.Etiqueta })
                .OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        }

        // ===== PYG en una hoja =====

        public PygHojaReporte ConstruirPygHoja(FiltrosEjecutivo f)
        {
            var r = new PygHojaReporte { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = f.Anio, MesCorte = Math.Min(12, Math.Max(1, f.Mes)) };
            var filas = new InformePYGService().ObtenerFilas(f.IdEmpresa, f.IdEscenario, f.Anio - 1, f.Anio);
            var delAnio = filas.Where(x => x.Año == f.Anio && x.Mes >= 1 && x.Mes <= 12).ToList();
            if (delAnio.Count == 0) { r.SinDatos = true; return r; }

            int corte = r.MesCorte;
            // Líneas en el orden del modelo (Ord); el «Efecto neto en el Ebitda» es una línea técnica y no se muestra.
            var lineas = delAnio.GroupBy(x => x.Ord).OrderBy(g => g.Key)
                .Select(g => new { Ord = g.Key, Desc = (g.First().Descripcion ?? "").Trim(), Sub = g.First().EsSubtotal })
                .Where(l => l.Desc.IndexOf("Efecto neto", StringComparison.OrdinalIgnoreCase) < 0).ToList();

            Func<int, int, int, decimal> valorMes = (anio, ord, mes) =>
                filas.Where(x => x.Año == anio && x.Ord == ord && x.Mes == mes).Sum(x => x.Valor);
            Func<int, int, decimal> acumReal = (anio, ord) =>
                filas.Where(x => x.Año == anio && x.Ord == ord && x.Mes == corte).Sum(x => x.ValorAcumulado);
            Func<int, decimal> acumPpto = ord =>
                filas.Where(x => x.Año == f.Anio && x.Ord == ord && x.Mes == corte).Sum(x => x.ValorPresupuestoAcumulado);

            r.HayPresupuesto = delAnio.Any(x => x.Mes <= corte && x.ValorPresupuestoAcumulado != 0m);
            r.HayAnioAnterior = filas.Any(x => x.Año == f.Anio - 1 && x.Mes == corte && x.ValorAcumulado != 0m);

            PygHojaFila armar(int ord, string desc, bool sub)
            {
                var fila = new PygHojaFila { Ord = ord, Descripcion = desc, EsSubtotal = sub, EsGastoOCosto = InformePYGService.EsLineaGastoOCosto(desc) };
                for (int m = 1; m <= 12; m++) fila.Meses.Add(m <= corte ? (decimal?)valorMes(f.Anio, ord, m) : null);
                fila.Acumulado = acumReal(f.Anio, ord);
                fila.AcumPresupuesto = acumPpto(ord);
                if (r.HayAnioAnterior) fila.AcumAnioAnterior = acumReal(f.Anio - 1, ord);
                return fila;
            }

            foreach (var l in lineas)
            {
                r.Filas.Add(armar(l.Ord, l.Desc, l.Sub));
                // El EBITDA no existe como línea del modelo: se reconstruye (utilidad operacional + depreciaciones y amortizaciones), como en el informe de PYG.
                if (string.Equals(l.Desc, "Utilidad operacional", StringComparison.OrdinalIgnoreCase))
                    r.Filas.Add(armarEbitda(filas, lineas.Select(x => (x.Ord, x.Desc)).ToList(), f.Anio, corte, r.HayAnioAnterior));
            }

            // Sin cifras en ninguna línea hasta el corte: no hay nada que mostrar.
            if (!r.Filas.Any(x => x.Acumulado != 0m || x.Meses.Any(v => v.HasValue && v.Value != 0m))) { r.SinDatos = true; r.Filas.Clear(); return r; }

            // Líneas completamente en cero (y sin ser subtotal) se omiten: la hoja debe caber en una página.
            r.Filas = r.Filas.Where(x => x.EsSubtotal || x.Acumulado != 0m || x.AcumPresupuesto != 0m || x.Meses.Any(v => v.HasValue && v.Value != 0m)).ToList();

            var ingresos = r.Filas.FirstOrDefault(x => string.Equals(x.Descripcion, "Ingresos", StringComparison.OrdinalIgnoreCase));
            decimal baseIng = ingresos != null ? ingresos.Acumulado : 0m;
            foreach (var x in r.Filas)
            {
                x.Av = baseIng != 0m ? (decimal?)(x.Acumulado / baseIng) : null;
                x.VarPresupuesto = r.HayPresupuesto && x.AcumPresupuesto != 0m ? (decimal?)((x.Acumulado - x.AcumPresupuesto) / Math.Abs(x.AcumPresupuesto)) : null;
                x.VarAnioAnterior = x.AcumAnioAnterior.HasValue && x.AcumAnioAnterior.Value != 0m ? (decimal?)((x.Acumulado - x.AcumAnioAnterior.Value) / Math.Abs(x.AcumAnioAnterior.Value)) : null;
            }
            return r;
        }

        private static PygHojaFila armarEbitda(List<PygFilaModelo> filas, List<(int Ord, string Desc)> lineas, int anio, int corte, bool hayAnt)
        {
            var ords = lineas.Where(l => ComponentesEbitda.Any(c => string.Equals(c, l.Desc, StringComparison.OrdinalIgnoreCase))).Select(l => l.Ord).ToList();
            Func<int, int, decimal> mes = (a, m) => filas.Where(x => x.Año == a && x.Mes == m && ords.Contains(x.Ord)).Sum(x => x.Valor);
            Func<int, decimal> acum = a => filas.Where(x => x.Año == a && x.Mes == corte && ords.Contains(x.Ord)).Sum(x => x.ValorAcumulado);
            var fila = new PygHojaFila { Ord = -1, Descripcion = "EBITDA", EsSubtotal = true };
            for (int m = 1; m <= 12; m++) fila.Meses.Add(m <= corte ? (decimal?)mes(anio, m) : null);
            fila.Acumulado = acum(anio);
            fila.AcumPresupuesto = filas.Where(x => x.Año == anio && x.Mes == corte && ords.Contains(x.Ord)).Sum(x => x.ValorPresupuestoAcumulado);
            if (hayAnt) fila.AcumAnioAnterior = acum(anio - 1);
            return fila;
        }

        // ===== ¿Cuánta plata me queda? =====

        public CajaDisponibleReporte ConstruirCaja(FiltrosEjecutivo f)
        {
            int corte = Math.Min(12, Math.Max(1, f.Mes));
            var svc = new InformeFlujoService(FlujoTipo.Caja);
            // La caja disponible solo tiene sentido con lo ejecutado: si el corte pedido es posterior al último mes real, se usa ese último mes.
            var ult = svc.ObtenerUltimoMesReal(f.IdEmpresa, f.IdEscenario);
            if (ult == null) return new CajaDisponibleReporte { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = f.Anio, MesCorte = corte, SinDatos = true };
            if (f.Anio * 12 + corte > ult.Anio * 12 + ult.Mes) { f = new FiltrosEjecutivo { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = ult.Anio, Mes = ult.Mes }; corte = ult.Mes; }
            var r = new CajaDisponibleReporte { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = f.Anio, MesCorte = corte };

            // 1) Lo real del año hasta el corte: de dónde vino y a dónde se fue la plata, y la caja con la que se llega.
            var real = svc.ConstruirReporte(new FiltrosFlujo
            {
                IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = 1, AnioHasta = f.Anio, MesHasta = corte, Medida = FlujoMedida.Real
            });
            if (real.SinDatos) { r.SinDatos = true; return r; }

            var c = real.Conciliacion;
            r.HayCajaInicial = c.HayCajaInicial; r.CajaInicial = c.CajaInicial;
            r.TotalFuentes = real.TotalFuentes; r.TotalUsos = real.TotalUsos; r.FlujoNetoAnio = real.TotalFuentes + real.TotalUsos;
            r.Tramos = real.FuentesUsos.Select(x => new CdTramo { Codigo = x.Codigo, Tipo = x.Tipo, Valor = x.Valor }).ToList();
            if (c.HayCajaInicial)
            {
                r.HayCajaCorte = true;
                r.CajaCorteDeBalance = c.HayCajaFinal;
                r.CajaCorte = c.HayCajaFinal ? c.CajaFinalBalance : c.CajaFinalCalculada;
                r.Cuadra = !c.HayCajaFinal || c.Cuadra;
            }

            // 2) Lo que viene: proyección hasta diciembre (meses posteriores al corte, con el flujo proyectado del modelo).
            try
            {
                var proy = svc.ConstruirReporte(new FiltrosFlujo
                {
                    IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = 1, AnioHasta = f.Anio, MesHasta = 12, Medida = FlujoMedida.Proyectado
                });
                if (!proy.SinDatos && c.HayCajaInicial)
                {
                    decimal caja = c.CajaInicial;
                    foreach (var m in proy.Meses.Where(x => x.Anio == f.Anio).OrderBy(x => x.Mes))
                    {
                        bool futuro = m.Mes > corte;
                        if (futuro && m.Mes == corte + 1) caja = r.CajaCorte;   // desde aquí se parte de la caja real del corte
                        caja += m.Neto;
                        r.Meses.Add(new CdMes { Anio = m.Anio, Mes = m.Mes, Proyectado = futuro, Flujo = m.Neto, CajaFin = caja });
                    }
                    var futuros = r.Meses.Where(x => x.Proyectado).ToList();
                    if (futuros.Count > 0)
                    {
                        r.HayProyeccion = true;
                        r.FlujoProyectado = futuros.Sum(x => x.Flujo);
                        r.CajaProyectada = futuros.Last().CajaFin;
                        var min = futuros.OrderBy(x => x.CajaFin).First();
                        r.HayMinimo = true; r.CajaMinima = min.CajaFin; r.MesMinimo = min.Mes;
                        r.MesesEnDeficit = futuros.Count(x => x.CajaFin < 0m);
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "InformeEjecutivo.CajaProyeccion"); }

            return r;
        }

        // ===== Informe para comité =====

        public ComiteReporte ConstruirComite(FiltrosEjecutivo f)
        {
            var r = new ComiteReporte { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = f.Anio, Mes = f.Mes };

            try
            {
                r.Gerencia = new InformeGerenciaMensualService().Construir(new FiltrosGerenciaMensual { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = f.Anio, Mes = f.Mes });
            }
            catch (Exception ex) { AppLogger.Error(ex, "InformeEjecutivo.Comite.Gerencia"); }
            try { r.Pyg = ConstruirPygHoja(f); } catch (Exception ex) { AppLogger.Error(ex, "InformeEjecutivo.Comite.Pyg"); }
            try { r.Caja = ConstruirCaja(f); } catch (Exception ex) { AppLogger.Error(ex, "InformeEjecutivo.Comite.Caja"); }
            try
            {
                var coms = new ComentariosGestionService().Listar(f.IdEmpresa, f.Anio, f.Mes, null, out bool disponible);
                if (disponible) r.Comentarios = coms;
            }
            catch (Exception ex) { AppLogger.Error(ex, "InformeEjecutivo.Comite.Comentarios"); }

            bool hayGerencia = r.Gerencia != null && !r.Gerencia.SinDatos;
            bool hayPyg = r.Pyg != null && !r.Pyg.SinDatos;
            bool hayCaja = r.Caja != null && !r.Caja.SinDatos;
            r.SinDatos = !(hayGerencia || hayPyg || hayCaja);
            return r;
        }
    }
}
