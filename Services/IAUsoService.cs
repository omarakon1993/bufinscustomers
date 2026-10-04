using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Punto único de control de acceso a cualquier consulta de IA del sistema (Análisis IA, resumen
    /// de Home, insights de PYG/Balance). Super Admin queda exento de todas las reglas. Para el resto,
    /// en este orden (la configuración por empresa vive en <see cref="ConfigIAEmpresa"/>):
    /// 1) la empresa tiene la IA habilitada (interruptor maestro);
    /// 2) el usuario tiene acceso (<c>Usuarios.AccesoConsultasIA</c>, permitido por defecto si es NULL);
    /// 3) la función está habilitada para la empresa;
    /// 4) el usuario no superó su tope diario de tokens;
    /// 5) presupuesto de tokens del periodo (propio o global; 0 = ilimitado), opcionalmente compartido
    ///    por el Grupo Empresarial (pool) y con día de corte propio. Al agotarse (o si la consulta
    ///    estimada lo excedería) se aplica la política de la empresa: bloquear, degradar o sobreconsumo.
    /// No lee recursos de idioma (regla del proyecto): <see cref="ResultadoAcceso.CodigoError"/> es un
    /// código y <see cref="ClaveMensaje"/> da la clave de recurso con que el llamador lo traduce.
    /// </summary>
    public class IAUsoService : BaseService
    {
        public class ResultadoAcceso
        {
            public bool Permitido { get; set; } = true;
            /// <summary>"EMPRESA_SIN_IA" | "SIN_ACCESO" | "FUNCION_NO_PERMITIDA" | "TOPE_USUARIO" |
            /// "PRESUPUESTO_AGOTADO" | "PRESUPUESTO_INSUFICIENTE" | null si Permitido.</summary>
            public string CodigoError { get; set; }
            /// <summary>true la primera vez (throttled) que la empresa agota su presupuesto — el
            /// controlador debe notificar a los Super Admin.</summary>
            public bool DebeAvisarAgotado { get; set; }
            /// <summary>true la primera vez (throttled) que la empresa cruza el 80% de su presupuesto.</summary>
            public bool DebeAvisarCercaDelLimite { get; set; }
            public long ConsumidoMes { get; set; }
            public long PresupuestoEfectivo { get; set; }
            public int PorcentajeConsumido { get; set; }
            /// <summary>Política "Degradar" en curso: usar modelo económico y respuestas más cortas.</summary>
            public bool Degradado { get; set; }
            /// <summary>Política "Sobreconsumo" en curso: se atiende normal y el exceso queda marcado en IAUsoLog.</summary>
            public bool Sobreconsumo { get; set; }
            /// <summary>Modelo fijo configurado para la empresa (null = el global).</summary>
            public string ModeloEmpresa { get; set; }
            /// <summary>Contexto de negocio propio de la empresa (null = sin contexto propio).</summary>
            public string ContextoEmpresa { get; set; }
        }

        /// <summary>Consumo y presupuesto del periodo en curso de una empresa (o de su pool).</summary>
        public class EstadoConsumo
        {
            public long Consumido { get; set; }
            /// <summary>0 = ilimitado.</summary>
            public long Presupuesto { get; set; }
            public DateTime InicioPeriodo { get; set; }
            public bool EnPool { get; set; }
            public int EmpresasEnPool { get; set; } = 1;
        }

        private readonly ConfiguracionIAEmpresaService _cfgEmpresa = new ConfiguracionIAEmpresaService();
        private readonly ConfiguracionSistemaService _cfgSistema = new ConfiguracionSistemaService();
        private readonly AuditoriaAnalisisIAService _auditoria = new AuditoriaAnalisisIAService();

        /// <summary>Clave de recurso (Strings.resx) del mensaje para un <see cref="ResultadoAcceso.CodigoError"/>.</summary>
        public static string ClaveMensaje(string codigoError)
        {
            switch (codigoError)
            {
                case "PRESUPUESTO_AGOTADO": return "IA_PresupuestoAgotadoMensaje";
                case "PRESUPUESTO_INSUFICIENTE": return "IA_PresupuestoInsuficienteMensaje";
                case "EMPRESA_SIN_IA": return "IA_EmpresaSinIAMensaje";
                case "FUNCION_NO_PERMITIDA": return "IA_FuncionNoPermitidaMensaje";
                case "TOPE_USUARIO": return "IA_TopeUsuarioMensaje";
                default: return "IA_SinAccesoMensaje";
            }
        }

        /// <param name="funcion">Una de <see cref="IAFuncion"/> (null = no validar funciones).</param>
        /// <param name="tokensEstimados">Estimación de la consulta que se va a hacer (0 = aún no se conoce).</param>
        /// <param name="tokensReservados">Tokens de consultas en curso de la misma empresa que aún no se han contabilizado.</param>
        public ResultadoAcceso EvaluarAcceso(Usuarios usuario, bool esSuperAdmin, int idEmpresa,
            string funcion = null, long tokensEstimados = 0, long tokensReservados = 0)
        {
            var resultado = new ResultadoAcceso();
            // Super Admin exento de los controles, pero el contexto de la empresa consultada sí aplica a sus respuestas.
            var cfg = _cfgEmpresa.ObtenerConfig(idEmpresa);
            resultado.ContextoEmpresa = cfg.ContextoNegocio;
            if (esSuperAdmin) return resultado;

            resultado.ModeloEmpresa = cfg.ModeloPermitido;

            if (!cfg.IaHabilitada) return Denegar(resultado, "EMPRESA_SIN_IA");
            if (usuario?.AccesoConsultasIA == false) return Denegar(resultado, "SIN_ACCESO");

            if (!string.IsNullOrEmpty(funcion) && cfg.FuncionesPermitidas != null
                && !cfg.FuncionesPermitidas.Contains(funcion, StringComparer.OrdinalIgnoreCase))
                return Denegar(resultado, "FUNCION_NO_PERMITIDA");

            if (usuario != null && cfg.TopeDiarioUsuario.GetValueOrDefault() > 0
                && _auditoria.SumarTokensUsuarioHoy(idEmpresa, usuario.Id) >= cfg.TopeDiarioUsuario.Value)
                return Denegar(resultado, "TOPE_USUARIO");

            var estado = ObtenerEstadoConsumo(idEmpresa, cfg);
            resultado.PresupuestoEfectivo = estado.Presupuesto;
            resultado.ConsumidoMes = estado.Consumido;
            if (estado.Presupuesto <= 0) return resultado; // ilimitado

            long presupuesto = estado.Presupuesto, consumido = estado.Consumido;
            resultado.PorcentajeConsumido = (int)Math.Min(100, consumido * 100 / presupuesto);

            bool agotado = consumido >= presupuesto;
            bool excederia = !agotado && tokensEstimados > 0 && consumido + tokensReservados + tokensEstimados > presupuesto;

            if (agotado || excederia)
            {
                if (agotado) resultado.DebeAvisarAgotado = IntentarRegistrarAviso(idEmpresa, agotado: true);

                switch (cfg.PoliticaAgotado)
                {
                    case IAPoliticaAgotado.Degradar: resultado.Degradado = true; break;
                    case IAPoliticaAgotado.Sobreconsumo: resultado.Sobreconsumo = true; break;
                    default: return Denegar(resultado, agotado ? "PRESUPUESTO_AGOTADO" : "PRESUPUESTO_INSUFICIENTE");
                }
            }
            else if (consumido >= (long)(presupuesto * 0.8))
            {
                resultado.DebeAvisarCercaDelLimite = IntentarRegistrarAviso(idEmpresa, agotado: false);
            }

            return resultado;
        }

        private static ResultadoAcceso Denegar(ResultadoAcceso r, string codigo)
        {
            r.Permitido = false;
            r.CodigoError = codigo;
            return r;
        }

        /// <summary>Consumo y presupuesto del periodo en curso de la empresa; si tiene pool de grupo, los del pool.</summary>
        public EstadoConsumo ObtenerEstadoConsumo(int idEmpresa, ConfigIAEmpresa cfg = null)
        {
            cfg = cfg ?? _cfgEmpresa.ObtenerConfig(idEmpresa);
            var estado = new EstadoConsumo { InicioPeriodo = InicioPeriodo(cfg.DiaCorte, DateTime.Now) };

            List<int> ids = new List<int> { idEmpresa };
            Dictionary<int, ConfigIAEmpresa> cfgs = null;

            if (cfg.PoolGrupo)
            {
                var todas = EmpresaCacheHelper.ObtenerEmpresasCacheadas();
                int? grupo = todas.FirstOrDefault(e => e.Id == idEmpresa)?.IdGrupoEmpresarial;
                if (grupo.HasValue)
                {
                    var delGrupo = todas.Where(e => e.IdGrupoEmpresarial == grupo).Select(e => e.Id).ToList();
                    cfgs = _cfgEmpresa.ObtenerConfigs(delGrupo);
                    cfgs[idEmpresa] = cfg;
                    var miembros = delGrupo.Where(id => id == idEmpresa || (cfgs.TryGetValue(id, out var c) && c.PoolGrupo)).ToList();
                    if (miembros.Count > 1)
                    {
                        ids = miembros;
                        estado.EnPool = true;
                        estado.EmpresasEnPool = miembros.Count;
                    }
                }
            }

            if (estado.EnPool)
            {
                // El presupuesto del pool es la suma de los de sus miembros; si alguno es ilimitado, el pool lo es.
                long total = 0;
                foreach (int id in ids)
                {
                    long p = (cfgs.TryGetValue(id, out var c) ? c.PresupuestoTokensMensual : null) ?? ObtenerPresupuestoGlobalDefault();
                    if (p <= 0) { total = 0; break; }
                    total += p;
                }
                estado.Presupuesto = total;
                estado.Consumido = _auditoria.SumarTokensDesde(ids, estado.InicioPeriodo);
            }
            else
            {
                estado.Presupuesto = cfg.PresupuestoTokensMensual ?? ObtenerPresupuestoGlobalDefault();
                // Camino rápido (acumulado mensual) solo con mes calendario; con día de corte se suma por fechas.
                estado.Consumido = cfg.DiaCorte.HasValue
                    ? _auditoria.SumarTokensDesde(ids, estado.InicioPeriodo)
                    : _auditoria.SumarTokensMes(idEmpresa);
            }
            return estado;
        }

        /// <summary>Inicio del periodo de consumo: el día de corte más reciente (1-28) o el día 1 del mes.</summary>
        public static DateTime InicioPeriodo(int? diaCorte, DateTime hoy)
        {
            int dia = Math.Max(1, Math.Min(28, diaCorte ?? 1));
            var inicio = new DateTime(hoy.Year, hoy.Month, dia);
            return hoy.Date >= inicio ? inicio : inicio.AddMonths(-1);
        }

        public long ObtenerPresupuestoGlobalDefault()
        {
            try
            {
                var v = _cfgSistema.ObtenerValor("IA_TokensMensualesPorEmpresa");
                if (long.TryParse(v, out long t) && t >= 0) return t;
            }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoService.ObtenerPresupuestoGlobalDefault"); }
            return 0;
        }

        /// <summary>Throttle de aviso (≤ 1 por empresa y tipo cada 24 h). Devuelve true solo la
        /// primera vez que se cruza el umbral dentro de esa ventana — el llamador decide si avisa.</summary>
        private bool IntentarRegistrarAviso(int idEmpresa, bool agotado)
        {
            string ck = "ia_presup_" + idEmpresa + "_" + (agotado ? "full" : "warn");
            if (System.Web.HttpRuntime.Cache[ck] != null) return false;
            System.Web.HttpRuntime.Cache.Insert(ck, 1, null, DateTime.Now.AddHours(24),
                System.Web.Caching.Cache.NoSlidingExpiration);
            return true;
        }

        /// <summary>Envía el aviso (ya traducido por el controlador con <c>R(...)</c>) a todos los Super Admin y,
        /// si se indica la empresa, también a sus Admin de Empresa (que lo abren en "Mi consumo de IA").</summary>
        public void EnviarAvisoPresupuestoATodosSuperAdmin(string titulo, string mensaje, bool agotado, int? idEmpresa = null)
        {
            try
            {
                var notif = new NotificacionesService();
                string tipo = agotado ? "error" : "warning";
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    notif.Crear(idAdmin, titulo, mensaje, tipo, "/AuditoriaHub");
                if (idEmpresa.HasValue)
                    foreach (int idAdminEmpresa in ObtenerIdsAdminEmpresa(idEmpresa.Value))
                        notif.Crear(idAdminEmpresa, titulo, mensaje, tipo, "/AnalisisIA");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "IAUsoService.EnviarAvisoPresupuestoATodosSuperAdmin");
            }
        }

        private List<int> ObtenerIdsAdminEmpresa(int idEmpresa)
        {
            var ids = new List<int>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Id FROM Usuarios WHERE Admin = 1 AND IdEmpresa = @IdEmpresa", cn))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["Id"]));
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoService.ObtenerIdsAdminEmpresa"); }
            return ids;
        }

        private List<int> ObtenerIdsSuperAdmin()
        {
            var ids = new List<int>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Id FROM Usuarios WHERE Admin = 2", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["Id"]));
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "IAUsoService.ObtenerIdsSuperAdmin"); }
            return ids;
        }
    }
}
