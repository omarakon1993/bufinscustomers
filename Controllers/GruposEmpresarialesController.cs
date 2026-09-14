using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    // Gestión de grupos empresariales y asignación de empresas.
    // Crear/Eliminar y la asignación de empresas al grupo (Gestionar/GuardarEmpresas) son
    // exclusivos de Super Admin. Un Admin de Empresa con el permiso ADMIN_CONFIG_GRUPOS_EMPRESARIALES
    // puede ver y editar (solo Nombre/Descripción/Activo) el único grupo al que pertenece su empresa.
    // El acceso de lectura que ese grupo habilita entre empresas hermanas se resuelve en EmpresaAccesoHelper.
    [ValidarSesion]
    public class GruposEmpresarialesController : BaseController
    {
        private const string PERMISO = "ADMIN_CONFIG_GRUPOS_EMPRESARIALES";

        private readonly GrupoEmpresarialService _service = new GrupoEmpresarialService();
        private readonly EmpresaService _empresaService = new EmpresaService();

        private static int? ObtenerIdGrupoDelUsuario(Usuarios usuario)
        {
            if (usuario?.IdEmpresa == null) return null;
            return EmpresaCacheHelper.ObtenerEmpresasCacheadas()
                .FirstOrDefault(e => e.Id == usuario.IdEmpresa.Value)?.IdGrupoEmpresarial;
        }

        private bool PuedeVerSuGrupo() =>
            UsuarioSesionHelper.EsAdminEmpresa() && UsuarioSesionHelper.TienePermiso(PERMISO);

        public ActionResult Index()
        {
            bool esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();
            if (!esSuperAdmin && !PuedeVerSuGrupo())
                return RedirectToAction("Index", "Home");

            ViewBag.EsSuperAdmin = esSuperAdmin;

            List<GrupoEmpresarial> grupos;
            if (esSuperAdmin)
            {
                grupos = _service.ObtenerTodos();
            }
            else
            {
                var idGrupo = ObtenerIdGrupoDelUsuario(UsuarioSesionHelper.UsuarioActual);
                var grupo = idGrupo.HasValue ? _service.ObtenerPorId(idGrupo.Value) : null;
                grupos = grupo != null ? new List<GrupoEmpresarial> { grupo } : new List<GrupoEmpresarial>();
            }

            return View("~/Views/Configuracion/GruposEmpresariales.cshtml", grupos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(GrupoEmpresarial grupo)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(grupo.Nombre))
                {
                    SetErrorMessage(R("Grupo_ErrorNombreObligatorio"));
                    return RedirectToAction("Index");
                }

                bool ok = _service.Crear(grupo);
                if (ok)
                {
                    SetSuccessMessage(R("Grupo_MensajeCreado"));
                    new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_GrupoCreado"), grupo.Nombre, "success", "/GruposEmpresariales");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Grupos, AuditoriaAccion.Crear,
                        "GruposEmpresariales", grupo.Id.ToString(), $"Grupo empresarial creado: {grupo.Nombre}", null, grupo);
                }
                else
                    SetErrorMessage(R("Grupo_ErrorCrear"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Grupo_ErrorCrear") + ": " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Editar(GrupoEmpresarial grupo)
        {
            bool esSuperAdmin = UsuarioSesionHelper.EsSuperAdmin();
            bool puedeEditarPropio = PuedeVerSuGrupo()
                && ObtenerIdGrupoDelUsuario(UsuarioSesionHelper.UsuarioActual) == grupo.Id;

            if (!esSuperAdmin && !puedeEditarPropio)
                return RedirectToAction("Index", "Home");

            try
            {
                if (string.IsNullOrWhiteSpace(grupo.Nombre))
                {
                    SetErrorMessage(R("Grupo_ErrorNombreObligatorio"));
                    return RedirectToAction("Index");
                }

                bool ok = _service.Editar(grupo);
                if (ok)
                {
                    SetSuccessMessage(R("Grupo_MensajeEditado"));
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Grupos, AuditoriaAccion.Editar,
                        "GruposEmpresariales", grupo.Id.ToString(), $"Grupo empresarial editado: {grupo.Nombre}", null, grupo);
                }
                else
                    SetErrorMessage(R("Grupo_ErrorEditar"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Grupo_ErrorEditar") + ": " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            try
            {
                var grupo = _service.ObtenerPorId(id);
                bool ok = _service.Eliminar(id);
                if (ok)
                {
                    SetSuccessMessage(R("Grupo_MensajeEliminado"));
                    new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_GrupoEliminado"), grupo?.Nombre, "warning", "/GruposEmpresariales");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Grupos, AuditoriaAccion.Eliminar,
                        "GruposEmpresariales", id.ToString(), $"Grupo empresarial eliminado: {grupo?.Nombre} (Id {id})", grupo, null);
                }
                else
                    SetErrorMessage(R("Grupo_ErrorEliminar"));
            }
            catch (Exception ex)
            {
                SetErrorMessage(R("Grupo_ErrorEliminar") + ": " + ex.Message);
            }

            return RedirectToAction("Index");
        }

        // Pantalla de checkboxes para asignar empresas al grupo.
        public ActionResult Gestionar(int id)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return RedirectToAction("Index", "Home");

            var grupo = _service.ObtenerPorId(id);
            if (grupo == null)
            {
                SetErrorMessage(R("Grupo_ErrorNoEncontrado"));
                return RedirectToAction("Index");
            }

            var todasLasEmpresas = _empresaService.ObtenerEmpresas();
            var empresasSeleccionables = todasLasEmpresas.Select(e => new EmpresaSeleccionableViewModel
            {
                Id = e.Id,
                Nombre = e.Nombre,
                Abreviatura = e.Abreviatura,
                Seleccionada = e.IdGrupoEmpresarial == id,
                NombreOtroGrupo = (e.IdGrupoEmpresarial.HasValue && e.IdGrupoEmpresarial != id) ? e.NombreGrupoEmpresarial : null
            }).OrderBy(e => e.Nombre).ToList();

            var viewModel = new GestionarGrupoEmpresasViewModel
            {
                Grupo = grupo,
                Empresas = empresasSeleccionables
            };

            return View("~/Views/Configuracion/GestionarGrupoEmpresas.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardarEmpresas(int idGrupo, List<int> idsEmpresas)
        {
            if (!UsuarioSesionHelper.EsSuperAdmin())
                return Json(new { success = false, message = R("Grupo_ErrorNoPermisos") });

            try
            {
                var grupo = _service.ObtenerPorId(idGrupo);
                if (grupo == null)
                    return Json(new { success = false, message = R("Grupo_ErrorNoEncontrado") });

                idsEmpresas = idsEmpresas ?? new List<int>();

                bool ok = _service.AsignarEmpresas(idGrupo, idsEmpresas);
                if (ok)
                {
                    new NotificacionesService().Crear(UsuarioSesionHelper.UsuarioActual?.Id ?? 0, R("Notif_GrupoEmpresasActualizadas"), grupo.Nombre, "success", "/GruposEmpresariales");
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Grupos, AuditoriaAccion.Asignar,
                        "GruposEmpresariales", idGrupo.ToString(),
                        $"Empresas del grupo '{grupo.Nombre}' actualizadas ({idsEmpresas.Count})", null, new { idsEmpresas });
                    return Json(new { success = true, message = R("Grupo_MensajeEmpresasAsignadas") });
                }

                return Json(new { success = false, message = R("Grupo_ErrorAsignarEmpresas") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = R("Grupo_ErrorAsignarEmpresas") + ": " + ex.Message });
            }
        }
    }
}
