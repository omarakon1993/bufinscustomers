using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Helpers;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Informe mensual de gerencia: junta, para UN mes de cierre, lo esencial de los informes de PYG, Balance, Flujo de caja,
    /// Indicadores y Líneas de negocio. No calcula nada nuevo: reutiliza los servicios de cada informe (así las cifras son idénticas
    /// a las de las pantallas originales) y cada sección es independiente — si una falla o no tiene datos, las demás se muestran.
    /// Todo sale de las tablas Modelo*.
    /// </summary>
    public class InformeGerenciaMensualService : BaseService
    {
        private static readonly string[] MesesAbrev =
            { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };

        /// <summary>Meses con Ingresos en el PYG (el cierre se mide por resultados, como en Indicadores).</summary>
        public List<GmMesDisponible> ObtenerMesesDisponibles(int idEmpresa, byte idEscenario)
        {
            return new InformePYGService().ObtenerMesesDisponibles(idEmpresa, idEscenario)
                .Select(m => new GmMesDisponible { Anio = m.Anio, Mes = m.Mes, Etiqueta = MesesAbrev[m.Mes - 1] + " " + m.Anio }).ToList();
        }

        public GerenciaMensualReporteViewModel Construir(FiltrosGerenciaMensual f)
        {
            var r = new GerenciaMensualReporteViewModel { IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, Anio = f.Anio, Mes = f.Mes };

            // 1) Resultados del mes (PYG) contra presupuesto y año anterior
            try
            {
                var pyg = new InformePYGService().ConstruirReporte(new FiltrosPYG
                {
                    IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = f.Mes, AnioHasta = f.Anio, MesHasta = f.Mes,
                    Comparar = PygComparar.Presupuesto
                });
                if (!pyg.SinDatos && pyg.IngresosFila != null)
                {
                    r.HayResultados = true;
                    r.HayAnioAnterior = pyg.HayAnioAnterior;
                    Action<string, PygFilaReporte> agregar = (codigo, fila) =>
                    {
                        if (fila == null) return;
                        r.Resultados.Add(new GmCifraResultado
                        {
                            Codigo = codigo, Real = fila.PeriodoReal, Presupuesto = fila.PeriodoPresupuesto,
                            VarPresupuesto = fila.PeriodoVariacionPorcentual, VarAnioAnterior = fila.PeriodoVariacionYoYPorcentual,
                            AcumReal = fila.AcumReal, AcumPresupuesto = fila.AcumPresupuesto, AcumVarPresupuesto = fila.AcumVariacionPorcentual
                        });
                    };
                    agregar("Ingresos", pyg.IngresosFila);
                    agregar("Ebitda", pyg.EbitdaFila);
                    agregar("UtilidadNeta", pyg.UtilidadNetaFila);
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "GerenciaMensual.PYG"); }

            // 2) Balance al cierre del mes (comparado con el cierre del año anterior)
            try
            {
                var bal = new InformeBalanceService().ConstruirReporte(new FiltrosBalance
                {
                    IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = f.Mes, AnioHasta = f.Anio, MesHasta = f.Mes,
                    Comparar = BalanceComparar.CierreAnterior
                });
                if (!bal.SinDatos)
                {
                    r.HayBalance = true;
                    r.CuadraBalance = bal.CuadraBalance;
                    foreach (var k in bal.Kpis)
                        r.Balance.Add(new GmKpiBalance { Codigo = k.Codigo, Valor = k.Valor, NoAplica = k.NoAplica, Variacion = k.VariacionPorcentual });
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "GerenciaMensual.Balance"); }

            // 3) Caja del mes (flujo de caja real + caja del balance)
            try
            {
                var flujo = new InformeFlujoService(FlujoTipo.Caja).ConstruirReporte(new FiltrosFlujo
                {
                    IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = f.Mes, AnioHasta = f.Anio, MesHasta = f.Mes,
                    Medida = FlujoMedida.Real
                });
                if (!flujo.SinDatos)
                {
                    r.HayCaja = true;
                    var neto = flujo.Kpis.FirstOrDefault(k => k.Codigo == "FlujoNeto");
                    var fcl = flujo.Kpis.FirstOrDefault(k => k.Codigo == "FclOperativo");
                    if (neto != null) { r.FlujoNeto = neto.Valor; r.FlujoNetoPresupuesto = neto.ValorPpto; }
                    if (fcl != null) r.FclOperativo = fcl.Valor;
                    var c = flujo.Conciliacion;
                    if (c != null)
                    {
                        r.HayCajaInicial = c.HayCajaInicial; r.CajaInicial = c.CajaInicial;
                        r.HayCajaFinal = c.HayCajaFinal; r.CajaFinal = c.CajaFinalBalance; r.CuadraCaja = c.Cuadra;
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "GerenciaMensual.Caja"); }

            // 4) Indicadores con semáforo
            try
            {
                var ind = new InformeIndicadoresService().ConstruirReporte(new FiltrosIndicadores
                {
                    IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = f.Mes, AnioHasta = f.Anio, MesHasta = f.Mes
                });
                if (!ind.SinDatos)
                {
                    r.HayIndicadores = true;
                    r.TotalVerde = ind.TotalVerde; r.TotalAmarillo = ind.TotalAmarillo; r.TotalNaranja = ind.TotalNaranja; r.TotalRojo = ind.TotalRojo;
                    foreach (var codigo in ind.Atencion.Take(6))
                    {
                        var v = ind.Indicadores.FirstOrDefault(x => x.Codigo == codigo);
                        if (v != null) r.Atencion.Add(new GmIndicadorAtencion { Codigo = v.Codigo, Formato = v.Formato, Valor = v.Valor, Nivel = v.Nivel });
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "GerenciaMensual.Indicadores"); }

            // 5) Líneas de negocio del mes
            try
            {
                var ln = new InformeLineasNegocioService().ConstruirReporte(new FiltrosLineasNegocio
                {
                    IdEmpresa = f.IdEmpresa, IdEscenario = f.IdEscenario, AnioDesde = f.Anio, MesDesde = f.Mes, AnioHasta = f.Anio, MesHasta = f.Mes
                });
                if (!ln.SinDatos)
                {
                    r.HayLineas = true;
                    r.Lineas = ln.Resumen.Take(6).ToList();
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "GerenciaMensual.Lineas"); }

            r.SinDatos = !(r.HayResultados || r.HayBalance || r.HayCaja || r.HayIndicadores || r.HayLineas);
            return r;
        }
    }
}
