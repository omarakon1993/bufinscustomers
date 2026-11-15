using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Linq;

namespace bufinscustomers.Controllers
{
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
