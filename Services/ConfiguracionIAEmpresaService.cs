using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>CRUD del override por empresa del presupuesto mensual de tokens de IA, y del
    /// interruptor de acceso a IA por usuario (ambos gestionados desde Configuración IA → Por Empresa).</summary>
    public class ConfiguracionIAEmpresaService : BaseService
    {
        /// <summary>Override de presupuesto mensual de tokens para la empresa, o null si no tiene
        /// (usa el valor global <c>IA_TokensMensualesPorEmpresa</c>).</summary>
        public long? ObtenerOverride(int idEmpresa)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "SELECT PresupuestoTokensMensual FROM ConfiguracionIAEmpresa WHERE IdEmpresa = @IdEmpresa", cn))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cn.Open();
                    var r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? (long?)null : Convert.ToInt64(r);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionIAEmpresaService.ObtenerOverride");
                return null;
            }
        }

        /// <summary>Guarda el override de presupuesto de la empresa, o lo elimina (vuelve a usar el
        /// valor global) cuando <paramref name="presupuesto"/> es null.</summary>
        public bool Guardar(int idEmpresa, long? presupuesto, int? idUsuarioActualizacion)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    cn.Open();

                    if (!presupuesto.HasValue)
                    {
                        using (var cmd = new SqlCommand(
                            "DELETE FROM ConfiguracionIAEmpresa WHERE IdEmpresa = @IdEmpresa", cn))
                        {
                            cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                            cmd.ExecuteNonQuery();
                        }
                        return true;
                    }

                    using (var cmd = new SqlCommand(@"
                        IF EXISTS (SELECT 1 FROM ConfiguracionIAEmpresa WHERE IdEmpresa = @IdEmpresa)
                            UPDATE ConfiguracionIAEmpresa
                               SET PresupuestoTokensMensual = @Presupuesto,
                                   FechaActualizacion = GETDATE(),
                                   IdUsuarioActualizacion = @IdUsuario
                             WHERE IdEmpresa = @IdEmpresa
                        ELSE
                            INSERT INTO ConfiguracionIAEmpresa (IdEmpresa, PresupuestoTokensMensual, FechaActualizacion, IdUsuarioActualizacion)
                            VALUES (@IdEmpresa, @Presupuesto, GETDATE(), @IdUsuario)", cn))
                    {
                        cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                        cmd.Parameters.AddWithValue("@Presupuesto", presupuesto.Value);
                        cmd.Parameters.AddWithValue("@IdUsuario", (object)idUsuarioActualizacion ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionIAEmpresaService.Guardar");
                return false;
            }
        }

        /// <summary>Usuarios normales/admin de la empresa (excluye Super Admin) con su acceso a IA.</summary>
        public List<UsuarioAccesoIAViewModel> ObtenerUsuariosDeEmpresa(int idEmpresa)
        {
            var lista = new List<UsuarioAccesoIAViewModel>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT Id, Nombre, Apellidos, Correo, Admin, AccesoConsultasIA
                    FROM Usuarios
                    WHERE IdEmpresa = @IdEmpresa AND (Admin IS NULL OR Admin <> 2)
                    ORDER BY Nombre, Apellidos", cn))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            bool? acceso = r["AccesoConsultasIA"] == DBNull.Value
                                ? (bool?)null
                                : Convert.ToBoolean(r["AccesoConsultasIA"]);

                            lista.Add(new UsuarioAccesoIAViewModel
                            {
                                Id             = Convert.ToInt32(r["Id"]),
                                NombreCompleto = $"{r["Nombre"]} {r["Apellidos"]}".Trim(),
                                Correo         = r["Correo"].ToString(),
                                Admin          = r["Admin"] == DBNull.Value ? (byte?)null : Convert.ToByte(r["Admin"]),
                                AccesoExplicito = acceso,
                                AccesoEfectivo  = acceso ?? true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionIAEmpresaService.ObtenerUsuariosDeEmpresa");
            }
            return lista;
        }

        /// <summary>Fija el acceso explícito de un usuario (null = volver al valor por defecto).</summary>
        public bool GuardarAccesoUsuario(int idUsuario, bool? acceso)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE Usuarios SET AccesoConsultasIA = @Acceso WHERE Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Acceso", (object)acceso ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", idUsuario);
                    cn.Open();
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionIAEmpresaService.GuardarAccesoUsuario");
                return false;
            }
        }
    }
}
