using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Gestor de hojas de cargue (Super Admin): catálogo de hojas Z_ del Excel → tablas Ini_, su alcance
    /// (Estándar = todas las empresas / Personalizada = empresas asignadas) y el diseño de sus tablas
    /// (crea y ajusta dbo.Ini_X + dbo.Staging_Ini_X). Todo el cargue (staging, validación, confirmación,
    /// historial/rollback, cierre de año, plantillas) lee este catálogo vía <see cref="TablasCargueHelper"/>.
    /// Pantalla de una sola página: la vista llama a estos endpoints por AJAX.
    /// </summary>
    [ValidarSesion]
    [SoloSuperAdmin]
    public class GestorHojasCargueController : BaseController
    {
        private readonly CargueHojasService _servicio = new CargueHojasService();
        private readonly EmpresaService _empresaService = new EmpresaService();

        public ActionResult Index()
        {
            return View("~/Views/Configuracion/GestorHojasCargue.cshtml");
        }

        // ── Lectura ──────────────────────────────────────────────────────────────────────────

        [HttpGet]
        public ActionResult Listar()
        {
            try
            {
                var catalogo = _servicio.ObtenerCatalogo();
                bool desdeBD = catalogo != null;
                var hojas = catalogo ?? CargueHojasCacheHelper.Todas().ToList();
                var empresas = _empresaService.ObtenerEmpresas().OrderBy(e => e.Nombre).Select(e => new { id = e.Id, nombre = e.Nombre }).ToList();

                return Json(new
                {
                    success = true,
                    desdeBD,
                    empresas,
                    hojas = hojas.Select(h => new
                    {
                        id = h.IdHoja,
                        hoja = h.NombreHoja,
                        tabla = h.TablaIni,
                        descripcion = h.Descripcion,
                        alcance = h.Alcance,
                        activo = h.Activo,
                        orden = h.Orden,
                        origen = h.Origen,
                        empresas = h.Empresas.Keys.ToList(),
                        modificada = h.FechaModificacion?.ToString("yyyy-MM-dd HH:mm")
                    })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.Listar");
                return Json(new { success = false, message = R("HojasCargue_Err_Cargar") }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public ActionResult Detalle(int id)
        {
            try
            {
                var hoja = BuscarHoja(id, exigirBD: false);
                if (hoja == null) return JsonError("HojasCargue_Err_NoExiste");

                var d = _servicio.ObtenerDetalle(hoja);
                return Json(new
                {
                    success = true,
                    existeIni = d.ExisteIni,
                    existeStaging = d.ExisteStaging,
                    filasIni = d.FilasIni,
                    filasStaging = d.FilasStaging,
                    filasPorEmpresa = d.FilasPorEmpresa.Select(kv => new { idEmpresa = kv.Key, filas = kv.Value }),
                    usadaPor = d.UsadaPor,
                    estructuraEditable = d.EstructuraEditable,
                    columnas = d.Columnas.Select(ColumnaJson),
                    columnasSistema = d.ColumnasSistema.Select(ColumnaJson),
                    diagnostico = d.Diagnostico.Select(x => new { nivel = x.Nivel, codigo = x.Codigo, detalle = x.Detalle })
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.Detalle");
                return JsonError("HojasCargue_Err_Cargar");
            }
        }

        /// <summary>Asistente "Nueva hoja": ¿la tabla Ini_ ya existe? (se registraría con su estructura actual).</summary>
        [HttpGet]
        public ActionResult VerificarTabla(string tabla)
        {
            tabla = (tabla ?? "").Trim();
            if (!CargueHojasService.NombreTablaValido(tabla))
                return Json(new { success = true, valida = false }, JsonRequestBehavior.AllowGet);
            try
            {
                bool existe = _servicio.ExisteTabla(tabla);
                bool existeStaging = _servicio.ExisteTabla(TablasCargueHelper.NombreStaging(tabla));
                var columnas = existe ? _servicio.ObtenerColumnas(tabla).Where(c => !c.EsSistema).Select(ColumnaJson).ToList() : null;
                bool tieneAnio = !existe || _servicio.ObtenerColumnas(tabla).Any(c => c.Nombre.Equals(CargueHojasService.ColumnaAnio, StringComparison.OrdinalIgnoreCase));
                return Json(new { success = true, valida = true, existe, existeStaging, tieneAnio, columnas }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.VerificarTabla");
                return JsonError("HojasCargue_Err_Cargar");
            }
        }

        /// <summary>SQL que ejecutaría "Crear" (para mostrarlo antes de confirmar).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VistaPreviaSql(string datos)
        {
            try
            {
                var nueva = JsonConvert.DeserializeObject<HojaCargueNueva>(datos ?? "{}") ?? new HojaCargueNueva();
                var catalogo = _servicio.ObtenerCatalogo() ?? new List<HojaCargue>();
                bool existe = CargueHojasService.NombreTablaValido((nueva.TablaIni ?? "").Trim()) && _servicio.ExisteTabla(nueva.TablaIni.Trim());
                CargueHojasService.ValidarNueva(nueva, catalogo, existe);

                // Tabla existente: se registra con su estructura (sin script; si faltara su staging, se crea igual que la Ini).
                string sql = existe ? null
                    : CargueHojasService.ComoScript(CargueHojasService.SentenciasCrearTablas(nueva.TablaIni, nueva.Columnas));
                return Json(new { success = true, existe, sql });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.VistaPreviaSql");
                return JsonError("HojasCargue_Err_Datos");
            }
        }

        // ── Escritura ────────────────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Crear(string datos)
        {
            try
            {
                var catalogo = ExigirCatalogo();
                var nueva = JsonConvert.DeserializeObject<HojaCargueNueva>(datos ?? "{}") ?? new HojaCargueNueva();
                var (idHoja, sql) = _servicio.Crear(nueva, catalogo, UsuarioActualId());
                CargueHojasCacheHelper.Invalidar();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.HojasCargue, AuditoriaAccion.Crear,
                    "CargueHojas", idHoja.ToString(),
                    $"Hoja de cargue creada: {nueva.NombreHoja} → {nueva.TablaIni} ({nueva.Alcance})" + (sql.Count > 0 ? " — tablas creadas en la BD." : " — tabla existente registrada."),
                    null,
                    new { hoja = nueva.NombreHoja, tabla = nueva.TablaIni, alcance = nueva.Alcance, empresas = nueva.Empresas, columnas = nueva.Columnas?.Select(c => c.Nombre + " " + c.TipoCompleto), sql },
                    entidadNombre: nueva.NombreHoja, severidad: AuditoriaSeveridad.Advertencia);

                NotificarSuperAdmins(R("Notif_HojaCargueCreada"),
                    string.Format(R("Notif_HojaCargueCreadaMsg"), nueva.NombreHoja, nueva.TablaIni,
                        R(nueva.Alcance == HojaCargue.AlcancePersonalizada ? "HojasCargue_TagPersonalizada" : "HojasCargue_TagEstandar"),
                        NombreUsuarioActual()),
                    "success");

                return Json(new { success = true, id = idHoja, message = string.Format(R("HojasCargue_Creada"), nueva.NombreHoja) });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (SqlException sex) { return JsonErrorSql(sex, "Crear"); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.Crear");
                return JsonError("HojasCargue_Err_Datos");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardarGeneral(int id, string nombreHoja, string descripcion, int orden, bool activo)
        {
            try
            {
                var catalogo = ExigirCatalogo();
                var hoja = catalogo.FirstOrDefault(h => h.IdHoja == id);
                if (hoja == null) return JsonError("HojasCargue_Err_NoExiste");

                var antes = new { hoja = hoja.NombreHoja, descripcion = hoja.Descripcion, orden = hoja.Orden, activo = hoja.Activo };
                _servicio.GuardarGeneral(hoja, nombreHoja, descripcion, orden, activo, catalogo, UsuarioActualId());
                CargueHojasCacheHelper.Invalidar();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.HojasCargue, AuditoriaAccion.Editar,
                    "CargueHojas", id.ToString(), $"Hoja de cargue editada: {nombreHoja?.Trim()} ({hoja.TablaIni})",
                    antes, new { hoja = nombreHoja?.Trim(), descripcion, orden, activo },
                    entidadNombre: nombreHoja?.Trim(),
                    severidad: activo != hoja.Activo ? AuditoriaSeveridad.Advertencia : null);

                return Json(new { success = true, message = R("HojasCargue_Guardada") });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (SqlException sex) { return JsonErrorSql(sex, "GuardarGeneral"); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.GuardarGeneral");
                return JsonError("HojasCargue_Err_Datos");
            }
        }

        /// <summary>
        /// Nuevo orden de las hojas tras arrastrarlas en la lista (mismo patrón que el gestor de menús):
        /// <paramref name="ids"/> en su nuevo orden y <paramref name="ordenes"/> con el valor de cada una.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ActualizarOrden(List<int> ids, List<int> ordenes)
        {
            try
            {
                var catalogo = ExigirCatalogo();
                if (ids == null || ordenes == null || ids.Count == 0 || ids.Count != ordenes.Count)
                    return JsonError("HojasCargue_Err_Orden");

                var antes = catalogo.OrderBy(h => h.Orden).Select(h => h.NombreHoja + "=" + h.Orden).ToList();
                _servicio.ActualizarOrden(ids, ordenes, UsuarioActualId());
                CargueHojasCacheHelper.Invalidar();

                var porId = catalogo.ToDictionary(h => h.IdHoja, h => h.NombreHoja);
                var despues = ids.Select((id, i) => (porId.TryGetValue(id, out var n) ? n : "#" + id) + "=" + ordenes[i]).ToList();
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.HojasCargue, AuditoriaAccion.Editar,
                    "CargueHojas", null, "Orden de las hojas de cargue actualizado", new { orden = antes }, new { orden = despues },
                    entidadNombre: "Orden de hojas");

                return Json(new { success = true, message = R("HojasCargue_OrdenActualizado") });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (SqlException sex) { return JsonErrorSql(sex, "ActualizarOrden"); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.ActualizarOrden");
                return JsonError("HojasCargue_Err_Orden");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardarAlcance(int id, string alcance, string empresas)
        {
            try
            {
                var catalogo = ExigirCatalogo();
                var hoja = catalogo.FirstOrDefault(h => h.IdHoja == id);
                if (hoja == null) return JsonError("HojasCargue_Err_NoExiste");

                var ids = (empresas ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0).Where(n => n > 0).Distinct().ToList();

                var antes = new { alcance = hoja.Alcance, empresas = hoja.Empresas.Keys.OrderBy(x => x).ToList() };
                _servicio.GuardarAlcance(hoja, alcance, ids, UsuarioActualId());
                CargueHojasCacheHelper.Invalidar();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.HojasCargue, AuditoriaAccion.Asignar,
                    "CargueHojas", id.ToString(), $"Alcance de la hoja {hoja.NombreHoja}: {alcance}",
                    antes, new { alcance, empresas = ids.OrderBy(x => x).ToList() },
                    entidadNombre: hoja.NombreHoja);

                return Json(new { success = true, message = R("HojasCargue_AlcanceGuardado") });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (SqlException sex) { return JsonErrorSql(sex, "GuardarAlcance"); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.GuardarAlcance");
                return JsonError("HojasCargue_Err_Datos");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AgregarColumna(int id, string columna)
        {
            return OperacionColumna(id, "AgregarColumna", hoja =>
            {
                var col = JsonConvert.DeserializeObject<ColumnaHoja>(columna ?? "{}") ?? new ColumnaHoja();
                var sql = _servicio.AgregarColumna(hoja, col);
                return ($"Columna agregada a {hoja.NombreHoja}: {col.Nombre} {col.TipoCompleto}", null, new { columna = col.Nombre, tipo = col.TipoCompleto, nulos = col.PermiteNulos, defecto = col.ValorDefecto, sql }, AuditoriaAccion.Crear, "HojasCargue_ColumnaAgregada");
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ModificarColumna(int id, string nombreOriginal, string columna)
        {
            return OperacionColumna(id, "ModificarColumna", hoja =>
            {
                var col = JsonConvert.DeserializeObject<ColumnaHoja>(columna ?? "{}") ?? new ColumnaHoja();
                var sql = _servicio.ModificarColumna(hoja, nombreOriginal, col);
                return ($"Columna modificada en {hoja.NombreHoja}: {nombreOriginal} → {col.Nombre} {col.TipoCompleto}", new { columna = nombreOriginal }, new { columna = col.Nombre, tipo = col.TipoCompleto, nulos = col.PermiteNulos, defecto = col.ValorDefecto, sql }, AuditoriaAccion.Editar, "HojasCargue_ColumnaModificada");
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EliminarColumna(int id, string nombre)
        {
            return OperacionColumna(id, "EliminarColumna", hoja =>
            {
                var sql = _servicio.EliminarColumna(hoja, nombre);
                return ($"Columna eliminada de {hoja.NombreHoja}: {nombre}", new { columna = nombre }, new { sql }, AuditoriaAccion.Eliminar, "HojasCargue_ColumnaEliminada");
            });
        }

        /// <summary>Esqueleto común de las 3 operaciones de columnas: catálogo, ejecución, auditoría, respuesta.</summary>
        private ActionResult OperacionColumna(int id, string contexto,
            Func<HojaCargue, (string descripcion, object antes, object despues, string accion, string claveOk)> operacion)
        {
            try
            {
                var catalogo = ExigirCatalogo();
                var hoja = catalogo.FirstOrDefault(h => h.IdHoja == id);
                if (hoja == null) return JsonError("HojasCargue_Err_NoExiste");

                var r = operacion(hoja);
                _servicio.TocarHoja(id, UsuarioActualId());
                CargueHojasCacheHelper.Invalidar();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.HojasCargue, r.accion,
                    "CargueHojas", id.ToString(), r.descripcion, r.antes, r.despues,
                    entidadNombre: hoja.NombreHoja,
                    severidad: r.accion == AuditoriaAccion.Eliminar ? AuditoriaSeveridad.Critico : AuditoriaSeveridad.Advertencia);

                return Json(new { success = true, message = R(r.claveOk) });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (JsonException) { return JsonError("HojasCargue_Err_Datos"); }
            catch (SqlException sex) { return JsonErrorSql(sex, contexto); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue." + contexto);
                return JsonError("HojasCargue_Err_Datos");
            }
        }

        /// <summary>
        /// Quita la hoja del catálogo. Con <paramref name="borrarTablas"/> también borra Ini_X/Staging_Ini_X
        /// (solo si están vacías y ningún SP las usa); sin él, las tablas y sus datos se conservan.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(int id, bool borrarTablas = false)
        {
            try
            {
                var catalogo = ExigirCatalogo();
                var hoja = catalogo.FirstOrDefault(h => h.IdHoja == id);
                if (hoja == null) return JsonError("HojasCargue_Err_NoExiste");

                var sql = _servicio.Eliminar(hoja, borrarTablas);
                CargueHojasCacheHelper.Invalidar();

                new AuditoriaService().RegistrarCambio(AuditoriaTipo.HojasCargue, AuditoriaAccion.Eliminar,
                    "CargueHojas", id.ToString(),
                    $"Hoja de cargue eliminada: {hoja.NombreHoja} → {hoja.TablaIni}" + (sql.Count > 0 ? " (tablas borradas de la BD)" : " (tablas conservadas)"),
                    new { hoja = hoja.NombreHoja, tabla = hoja.TablaIni, alcance = hoja.Alcance, empresas = hoja.Empresas.Keys.ToList() },
                    new { sql }, entidadNombre: hoja.NombreHoja);

                NotificarSuperAdmins(R("Notif_HojaCargueEliminada"),
                    string.Format(R("Notif_HojaCargueEliminadaMsg"), hoja.NombreHoja, hoja.TablaIni, NombreUsuarioActual(),
                        R(sql.Count > 0 ? "Notif_HojaCargueTablasBorradas" : "Notif_HojaCargueTablasConservadas")),
                    "warning");

                return Json(new { success = true, message = string.Format(R("HojasCargue_Eliminada"), hoja.NombreHoja) });
            }
            catch (CargueHojasService.GestorHojasException gex) { return JsonError(gex); }
            catch (SqlException sex) { return JsonErrorSql(sex, "Eliminar"); }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.Eliminar");
                return JsonError("HojasCargue_Err_Datos");
            }
        }

        /// <summary>Plantilla que recibiría la empresa (todas sus hojas activas), para comprobarla desde el gestor.</summary>
        [HttpGet]
        public ActionResult DescargarPlantillaPrueba(int idEmpresa)
        {
            try
            {
                var hojas = idEmpresa > 0 ? TablasCargueHelper.HojasParaEmpresa(idEmpresa) : CargueHojasCacheHelper.Estandar();
                byte[] bytes = new PlantillaCargueService().GenerarBytes(hojas);
                var empresa = _empresaService.ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa);
                string sufijo = Regex.Replace(!string.IsNullOrWhiteSpace(empresa?.Abreviatura) ? empresa.Abreviatura : (empresa?.Nombre ?? "Estandar"), "[^A-Za-z0-9]", "_");
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PlantillaBUFINS_{sufijo}.xlsx");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "GestorHojasCargue.DescargarPlantillaPrueba");
                SetErrorMessage(R("HojasCargue_Err_Plantilla"));
                return RedirectToAction("Index");
            }
        }

        // ── Utilidades ───────────────────────────────────────────────────────────────────────

        /// <summary>Catálogo leído de la BD (no del cache). Lanza si el script Sql/021 no se ha ejecutado.</summary>
        private List<HojaCargue> ExigirCatalogo()
        {
            var catalogo = _servicio.ObtenerCatalogo();
            if (catalogo == null) throw new CargueHojasService.GestorHojasException("HojasCargue_Err_SinScript");
            return catalogo;
        }

        private HojaCargue BuscarHoja(int id, bool exigirBD)
        {
            var catalogo = exigirBD ? ExigirCatalogo() : (_servicio.ObtenerCatalogo() ?? CargueHojasCacheHelper.Todas().ToList());
            return catalogo.FirstOrDefault(h => h.IdHoja == id);
        }

        private int UsuarioActualId() => UsuarioSesionHelper.UsuarioActual?.Id ?? 0;

        private string NombreUsuarioActual()
        {
            var u = UsuarioSesionHelper.UsuarioActual;
            return ((u?.Nombre ?? "") + " " + (u?.Apellidos ?? "")).Trim();
        }

        /// <summary>
        /// Notificación interna a todos los Super Admin (incluido quien hizo el cambio): crear o eliminar una
        /// hoja cambia lo que se exige en el cargue de las empresas y la estructura de la base de datos.
        /// Nunca rompe la operación (NotificacionesService no lanza; la consulta de ids se protege aquí).
        /// </summary>
        private void NotificarSuperAdmins(string titulo, string mensaje, string tipo)
        {
            try
            {
                var notif = new NotificacionesService();
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Id FROM Usuarios WHERE Admin = 2", cn))
                {
                    cn.Open();
                    var ids = new List<int>();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) ids.Add(Convert.ToInt32(r["Id"]));
                    foreach (int id in ids)
                        notif.Crear(id, titulo, mensaje, tipo, "/GestorHojasCargue");
                }
            }
            catch (Exception ex) { AppLogger.Error(ex, "GestorHojasCargue.NotificarSuperAdmins"); }
        }

        private static object ColumnaJson(ColumnaHoja c) => new
        {
            nombre = c.Nombre,
            tipo = c.Tipo,
            longitud = c.Longitud,
            precision = c.Precision,
            escala = c.Escala,
            nulos = c.PermiteNulos,
            defecto = c.ValorDefecto,
            tipoCompleto = c.TipoCompleto
        };

        private JsonResult JsonError(string clave) =>
            Json(new { success = false, message = R(clave) }, JsonRequestBehavior.AllowGet);

        private JsonResult JsonError(CargueHojasService.GestorHojasException gex)
        {
            string msg = R(gex.Message);
            if (!string.IsNullOrWhiteSpace(gex.Detalle)) msg += " (" + gex.Detalle + ")";
            return Json(new { success = false, message = msg });
        }

        private JsonResult JsonErrorSql(SqlException sex, string contexto)
        {
            AppLogger.Error(sex, "GestorHojasCargue." + contexto);
            return Json(new { success = false, message = R("HojasCargue_Err_Sql") + " " + sex.Message });
        }
    }
}
