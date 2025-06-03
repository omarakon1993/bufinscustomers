using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class EmpresaService
    {
        private readonly string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        public List<Empresas> ObtenerEmpresas()
        {
            List<Empresas> empresas = new List<Empresas>();

            using (SqlConnection connection = new SqlConnection(cadena))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerEmpresas", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Empresas empresa = new Empresas();
                            empresa.Id = (int)reader["EmpId"];
                            empresa.Nombre = (string)reader["EmpNombre"];
                            empresa.Nit = (string)reader["EmpNit"];
                            empresa.Direccion = (string)reader["EmpDireccion"];
                            empresa.Telefono = (string)reader["EmpTelefono"];
                            empresas.Add(empresa);
                        }
                    }
                }
            }

            return empresas;
        }

        // Crear empresa
        public bool CrearEmpresa(Empresas empresa, out string mensaje)
        {
            bool registrado = false;
            mensaje = "";

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarEmpresa", cn);
                cmd.Parameters.AddWithValue("@EmpNombre", empresa.Nombre);
                cmd.Parameters.AddWithValue("@EmpNit", empresa.Nit);
                cmd.Parameters.AddWithValue("@EmpDireccion", empresa.Direccion);
                cmd.Parameters.AddWithValue("@EmpTelefono", empresa.Telefono);
                cmd.Parameters.Add("@Registrado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@Mensaje", SqlDbType.VarChar, 100).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                registrado = Convert.ToBoolean(cmd.Parameters["@Registrado"].Value);
                mensaje = cmd.Parameters["@Mensaje"].Value.ToString();
            }

            return registrado;
        }

        // Editar empresa
        public bool EditarEmpresa(Empresas empresa)
        {
            bool actualizado = false;

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_EditarEmpresa", cn);
                cmd.Parameters.AddWithValue("@EmpId", empresa.Id);
                cmd.Parameters.AddWithValue("@EmpNombre", empresa.Nombre);
                cmd.Parameters.AddWithValue("@EmpNit", empresa.Nit);
                cmd.Parameters.AddWithValue("@EmpDireccion", empresa.Direccion);
                cmd.Parameters.AddWithValue("@EmpTelefono", empresa.Telefono);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                int filas = cmd.ExecuteNonQuery();
                actualizado = filas > 0;
            }

            return actualizado;
        }

        // Eliminar empresa
        public bool EliminarEmpresa(int idEmpresa)
        {
            bool eliminado = false;

            using (SqlConnection cn = new SqlConnection(cadena))
            {
                SqlCommand cmd = new SqlCommand("sp_EliminarEmpresa", cn);
                cmd.Parameters.AddWithValue("@EmpId", idEmpresa);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                int filas = cmd.ExecuteNonQuery();
                eliminado = filas > 0;
            }

            return eliminado;
        }
    }
}
