using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    public class ReportesService : BaseService
    {
        //private readonly string cadena = "Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;Persist Security Info=True;User ID=oglearni_bufins;Password=Bufins2025**;Encrypt=false";

        public List<Reportes> ObtenerReportes()
        {
            List<Reportes> reportes = new List<Reportes>();

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerReportes", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Reportes reporte = new Reportes();
                            reporte.Id = (int)reader["Id"];
                            reporte.IdEmpresa = (int)reader["IdEmpresa"];
                            reporte.Nombre = reader["Nombre"].ToString();
                            reporte.AnioInicial = reader["AñoInicial"]?.ToString();
                            reporte.AnioFinal = reader["AñoFinal"]?.ToString();
                            reporte.Descripcion = reader["Descripcion"]?.ToString();
                            reporte.EnlaceHTML = reader["EnlaceHTML"].ToString();
                            reporte.NombreEmpresa = reader["NombreEmpresa"]?.ToString();
                            reportes.Add(reporte);
                        }
                    }
                }
            }

            return reportes;
        }

        // Crear reporte
        public bool CrearReporte(Reportes reporte, out string mensaje)
        {
            bool registrado = false;
            mensaje = "";

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarReporte", cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", reporte.IdEmpresa);
                cmd.Parameters.AddWithValue("@Nombre", reporte.Nombre);
                cmd.Parameters.AddWithValue("@AñoInicial", reporte.AnioInicial ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AñoFinal", reporte.AnioFinal ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Descripcion", reporte.Descripcion ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@EnlaceHTML", reporte.EnlaceHTML);
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

        // Editar reporte
        public bool EditarReporte(Reportes reporte)
        {
            bool actualizado = false;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_EditarReporte", cn);
                cmd.Parameters.AddWithValue("@Id", reporte.Id);
                cmd.Parameters.AddWithValue("@IdEmpresa", reporte.IdEmpresa);
                cmd.Parameters.AddWithValue("@Nombre", reporte.Nombre);
                cmd.Parameters.AddWithValue("@AñoInicial", reporte.AnioInicial ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AñoFinal", reporte.AnioFinal ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Descripcion", reporte.Descripcion ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@EnlaceHTML", reporte.EnlaceHTML);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                int filas = cmd.ExecuteNonQuery();
                actualizado = filas > 0;
            }

            return actualizado;
        }

        // Eliminar reporte
        public bool EliminarReporte(int idReporte)
        {
            bool eliminado = false;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_EliminarReporte", cn);
                cmd.Parameters.AddWithValue("@Id", idReporte);
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                int filas = cmd.ExecuteNonQuery();
                eliminado = filas > 0;
            }

            return eliminado;
        }

        // Obtener empresas para el dropdown
        public List<Empresas> ObtenerEmpresas()
        {
            List<Empresas> empresas = new List<Empresas>();

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
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
                            empresas.Add(empresa);
                        }
                    }
                }
            }

            return empresas;
        }
    }
}
