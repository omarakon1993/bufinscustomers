using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;
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
    }
}