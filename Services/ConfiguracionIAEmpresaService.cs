using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace bufinscustomers.Services
{
    /// <summary>CRUD del override por empresa del presupuesto mensual de tokens de IA, y del
    /// interruptor de acceso a IA por usuario (ambos gestionados desde Configuración IA → Por Empresa).</summary>
    public class ConfiguracionIAEmpresaService : BaseService
    {
        private const string ColumnasBase = "IdEmpresa, PresupuestoTokensMensual";
        private const string ColumnasFase2 = ColumnasBase +
            ", IaHabilitada, PoliticaAgotado, PoolGrupo, DiaCorte, ModeloPermitido, FuncionesPermitidas, TopeDiarioUsuario";
        private const string ColumnasFase3 = ColumnasFase2 + ", ContextoNegocio";

        /// <summary>Configuración de IA de la empresa; sin fila (o antes de ejecutar Sql/015) devuelve los valores por defecto.</summary>
        public ConfigIAEmpresa ObtenerConfig(int idEmpresa)
        {
            var cfg = ObtenerConfigs(new List<int> { idEmpresa });
            return cfg.TryGetValue(idEmpresa, out var c) ? c : new ConfigIAEmpresa { IdEmpresa = idEmpresa };
        }

        /// <summary>Configuración de varias empresas (las que no tienen fila no aparecen en el resultado).</summary>
        public Dictionary<int, ConfigIAEmpresa> ObtenerConfigs(List<int> idsEmpresa)
        {
            var res = new Dictionary<int, ConfigIAEmpresa>();
            if (idsEmpresa == null || idsEmpresa.Count == 0) return res;
            try
            {
                // Compatible con esquemas sin migrar: Sql/016 (contexto) → Sql/015 (control) → solo presupuesto.
                try { LeerConfigs(idsEmpresa, ColumnasFase3, 3, res); }
                catch (SqlException ex) when (ex.Number == 207)
                {
                    res.Clear();
                    try { LeerConfigs(idsEmpresa, ColumnasFase2, 2, res); }
                    catch (SqlException ex2) when (ex2.Number == 207)
                    {
                        res.Clear();
                        LeerConfigs(idsEmpresa, ColumnasBase, 1, res);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionIAEmpresaService.ObtenerConfigs");
            }
            return res;
        }

        private void LeerConfigs(List<int> ids, string columnas, int nivel, Dictionary<int, ConfigIAEmpresa> res)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand())
            {
                var parametros = new List<string>();
                for (int i = 0; i < ids.Count; i++)
                {
                    parametros.Add("@id" + i);
                    cmd.Parameters.AddWithValue("@id" + i, ids[i]);
                }
                cmd.Connection = cn;
                cmd.CommandText = $"SELECT {columnas} FROM ConfiguracionIAEmpresa WHERE IdEmpresa IN ({string.Join(",", parametros)})";
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var c = new ConfigIAEmpresa
                        {
                            IdEmpresa = Convert.ToInt32(r["IdEmpresa"]),
                            PresupuestoTokensMensual = r["PresupuestoTokensMensual"] == DBNull.Value ? (long?)null : Convert.ToInt64(r["PresupuestoTokensMensual"])
                        };
                        if (nivel >= 2)
                        {
                            c.IaHabilitada = Convert.ToBoolean(r["IaHabilitada"]);
                            c.PoliticaAgotado = NormalizarPolitica(r["PoliticaAgotado"] as string);
                            c.PoolGrupo = Convert.ToBoolean(r["PoolGrupo"]);
                            c.DiaCorte = r["DiaCorte"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["DiaCorte"]);
                            c.ModeloPermitido = r["ModeloPermitido"] as string;
                            var funciones = r["FuncionesPermitidas"] as string;
                            c.FuncionesPermitidas = string.IsNullOrWhiteSpace(funciones)
                                ? null
                                : funciones.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();
                            c.TopeDiarioUsuario = r["TopeDiarioUsuario"] == DBNull.Value ? (long?)null : Convert.ToInt64(r["TopeDiarioUsuario"]);
                        }
                        if (nivel >= 3)
                            c.ContextoNegocio = r["ContextoNegocio"] as string;
                        res[c.IdEmpresa] = c;
                    }
                }
            }
        }

        public static string NormalizarPolitica(string politica)
        {
            if (string.Equals(politica, IAPoliticaAgotado.Degradar, StringComparison.OrdinalIgnoreCase)) return IAPoliticaAgotado.Degradar;
            if (string.Equals(politica, IAPoliticaAgotado.Sobreconsumo, StringComparison.OrdinalIgnoreCase)) return IAPoliticaAgotado.Sobreconsumo;
            return IAPoliticaAgotado.Bloquear;
        }

        /// <summary>Guarda (upsert) la configuración completa de la empresa. Requiere Sql/015 (y Sql/016 para el contexto propio).</summary>
        public bool GuardarConfig(ConfigIAEmpresa cfg, int? idUsuarioActualizacion)
        {
            try
            {
                try { EjecutarGuardado(cfg, idUsuarioActualizacion, true); }
                catch (SqlException ex) when (ex.Number == 207) // Sql/016 aún no se ejecutó: se guarda sin el contexto propio
                {
                    EjecutarGuardado(cfg, idUsuarioActualizacion, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "ConfiguracionIAEmpresaService.GuardarConfig");
                return false;
            }
        }

        private void EjecutarGuardado(ConfigIAEmpresa cfg, int? idUsuarioActualizacion, bool conContexto)
        {
            string setCtx = conContexto ? ", ContextoNegocio = @Contexto" : "";
            string colCtx = conContexto ? ", ContextoNegocio" : "";
            string valCtx = conContexto ? ", @Contexto" : "";

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM ConfiguracionIAEmpresa WHERE IdEmpresa = @IdEmpresa)
                    UPDATE ConfiguracionIAEmpresa
                       SET PresupuestoTokensMensual = @Presupuesto, IaHabilitada = @Hab, PoliticaAgotado = @Pol,
                           PoolGrupo = @Pool, DiaCorte = @Dia, ModeloPermitido = @Modelo,
                           FuncionesPermitidas = @Funciones, TopeDiarioUsuario = @Tope{setCtx},
                           FechaActualizacion = GETDATE(), IdUsuarioActualizacion = @IdUsuario
                     WHERE IdEmpresa = @IdEmpresa
                ELSE
                    INSERT INTO ConfiguracionIAEmpresa
                        (IdEmpresa, PresupuestoTokensMensual, IaHabilitada, PoliticaAgotado, PoolGrupo, DiaCorte,
                         ModeloPermitido, FuncionesPermitidas, TopeDiarioUsuario{colCtx}, FechaActualizacion, IdUsuarioActualizacion)
                    VALUES
                        (@IdEmpresa, @Presupuesto, @Hab, @Pol, @Pool, @Dia, @Modelo, @Funciones, @Tope{valCtx}, GETDATE(), @IdUsuario)", cn))
            {
                cmd.Parameters.AddWithValue("@IdEmpresa", cfg.IdEmpresa);
                cmd.Parameters.AddWithValue("@Presupuesto", (object)cfg.PresupuestoTokensMensual ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Hab", cfg.IaHabilitada);
                cmd.Parameters.AddWithValue("@Pol", NormalizarPolitica(cfg.PoliticaAgotado));
                cmd.Parameters.AddWithValue("@Pool", cfg.PoolGrupo);
                cmd.Parameters.AddWithValue("@Dia", (object)cfg.DiaCorte ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Modelo", string.IsNullOrWhiteSpace(cfg.ModeloPermitido) ? (object)DBNull.Value : cfg.ModeloPermitido.Trim());
                cmd.Parameters.AddWithValue("@Funciones", cfg.FuncionesPermitidas == null || cfg.FuncionesPermitidas.Count == 0
                    ? (object)DBNull.Value : string.Join(",", cfg.FuncionesPermitidas));
                cmd.Parameters.AddWithValue("@Tope", (object)cfg.TopeDiarioUsuario ?? DBNull.Value);
                if (conContexto)
                    cmd.Parameters.AddWithValue("@Contexto", string.IsNullOrWhiteSpace(cfg.ContextoNegocio) ? (object)DBNull.Value : cfg.ContextoNegocio.Trim());
                cmd.Parameters.AddWithValue("@IdUsuario", (object)idUsuarioActualizacion ?? DBNull.Value);
                cn.Open();
                cmd.ExecuteNonQuery();
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
