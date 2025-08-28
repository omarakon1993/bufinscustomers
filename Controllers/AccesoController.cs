using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using bufinscustomers.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using System.IO;
using System.ComponentModel;
using System.Web.Script.Serialization;
using Antlr.Runtime.Misc;
using bufinscustomers.Helpers;
using Newtonsoft.Json;

namespace bufinscustomers.Controllers
{
    public class AccesoController : BaseController
    {
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

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
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
            oUsuario.Clave = oUsuario.Clave.Trim();

            var Usuario = "";   
            var Correo = "";   

            if (!EsCorreoValido(oUsuario.Correo))
            {
                Usuario = oUsuario.Correo.Trim();
            }
            else
            {
                Correo = oUsuario.Correo.Trim();
            }

            // Luego convierte
            oUsuario.Clave = ConvertirSha256(oUsuario.Clave);

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_ValidarUsuario", cn);
                cmd.Parameters.AddWithValue("Usuario", Usuario);
                cmd.Parameters.AddWithValue("Correo", Correo);
                cmd.Parameters.AddWithValue("Clave", oUsuario.Clave);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                oUsuario.Id = Convert.ToInt32(cmd.ExecuteScalar().ToString());
            }

            if (oUsuario.Id != 0)
            {
                Session["usuario"] = oUsuario;
                Session["IdUsuario"] = oUsuario.Id;
                return RedirectToAction("index", "Home");
            }
            else
            {
                ViewData["Mensaje"] = "usuario o clave incorrecta";
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
    }
}