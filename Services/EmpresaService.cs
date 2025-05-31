using bufinscustomers.Models;
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
    }
}
