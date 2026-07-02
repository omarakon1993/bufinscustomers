using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Administra qué tablas financieras usa cada empresa para generar el resumen ejecutivo IA del Dashboard.
    /// </summary>
    public class EmpresaTablasResumenIAService : BaseService
    {
        public List<string> ObtenerTablasAsignadas(int idEmpresa)
        {
            var tablas = new List<string>();
            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                SqlCommand cmd = new SqlCommand(
                    "SELECT NombreTabla FROM EmpresaTablasResumenIA WHERE IdEmpresa = @IdEmpresa", cn);
                cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                cn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        tablas.Add(reader["NombreTabla"].ToString());
                }
            }
            return tablas;
        }

        /// <summary>
        /// Reemplaza el conjunto completo de tablas asignadas a una empresa (delete + reinsert transaccional).
        /// </summary>
        public void GuardarTablasEmpresa(int idEmpresa, List<string> tablas, int usuarioAsigno)
        {
            var tablasValidas = new InformeTablasDatosService().ObtenerTablasDisponibles();
            var nombresValidos = new HashSet<string>();
            foreach (var t in tablasValidas) nombresValidos.Add(t.NombreTabla);

            using (SqlConnection cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();
                using (SqlTransaction transaction = cn.BeginTransaction())
                {
                    try
                    {
                        using (SqlCommand cmdDelete = new SqlCommand(
                            "DELETE FROM EmpresaTablasResumenIA WHERE IdEmpresa = @IdEmpresa", cn, transaction))
                        {
                            cmdDelete.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                            cmdDelete.ExecuteNonQuery();
                        }

                        if (tablas != null)
                        {
                            foreach (var nombreTabla in tablas)
                            {
                                if (!nombresValidos.Contains(nombreTabla)) continue;

                                using (SqlCommand cmdInsert = new SqlCommand(
                                    @"INSERT INTO EmpresaTablasResumenIA (IdEmpresa, NombreTabla, UsuarioAsigno, FechaAsignacion)
                                      VALUES (@IdEmpresa, @NombreTabla, @UsuarioAsigno, GETDATE())", cn, transaction))
                                {
                                    cmdInsert.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                                    cmdInsert.Parameters.AddWithValue("@NombreTabla", nombreTabla);
                                    cmdInsert.Parameters.AddWithValue("@UsuarioAsigno", usuarioAsigno);
                                    cmdInsert.ExecuteNonQuery();
                                }
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
