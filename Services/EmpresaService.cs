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
        /// <summary>
        /// Id de la empresa principal (Bufins) a la que se asocian por defecto los Super Admin.
        /// Prioridad: clave de sistema <c>EmpresaPrincipalId</c> → empresa llamada "Bufins"
        /// (o que empiece por "Bufins") → <c>null</c> si no se encuentra ninguna.
        /// </summary>
        public int? ObtenerIdEmpresaPrincipal()
        {
            try
            {
                var cfg = new ConfiguracionSistemaService().ObtenerValor("EmpresaPrincipalId");
                if (int.TryParse(cfg, out int idCfg) && idCfg > 0)
                    return idCfg;
            }
            catch { /* la clave puede no existir */ }

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    @"SELECT TOP 1 EmpId FROM dbo.Empresas
                      WHERE EmpNombre = 'Bufins' OR EmpNombre LIKE 'Bufins%'
                      ORDER BY CASE WHEN EmpNombre = 'Bufins' THEN 0 ELSE 1 END, EmpId", cn))
                {
                    cn.Open();
                    var o = cmd.ExecuteScalar();
                    return (o != null && o != DBNull.Value) ? (int?)Convert.ToInt32(o) : null;
                }
            }
            catch { return null; }
        }

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
            AsignarPaginaWeb(empresas);

            return empresas.OrderBy(e => e.Id).ToList();
        }

        /// <summary>
        /// sp_ObtenerEmpresas no conoce la columna EmpPaginaWeb (Sql/019): se completa con una consulta inline
        /// separada. Si la columna aún no existe en esa BD, las empresas simplemente quedan sin página web.
        /// </summary>
        private void AsignarPaginaWeb(List<Empresas> empresas)
        {
            if (empresas.Count == 0) return;
            try
            {
                var webs = new Dictionary<int, string>();
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT EmpId, EmpPaginaWeb FROM dbo.Empresas WHERE EmpPaginaWeb IS NOT NULL", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            webs[(int)r["EmpId"]] = (string)r["EmpPaginaWeb"];
                }
                foreach (var e in empresas)
                    if (webs.TryGetValue(e.Id, out var w)) e.PaginaWeb = w;
            }
            catch (SqlException ex) when (ex.Number == 207) { /* columna EmpPaginaWeb inexistente (BD sin Sql/019) */ }
        }

        /// <summary>
        /// Guarda la página web (ya validada/normalizada). Devuelve false si la columna aún no existe en la BD
        /// (Sql/019) para que el llamador avise en vez de dar por guardado un dato que no se almacenó.
        /// </summary>
        private bool GuardarPaginaWeb(int idEmpresa, string paginaWeb)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("UPDATE dbo.Empresas SET EmpPaginaWeb = @w WHERE EmpId = @id", cn))
                {
                    cmd.Parameters.AddWithValue("@w", string.IsNullOrWhiteSpace(paginaWeb) ? (object)DBNull.Value : paginaWeb);
                    cmd.Parameters.AddWithValue("@id", idEmpresa);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (SqlException ex) when (ex.Number == 207) { return false; }
        }

        /// <summary>Id de la empresa recién creada (el SP no lo devuelve): la última con ese NIT y nombre.</summary>
        private int? BuscarIdEmpresa(string nit, string nombre)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand("SELECT TOP 1 EmpId FROM dbo.Empresas WHERE EmpNit = @nit AND EmpNombre = @nombre ORDER BY EmpId DESC", cn))
            {
                cmd.Parameters.AddWithValue("@nit", nit ?? "");
                cmd.Parameters.AddWithValue("@nombre", nombre ?? "");
                cn.Open();
                var o = cmd.ExecuteScalar();
                return (o != null && o != DBNull.Value) ? (int?)Convert.ToInt32(o) : null;
            }
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
        public bool CrearEmpresa(Empresas empresa, out string mensaje, out string advertencia)
        {
            bool registrado = false;
            mensaje = "";
            advertencia = null;

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

            if (registrado)
            {
                // El SP no devuelve el Id: se busca para poder guardar la página web (y para que la auditoría lo registre).
                int? id = BuscarIdEmpresa(empresa.Nit, empresa.Nombre);
                if (id.HasValue)
                {
                    empresa.Id = id.Value;
                    if (!string.IsNullOrWhiteSpace(empresa.PaginaWeb) && !GuardarPaginaWeb(id.Value, empresa.PaginaWeb))
                        advertencia = "WEB_SIN_COLUMNA";
                }
            }

            return registrado;
        }

        // Editar empresa
        public bool EditarEmpresa(Empresas empresa, out string mensaje, out string advertencia)
        {
            bool actualizado = false;
            mensaje = "";
            advertencia = null;

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

            // Se guarda también vacío (borrar la web). Solo avisa si la columna no existe y había algo que guardar.
            if (actualizado && !GuardarPaginaWeb(empresa.Id, empresa.PaginaWeb) && !string.IsNullOrWhiteSpace(empresa.PaginaWeb))
                advertencia = "WEB_SIN_COLUMNA";

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
