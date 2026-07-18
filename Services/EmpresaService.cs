using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    public class EmpresaService : BaseService
    {
        public List<Empresas> ObtenerEmpresas()
        {
            List<Empresas> empresas = new List<Empresas>();

            using (SqlConnection connection = new SqlConnection(CadenaConexion))
            {
                using (SqlCommand command = new SqlCommand("sp_ObtenerEmpresas", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();

                    using ( SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Empresas empresa = new Empresas();
                            empresa.Id = (int)reader["EmpId"];
                            empresa.Nombre = (string)reader["EmpNombre"];
                            empresa.Nit = (string)reader["EmpNit"];
                            empresa.Direccion = (string)reader["EmpDireccion"];
                            empresa.Telefono = reader["EmpTelefono"] != DBNull.Value ? (string)reader["EmpTelefono"] : string.Empty;
                            empresa.Correo = reader["EmpCorreo"] != DBNull.Value ? (string)reader["EmpCorreo"] : string.Empty;
                            try { empresa.Abreviatura = reader["EmpAbreviatura"] != DBNull.Value ? (string)reader["EmpAbreviatura"] : string.Empty; }
                            catch (IndexOutOfRangeException) { }
                            empresas.Add(empresa);
                        }
                    }
                }
            }

            AsignarGrupoEmpresarial(empresas);

            return empresas.OrderBy(e => e.Id).ToList();
        }

        /// <summary>
        /// sp_ObtenerEmpresas no conoce la columna IdGrupoEmpresarial (agregada después a la
        /// tabla Empresas), así que el grupo se completa con una consulta inline separada
        /// en vez de modificar el stored procedure existente.
        /// </summary>
        private void AsignarGrupoEmpresarial(List<Empresas> empresas)
        {
            if (empresas.Count == 0) return;

            var grupos = new Dictionary<int, (int? IdGrupo, string NombreGrupo)>();

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    @"SELECT e.EmpId AS Id, e.IdGrupoEmpresarial, g.Nombre AS NombreGrupo
                      FROM Empresas e
                      LEFT JOIN GruposEmpresariales g ON g.Id = e.IdGrupoEmpresarial", cn);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = (int)reader["Id"];
                        int? idGrupo = reader["IdGrupoEmpresarial"] == DBNull.Value ? (int?)null : (int)reader["IdGrupoEmpresarial"];
                        string nombreGrupo = reader["NombreGrupo"] == DBNull.Value ? null : (string)reader["NombreGrupo"];
                        grupos[id] = (idGrupo, nombreGrupo);
                    }
                }
            }

            foreach (var empresa in empresas)
            {
                if (grupos.TryGetValue(empresa.Id, out var info))
                {
                    empresa.IdGrupoEmpresarial = info.IdGrupo;
                    empresa.NombreGrupoEmpresarial = info.NombreGrupo;
                }
            }
        }

        // Crear empresa
        public bool CrearEmpresa(Empresas empresa, out string mensaje)
        {
            bool registrado = false;
            mensaje = "";

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_RegistrarEmpresa", cn);
                cmd.Parameters.AddWithValue("@EmpNombre", empresa.Nombre);
                cmd.Parameters.AddWithValue("@EmpNit", empresa.Nit);
                cmd.Parameters.AddWithValue("@EmpDireccion", empresa.Direccion);
                cmd.Parameters.AddWithValue("@EmpTelefono", string.IsNullOrWhiteSpace(empresa.Telefono) ? (object)DBNull.Value : empresa.Telefono);
                cmd.Parameters.AddWithValue("@EmpCorreo", string.IsNullOrWhiteSpace(empresa.Correo) ? (object)DBNull.Value : empresa.Correo);
                cmd.Parameters.AddWithValue("@EmpAbreviatura", string.IsNullOrWhiteSpace(empresa.Abreviatura) ? (object)DBNull.Value : empresa.Abreviatura);
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
        public bool EditarEmpresa(Empresas empresa, out string mensaje)
        {
            bool actualizado = false;
            mensaje = "";

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand("sp_EditarEmpresa", cn);
                cmd.Parameters.AddWithValue("@EmpId", empresa.Id);
                cmd.Parameters.AddWithValue("@EmpNombre", empresa.Nombre);
                cmd.Parameters.AddWithValue("@EmpNit", empresa.Nit);
                cmd.Parameters.AddWithValue("@EmpDireccion", empresa.Direccion);
                cmd.Parameters.AddWithValue("@EmpTelefono", string.IsNullOrWhiteSpace(empresa.Telefono) ? (object)DBNull.Value : empresa.Telefono);
                cmd.Parameters.AddWithValue("@EmpCorreo", string.IsNullOrWhiteSpace(empresa.Correo) ? (object)DBNull.Value : empresa.Correo);
                cmd.Parameters.AddWithValue("@EmpAbreviatura", string.IsNullOrWhiteSpace(empresa.Abreviatura) ? (object)DBNull.Value : empresa.Abreviatura);
                cmd.Parameters.Add("@Actualizado", SqlDbType.Bit).Direction = ParameterDirection.Output;
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 200).Direction = ParameterDirection.Output;
                cmd.CommandType = CommandType.StoredProcedure;
                cn.Open();
                cmd.ExecuteNonQuery();
                actualizado = Convert.ToBoolean(cmd.Parameters["@Actualizado"].Value);
                mensaje = cmd.Parameters["@Mensaje"].Value.ToString();
            }

            return actualizado;
        }

        // Eliminar empresa
        public bool EliminarEmpresa(int idEmpresa)
        {
            bool eliminado = false;

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
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
