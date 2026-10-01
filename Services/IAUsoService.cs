using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Punto único de control de acceso a cualquier consulta de IA del sistema (Análisis IA, resumen
    /// de Home, insights de PYG/Balance). Reemplaza la lógica de cuota que antes estaba duplicada en
    /// cada controlador. Dos reglas, evaluadas en este orden:
    /// 1) el usuario debe tener acceso habilitado (<c>Usuarios.AccesoConsultasIA</c>, permitido por
    ///    defecto si es NULL) — Super Admin siempre pasa;
    /// 2) la empresa no debe haber agotado su presupuesto mensual de tokens (override en
    ///    <c>ConfiguracionIAEmpresaService</c> o, si no hay override, la clave global
    ///    <c>IA_TokensMensualesPorEmpresa</c>; 0 = ilimitado) — Super Admin también exento.
    /// No lee recursos de idioma (regla del proyecto: eso es responsabilidad del controlador, que
    /// resuelve <see cref="ResultadoAcceso.CodigoError"/> con <c>R(...)</c>).
    /// </summary>
    public class IAUsoService : BaseService
    {
        public class ResultadoAcceso
        {
            public bool Permitido { get; set; } = true;
            /// <summary>"SIN_ACCESO" | "PRESUPUESTO_AGOTADO" | null si Permitido.</summary>
            public string CodigoError { get; set; }
            /// <summary>true la primera vez (throttled) que la empresa agota su presupuesto — el
            /// controlador debe notificar a los Super Admin.</summary>
            public bool DebeAvisarAgotado { get; set; }
            /// <summary>true la primera vez (throttled) que la empresa cruza el 80% de su presupuesto.</summary>
            public bool DebeAvisarCercaDelLimite { get; set; }
            public long ConsumidoMes { get; set; }
            public long PresupuestoEfectivo { get; set; }
            public int PorcentajeConsumido { get; set; }
        }

        private readonly ConfiguracionIAEmpresaService _cfgEmpresa = new ConfiguracionIAEmpresaService();
        private readonly ConfiguracionSistemaService _cfgSistema = new ConfiguracionSistemaService();
        private readonly AuditoriaAnalisisIAService _auditoria = new AuditoriaAnalisisIAService();

        public ResultadoAcceso EvaluarAcceso(Usuarios usuario, bool esSuperAdmin, int idEmpresa)
        {
            var resultado = new ResultadoAcceso();
            if (esSuperAdmin) return resultado;

            if (usuario?.AccesoConsultasIA == false)
            {
                resultado.Permitido = false;
                resultado.CodigoError = "SIN_ACCESO";
                return resultado;
            }

            long presupuesto = ObtenerPresupuestoEfectivo(idEmpresa);
            resultado.PresupuestoEfectivo = presupuesto;
            if (presupuesto <= 0) return resultado; // ilimitado

            long consumido = _auditoria.SumarTokensMes(idEmpresa);
            resultado.ConsumidoMes = consumido;
            resultado.PorcentajeConsumido = (int)Math.Min(100, consumido * 100 / presupuesto);

            if (consumido >= presupuesto)
            {
                resultado.Permitido = false;
                resultado.CodigoError = "PRESUPUESTO_AGOTADO";
                resultado.DebeAvisarAgotado = IntentarRegistrarAviso(idEmpresa, agotado: true);
            }
            else if (consumido >= (long)(presupuesto * 0.8))
            {
                resultado.DebeAvisarCercaDelLimite = IntentarRegistrarAviso(idEmpresa, agotado: false);
            }

            return resultado;
        }

        /// <summary>Presupuesto mensual de tokens aplicable a la empresa: su override si existe, si no
        /// el valor global <c>IA_TokensMensualesPorEmpresa</c> (0 = ilimitado en ambos casos).</summary>
        public long ObtenerPresupuestoEfectivo(int idEmpresa)
        {
            var over = _cfgEmpresa.ObtenerOverride(idEmpresa);
            if (over.HasValue) return over.Value;
            return ObtenerPresupuestoGlobalDefault();
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

        /// <summary>Envía a todos los Super Admin un aviso ya traducido (el controlador resuelve el
        /// texto con <c>R(...)</c> — este servicio solo reparte el envío).</summary>
        public void EnviarAvisoPresupuestoATodosSuperAdmin(string titulo, string mensaje, bool agotado)
        {
            try
            {
                var notif = new NotificacionesService();
                foreach (int idAdmin in ObtenerIdsSuperAdmin())
                    notif.Crear(idAdmin, titulo, mensaje, agotado ? "error" : "warning", "/AuditoriaHub");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "IAUsoService.EnviarAvisoPresupuestoATodosSuperAdmin");
            }
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
