using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;

namespace bufinscustomers.Controllers
{
    /// <summary>
    /// Opciones de paleta para el selector «Paleta de colores del informe» (ver CLAUDE.md, «Tema de colores de los informes»).
    /// Devuelve siempre los tokens ya derivados por <see cref="TemaInforme"/>: el navegador no reimplementa el algoritmo.
    /// </summary>
    [ValidarSesion]
    public class InformeTemaController : BaseController
    {
        private readonly TemaInformeService _servicio = new TemaInformeService();

        /// <summary>
        /// Bufins + las DOS paletas corporativas de la empresa indicada (siempre las dos entradas: <c>registrada=false</c> si aún no
        /// existen, para que el selector lo indique). Sin empresa o sin acceso a ella: ambas sin registrar.
        /// </summary>
        [HttpGet]
        public ActionResult Opciones(int? idEmpresa)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            var presets = TemaInforme.Presets.Select(t => t.ParaCliente(TemaInformeService.NombreVisible(t, R))).ToList();

            bool conAcceso = idEmpresa.HasValue && EmpresaAccesoHelper.TieneAcceso(UsuarioSesionHelper.UsuarioActual, idEmpresa.Value);
            var guardadas = conAcceso ? new EmpresaPaletaService().Obtener(idEmpresa.Value) : new List<EmpresaPaleta>();

            var corporativas = new List<object>();
            for (byte slot = 1; slot <= 2; slot++)
            {
                var pal = guardadas.FirstOrDefault(x => x.Slot == slot);
                var tema = pal == null ? null : _servicio.DesdePaleta(pal);
                if (tema != null && tema.Id == TemaInforme.IdBufins) tema = null;     // colores guardados inválidos: cuenta como sin registrar
                string nombre = conAcceso ? TemaInformeService.NombreCorporativa(idEmpresa.Value, slot) : string.Format(R("Tema_Corp_Generica"), slot);
                corporativas.Add(new
                {
                    slot,
                    registrada = tema != null,
                    nombre,
                    tema = tema == null ? null : tema.ParaCliente(nombre)
                });
            }
            return Json(new { presets, corporativas }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Tokens de un tema (se usa para «Personalizada» con los tres colores). Valor inválido → <c>exito=false</c> con el motivo.</summary>
        [HttpGet]
        public ActionResult Resolver(string tema, string p, string s, string a)
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            if (tema == TemaInformeService.IdPersonalizada)
            {
                string aviso;
                string error = TemaInforme.Validar(p, s, a, out aviso);
                if (error != null) return Json(new { exito = false, mensaje = R(error) }, JsonRequestBehavior.AllowGet);
                var t = _servicio.Resolver(UsuarioSesionHelper.UsuarioActual, tema, p, s, a);
                return Json(new { exito = true, aviso = aviso == null ? null : R(aviso), tema = t.ParaCliente(TemaInformeService.NombreVisible(t, R)) }, JsonRequestBehavior.AllowGet);
            }
            var r = _servicio.Resolver(UsuarioSesionHelper.UsuarioActual, tema, p, s, a);
            return Json(new { exito = true, tema = r.ParaCliente(TemaInformeService.NombreVisible(r, R)) }, JsonRequestBehavior.AllowGet);
        }
    }
}
