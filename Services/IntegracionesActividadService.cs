using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>Un cargue que una integración envió por la API, con cómo va (tabla dbo.IntegracionesCargues + el lote si todavía existe).</summary>
    public class CargueIntegracion
    {
        public long IdLote { get; set; }
        public int Anio { get; set; }
        public string Modo { get; set; }
        public int IdEscenario { get; set; }
        public string Archivo { get; set; }
        public int Filas { get; set; }
        public int Errores { get; set; }
        public int Advertencias { get; set; }
        public string EstadoRecibido { get; set; }
        public DateTime Recibido { get; set; }
        public string Resolucion { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string ResueltoPor { get; set; }
        public bool LoteExiste { get; set; }
        public string EstadoActual { get; set; }

        /// <summary>PendienteRevision | ConErrores | EnProceso | Confirmado | Descartado | Simulado | SinResolver (venció o lo reemplazó otro envío)</summary>
        public string Situacion
        {
            get
            {
                if (!string.IsNullOrEmpty(Resolucion)) return Resolucion;
                if (!LoteExiste) return "SinResolver";
                switch (EstadoActual)
                {
                    case CargueLoteEstado.ConErrores: return "ConErrores";
                    case CargueLoteEstado.ValidadoOk:
                    case CargueLoteEstado.ConAdvertencias: return "PendienteRevision";
                    case CargueLoteEstado.EnValidacion: return "EnProceso";
                    default: return "SinResolver";
                }
            }
        }
    }

    public class MensajeSalud
    {
        /// <summary>problema | atencion | info | ok</summary>
        public string Nivel { get; set; }
        public string Codigo { get; set; }
        public string[] Args { get; set; } = Array.Empty<string>();
    }

    public class SaludIntegracion
    {
        /// <summary>problema (rojo) | atencion (ámbar) | ok (verde) | inactiva (gris)</summary>
        public string Nivel { get; set; } = "ok";
        public List<MensajeSalud> Mensajes { get; set; } = new List<MensajeSalud>();
    }

    /// <summary>
    /// Seguimiento de lo que hace cada integración (panel «Actividad» de la pantalla Integraciones): historial de envíos
    /// (dbo.IntegracionesCargues, script 051) y un semáforo en lenguaje de negocio que resume envíos, revisiones pendientes, modelos y
    /// avisos al webhook. Todo se consulta SIEMPRE por <c>IdIntegracion</c> (el controlador ya validó el acceso a su empresa).
    /// Tolera tablas ausentes: sin el script 051 el panel muestra lo que sí existe.
    /// </summary>
    public class IntegracionesActividadService : BaseService
    {
        private static bool _disp; private static DateTime _dispHasta = DateTime.MinValue;

        public static bool Disponible()
        {
            if (DateTime.Now < _dispHasta) return _disp;
            try
            {
                _disp = Bd.Escalar<int>(CadenaConexion, "SELECT CASE WHEN OBJECT_ID('dbo.IntegracionesCargues','U') IS NOT NULL THEN 1 ELSE 0 END") == 1;
            }
            catch { _disp = false; }
            _dispHasta = DateTime.Now.AddSeconds(_disp ? 60 : 15);
            return _disp;
        }

        // ── Escritura (la llaman la API y el orquestador; nunca lanzan) ─────────────────

        /// <summary>Registra (o actualiza, si el mismo lote se valida de nuevo) un cargue recibido por la API.</summary>
        public void RegistrarRecibido(int idIntegracion, CargueLote lote, string archivo)
        {
            if (lote == null || !Disponible()) return;
            try
            {
                Bd.Ejecutar(CadenaConexion, @"
                    DELETE FROM dbo.IntegracionesCargues WHERE Recibido < DATEADD(DAY, -90, GETDATE());
                    UPDATE dbo.IntegracionesCargues
                       SET Filas = @F, Errores = @E, Advertencias = @A, EstadoRecibido = @S, Archivo = @Ar
                     WHERE IdLote = @L AND IdIntegracion = @I;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.IntegracionesCargues (IdLote, IdIntegracion, Anio, Modo, IdEscenario, Archivo, Filas, Errores, Advertencias, EstadoRecibido)
                        VALUES (@L, @I, @An, @M, @Es, @Ar, @F, @E, @A, @S);",
                    new
                    {
                        L = lote.IdLote,
                        I = idIntegracion,
                        An = lote.Anio,
                        M = lote.Modo == 0 ? "ejecucion" : "historico",
                        Es = lote.IdEscenario,
                        Ar = (object)Recorte(archivo, 260) ?? DBNull.Value,
                        F = lote.TotalFilas,
                        E = lote.TotalErrores,
                        A = lote.TotalAdvertencias,
                        S = lote.Estado ?? ""
                    });
            }
            catch (Exception ex) { AppLogger.Warn("IntegracionesActividad.RegistrarRecibido: " + ex.Message); }
        }

        /// <summary>
        /// Marca cómo terminó un cargue (<c>Confirmado</c> | <c>Descartado</c> | <c>Simulado</c>). <paramref name="idUsuario"/> decide «persona» o
        /// «integracion» (si es el usuario técnico de la integración). Solo si el lote es de una integración (si no, no hace nada).
        /// </summary>
        public void Resolver(int? idIntegracion, long idLote, string resolucion, int idUsuario)
        {
            if (!idIntegracion.HasValue || !Disponible()) return;
            try
            {
                var integ = new IntegracionesService().Obtener(idIntegracion.Value);
                string por = integ != null && integ.IdUsuarioTecnico == idUsuario ? "integracion" : "persona";
                Bd.Ejecutar(CadenaConexion, @"
                    UPDATE dbo.IntegracionesCargues SET Resolucion = @R, FechaResolucion = GETDATE(), ResueltoPor = @P
                     WHERE IdLote = @L AND IdIntegracion = @I AND Resolucion IS NULL",
                    new { R = resolucion, P = por, L = idLote, I = idIntegracion.Value });
            }
            catch (Exception ex) { AppLogger.Warn("IntegracionesActividad.Resolver: " + ex.Message); }
        }

        // ── Lectura ─────────────────────────────────────────────────────────────────────

        public List<CargueIntegracion> UltimosCargues(int idIntegracion, int n = 10)
        {
            var lista = new List<CargueIntegracion>();
            if (!Disponible()) return lista;
            Bd.Leer(CadenaConexion, @"
                SELECT TOP (@N) c.IdLote, c.Anio, c.Modo, c.IdEscenario, c.Archivo, c.Filas, c.Errores, c.Advertencias, c.EstadoRecibido, c.Recibido,
                       c.Resolucion, c.FechaResolucion, c.ResueltoPor, l.IdLote AS LoteId, l.Estado AS EstadoActual
                  FROM dbo.IntegracionesCargues c
                  LEFT JOIN dbo.CarguesLotes l ON l.IdLote = c.IdLote
                 WHERE c.IdIntegracion = @I
                 ORDER BY c.Recibido DESC, c.IdLote DESC",
                new { N = n, I = idIntegracion },
                r => lista.Add(new CargueIntegracion
                    {
                        IdLote = Convert.ToInt64(r["IdLote"]), Anio = Convert.ToInt32(r["Anio"]), Modo = r["Modo"].ToString(),
                        IdEscenario = Convert.ToInt32(r["IdEscenario"]), Archivo = r["Archivo"] as string,
                        Filas = Convert.ToInt32(r["Filas"]), Errores = Convert.ToInt32(r["Errores"]), Advertencias = Convert.ToInt32(r["Advertencias"]),
                        EstadoRecibido = r["EstadoRecibido"].ToString(), Recibido = Convert.ToDateTime(r["Recibido"]),
                        Resolucion = r["Resolucion"] as string,
                        FechaResolucion = r["FechaResolucion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaResolucion"]),
                        ResueltoPor = r["ResueltoPor"] as string,
                        LoteExiste = r["LoteId"] != DBNull.Value, EstadoActual = r["EstadoActual"] as string
                    }));
            return lista;
        }

        /// <summary>
        /// El semáforo de una integración, en lenguaje de negocio. Rojo = algo falló y requiere acción; ámbar = conviene mirar;
        /// verde = en orden; gris = inactiva o revocada. Los mensajes van por código (el controlador los traduce).
        /// </summary>
        public SaludIntegracion Salud(Integracion integ)
        {
            var s = new SaludIntegracion();
            if (integ == null) return s;

            if (!integ.Vigente)
            {
                s.Nivel = "inactiva";
                s.Mensajes.Add(new MensajeSalud { Nivel = "info", Codigo = "Inactiva" });
                return s;
            }
            if (!integ.FechaUltimoUso.HasValue)
            {
                s.Nivel = "atencion";
                s.Mensajes.Add(new MensajeSalud { Nivel = "atencion", Codigo = "NuncaUsada" });
                return s;
            }

            try
            {
                var cargues = UltimosCargues(integ.Id, 10);

                var ultimo = cargues.FirstOrDefault();
                if (ultimo != null && ultimo.Situacion == "ConErrores")
                    s.Mensajes.Add(new MensajeSalud { Nivel = "problema", Codigo = "UltimoConErrores", Args = new[] { ultimo.Archivo ?? ("#" + ultimo.IdLote), ultimo.Errores.ToString() } });

                var pendientes = cargues.Where(c => c.Situacion == "PendienteRevision").ToList();
                if (pendientes.Count > 0)
                {
                    bool viejos = pendientes.Any(p => (DateTime.Now - p.Recibido).TotalHours > 24);
                    s.Mensajes.Add(new MensajeSalud { Nivel = viejos ? "atencion" : "info", Codigo = viejos ? "PendientesViejos" : "Pendientes", Args = new[] { pendientes.Count.ToString() } });
                }

                if (ultimo != null && ultimo.Situacion == "Confirmado" && ultimo.FechaResolucion.HasValue)
                    s.Mensajes.Add(new MensajeSalud { Nivel = "ok", Codigo = "UltimoConfirmado", Args = new[] { ultimo.FechaResolucion.Value.ToString("dd/MM/yyyy HH:mm") } });
            }
            catch (Exception ex) { AppLogger.Warn("IntegracionesActividad.Salud(cargues): " + ex.Message); }

            try
            {
                var ej = ApiEjecucionesService.Disponible() ? UltimaEjecucion(integ.Id) : null;
                if (ej != null && (ej == "ConErrores" || ej == "Interrumpida"))
                    s.Mensajes.Add(new MensajeSalud { Nivel = ej == "ConErrores" ? "problema" : "atencion", Codigo = "ModelosConProblemas" });
            }
            catch (Exception ex) { AppLogger.Warn("IntegracionesActividad.Salud(modelos): " + ex.Message); }

            try
            {
                if (WebhooksService.Disponible())
                {
                    var wh = new WebhooksService();
                    if (!string.IsNullOrEmpty(wh.ObtenerConfig(integ.Id).Url))
                    {
                        var ultimaEntrega = wh.UltimasEntregas(integ.Id, 1).FirstOrDefault();
                        if (ultimaEntrega != null && ultimaEntrega.Estado == "Fallido")
                            s.Mensajes.Add(new MensajeSalud { Nivel = "problema", Codigo = "WebhookFallo" });
                    }
                }
            }
            catch (Exception ex) { AppLogger.Warn("IntegracionesActividad.Salud(webhook): " + ex.Message); }

            if (s.Mensajes.Count == 0) s.Mensajes.Add(new MensajeSalud { Nivel = "ok", Codigo = "TodoBien" });

            int Peso(string n) => n == "problema" ? 3 : n == "atencion" ? 2 : n == "info" ? 1 : 0;
            s.Mensajes = s.Mensajes.OrderByDescending(m => Peso(m.Nivel)).ToList();
            s.Nivel = s.Mensajes.Any(m => m.Nivel == "problema") ? "problema" : s.Mensajes.Any(m => m.Nivel == "atencion") ? "atencion" : "ok";
            return s;
        }

        /// <summary>Estado de la última ejecución de modelos de la integración (null si no hay).</summary>
        public string UltimaEjecucion(int idIntegracion)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT TOP 1 Estado FROM dbo.ApiEjecuciones WHERE IdIntegracion = @I ORDER BY Id DESC", cn))
            {
                cmd.Parameters.AddWithValue("@I", idIntegracion);
                cn.Open();
                return cmd.ExecuteScalar() as string;
            }
        }

        public List<(long Id, string Estado, int IdEscenario, DateTime Inicio, DateTime? Fin)> UltimasEjecuciones(int idIntegracion, int n = 5)
        {
            var lista = new List<(long, string, int, DateTime, DateTime?)>();
            if (!ApiEjecucionesService.Disponible()) return lista;
            Bd.Leer(CadenaConexion, "SELECT TOP (@N) Id, Estado, IdEscenario, FechaInicio, FechaFin FROM dbo.ApiEjecuciones WHERE IdIntegracion = @I ORDER BY Id DESC",
                new { N = n, I = idIntegracion },
                r => lista.Add((Convert.ToInt64(r["Id"]), r["Estado"].ToString(), Convert.ToInt32(r["IdEscenario"]), Convert.ToDateTime(r["FechaInicio"]),
                               r["FechaFin"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaFin"]))));
            return lista;
        }

        private static string Recorte(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
