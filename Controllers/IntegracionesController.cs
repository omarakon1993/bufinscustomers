using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace bufinscustomers.Controllers
{
    /// <summary>Semáforo de una integración ya traducido (lo pinta la pantalla Integraciones).</summary>
    public class SaludIntegracionVM
    {
        /// <summary>problema | atencion | ok | inactiva</summary>
        public string Nivel { get; set; }
        public List<string> Textos { get; set; } = new List<string>();
    }

    /// <summary>
    /// Administración de las integraciones de la API de ingesta (credenciales por empresa). Super Admin y Admin de Empresa:
    /// el Admin de Empresa solo ve y modifica las de su empresa o grupo (<see cref="EmpresaAccesoHelper"/>). La clave en claro
    /// solo viaja en la respuesta de Crear/Rotar y nunca se registra (ni en el log ni en la auditoría).
    /// Detalle en <c>.claude/notas/api-ingesta.md</c>.
    /// </summary>
    [ValidarSesion]
    [RequierePermiso("ADMIN_CONFIG_INTEGRACIONES")]
    public class IntegracionesController : BaseController
    {
        private readonly IntegracionesService _servicio = new IntegracionesService();
        private readonly WebhooksService _webhooks = new WebhooksService();
        private readonly IntegracionesActividadService _actividad = new IntegracionesActividadService();

        private static bool PuedeAdministrar() => UsuarioSesionHelper.EsSuperAdmin() || UsuarioSesionHelper.EsAdminEmpresa();

        public ActionResult Index()
        {
            if (!PuedeAdministrar()) return RedirectToAction("Index", "Home");

            var usuario = UsuarioSesionHelper.UsuarioActual;
            bool disponible = IntegracionesService.Disponible();
            var permitidas = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario);   // null = todas

            ViewBag.Disponible = disponible;
            ViewBag.BaseApi = Request.Url.Scheme + "://" + Request.Url.Authority + "/api/v1";
            var lista = disponible ? _servicio.Listar(permitidas) : new System.Collections.Generic.List<Integracion>();

            // Semáforo de cada integración (en palabras de negocio); si algo falla, la pantalla funciona igual sin él.
            var salud = new Dictionary<int, SaludIntegracionVM>();
            foreach (var i in lista)
            {
                try { salud[i.Id] = TraducirSalud(_actividad.Salud(i)); }
                catch (Exception ex) { AppLogger.Warn("Integraciones.Salud: " + ex.Message); }
            }
            ViewBag.Salud = salud;

            // Agente de carpeta descargable (el asistente ofrece el botón y la huella para verificar la descarga)
            string huellaAgente = HuellaAgente(out long bytesAgente);
            ViewBag.AgenteHuella = huellaAgente;
            ViewBag.AgenteMB = Math.Round(bytesAgente / 1048576.0, 1);
            return View("~/Views/Configuracion/Integraciones.cshtml", lista);
        }

        /// <summary>Usuarios de la empresa que pueden actuar como usuario técnico (alimenta el desplegable del modal «Nueva»).</summary>
        [HttpGet]
        public ActionResult UsuariosDeEmpresa(int idEmpresa)
        {
            if (!PuedeAdministrar() || !EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, idEmpresa))
                return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            var usuarios = _servicio.ListarUsuariosElegibles(idEmpresa).Select(u => new { id = u.Id, nombre = u.Nombre, usuario = u.Usuario });
            return Json(new { success = true, usuarios });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(int idEmpresa, string nombre, int idUsuarioTecnico, bool permiteConfirmar = false, string ipPermitidas = null, bool sandbox = false)
        {
            if (!PuedeAdministrar() || !EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, idEmpresa))
                return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                var yo = UsuarioSesionHelper.UsuarioActual;
                var r = _servicio.Crear(idEmpresa, nombre, idUsuarioTecnico, permiteConfirmar, ipPermitidas, yo?.Id, sandbox);
                if (!r.ok) return Json(new { success = false, message = TraducirError(r.error) });

                var integ = _servicio.Obtener(r.idIntegracion);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Crear, "Integracion", r.idIntegracion.ToString(),
                    $"Creó la integración API '{integ?.Nombre}' ({(sandbox ? "PRUEBAS" : "producción")}) de '{integ?.NombreEmpresa}' (usuario técnico: {integ?.NombreUsuarioTecnico}; confirmación por API: {(permiteConfirmar ? "permitida" : "no permitida")}).",
                    null, new { integ?.Nombre, idEmpresa, idUsuarioTecnico, permiteConfirmar, sandbox, ipPermitidas = integ?.IpPermitidas, prefijo = integ?.Prefijo },
                    idEmpresa, entidadNombre: integ?.Nombre);

                return Json(new { success = true, message = string.Format(R("Integ_Creada"), integ?.Nombre), clave = r.claveEnClaro, nombre = integ?.Nombre });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(int id, string nombre, bool activo, bool permiteConfirmar, string ipPermitidas = null)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                if (!_servicio.Actualizar(id, nombre, activo, permiteConfirmar, ipPermitidas, out string error))
                    return Json(new { success = false, message = error == null ? R("Integ_ErrNoEditable") : TraducirError(error) });

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Editar, "Integracion", id.ToString(),
                    $"Editó la integración API '{integ.Nombre}' de '{integ.NombreEmpresa}'.",
                    new { integ.Nombre, integ.Activo, integ.PermiteConfirmar, integ.IpPermitidas },
                    new { Nombre = nombre?.Trim(), activo, permiteConfirmar, ipPermitidas },
                    integ.IdEmpresa, entidadNombre: nombre?.Trim());
                return Json(new { success = true, message = string.Format(R("Integ_Editada"), nombre?.Trim()) });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        /// <summary>Genera una clave nueva: la anterior deja de servir al instante.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Rotar(int id)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                var r = _servicio.Rotar(id);
                if (!r.ok) return Json(new { success = false, message = R("Integ_ErrNoEditable") });

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Editar, "Integracion", id.ToString(),
                    $"Rotó la clave de la integración API '{integ.Nombre}' de '{integ.NombreEmpresa}' (la anterior quedó inválida).",
                    new { prefijoAnterior = integ.Prefijo }, null, integ.IdEmpresa, entidadNombre: integ.Nombre, severidad: AuditoriaSeveridad.Advertencia);
                return Json(new { success = true, message = string.Format(R("Integ_Rotada"), integ.Nombre), clave = r.clave, nombre = integ.Nombre });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        /// <summary>Revoca la credencial de forma definitiva (no se reactiva: se crea otra).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Revocar(int id)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                if (!_servicio.Revocar(id)) return Json(new { success = false, message = R("Integ_ErrNoEditable") });

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Eliminar, "Integracion", id.ToString(),
                    $"Revocó la integración API '{integ.Nombre}' de '{integ.NombreEmpresa}'.",
                    new { integ.Nombre, integ.Prefijo }, null, integ.IdEmpresa, entidadNombre: integ.Nombre, severidad: AuditoriaSeveridad.Advertencia);
                return Json(new { success = true, message = string.Format(R("Integ_Revocada"), integ.Nombre) });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        // ── Panel de actividad ───────────────────────────────────────────────────────────

        /// <summary>Todo lo que hace la integración, en lenguaje de negocio: semáforo, conexión, últimos envíos, modelos y avisos. Solo si el usuario tiene acceso a su empresa.</summary>
        [HttpGet]
        public ActionResult Actividad(int id)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                var salud = TraducirSalud(_actividad.Salud(integ));
                string ModoTexto(string m) => m == "ejecucion" ? R("RevisarCargue_ModoEjecucion") : R("RevisarCargue_ModoHistorico");

                var cargues = _actividad.UltimosCargues(id, 10).Select(c =>
                {
                    string sit = R("Integ_Pn_Sit_" + c.Situacion);
                    if (!string.IsNullOrEmpty(c.ResueltoPor) && (c.Resolucion == "Confirmado" || c.Resolucion == "Descartado"))
                        sit += " · " + R(c.ResueltoPor == "integracion" ? "Integ_Pn_PorIntegracion" : "Integ_Pn_PorPersona");
                    bool revisable = c.Situacion == "PendienteRevision" || c.Situacion == "ConErrores";
                    return new
                    {
                        idLote = c.IdLote, fecha = c.Recibido.ToString("dd/MM HH:mm"), archivo = c.Archivo, periodo = c.Anio + " · " + ModoTexto(c.Modo),
                        filas = c.Filas, errores = c.Errores, advertencias = c.Advertencias, situacion = c.Situacion, situacionTexto = sit,
                        url = revisable ? Url.Action("RevisarCargue", "Datos", new { idLote = c.IdLote }) : null
                    };
                }).ToList();

                var modelos = _actividad.UltimasEjecuciones(id, 5).Select(e => new
                {
                    id = e.Id, estado = e.Estado, estadoTexto = R("Integ_Pn_Ej_" + e.Estado), inicio = e.Inicio.ToString("dd/MM HH:mm"),
                    fin = e.Fin?.ToString("HH:mm:ss")
                }).ToList();

                var webhookCfg = WebhooksService.Disponible() ? _webhooks.ObtenerConfig(id) : null;
                var entregas = webhookCfg != null && !string.IsNullOrEmpty(webhookCfg.Url)
                    ? _webhooks.UltimasEntregas(id, 5).Select(e => new { fecha = e.Fecha.ToString("dd/MM HH:mm:ss"), evento = e.Evento, estado = e.Estado, intentos = e.Intentos }).ToList()
                    : null;

                return Json(new
                {
                    success = true, nombre = integ.Nombre, nivel = salud.Nivel, mensajes = salud.Textos,
                    ultimoUso = integ.FechaUltimoUso?.ToString("dd/MM/yyyy HH:mm"), ipUltimoUso = integ.IpUltimoUso,
                    seguimientoDisponible = IntegracionesActividadService.Disponible(),
                    cargues, modelos, entregas
                });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        private SaludIntegracionVM TraducirSalud(SaludIntegracion s)
        {
            var vm = new SaludIntegracionVM { Nivel = s.Nivel };
            foreach (var m in s.Mensajes)
            {
                string plantilla = R("Integ_Pn_S_" + m.Codigo);
                vm.Textos.Add(m.Args != null && m.Args.Length > 0 ? string.Format(plantilla, m.Args.Cast<object>().ToArray()) : plantilla);
            }
            return vm;
        }

        // ── Webhook saliente ─────────────────────────────────────────────────────────────

        [HttpGet]
        public ActionResult WebhookEstado(int id)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            if (!WebhooksService.Disponible()) return Json(new { success = true, disponible = false });
            var cfg = _webhooks.ObtenerConfig(id);
            return Json(new
            {
                success = true, disponible = true, nombre = integ.Nombre, url = cfg.Url, tieneSecreto = cfg.TieneSecreto,
                entregas = _webhooks.UltimasEntregas(id).Select(e => new
                {
                    fecha = e.Fecha.ToString("dd/MM HH:mm:ss"), evento = e.Evento, estado = e.Estado, intentos = e.Intentos, codigo = e.Codigo, error = e.Error
                })
            });
        }

        /// <summary>Guarda la URL (vacía = desactivar). La primera vez genera el secreto y lo devuelve (única vez que se ve).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult WebhookGuardar(int id, string url)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                var r = _webhooks.Guardar(id, url);
                if (!r.ok) return Json(new { success = false, message = TraducirErrorWebhook(r.error) });

                bool quitado = string.IsNullOrWhiteSpace(url);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Editar, "Integracion", id.ToString(),
                    quitado ? $"Desactivó el webhook de la integración API '{integ.Nombre}' de '{integ.NombreEmpresa}'."
                            : $"Configuró el webhook de la integración API '{integ.Nombre}' de '{integ.NombreEmpresa}' (destino: {HostDe(url)}){(r.secretoNuevo != null ? "; se generó su secreto" : "")}.",
                    null, new { host = quitado ? null : HostDe(url) }, integ.IdEmpresa, entidadNombre: integ.Nombre);

                return Json(new { success = true, message = R(quitado ? "Integ_Wh_Quitado" : "Integ_Wh_Guardado"), secreto = r.secretoNuevo });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult WebhookRotar(int id)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            try
            {
                var r = _webhooks.RotarSecreto(id);
                if (!r.ok) return Json(new { success = false, message = TraducirErrorWebhook(r.error) });
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Editar, "Integracion", id.ToString(),
                    $"Rotó el secreto del webhook de la integración API '{integ.Nombre}' de '{integ.NombreEmpresa}'.",
                    null, null, integ.IdEmpresa, entidadNombre: integ.Nombre, severidad: AuditoriaSeveridad.Advertencia);
                return Json(new { success = true, message = R("Integ_Wh_SecretoRotado"), secreto = r.secreto });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult WebhookProbar(int id)
        {
            var integ = Propia(id);
            if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            var r = _webhooks.EnviarPrueba(id);
            if (r.ok) return Json(new { success = true, message = string.Format(R("Integ_Wh_PruebaOk"), r.codigo) });
            string motivo = r.error == "sin_webhook" ? R("Integ_Wh_ErrSinWebhook") : (r.codigo.HasValue ? "HTTP " + r.codigo : (r.error ?? "?"));
            return Json(new { success = false, message = string.Format(R("Integ_Wh_PruebaFallo"), motivo) });
        }

        // ── Agente de carpeta (descarga) ─────────────────────────────────────────────────

        private const string RutaAgente = "~/App_Data/Agente/AgenteBufins.exe";
        private static string _agenteHash, _agenteHashDe;
        private static readonly object _lockHash = new object();

        /// <summary>SHA-256 del ejecutable (en mayúsculas), recalculado solo si el archivo cambió. Null si no está.</summary>
        private string HuellaAgente(out long bytes)
        {
            bytes = 0;
            try
            {
                var fi = new System.IO.FileInfo(Server.MapPath(RutaAgente));
                if (!fi.Exists) return null;
                bytes = fi.Length;
                string marca = fi.LastWriteTimeUtc.Ticks + ":" + fi.Length;
                lock (_lockHash)
                {
                    if (_agenteHashDe != marca)
                    {
                        using (var fs = fi.OpenRead())
                            _agenteHash = BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(fs)).Replace("-", "");
                        _agenteHashDe = marca;
                    }
                    return _agenteHash;
                }
            }
            catch (Exception ex) { AppLogger.Warn("Integraciones.HuellaAgente: " + ex.Message); return null; }
        }

        /// <summary>
        /// Descarga el agente de carpeta (<c>App_Data/Agente/AgenteBufins.exe</c>, no público: lo deja ahí el despliegue con
        /// <c>Publicar-Agente.ps1</c>). Solo Super Admin y Admin de Empresa; se audita cada descarga.
        /// </summary>
        [HttpGet]
        public ActionResult DescargarAgente()
        {
            if (!PuedeAdministrar()) return RedirectToAction("Index", "Home");
            string huella = HuellaAgente(out long bytes);
            if (huella == null)
            {
                SetErrorMessage(R("Integ_As_AgenteNoDisp"));
                return RedirectToAction("Index");
            }
            new AuditoriaService().RegistrarCambio(AuditoriaTipo.Integraciones, AuditoriaAccion.Exportar, "AgenteBufins", huella.Substring(0, 12),
                $"Descargó el agente de carpeta AgenteBufins.exe (SHA-256 {huella.Substring(0, 12)}…).", null, new { huella, bytes },
                UsuarioSesionHelper.UsuarioActual?.IdEmpresa, entidadNombre: "AgenteBufins.exe");
            Response.Headers["Cache-Control"] = "no-store";
            return File(System.IO.File.ReadAllBytes(Server.MapPath(RutaAgente)), "application/octet-stream", "AgenteBufins.exe");
        }

        // ── Asistente de conexión ────────────────────────────────────────────────────────

        private static readonly Dictionary<int, DateTime> _ultimaAyuda = new Dictionary<int, DateTime>();
        private static readonly object _lockAyuda = new object();

        /// <summary>
        /// Opción «Necesito que me ayuden» del asistente: avisa (campana) a los Super Admin con el caso y el contacto.
        /// No guarda nada más; límite de 1 solicitud por usuario cada 2 minutos. La integración es opcional.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SolicitarAyuda(int? idIntegracion, string mensaje, string contacto)
        {
            if (!PuedeAdministrar()) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            mensaje = (mensaje ?? "").Trim();
            contacto = (contacto ?? "").Trim();
            if (mensaje.Length == 0) return Json(new { success = false, message = R("Integ_As_ErrMensaje") });
            if (mensaje.Length > 1000 || contacto.Length > 200) return Json(new { success = false, message = R("Integ_As_ErrLargo") });

            Integracion integ = null;
            if (idIntegracion.HasValue)
            {
                integ = Propia(idIntegracion.Value);
                if (integ == null) return Json(new { success = false, message = R("Integ_ErrSinAcceso") });
            }

            var yo = UsuarioSesionHelper.UsuarioActual;
            lock (_lockAyuda)
            {
                if (_ultimaAyuda.TryGetValue(yo.Id, out var ultima) && (DateTime.UtcNow - ultima).TotalMinutes < 2)
                    return Json(new { success = false, message = R("Integ_As_ErrEspere") });
                _ultimaAyuda[yo.Id] = DateTime.UtcNow;
            }

            try
            {
                string quien = $"{yo.Nombre} {yo.Apellidos}".Trim();
                string cual = integ != null ? $"{integ.Nombre} ({integ.NombreEmpresa})" : R("Integ_As_SinIntegracion");
                string texto = string.Format(R("Notif_AyudaIntegracionMsg"), quien, cual, string.IsNullOrEmpty(contacto) ? "-" : contacto, mensaje);
                string url = Url.Action("Index", "Integraciones");
                var notif = new NotificacionesService();
                foreach (var idSuper in _servicio.ObtenerIdsSuperAdmin())
                    notif.Crear(idSuper, R("Notif_AyudaIntegracion"), texto, "info", url);
                return Json(new { success = true, message = R("Integ_As_AyudaEnviada") });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        private static string HostDe(string url) => Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var u) ? u.Host : "?";

        private string TraducirErrorWebhook(string codigo)
        {
            switch (codigo)
            {
                case "url_invalida": return R("Integ_Wh_ErrUrl");
                case "solo_https": return R("Integ_Wh_ErrSoloHttps");
                case "destino_privado": return R("Integ_Wh_ErrDestinoPrivado");
                case "host_no_resuelve": return R("Integ_Wh_ErrHostNoResuelve");
                case "cifrado": return R("Integ_Wh_ErrCifrado");
                default: return R("Integ_Wh_ErrNoDisponible");
            }
        }

        /// <summary>La integración, solo si el usuario puede administrar y tiene acceso a su empresa (si no, null).</summary>
        private Integracion Propia(int id)
        {
            if (!PuedeAdministrar()) return null;
            var integ = _servicio.Obtener(id);
            return integ != null && EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, integ.IdEmpresa) ? integ : null;
        }

        /// <summary>Traduce los códigos de error del servicio (que no lee recursos de idioma).</summary>
        private string TraducirError(string codigo)
        {
            if (codigo == null) return R("Integ_ErrNoEditable");
            if (codigo.StartsWith("ip:")) return string.Format(R("Integ_ErrIp"), codigo.Substring(3));
            switch (codigo)
            {
                case "nombre": return R("Integ_ErrNombre");
                case "usuario": return R("Integ_ErrUsuario");
                case "usuario_en_uso": return R("Integ_ErrUsuarioEnUso");
                case "sandbox_no_disponible": return R("Integ_ErrSandboxNoDisponible");
                case "ips_largo": return R("Integ_ErrIpsLargo");
                default: return R("Integ_ErrNoEditable");
            }
        }
    }
}
