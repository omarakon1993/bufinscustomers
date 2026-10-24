using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Actualización de los datos de Pulso Bufins público de bufins.com. Solo Super Admin.
    /// El proceso pesado corre fuera de IIS (PulsoEtl.exe); aquí solo se consulta el estado y se lanza.
    /// </summary>
    [ValidarSesion]
    [SoloSuperAdmin]
    public class PulsoDatosController : BaseController
    {
        private readonly PulsoActualizacionService _svc = new PulsoActualizacionService();

        public ActionResult Index()
        {
            return View("~/Views/Configuracion/PulsoDatos.cshtml");
        }

        /// <summary>Estado para el sondeo de la pantalla (datos publicados, fuente, avance del proceso).</summary>
        [HttpGet]
        public JsonResult Estado()
        {
            var r = Json(_svc.ObtenerEstado(), JsonRequestBehavior.AllowGet);
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            return r;
        }

        /// <summary>Uso anónimo de Pulso Bufins (contadores agregados de los últimos 30 días).</summary>
        [HttpGet]
        public JsonResult Uso()
        {
            var r = Json(_svc.ObtenerUso(30), JsonRequestBehavior.AllowGet);
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            return r;
        }

        /// <summary>Guarda las rutas del .exe y de la carpeta de datos en dbo.ConfiguracionSistema (vacío = autodetectar).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult GuardarRutas(string exe, string datos)
        {
            var err = _svc.GuardarRutas(exe, datos);
            if (err == null)
            {
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Editar,
                    "PulsoDatos", "rutas", "Rutas de actualización de Pulso Bufins",
                    null, new { exe, datos }, UsuarioSesionHelper.UsuarioActual?.IdEmpresa);
                return Json(new { ok = true, mensaje = R("ObsDatos_MsgRutasOk") });
            }
            var clave = err == "ExeNoExiste" ? "ObsDatos_MsgRutaExeNoExiste" : err == "CarpetaNoExiste" ? "ObsDatos_MsgRutaDatosNoExiste" : "ObsDatos_MsgErrorInicio";
            return Json(new { ok = false, mensaje = R(clave) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Actualizar(bool forzar = false)
        {
            var res = _svc.Iniciar(forzar);
            if (res.Ok)
            {
                new AuditoriaService().RegistrarCambio(AuditoriaTipo.Configuracion, AuditoriaAccion.Ejecutar,
                    "PulsoDatos", "etl", "Actualización de datos de Pulso Bufins (bufins.com)" + (forzar ? " [forzada]" : ""),
                    null, new { forzar }, UsuarioSesionHelper.UsuarioActual?.IdEmpresa);
                return Json(new { ok = true, mensaje = R("ObsDatos_MsgIniciado") });
            }

            string clave;
            switch (res.Codigo)
            {
                case "YaEnCurso": clave = "ObsDatos_MsgYaEnCurso"; break;
                case "SinConfigurar":
                case "ExeNoExiste":
                case "CarpetaNoExiste": clave = "ObsDatos_MsgNoConfigurado"; break;
                default: clave = "ObsDatos_MsgErrorInicio"; break;
            }
            return Json(new { ok = false, mensaje = R(clave) + (string.IsNullOrEmpty(res.Detalle) ? "" : " (" + res.Detalle + ")") });
        }
    }
}
