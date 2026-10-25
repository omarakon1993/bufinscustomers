using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Script.Serialization;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Supuestos del balance del ppto: administración por empresa de dbo.Rel_BalancePpto
    /// (edición en grilla, Excel, autocompletado e insights de completitud).
    /// Solo Valor es editable; las filas CALCULO son informativas.
    /// </summary>
    [ValidarSesion]
    public class SupuestosBalancePptoController : BaseController
    {
        private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly SupuestosBalancePptoService _svc = new SupuestosBalancePptoService();

        // Acceso por empresa/grupo (nunca comparar usuario.IdEmpresa directo)
        private bool Acceso(int idEmpresa) => EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, idEmpresa);

        private string NombreEmpresa(int idEmpresa)
        {
            var e = EmpresaCacheHelper.ObtenerEmpresasCacheadas().FirstOrDefault(x => x.Id == idEmpresa);
            return e != null ? e.Nombre : idEmpresa.ToString();
        }

        // ───────────── Vista ─────────────

        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public ActionResult Index()
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            var empresas = new EmpresaService().ObtenerEmpresas();
            if (!UsuarioSesionHelper.EsSuperAdmin())
            {
                var ids = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new List<int>();
                empresas = empresas.Where(e => ids.Contains(e.Id)).ToList();
            }
            ViewBag.Empresas = empresas;
            ViewBag.IdEmpresaUsuario = usuario?.IdEmpresa ?? 0;
            ViewBag.Disponible = _svc.Disponible();
            return View("~/Views/Configuracion/SupuestosBalancePpto.cshtml");
        }

        // ───────────── Datos ─────────────

        [HttpGet]
        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public JsonResult Obtener(int idEmpresa)
        {
            try
            {
                if (!Acceso(idEmpresa)) return Json(new { success = false, message = R("SBP_SinAcceso") }, JsonRequestBehavior.AllowGet);
                var resumen = _svc.ObtenerResumen(idEmpresa);
                if (!resumen.Disponible) return Json(new { success = true, disponible = false }, JsonRequestBehavior.AllowGet);

                var filas = _svc.Obtener(idEmpresa).Select(f => new
                {
                    id = f.Id, mes = f.Mes, descripcion = f.Descripcion, descripcionBase = f.DescripcionBase,
                    formula = f.Formula, mesBase = f.MesBase, tipoUnidad = f.TipoUnidad, calculo = f.EsCalculo, valor = f.Valor,
                    modificado = f.FechaModificacion.HasValue ? f.FechaModificacion.Value.ToString("yyyy-MM-dd HH:mm") : null
                });
                return Json(new
                {
                    success = true, disponible = true, filas,
                    resumen = ResumenJson(resumen)
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }, JsonRequestBehavior.AllowGet); }
        }

        private object ResumenJson(ResumenSupuestos r) => new
        {
            tieneFilas = r.TieneFilas, total = r.Total, diligenciados = r.Diligenciados, pendientes = r.Pendientes,
            calculo = r.Calculo, porcentaje = r.Porcentaje, lineasConPendientes = r.LineasConPendientes,
            ultimaCarga = r.UltimaCarga.HasValue ? r.UltimaCarga.Value.ToString("yyyy-MM-dd HH:mm") : null,
            ultimaModificacion = r.UltimaModificacion.HasValue ? r.UltimaModificacion.Value.ToString("yyyy-MM-dd HH:mm") : null,
            ultimoUsuario = r.UltimoUsuario,
            lineas = r.Lineas.Select(l => new { id = l.Id, descripcion = l.Descripcion, pendientes = l.Pendientes, total = l.Total, meses = l.MesesPendientes })
        };

        // ───────────── Edición ─────────────

        [HttpPost, ValidateAntiForgeryToken]
        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public JsonResult Guardar(int idEmpresa, string cambiosJson)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (!Acceso(idEmpresa)) return Json(new { success = false, message = R("SBP_SinAcceso") });

                var cambios = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<CambioSupuesto>>(cambiosJson ?? "[]");
                if (cambios == null || cambios.Count == 0) return Json(new { success = false, message = R("SBP_SinCambios") });
                if (cambios.Any(c => c.Mes < 1 || c.Mes > 12)) return Json(new { success = false, message = R("SBP_Hall_MesInvalido") });

                int n = _svc.GuardarValores(idEmpresa, usuario.Id, cambios);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Editar,
                    "SupuestosBalancePpto", idEmpresa.ToString(),
                    $"Supuestos del balance del ppto de {NombreEmpresa(idEmpresa)}: {n} valor(es) editado(s) manualmente",
                    null, cambios, idEmpresa: idEmpresa);
                return Json(new { success = true, actualizadas = n, resumen = ResumenJson(_svc.ObtenerResumen(idEmpresa)) });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public JsonResult Inicializar(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (!Acceso(idEmpresa)) return Json(new { success = false, message = R("SBP_SinAcceso") });
                int n = _svc.Inicializar(idEmpresa, usuario.Id);
                if (n == 0) return Json(new { success = false, message = R("SBP_YaInicializada") });
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Crear,
                    "SupuestosBalancePpto", idEmpresa.ToString(),
                    $"Supuestos del balance del ppto inicializados (valores vacíos) para {NombreEmpresa(idEmpresa)}: {n} fila(s)",
                    null, new { filas = n }, idEmpresa: idEmpresa);
                return Json(new { success = true, filas = n });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        // ───────────── Excel ─────────────

        [HttpPost, ValidateAntiForgeryToken]
        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public JsonResult CargarExcel(int idEmpresa, HttpPostedFileBase archivoExcel)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                if (!Acceso(idEmpresa)) return Json(new { success = false, message = R("SBP_SinAcceso") });
                if (archivoExcel == null || archivoExcel.ContentLength == 0) return Json(new { success = false, message = R("SBP_ArchivoVacio") });
                string ext = Path.GetExtension(archivoExcel.FileName ?? "").ToLowerInvariant();
                if (ext != ".xlsx" && ext != ".xlsm") return Json(new { success = false, message = R("SBP_ArchivoExt") });

                ResultadoCargaSupuestos res;
                using (var ms = new MemoryStream())
                {
                    archivoExcel.InputStream.CopyTo(ms);
                    ms.Position = 0;
                    try
                    {
                        using (var wb = new XLWorkbook(ms))
                            res = _svc.CargarDesdeExcel(idEmpresa, usuario.Id, wb);
                    }
                    catch (Exception exLectura) when (!(exLectura is System.Data.SqlClient.SqlException))
                    {
                        AppLogger.Error(exLectura, "SupuestosBalancePpto.CargarExcel lectura");
                        return Json(new { success = false, message = R("SBP_ArchivoIlegible") });
                    }
                }

                foreach (var h in res.Hallazgos)
                    h.Mensaje = string.Format(R("SBP_Hall_" + h.Codigo), h.Dato);

                if (res.Exito)
                {
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Cargues, AuditoriaAccion.Crear,
                        "SupuestosBalancePpto", idEmpresa.ToString(),
                        $"Carga Excel de supuestos del balance del ppto de {NombreEmpresa(idEmpresa)}: {res.FilasActualizadas} valor(es) actualizado(s) de {res.FilasLeidas} fila(s) leídas ({archivoExcel.FileName})",
                        null, new { archivo = archivoExcel.FileName, res.FilasLeidas, res.FilasActualizadas, advertencias = res.Hallazgos.Count }, idEmpresa: idEmpresa);
                }
                else if (res.Hallazgos.Count > 0)
                {
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Cargues, AuditoriaAccion.Crear,
                        "SupuestosBalancePpto", idEmpresa.ToString(),
                        $"Carga Excel de supuestos RECHAZADA para {NombreEmpresa(idEmpresa)}: {res.Hallazgos.Count(x => x.Severidad == "Error")} error(es)",
                        null, new { archivo = archivoExcel.FileName }, idEmpresa: idEmpresa);
                }

                return Json(new
                {
                    success = res.Exito,
                    message = res.Exito ? string.Format(R("SBP_CargaOk"), res.FilasActualizadas) : R("SBP_CargaErrores"),
                    actualizadas = res.FilasActualizadas,
                    leidas = res.FilasLeidas,
                    hallazgos = res.Hallazgos.Select(h => new { fila = h.Fila, columna = h.Columna, severidad = h.Severidad, mensaje = h.Mensaje }),
                    resumen = res.Exito ? ResumenJson(_svc.ObtenerResumen(idEmpresa)) : null
                });
            }
            catch (Exception ex) { return Json(new { success = false, message = MensajeError(ex) }); }
        }

        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public ActionResult DescargarExcel(int idEmpresa)
        {
            if (!Acceso(idEmpresa)) return RedirectToAction("Index");
            try
            {
                var bytes = _svc.GenerarExcel(idEmpresa, soloEstructura: false);
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Exportar,
                    "SupuestosBalancePpto", idEmpresa.ToString(), $"Descarga Excel de supuestos del balance del ppto de {NombreEmpresa(idEmpresa)}",
                    null, null, idEmpresa: idEmpresa);
                return File(bytes, XlsxMime, $"Supuestos_Balance_Ppto_{Limpio(NombreEmpresa(idEmpresa))}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            }
            catch (Exception ex) { SetErrorMessage(MensajeError(ex)); return RedirectToAction("Index"); }
        }

        [RequierePermiso(SupuestosBalancePptoService.CodigoMenu)]
        public ActionResult DescargarPlantilla(int idEmpresa)
        {
            if (!Acceso(idEmpresa)) return RedirectToAction("Index");
            try
            {
                var bytes = _svc.GenerarExcel(idEmpresa, soloEstructura: true);
                return File(bytes, XlsxMime, $"Plantilla_Supuestos_Balance_Ppto_{Limpio(NombreEmpresa(idEmpresa))}.xlsx");
            }
            catch (Exception ex) { SetErrorMessage(MensajeError(ex)); return RedirectToAction("Index"); }
        }

        private static string Limpio(string s)
            => new string((s ?? "").Where(c => char.IsLetterOrDigit(c)).ToArray());

        // ───────────── Aviso para Ejecución de Modelos ─────────────

        /// <summary>
        /// Estado de completitud para el aviso de Ejecución de Modelos. Sin [RequierePermiso]: lo consulta
        /// quien ejecuta modelos; solo exige acceso a la empresa y devuelve si puede abrir la opción.
        /// </summary>
        [HttpGet]
        public JsonResult EstadoPendientes(int idEmpresa)
        {
            try
            {
                if (!Acceso(idEmpresa)) return Json(new { mostrar = false }, JsonRequestBehavior.AllowGet);
                int pend = _svc.ContarPendientes(idEmpresa);
                if (pend == -1)
                {
                    // Sin filas: también es un "no completo" (solo si el módulo está instalado)
                    bool instalado = _svc.Disponible();
                    if (!instalado) return Json(new { mostrar = false }, JsonRequestBehavior.AllowGet);
                }
                if (pend == 0) return Json(new { mostrar = false }, JsonRequestBehavior.AllowGet);

                bool ingles = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "en";
                _svc.ObtenerMenu(ingles, out string nombre, out string controller, out string action);
                bool puede = UsuarioSesionHelper.TienePermiso(SupuestosBalancePptoService.CodigoMenu);
                return Json(new
                {
                    mostrar = true,
                    sinFilas = pend == -1,
                    pendientes = pend < 0 ? 0 : pend,
                    menuNombre = nombre ?? R("SBP_MenuNombreDefecto"),
                    url = puede ? Url.Action(action, controller, new { idEmpresa }) : null
                }, JsonRequestBehavior.AllowGet);
            }
            catch { return Json(new { mostrar = false }, JsonRequestBehavior.AllowGet); }
        }
    }
}
