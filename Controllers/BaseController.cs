using System;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;
using BC = BCrypt.Net.BCrypt;

namespace bufinscustomers.Controllers
{
    public abstract class BaseController : Controller
    {
        protected static readonly string CadenaConexion =
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        // Kept for reading legacy SHA256 hashes during migration — do NOT use for new passwords
        protected static string ConvertirSha256(string texto)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(texto));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        protected static string HashearContrasena(string contrasena)
        {
            return BC.HashPassword(contrasena, workFactor: 12);
        }

        // Returns true if the plain-text password matches the stored hash.
        // Supports both BCrypt (new) and SHA256 (legacy migration).
        protected static bool VerificarContrasena(string contrasena, string hashalmacenado)
        {
            if (EsHashBCrypt(hashalmacenado))
                return BC.Verify(contrasena, hashalmacenado);
            return ConvertirSha256(contrasena) == hashalmacenado;
        }

        protected static bool EsHashBCrypt(string hash)
        {
            return hash != null &&
                   (hash.StartsWith("$2a$") || hash.StartsWith("$2b$") || hash.StartsWith("$2y$"));
        }

        /// <summary>
        /// Manejo centralizado de errores
        /// </summary>
        public void SetErrorMessage(string mensaje)
        {
            TempData["ErrorMessage"] = mensaje;
        }

        /// <summary>
        /// Manejo centralizado de mensajes de éxito
        /// </summary>
        public void SetSuccessMessage(string mensaje)
        {
            TempData["SuccessMessage"] = mensaje;
        }

        /// <summary>
        /// Manejo centralizado de mensajes informativos
        /// </summary>
        public void SetInfoMessage(string mensaje)
        {
            TempData["InfoMessage"] = mensaje;
        }

        // Accede a App_GlobalResources respetando la cultura actual del hilo.
        protected string R(string key) =>
            System.Web.HttpContext.GetGlobalResourceObject("Strings", key)?.ToString() ?? key;

        /// <summary>Idioma de la sesión ("es" | "en") para indicarle al modelo en qué idioma responder.</summary>
        protected static string IdiomaIA =>
            System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;

        /// <summary>
        /// Control de acceso y presupuesto de IA común a todos los puntos de IA (ver IAUsoService):
        /// avisa a los Super Admin al 80 % / 100 % del presupuesto de la empresa y, si la consulta no
        /// está permitida, devuelve la respuesta JSON de error ya traducida; null si puede continuar.
        /// </summary>
        protected JsonResult ValidarAccesoIA(Usuarios usuario, bool esSuperAdmin, int idEmpresa, string funcion = null)
        {
            var iaUso = new IAUsoService();
            var acceso = iaUso.EvaluarAcceso(usuario, esSuperAdmin, idEmpresa, funcion);

            if (acceso.DebeAvisarAgotado || acceso.DebeAvisarCercaDelLimite)
            {
                bool agotado = acceso.DebeAvisarAgotado;
                string nombreEmpresa = new EmpresaService().ObtenerEmpresas().FirstOrDefault(e => e.Id == idEmpresa)?.Nombre ?? ("#" + idEmpresa);
                string titulo = R(agotado ? "Notif_IAPresupuestoAgotadoTitulo" : "Notif_IAPresupuestoAvisoTitulo");
                string msg = string.Format(R(agotado ? "Notif_IAPresupuestoAgotadoMsg" : "Notif_IAPresupuestoAvisoMsg"),
                    nombreEmpresa, acceso.ConsumidoMes.ToString("N0"), acceso.PresupuestoEfectivo.ToString("N0"), acceso.PorcentajeConsumido);
                iaUso.EnviarAvisoPresupuestoATodosSuperAdmin(titulo, msg, agotado, idEmpresa);
            }

            if (acceso.Permitido) return null;

            return Json(new IAConsultaResponse
            {
                Exitoso = false,
                Error = R(IAUsoService.ClaveMensaje(acceso.CodigoError))
            });
        }
    }
}