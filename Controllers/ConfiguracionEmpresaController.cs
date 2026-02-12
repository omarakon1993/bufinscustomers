using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System;
using System.Linq;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    public class ConfiguracionEmpresaController : BaseController
    {
        private readonly ConfiguracionEmpresaService _configuracionService;

        public ConfiguracionEmpresaController()
        {
            _configuracionService = new ConfiguracionEmpresaService();
        }

        #region Vista Principal

        public ActionResult Index()
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    SetErrorMessage("Sesi�n no v�lida. Por favor, inicie sesi�n nuevamente.");
                    return RedirectToAction("Login", "Acceso");
                }

                var empresas = _configuracionService.ObtenerEmpresas();
                
                if (!UsuarioSesionHelper.EsSuperAdmin())
                {
                    empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();
                }

                ViewBag.Empresas = empresas;
                ViewBag.EsAdmin = UsuarioSesionHelper.EsSuperAdmin();
                
                return View("~/Views/Configuracion/ConfiguracionesEmpresas.cshtml");
            }
            catch (Exception ex)
            {
                SetErrorMessage($"Error al cargar la vista: {ex.Message}");
                return RedirectToAction("Index", "Home");
            }
        }

        #endregion

        #region Obtener Configuraci�n

        [HttpGet]
        public JsonResult ObtenerConfiguracion(int idEmpresa)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" }, JsonRequestBehavior.AllowGet);
                }

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != idEmpresa)
                {
                    return Json(new { success = false, message = "No tiene permisos para ver esta configuraci�n" }, JsonRequestBehavior.AllowGet);
                }

                var configuracion = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
                
                if (configuracion == null)
                {
                    configuracion = new ConfiguracionEmpresa
                    {
                        IdEmpresa = idEmpresa,
                        AnioEjecucion = DateTime.Now.Year,
                        Moneda = "CO$",
                        Unidad = "MILES",
                        SignoCreditos = string.Empty
                    };
                }

                return Json(new
                {
                    success = true,
                    configuracion = new
                    {
                        configuracion.Id,
                        configuracion.IdEmpresa,
                        configuracion.AnioEjecucion,
                        configuracion.SignoCreditos,
                        configuracion.Moneda,
                        configuracion.Unidad,
                        configuracion.NombreEmpresa,
                        EmpresasConsolidar = configuracion.EmpresasConsolidar.Select(e => new { e.Id, e.NombreEmpresa, e.Orden }),
                        Paises = configuracion.Paises.Select(p => new { p.Id, p.NombrePais, p.Orden }),
                        Categorias = configuracion.Categorias.Select(c => new { c.Id, c.NombreCategoria, c.Orden }),
                        Tipos = configuracion.Tipos.Select(t => new { t.Id, t.NombreTipo, t.Orden }),
                        LineasNegocio = configuracion.LineasNegocio.Select(l => new { l.Id, l.NombreLinea, l.Orden }),
                        Ajuste1 = configuracion.Ajuste1.Select(a => new { a.Id, a.NombreAjuste, a.Orden }),
                        Ajuste2 = configuracion.Ajuste2.Select(a => new { a.Id, a.NombreAjuste, a.Orden })
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        #endregion

        #region Guardar Configuraci�n B�sica

        [HttpPost]
        public JsonResult GuardarConfiguracionBasica(ConfiguracionEmpresa configuracion)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" });
                }

                if (!UsuarioSesionHelper.EsSuperAdmin() && usuario.IdEmpresa != configuracion.IdEmpresa)
                {
                    return Json(new { success = false, message = "No tiene permisos para modificar esta configuraci�n" });
                }

                if (configuracion.AnioEjecucion < 2000 || configuracion.AnioEjecucion > 2100)
                {
                    return Json(new { success = false, message = "El a�o de ejecuci�n debe estar entre 2000 y 2100" });
                }

                if (string.IsNullOrWhiteSpace(configuracion.Moneda))
                {
                    return Json(new { success = false, message = "Debe seleccionar una moneda" });
                }

                if (string.IsNullOrWhiteSpace(configuracion.Unidad))
                {
                    return Json(new { success = false, message = "Debe seleccionar una unidad" });
                }

                string mensaje;
                bool resultado = _configuracionService.GuardarConfiguracionBasica(
                    configuracion, 
                    usuario.Id, 
                    out mensaje
                );

                if (resultado)
                {
                    return Json(new { success = true, message = mensaje });
                }
                else
                {
                    return Json(new { success = false, message = mensaje });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        #endregion

        #region Agregar �tem

        [HttpPost]
        public JsonResult AgregarItem(string tipo, int idConfiguracion, string valor)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" });
                }

                if (string.IsNullOrWhiteSpace(valor))
                {
                    return Json(new { success = false, message = "El valor no puede estar vac�o" });
                }

                var tiposValidos = new[] { "Empresa", "Pais", "Categoria", "Tipo", "LineaNegocio", "Ajuste1", "Ajuste2" };
                if (!tiposValidos.Contains(tipo))
                {
                    return Json(new { success = false, message = "Tipo de configuraci�n no v�lido" });
                }

                string mensaje;
                int idInsertado;
                bool resultado = _configuracionService.AgregarItemConfiguracion(
                    tipo, 
                    idConfiguracion, 
                    valor.Trim(), 
                    out mensaje, 
                    out idInsertado
                );

                if (resultado)
                {
                    return Json(new { success = true, message = mensaje, id = idInsertado });
                }
                else
                {
                    return Json(new { success = false, message = mensaje });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        #endregion

        #region Eliminar �tem

        [HttpPost]
        public JsonResult EliminarItem(string tipo, int id)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" });
                }

                string mensaje;
                bool resultado = _configuracionService.EliminarItemConfiguracion(tipo, id, out mensaje);

                if (resultado)
                {
                    return Json(new { success = true, message = mensaje });
                }
                else
                {
                    return Json(new { success = false, message = mensaje });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        #endregion

        #region Actualizar Orden

        [HttpPost]
        public JsonResult ActualizarOrden(string tipo, string ids)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" });
                }

                if (string.IsNullOrWhiteSpace(ids))
                {
                    return Json(new { success = false, message = "No se proporcionaron IDs" });
                }

                string mensaje;
                bool resultado = _configuracionService.ActualizarOrdenConfiguracion(tipo, ids, out mensaje);

                if (resultado)
                {
                    return Json(new { success = true, message = mensaje });
                }
                else
                {
                    return Json(new { success = false, message = mensaje });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        #endregion

        #region M�todos de Utilidad

        [HttpGet]
        public JsonResult ObtenerEmpresas()
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" }, JsonRequestBehavior.AllowGet);
                }

                var empresas = _configuracionService.ObtenerEmpresas();
                
                if (!UsuarioSesionHelper.EsSuperAdmin())
                {
                    empresas = empresas.Where(e => e.Id == usuario.IdEmpresa).ToList();
                }

                return Json(new
                {
                    success = true,
                    empresas = empresas.Select(e => new { e.Id, e.Nombre })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" }, JsonRequestBehavior.AllowGet);
            }
        }

        #endregion
    }
}