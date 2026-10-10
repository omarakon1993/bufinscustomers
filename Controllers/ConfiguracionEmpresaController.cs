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
                    var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new System.Collections.Generic.List<int>();
                    empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
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

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
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
                        Ajuste2 = configuracion.Ajuste2.Select(a => new { a.Id, a.NombreAjuste, a.Orden }),
                        AnosHistoricos = configuracion.AnosHistoricos.Select(a => new { a.Id, a.NombreAno, a.Orden })
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
        [ValidateAntiForgeryToken]
        public JsonResult GuardarConfiguracionBasica(ConfiguracionEmpresa configuracion)
        {
            try
            {
                var usuario = UsuarioSesionHelper.UsuarioActual;
                
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Sesi�n no v�lida" });
                }

                if (!EmpresaAccesoHelper.TieneAcceso(usuario, configuracion.IdEmpresa))
                {
                    return Json(new { success = false, message = "No tiene permisos para modificar esta configuraci�n" });
                }

                if (configuracion.AnioEjecucion < 2000 || configuracion.AnioEjecucion > 2100)
                {
                    return Json(new { success = false, message = "El a�o de ejecuci�n debe estar entre 2000 y 2100" });
                }

                var configActual = _configuracionService.ObtenerConfiguracionPorEmpresa(configuracion.IdEmpresa);
                if (configActual != null && configActual.AnioEjecucion >= 2000 && configActual.AnioEjecucion != configuracion.AnioEjecucion)
                {
                    return Json(new { success = false, message = $"No puede cambiar el a�o de ejecuci�n directamente (actual: {configActual.AnioEjecucion}). Use el bot�n 'Cerrar a�o de ejecuci�n' para archivar los datos actuales y asignar el nuevo a�o." });
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
                    new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Editar,
                        "ConfiguracionEmpresa", configuracion.IdEmpresa.ToString(),
                        $"Configuración básica guardada (empresa {configuracion.IdEmpresa})",
                        null, configuracion, idEmpresa: configuracion.IdEmpresa);
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
        [ValidateAntiForgeryToken]
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

                var tiposValidos = new[] { "Empresa", "Pais", "Categoria", "Tipo", "LineaNegocio", "Ajuste1", "Ajuste2", "AnoHistorico" };
                if (!tiposValidos.Contains(tipo))
                {
                    return Json(new { success = false, message = "Tipo de configuraci�n no v�lido" });
                }

                if (tipo == "AnoHistorico")
                {
                    var anioEjecucion = _configuracionService.ObtenerAnioEjecucion(idConfiguracion);
                    if (anioEjecucion.HasValue && valor.Trim() == anioEjecucion.Value.ToString())
                    {
                        return Json(new { success = false, message = $"El año histórico no puede ser igual al año de ejecución ({anioEjecucion.Value})" });
                    }
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
        [ValidateAntiForgeryToken]
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
        [ValidateAntiForgeryToken]
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

        #region Cerrar Año de Ejecución

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CerrarAnioEjecucion(int idEmpresa, int nuevoAnio)
        {
            var usuario = UsuarioSesionHelper.UsuarioActual;
            if (usuario == null)
                return Json(new { success = false, message = "Sesión no válida." });

            if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa))
                return Json(new { success = false, message = "No tiene permisos para esta operación." });

            var config = _configuracionService.ObtenerConfiguracionPorEmpresa(idEmpresa);
            if (config == null)
                return Json(new { success = false, message = "La empresa no tiene configuración creada." });

            int anioActual = config.AnioEjecucion;

            if (nuevoAnio < 2000 || nuevoAnio > 2100)
                return Json(new { success = false, message = "El nuevo año debe estar entre 2000 y 2100." });

            if (nuevoAnio == anioActual)
                return Json(new { success = false, message = $"El nuevo año ({nuevoAnio}) es igual al año actual de ejecución." });

            bool yaEsHistorico = config.AnosHistoricos?.Any(a => a.NombreAno?.Trim() == nuevoAnio.ToString()) ?? false;
            if (yaEsHistorico)
                return Json(new { success = false, message = $"El año {nuevoAnio} ya está registrado como año histórico." });

            // Fuente única: Helpers/TablasCargueHelper.cs (compartida con el cargue y el historial).
            // Incluye las tablas personalizadas de la empresa (p. ej. Ini_HistPrecios_Churido).
            var tablasIni = TablasCargueHelper.TablasIniParaEmpresa(idEmpresa);
            var tablasZ = TablasCargueHelper.TablasZ;

            try
            {
                using (var conn = new System.Data.SqlClient.SqlConnection(CadenaConexion))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Convertir datos de ejecución → histórico en todas las Ini_
                            foreach (var tabla in tablasIni)
                            {
                                using (var cmd = new System.Data.SqlClient.SqlCommand(
                                    $"IF OBJECT_ID('{tabla}') IS NOT NULL UPDATE [dbo].[{tabla}] SET Historico_Log = 1 WHERE IdEmpresa_Log = @IdEmpresa AND Historico_Log = 0",
                                    conn, tx))
                                {
                                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            // 2. Limpiar tablas Z_ de esa empresa
                            foreach (var tabla in tablasZ)
                            {
                                using (var cmd = new System.Data.SqlClient.SqlCommand(
                                    $"IF OBJECT_ID('{tabla}') IS NOT NULL DELETE FROM [dbo].[{tabla}] WHERE IdEmpresa = @IdEmpresa OR IdEmpresa IS NULL",
                                    conn, tx))
                                {
                                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            // 3. Registrar año anterior como histórico
                            using (var cmd = new System.Data.SqlClient.SqlCommand("sp_AgregarItemConfiguracion", conn, tx))
                            {
                                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                                cmd.Parameters.AddWithValue("@Tipo", "AnoHistorico");
                                cmd.Parameters.AddWithValue("@IdConfiguracion", config.Id);
                                cmd.Parameters.AddWithValue("@Valor", anioActual.ToString());
                                cmd.Parameters.Add("@Resultado", System.Data.SqlDbType.Bit).Direction = System.Data.ParameterDirection.Output;
                                cmd.Parameters.Add("@Mensaje", System.Data.SqlDbType.VarChar, 255).Direction = System.Data.ParameterDirection.Output;
                                cmd.Parameters.Add("@IdInsertado", System.Data.SqlDbType.Int).Direction = System.Data.ParameterDirection.Output;
                                cmd.ExecuteNonQuery();
                            }

                            // 4. Actualizar AnioEjecucion al nuevo año
                            using (var cmd = new System.Data.SqlClient.SqlCommand("sp_GuardarConfiguracionBasica", conn, tx))
                            {
                                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                                cmd.Parameters.AddWithValue("@AnioEjecucion", nuevoAnio);
                                cmd.Parameters.AddWithValue("@SignoCreditos", config.SignoCreditos ?? string.Empty);
                                cmd.Parameters.AddWithValue("@Moneda", config.Moneda);
                                cmd.Parameters.AddWithValue("@Unidad", config.Unidad);
                                cmd.Parameters.AddWithValue("@UsuarioModificacion", usuario.Id);
                                cmd.Parameters.Add("@Resultado", System.Data.SqlDbType.Bit).Direction = System.Data.ParameterDirection.Output;
                                cmd.Parameters.Add("@Mensaje", System.Data.SqlDbType.VarChar, 255).Direction = System.Data.ParameterDirection.Output;
                                cmd.ExecuteNonQuery();
                            }

                            // 5. Registrar en auditoría
                            using (var cmd = new System.Data.SqlClient.SqlCommand(@"
                                INSERT INTO dbo.AuditoriaCargues (FechaCargue, IdUsuario, Usuario, IdEmpresa, NombreEmpresa, NombreArchivo)
                                VALUES (@Fecha, @IdUsuario, @Usuario, @IdEmpresa, @NombreEmpresa, @NombreArchivo)",
                                conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@Fecha", DateTime.Now);
                                cmd.Parameters.AddWithValue("@IdUsuario", usuario.Id);
                                cmd.Parameters.AddWithValue("@Usuario", $"{usuario.Nombre} {usuario.Apellidos}".Trim());
                                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                                cmd.Parameters.AddWithValue("@NombreEmpresa", config.NombreEmpresa ?? "");
                                cmd.Parameters.AddWithValue("@NombreArchivo", $"Cierre año {anioActual} a {nuevoAnio}");
                                cmd.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            throw;
                        }
                    }
                }

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Editar,
                    "ConfiguracionEmpresa", idEmpresa.ToString(),
                    $"Cierre de año: {anioActual} archivado como histórico, nuevo año de ejecución {nuevoAnio}",
                    null, new { idEmpresa, anioAnterior = anioActual, anioNuevo = nuevoAnio }, idEmpresa: idEmpresa);

                return Json(new
                {
                    success = true,
                    message = $"Transición completada. El año {anioActual} quedó archivado como histórico y el nuevo año de ejecución es {nuevoAnio}.",
                    anioNuevo = nuevoAnio
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error durante la transición: {ex.Message}" });
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
                    var idsPermitidos = EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas(usuario) ?? new System.Collections.Generic.List<int>();
                    empresas = empresas.Where(e => idsPermitidos.Contains(e.Id)).ToList();
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