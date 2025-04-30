using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace bufinscustomers.Controllers
{
    public class AccesoController : Controller
    {
        //static string cadena = "Data Source=190.90.160.168\\MSSQLSERVER2016;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";
        static string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        // GET: Acceso
        public ActionResult Login()
        {
            return View();
        }
        public ActionResult Registrar()
        {
            return View();
        }
        [HttpPost]
        public ActionResult Registrar(Usuarios oUsuario)
        {
            bool registrado;
            string mensaje;

            // ⚡ Elimina espacios de correo y clave
            oUsuario.Correo = oUsuario.Correo.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();
            oUsuario.ConfirmarClave = oUsuario.ConfirmarClave.Trim();

            if (oUsuario.Clave == oUsuario.ConfirmarClave)
            {
                oUsuario.Clave = ConvertirSha256(oUsuario.Clave);
            }
            else
            {
                ViewData["Mensaje"] = "Las contraseñas no coinciden";
                return View();
            }

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarUsuario", cn);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.Parameters.Add("Registrado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("Mensaje", SqlDbType.VarChar, 100).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                registrado = Convert.ToBoolean(cmd.Parameters["Registrado"].Value);
                mensaje = cmd.Parameters["Mensaje"].Value.ToString();
            }

            ViewData["Mensaje"] = mensaje;
            if (registrado)
            {
                return RedirectToAction("Login", "Acceso");
            }
            else
            {
                return View();
            }
        }

        [HttpPost]
        public ActionResult Login(Usuarios oUsuario)
        {
            // ⚡ Elimina espacios
            oUsuario.Correo = oUsuario.Correo.Trim();
            oUsuario.Clave = oUsuario.Clave.Trim();

            if (!EsCorreoValido(oUsuario.Correo))
            {
                ViewData["Mensaje"] = "El correo ingresado no tiene un formato válido.";
                return View();
            }

            // Luego convierte
            oUsuario.Clave = ConvertirSha256(oUsuario.Clave);

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_ValidarUsuario", cn);
                cmd.Parameters.AddWithValue("Correo", oUsuario.Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                oUsuario.Id = Convert.ToInt32(cmd.ExecuteScalar().ToString());
            }

            if (oUsuario.Id != 0)
            {
                Session["usuario"] = oUsuario;
                return RedirectToAction("index", "Home");
            }
            else
            {
                ViewData["Mensaje"] = "usuario no encontrado";
                return View();
            }
        }

        private bool EsCorreoValido(string correo)
        {
            // Expresión regular para validar correo
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            Regex regex = new Regex(pattern);
            return regex.IsMatch(correo);
        }

        public static string ConvertirSha256(string texto)
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


    }
}