using System;
using System.Collections.Generic;
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

        /// <summary>Serialización de los datos que se envían a la IA: omite los campos nulos (menos tokens, mismo contenido).</summary>
        protected static readonly Newtonsoft.Json.JsonSerializerSettings JsonIASinNulos =
            new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore };

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

        /// <summary>
        /// Mensaje seguro para devolver al navegador cuando una acción falla (H8). Siempre registra la excepción completa
        /// en AppLogger con un código de referencia. Si la excepción es TÉCNICA (SQL del sistema, nulos, E/S, tiempos de espera...)
        /// devuelve un texto genérico traducido con ese código, para no filtrar nombres de tablas, rutas ni versiones; si es una
        /// excepción de regla de negocio (mensaje escrito por la aplicación, o error de usuario lanzado con THROW/RAISERROR) conserva su mensaje.
        /// </summary>
        protected string MensajeError(Exception ex, [System.Runtime.CompilerServices.CallerMemberName] string accion = null)
        {
            string codigo = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            try { Helpers.AppLogger.Error(ex, GetType().Name + "." + accion + " ref=" + codigo); } catch { }

            Exception b = ex == null ? null : ex.GetBaseException();
            var sql = (b as System.Data.SqlClient.SqlException) ?? (ex as System.Data.SqlClient.SqlException);
            bool tecnica;
            if (sql != null)
                tecnica = !(sql.Class <= 16 && sql.Number >= 50000); // 50000+ = THROW/RAISERROR escrito por nosotros (regla de negocio)
            else
                tecnica = b is NullReferenceException || b is IndexOutOfRangeException || b is InvalidCastException
                    || b is KeyNotFoundException || b is FormatException || b is System.IO.IOException
                    || b is TimeoutException || b is System.Web.HttpException || b is System.ComponentModel.Win32Exception
                    || b is OutOfMemoryException || b is ArgumentOutOfRangeException || b is OverflowException
                    || b is DivideByZeroException || b is System.Net.WebException || b is System.Net.Http.HttpRequestException
                    || b is System.Threading.Tasks.TaskCanceledException || b is UnauthorizedAccessException;

            if (!tecnica && ex != null && !string.IsNullOrWhiteSpace(ex.Message)) return ex.Message;
            return string.Format(R("Err_InternoRef"), codigo);
        }

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