using bufinscustomers.Helpers;
using bufinscustomers.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class EjecucionModeloItem
    {
        public string Modelo { get; set; }
        /// <summary>Pendiente | EnCurso | Completado | Error | Simulado</summary>
        public string Estado { get; set; }
        public string Mensaje { get; set; }
        public double? Segundos { get; set; }
    }

    public class EjecucionApi
    {
        public long Id { get; set; }
        /// <summary>EnCurso | Completada | ConErrores | Interrumpida | Simulada</summary>
        public string Estado { get; set; }
        public int IdEscenario { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime? Fin { get; set; }
        public List<EjecucionModeloItem> Modelos { get; set; } = new List<EjecucionModeloItem>();
    }

    /// <summary>
    /// «Recalcular los informes» por la API: ejecuta TODOS los modelos activos de la empresa (en su orden) en segundo plano y deja el
    /// estado en <c>dbo.ApiEjecuciones</c> (script 049). Cada modelo es el mismo procedimiento que corre «Ejecución de Modelos»
    /// (nombre leído de la base, nunca de la petición; último result set con CodMessage/ErrorMessage; 300 s por modelo).
    /// Las integraciones de pruebas (sandbox) no ejecutan nada: la ejecución queda «Simulada».
    /// </summary>
    public class ApiEjecucionesService : BaseService
    {
        /// <summary>Una ejecución «EnCurso» más vieja que esto se da por interrumpida (el sitio se reinició o el proceso murió).</summary>
        private const int MinutosParaInterrumpida = 45;

        private static bool _disp; private static DateTime _dispHasta = DateTime.MinValue;

        public static bool Disponible()
        {
            if (DateTime.Now < _dispHasta) return _disp;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.ApiEjecuciones','U') IS NOT NULL THEN 1 ELSE 0 END", cn))
                {
                    cn.Open();
                    _disp = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch { _disp = false; }
            _dispHasta = DateTime.Now.AddSeconds(_disp ? 60 : 15);
            return _disp;
        }

        /// <summary>
        /// Inicia la ejecución. Códigos: <c>no_disponible</c> (falta el script 049), <c>escenario_invalido</c>, <c>sin_modelos</c>,
        /// <c>ejecucion_en_curso</c> (ya hay una para esa empresa y escenario; <c>id</c> es la que está corriendo).
        /// </summary>
        public (bool ok, string codigo, long id) Iniciar(Integracion integ, Usuarios tecnico, int idEscenario)
        {
            if (!Disponible()) return (false, "no_disponible", 0);
            if (!new EscenarioService().ObtenerTodos().Any(e => e.Id == idEscenario && e.Activo)) return (false, "escenario_invalido", 0);

            var modelos = new ModeloService().ObtenerModelosActivos().OrderBy(m => m.Orden).ThenBy(m => m.Nombre).ToList();
            if (modelos.Count == 0) return (false, "sin_modelos", 0);

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                // Las «EnCurso» demasiado viejas se marcan interrumpidas y no bloquean la siguiente.
                using (var cmdV = new SqlCommand(@"
                    UPDATE dbo.ApiEjecuciones SET Estado = 'Interrumpida', FechaFin = GETDATE()
                     WHERE IdEmpresa = @E AND Estado = 'EnCurso' AND FechaInicio < DATEADD(MINUTE, -@M, GETDATE())", cn))
                {
                    cmdV.Parameters.AddWithValue("@E", integ.IdEmpresa);
                    cmdV.Parameters.AddWithValue("@M", MinutosParaInterrumpida);
                    cmdV.ExecuteNonQuery();
                }
                using (var cmdC = new SqlCommand(
                    "SELECT TOP 1 Id FROM dbo.ApiEjecuciones WHERE IdEmpresa = @E AND IdEscenario = @S AND Estado = 'EnCurso' ORDER BY Id DESC", cn))
                {
                    cmdC.Parameters.AddWithValue("@E", integ.IdEmpresa);
                    cmdC.Parameters.AddWithValue("@S", idEscenario);
                    var r = cmdC.ExecuteScalar();
                    if (r != null && r != DBNull.Value) return (false, "ejecucion_en_curso", Convert.ToInt64(r));
                }

                var items = modelos.Select(m => new EjecucionModeloItem { Modelo = m.Nombre, Estado = integ.Sandbox ? "Simulado" : "Pendiente" }).ToList();
                long id;
                using (var cmdI = new SqlCommand(@"
                    INSERT INTO dbo.ApiEjecuciones (IdIntegracion, IdEmpresa, IdEscenario, Estado, FechaFin, Detalle)
                    VALUES (@I, @E, @S, @Est, " + (integ.Sandbox ? "GETDATE()" : "NULL") + @", @D);
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
                {
                    cmdI.Parameters.AddWithValue("@I", integ.Id);
                    cmdI.Parameters.AddWithValue("@E", integ.IdEmpresa);
                    cmdI.Parameters.AddWithValue("@S", idEscenario);
                    cmdI.Parameters.AddWithValue("@Est", integ.Sandbox ? "Simulada" : "EnCurso");
                    cmdI.Parameters.AddWithValue("@D", JsonSerializer.Serialize(items));
                    id = (long)cmdI.ExecuteScalar();
                }

                if (integ.Sandbox) new WebhooksService().NotificarModelosCompletados(integ, id, "Simulada", idEscenario, items);

                if (!integ.Sandbox)
                {
                    // Segundo plano: la llamada responde ya (202) y el cliente consulta GET /ejecuciones/{id}.
                    Task.Run(() => EjecutarEnSegundoPlano(id, integ, tecnico, idEscenario, modelos, items));
                }
                return (true, null, id);
            }
        }

        /// <summary>La ejecución, solo si es de esa integración (si no, null). Una «EnCurso» vieja se devuelve como interrumpida.</summary>
        public EjecucionApi Obtener(long id, int idIntegracion)
        {
            if (!Disponible()) return null;
            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (var cmdV = new SqlCommand(@"
                    UPDATE dbo.ApiEjecuciones SET Estado = 'Interrumpida', FechaFin = GETDATE()
                     WHERE Id = @Id AND Estado = 'EnCurso' AND FechaInicio < DATEADD(MINUTE, -@M, GETDATE())", cn))
                {
                    cmdV.Parameters.AddWithValue("@Id", id);
                    cmdV.Parameters.AddWithValue("@M", MinutosParaInterrumpida);
                    cmdV.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand(
                    "SELECT Id, IdEscenario, Estado, FechaInicio, FechaFin, Detalle FROM dbo.ApiEjecuciones WHERE Id = @Id AND IdIntegracion = @I", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@I", idIntegracion);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        var e = new EjecucionApi
                        {
                            Id = Convert.ToInt64(r["Id"]), IdEscenario = Convert.ToInt32(r["IdEscenario"]), Estado = r["Estado"].ToString(),
                            Inicio = Convert.ToDateTime(r["FechaInicio"]),
                            Fin = r["FechaFin"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaFin"])
                        };
                        try { e.Modelos = JsonSerializer.Deserialize<List<EjecucionModeloItem>>(r["Detalle"] as string ?? "[]") ?? new List<EjecucionModeloItem>(); }
                        catch (Exception) { e.Modelos = new List<EjecucionModeloItem>(); }
                        return e;
                    }
                }
            }
        }

        // ── Segundo plano ──────────────────────────────────────────────────────────────

        private void EjecutarEnSegundoPlano(long id, Integracion integ, Usuarios tecnico, int idEscenario,
            List<ModeloEjecucion> modelos, List<EjecucionModeloItem> items)
        {
            string estadoFinal = "Completada";
            try
            {
                for (int i = 0; i < modelos.Count; i++)
                {
                    items[i].Estado = "EnCurso";
                    Guardar(id, "EnCurso", items, finalizada: false);

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (ok, mensaje) = EjecutarModelo(modelos[i], integ.IdEmpresa, idEscenario, tecnico.Id);
                    sw.Stop();

                    items[i].Estado = ok ? "Completado" : "Error";
                    items[i].Mensaje = mensaje;
                    items[i].Segundos = Math.Round(sw.Elapsed.TotalSeconds, 1);
                    if (!ok) estadoFinal = "ConErrores";
                    Guardar(id, "EnCurso", items, finalizada: false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ApiEjecuciones.EjecutarEnSegundoPlano");
                estadoFinal = "ConErrores";
                foreach (var it in items.Where(x => x.Estado == "EnCurso" || x.Estado == "Pendiente"))
                { it.Estado = "Error"; it.Mensaje = "La ejecución se interrumpió por un error interno."; }
            }

            Guardar(id, estadoFinal, items, finalizada: true);
            new WebhooksService().NotificarModelosCompletados(integ, id, estadoFinal, idEscenario, items);

            // Auditoría y aviso: sin contexto HTTP, así que el actor va explícito (el usuario técnico de la integración).
            try
            {
                int ok = items.Count(x => x.Estado == "Completado"), err = items.Count(x => x.Estado == "Error");
                string empresa = new EmpresaService().ObtenerEmpresas().FirstOrDefault(e => e.Id == integ.IdEmpresa)?.Nombre ?? "Empresa";
                new AuditoriaService().Registrar(new RegistroAuditoria
                {
                    Tipo = AuditoriaTipo.Modelos, Accion = AuditoriaAccion.Ejecutar, Entidad = "ApiEjecuciones", EntidadId = id.ToString(),
                    EntidadNombre = empresa, IdEmpresa = integ.IdEmpresa, IdUsuario = tecnico.Id,
                    NombreUsuario = ((tecnico.Nombre ?? "") + " " + (tecnico.Apellidos ?? "")).Trim(),
                    Severidad = err > 0 ? AuditoriaSeveridad.Advertencia : AuditoriaSeveridad.Info,
                    Descripcion = $"La integración '{integ.Nombre}' ejecutó los modelos de '{empresa}' por la API (escenario {idEscenario}): {ok} completado(s), {err} con error."
                });
                if (err > 0) NotificarErrores(integ, empresa, ok, err);
            }
            catch (Exception ex) { AppLogger.Warn("ApiEjecuciones.Auditoria: " + ex.Message); }
        }

        /// <summary>Ejecuta un modelo (mismo contrato que «Ejecución de Modelos»). Un error técnico no filtra detalles: queda en el log.</summary>
        private (bool ok, string mensaje) EjecutarModelo(ModeloEjecucion modelo, int idEmpresa, int idEscenario, int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(modelo.NombreSP, cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    cmd.CommandTimeout = 300;
                    cn.Open();

                    int codMessage = 1;
                    string mensaje = "Ejecutado correctamente.";
                    using (var reader = cmd.ExecuteReader())
                    {
                        do
                        {
                            bool esStatus = false;
                            for (int i = 0; i < reader.FieldCount; i++)
                                if (string.Equals(reader.GetName(i), "CodMessage", StringComparison.OrdinalIgnoreCase)) { esStatus = true; break; }

                            while (reader.Read())
                            {
                                if (!esStatus) continue;   // el resultado de datos no se necesita: solo se consume
                                try { codMessage = Convert.ToInt32(reader["CodMessage"]); } catch { }
                                try { var m = reader["ErrorMessage"]; if (m != DBNull.Value) mensaje = m.ToString(); } catch { }
                            }
                        } while (reader.NextResult());
                    }
                    return (codMessage == 1, mensaje);
                }
            }
            catch (SqlException ex) when (ex.Number >= 50000)
            {
                return (false, ex.Message);   // error de regla de negocio lanzado a propósito por el procedimiento (THROW / RAISERROR)
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"ApiEjecuciones.EjecutarModelo({modelo.Nombre})");
                return (false, "El modelo no pudo ejecutarse por un error técnico. Contacte a soporte indicando la hora de la ejecución.");
            }
        }

        private void Guardar(long id, string estado, List<EjecucionModeloItem> items, bool finalizada)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE dbo.ApiEjecuciones SET Estado = @Est, Detalle = @D" + (finalizada ? ", FechaFin = GETDATE()" : "") + " WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Est", estado);
                    cmd.Parameters.AddWithValue("@D", JsonSerializer.Serialize(items));
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "ApiEjecuciones.Guardar"); }
        }

        /// <summary>Avisa (campana, en español) a los Admin de la empresa que una ejecución por la API terminó con errores.</summary>
        private void NotificarErrores(Integracion integ, string empresa, int ok, int err)
        {
            var es = new System.Globalization.CultureInfo("es-CO");
            string Txt(string k) => bufinscustomers.App_GlobalResources.Strings.ResourceManager.GetString(k, es) ?? k;
            string titulo = Txt("Notif_ModelosApiErrores");
            string mensaje = string.Format(Txt("Notif_ModelosApiErroresMsg"), integ.Nombre, empresa, ok, err);

            var destinatarios = new List<int>();
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT Id FROM dbo.Usuarios WHERE IdEmpresa = @E AND Admin = 1 AND Id <> @T", cn))
            {
                cmd.Parameters.AddWithValue("@E", integ.IdEmpresa);
                cmd.Parameters.AddWithValue("@T", integ.IdUsuarioTecnico);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) destinatarios.Add(r.GetInt32(0));
            }
            var notif = new NotificacionesService();
            foreach (var id in destinatarios) notif.Crear(id, titulo, mensaje, "warning", "/Datos/Modelo");
        }
    }
}
