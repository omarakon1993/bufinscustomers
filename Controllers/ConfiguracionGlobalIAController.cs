using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    [SoloSuperAdmin]
    public class ConfiguracionGlobalIAController : BaseController
    {
        private readonly ConfiguracionSistemaService _svc = new ConfiguracionSistemaService();

        public ActionResult Index()
        {
            var items = _svc.ObtenerTodos();
            return View("~/Views/Configuracion/ConfiguracionGlobalIA.cshtml", items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Guardar(ConfiguracionSistemaItem item)
        {
            if (string.IsNullOrWhiteSpace(item?.Clave))
            {
                SetErrorMessage(R("CfgIA_MsgErrorGuardar") ?? "La clave no puede estar vacía.");
                return RedirectToAction("Index");
            }

            // MaxTokens: validar rango
            if (item.Clave == "OpenAIMaxTokens")
            {
                if (int.TryParse(item.Valor, out int t) && (t < 100 || t > 100000))
                {
                    SetErrorMessage("El valor de MaxTokens debe estar entre 100 y 100000.");
                    return RedirectToAction("Index");
                }
            }

            bool ok = _svc.Guardar(item.Clave, item.Valor ?? "", item.Descripcion ?? "");
            if (ok)
                SetSuccessMessage(R("CfgIA_MsgGuardadoOk") ?? "Configuración guardada correctamente.");
            else
                SetErrorMessage(R("CfgIA_MsgErrorGuardar") ?? "Error al guardar la configuración.");

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(string clave)
        {
            bool ok = _svc.Eliminar(clave ?? "");
            if (ok)
                SetSuccessMessage("Configuración eliminada.");
            else
                SetErrorMessage("No se pudo eliminar la configuración.");

            return RedirectToAction("Index");
        }

        /// <summary>
        /// Devuelve en claro el valor de una clave secreta (p. ej. la API key) para poder editarla.
        /// Solo Super Admin (gate a nivel de clase) y queda registrado en la auditoría.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Revelar(string clave)
        {
            if (!ConfiguracionSistemaService.EsClaveSecreta(clave))
                return Json(new { ok = false, mensaje = R("CfgIA_JS_ClaveNoRevelable") ?? "Esta clave no admite revelado." });

            string valor = _svc.ObtenerValor(clave);
            var u = UsuarioSesionHelper.UsuarioActual;

            new AuditoriaService().RegistrarCambio(
                AuditoriaTipo.Configuracion, "Revelar", "ConfiguracionSistema", clave,
                $"Secreto '{clave}' revelado en Configuración Global IA",
                null, null, u?.IdEmpresa, severidad: AuditoriaSeveridad.Advertencia);
            AppLogger.Warn($"Secreto de configuración revelado: {clave} (usuario {u?.Id})");

            return Json(new { ok = true, valor = valor ?? "" });
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerModelosOpenAI()
        {
            try
            {
                string apiKey = (_svc.ObtenerValor("OpenAIApiKey") ?? "").Trim();
                if (string.IsNullOrEmpty(apiKey))
                    return Json(new { ok = false, mensaje = R("CfgIA_JS_ApiKeyVacia") ?? "Primero guarda una API Key válida." },
                        JsonRequestBehavior.AllowGet);

                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", apiKey);
                    http.Timeout = TimeSpan.FromSeconds(12);

                    var resp = await http.GetAsync("https://api.openai.com/v1/models");
                    if (!resp.IsSuccessStatusCode)
                        return Json(new { ok = false, mensaje = "OpenAI respondió: " + (int)resp.StatusCode },
                            JsonRequestBehavior.AllowGet);

                    var body = await resp.Content.ReadAsStringAsync();
                    dynamic json = JsonConvert.DeserializeObject(body);

                    var modelos = new List<string>();
                    foreach (var m in json.data)
                    {
                        string id = m.id?.ToString() ?? "";
                        if (EsModeloChat(id))
                            modelos.Add(id);
                    }
                    modelos.Sort();

                    return Json(new { ok = true, modelos }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ── Por Empresa: presupuesto de tokens + acceso de usuarios a IA ──────────────────

        // ── Uso y costos (observabilidad global) ─────────────────────────────────────────

        [HttpGet]
        public JsonResult ObtenerObservabilidadIA(string desde, string hasta)
        {
            try
            {
                DateTime fHasta = DateTime.TryParse(hasta, out var h) ? h.Date : DateTime.Now.Date;
                DateTime fDesde = DateTime.TryParse(desde, out var d) ? d.Date : fHasta.AddDays(-29);
                if (fDesde > fHasta) { var tmp = fDesde; fDesde = fHasta; fHasta = tmp; }

                var o = new IAUsoReporteService().Observabilidad(fDesde, fHasta);

                // Nombres de empresa (la consulta devuelve el Id) y etiquetas de función traducidas.
                var nombres = EmpresaCacheHelper.ObtenerEmpresasCacheadas().ToDictionary(e => e.Id.ToString(), e => e.Nombre);
                foreach (var g in o.PorEmpresa)
                    g.Clave = nombres.TryGetValue(g.Clave ?? "", out var n) ? n : ("#" + g.Clave);
                foreach (var g in o.PorFuncion)
                    g.Clave = EtiquetaFuncion(g.Clave);
                foreach (var e in o.Errores)
                    e.Funcion = EtiquetaFuncion(e.Funcion);

                var errores = o.Errores.Select(e => new
                {
                    e.Fecha,
                    Empresa = nombres.TryGetValue(e.IdEmpresa.ToString(), out var n) ? n : ("#" + e.IdEmpresa),
                    e.Funcion,
                    Detalle = TraducirCodigoError(e.Error)
                });

                return Json(new
                {
                    ok = true,
                    desde = fDesde.ToString("yyyy-MM-dd"),
                    hasta = fHasta.ToString("yyyy-MM-dd"),
                    o.Disponible,
                    o.Totales,
                    o.PorDia,
                    o.PorFuncion,
                    o.PorModelo,
                    o.PorEmpresa,
                    Errores = errores
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionGlobalIAController.ObtenerObservabilidadIA");
                return Json(new { ok = false, mensaje = R("CfgIA_Uso_Error") }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Borra todas las estadísticas de uso de IA (IAUsoLog). Solo Super Admin (gate de clase).
        /// El presupuesto mensual y la auditoría de consultas no se tocan.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult LimpiarUsoIA()
        {
            try
            {
                int borradas = new IAUsoReporteService().LimpiarRegistroUso();
                if (borradas < 0)
                    return Json(new { ok = false, mensaje = R("CfgIA_Uso_NoDisponible") });

                var u = UsuarioSesionHelper.UsuarioActual;
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Eliminar,
                    "IAUsoLog", null, $"Estadísticas de uso de IA eliminadas ({borradas} registros)",
                    new { registros = borradas }, null, u?.IdEmpresa);

                return Json(new { ok = true, borradas, mensaje = string.Format(R("CfgIA_Uso_LimpiarOk"), borradas.ToString("N0")) });
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionGlobalIAController.LimpiarUsoIA");
                return Json(new { ok = false, mensaje = R("CfgIA_Uso_LimpiarError") });
            }
        }

        private string EtiquetaFuncion(string funcion)
        {
            string clave = "CfgIA_Emp_Func_" + funcion;
            string texto = R(clave);
            return texto == clave ? funcion : texto;
        }

        /// <summary>Los rechazos del gateway se guardan como código (p. ej. PRESUPUESTO_AGOTADO): se muestran con su mensaje.</summary>
        private string TraducirCodigoError(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";
            if (error.All(ch => char.IsUpper(ch) || ch == '_'))
            {
                string clave = IAUsoService.ClaveMensaje(error);
                string texto = R(clave);
                return texto == clave ? error : texto;
            }
            return error;
        }

        private static readonly string[] FuncionesIA =
            { IAFuncion.Chat, IAFuncion.ResumenHome, IAFuncion.InsightsPYG, IAFuncion.InsightsBalance };

        [HttpGet]
        public JsonResult ObtenerResumenEmpresaIA(int idEmpresa)
        {
            try
            {
                var empresa = new EmpresaService().ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                if (empresa == null)
                    return Json(new { ok = false, mensaje = R("Common_SinPermisos") }, JsonRequestBehavior.AllowGet);

                var iaUso = new IAUsoService();
                var cfgEmpresa = new ConfiguracionIAEmpresaService();

                var cfg = cfgEmpresa.ObtenerConfig(idEmpresa);
                long global = iaUso.ObtenerPresupuestoGlobalDefault();
                var estado = iaUso.ObtenerEstadoConsumo(idEmpresa, cfg);
                int pct = estado.Presupuesto > 0 ? (int)Math.Min(100, estado.Consumido * 100 / estado.Presupuesto) : 0;

                var resumen = new ResumenIAEmpresaViewModel
                {
                    IdEmpresa = idEmpresa,
                    NombreEmpresa = empresa.Nombre,
                    PresupuestoOverride = cfg.PresupuestoTokensMensual,
                    PresupuestoGlobalDefault = global,
                    PresupuestoEfectivo = estado.Presupuesto,
                    Ilimitado = estado.Presupuesto <= 0,
                    ConsumidoMes = estado.Consumido,
                    PorcentajeConsumido = pct,
                    Config = cfg,
                    InicioPeriodo = estado.InicioPeriodo.ToString("dd/MM/yyyy"),
                    EnPool = estado.EnPool,
                    EmpresasEnPool = estado.EmpresasEnPool,
                    TieneGrupo = empresa.IdGrupoEmpresarial.HasValue,
                    NombreGrupo = empresa.NombreGrupoEmpresarial,
                    Usuarios = cfgEmpresa.ObtenerUsuariosDeEmpresa(idEmpresa)
                };

                return Json(new { ok = true, resumen }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionGlobalIAController.ObtenerResumenEmpresaIA");
                return Json(new { ok = false, mensaje = R("CfgIA_Emp_MsgErrorCargar") }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Guarda el control de IA de la empresa. <paramref name="presupuesto"/> null = usar el valor
        /// global; 0 = ilimitado; N = tope mensual propio. <paramref name="funciones"/> es un CSV de
        /// <see cref="IAFuncion"/> (vacío o todas = todas habilitadas).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GuardarConfigEmpresaIA(int idEmpresa, long? presupuesto, bool iaHabilitada, string politica,
            bool poolGrupo, int? diaCorte, string modeloPermitido, string funciones, long? topeDiarioUsuario, string contextoNegocio = null)
        {
            if ((presupuesto.HasValue && presupuesto.Value < 0)
                || (topeDiarioUsuario.HasValue && topeDiarioUsuario.Value < 0)
                || (diaCorte.HasValue && (diaCorte.Value < 1 || diaCorte.Value > 28))
                || (contextoNegocio != null && contextoNegocio.Length > 4000))
                return Json(new { ok = false, mensaje = R("CfgIA_Emp_MsgDatosInvalidos") });

            var elegidas = (funciones ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => FuncionesIA.Contains(f))
                .Distinct()
                .ToList();

            var empresa = new EmpresaService().ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
            if (empresa == null)
                return Json(new { ok = false, mensaje = R("Common_SinPermisos") });

            var nueva = new ConfigIAEmpresa
            {
                IdEmpresa = idEmpresa,
                PresupuestoTokensMensual = presupuesto,
                IaHabilitada = iaHabilitada,
                PoliticaAgotado = ConfiguracionIAEmpresaService.NormalizarPolitica(politica),
                // El pool solo tiene sentido si la empresa pertenece a un Grupo Empresarial.
                PoolGrupo = poolGrupo && empresa.IdGrupoEmpresarial.HasValue,
                DiaCorte = diaCorte,
                ModeloPermitido = string.IsNullOrWhiteSpace(modeloPermitido) ? null : modeloPermitido.Trim(),
                FuncionesPermitidas = (elegidas.Count == 0 || elegidas.Count == FuncionesIA.Length) ? null : elegidas,
                TopeDiarioUsuario = topeDiarioUsuario.GetValueOrDefault() > 0 ? topeDiarioUsuario : null,
                ContextoNegocio = string.IsNullOrWhiteSpace(contextoNegocio) ? null : contextoNegocio.Trim()
            };

            var servicio = new ConfiguracionIAEmpresaService();
            var anterior = servicio.ObtenerConfig(idEmpresa);
            var usuarioActual = UsuarioSesionHelper.UsuarioActual;
            bool ok = servicio.GuardarConfig(nueva, usuarioActual?.Id);
            if (ok)
            {
                IAModuloHelper.Invalidar(idEmpresa);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Editar,
                    "ConfiguracionIAEmpresa", idEmpresa.ToString(),
                    $"Control de IA actualizado para {empresa.Nombre}",
                    anterior, nueva, idEmpresa);
            }

            return Json(new { ok, mensaje = ok ? R("CfgIA_Emp_MsgGuardadoOk") : R("CfgIA_Emp_MsgErrorGuardar") });
        }

        /// <summary><paramref name="acceso"/> null = volver al valor por defecto (permitido);
        /// true/false = acceso explícito.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GuardarAccesoUsuarioIA(int idUsuario, bool? acceso)
        {
            bool ok = new ConfiguracionIAEmpresaService().GuardarAccesoUsuario(idUsuario, acceso);
            if (ok)
            {
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Usuarios, AuditoriaAccion.Editar,
                    "Usuarios", idUsuario.ToString(),
                    $"Acceso a consultas de IA actualizado (Id {idUsuario})",
                    null, new { idUsuario, acceso });
            }

            return Json(new { ok, mensaje = ok ? R("CfgIA_Emp_MsgGuardadoOk") : R("CfgIA_Emp_MsgErrorGuardar") });
        }

        private static bool EsModeloChat(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("o4", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase);
        }
    }
}
