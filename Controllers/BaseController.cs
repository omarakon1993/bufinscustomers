using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    public abstract class BaseController : Controller
    {
        /// <summary>
        /// Cadena de conexión centralizada obtenida del Web.config
        /// </summary>
        protected static readonly string CadenaConexion = 
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        /// <summary>
        /// Método para convertir texto a SHA256 (reutilizable en todos los controladores)
        /// </summary>
        protected static string ConvertirSha256(string texto)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(texto));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2")); // Hexadecimal minúscula
                }
                return builder.ToString();
            }
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
    }
}