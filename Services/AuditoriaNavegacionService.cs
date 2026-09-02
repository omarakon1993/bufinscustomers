/*
 * TABLA REQUERIDA EN BD — ejecutar una vez:
 *
 *   CREATE TABLE dbo.AuditoriaNavegacion (
 *       Id            BIGINT        IDENTITY(1,1) CONSTRAINT PK_AuditoriaNavegacion PRIMARY KEY,
 *       Fecha         DATETIME      NOT NULL CONSTRAINT DF_AudNav_Fecha DEFAULT (GETDATE()),
 *       IdUsuario     INT           NULL,
 *       NombreUsuario NVARCHAR(150) NULL,
 *       IdEmpresa     INT           NULL,
 *       RolUsuario    TINYINT       NULL,
 *       Controller    NVARCHAR(80)  NULL,
 *       [Action]      NVARCHAR(80)  NULL,
 *       CodigoMenu    NVARCHAR(60)  NULL,
 *       TituloPagina  NVARCHAR(150) NULL,
 *       IpAddress     NVARCHAR(45)  NULL,
 *       UserAgent     NVARCHAR(300) NULL
 *   );
 *   CREATE INDEX IX_AudNav_Fecha   ON dbo.AuditoriaNavegacion (Fecha DESC);
 *   CREATE INDEX IX_AudNav_Usuario ON dbo.AuditoriaNavegacion (IdUsuario, Fecha DESC);
 *   CREATE INDEX IX_AudNav_Menu    ON dbo.AuditoriaNavegacion (CodigoMenu, Fecha DESC);
 *   CREATE INDEX IX_AudNav_Empresa ON dbo.AuditoriaNavegacion (IdEmpresa, Fecha DESC);
 *
 *   (Opcional) opción de menú para el visor:
 *   INSERT INTO MenuOpciones
 *       (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden,
 *        Controller, [Action], Activo, IdGrupo, IdCategoria,
 *        SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
 *   VALUES
 *       ('INFORMES_NAVEGACION', 'Páginas Visitadas', 'Page Visits',
 *        'Registro de páginas visitadas por cada usuario', 'fas fa-route', 95,
 *        'InformeNavegacion', 'Index', 1, NULL,
 *        (SELECT TOP 1 Id FROM CategoriasMenu WHERE Nombre = 'Informes'),
 *        0, 0, 0);
 */

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Text;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Registro y consulta del informe de páginas visitadas. La escritura nunca lanza (cae a
    /// <c>Trace</c>) y la purga por retención se hace "por debajo": una vez cada 24 h, en la
    /// misma llamada en segundo plano que inserta una visita — sin job de SQL Agent.
    /// </summary>
    public class AuditoriaNavegacionService : BaseService
    {
        private static readonly int RETENCION_DIAS = LeerInt("NavegacionRetencionDias", 90);
        private const int PURGA_BATCH = 5000;   // filas por lote al purgar
        private const int PURGA_MAX_ITER = 40;  // tope: 200k filas por ejecución diaria

        private static DateTime _ultimaPurga = DateTime.MinValue;
        private static readonly object _purgaLock = new object();

        // ── Escritura ───────────────────────────────────────────────────────

        public void Registrar(RegistroNavegacion r)
        {
            if (r == null) return;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.AuditoriaNavegacion
                        (Fecha, IdUsuario, NombreUsuario, IdEmpresa, RolUsuario,
                         Controller, [Action], CodigoMenu, TituloPagina, IpAddress, UserAgent)
                    VALUES
                        (@Fecha, @IdUsuario, @NombreUsuario, @IdEmpresa, @RolUsuario,
                         @Controller, @Action, @CodigoMenu, @TituloPagina, @IpAddress, @UserAgent)", cn))
                {
                    cmd.Parameters.AddWithValue("@Fecha",         r.Fecha == default(DateTime) ? DateTime.Now : r.Fecha);
                    cmd.Parameters.AddWithValue("@IdUsuario",      (object)r.IdUsuario ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@NombreUsuario",  (object)Rec(r.NombreUsuario, 150) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdEmpresa",      (object)r.IdEmpresa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@RolUsuario",     (object)r.RolUsuario ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Controller",     (object)Rec(r.Controller, 80) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Action",         (object)Rec(r.Action, 80) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@CodigoMenu",     (object)Rec(r.CodigoMenu, 60) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@TituloPagina",   (object)Rec(r.TituloPagina, 150) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IpAddress",      (object)Rec(r.IpAddress, 45) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserAgent",      (object)Rec(r.UserAgent, 300) ?? DBNull.Value);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaNavegacionService.Registrar] {0}", ex.Message);
            }

            PurgarSiToca();
        }

        /// <summary>Purga por retención — como mucho una vez cada 24 h, en lotes acotados.</summary>
        private void PurgarSiToca()
        {
            if ((DateTime.Now - _ultimaPurga).TotalHours < 24) return;
            lock (_purgaLock)
            {
                if ((DateTime.Now - _ultimaPurga).TotalHours < 24) return;
                _ultimaPurga = DateTime.Now;
            }

            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SET NOCOUNT ON;
                    DECLARE @b INT = 1, @i INT = 0;
                    WHILE @b > 0 AND @i < @MaxIter
                    BEGIN
                        DELETE TOP (@Batch) FROM dbo.AuditoriaNavegacion
                        WHERE Fecha < DATEADD(DAY, -@Dias, GETDATE());
                        SET @b = @@ROWCOUNT;
                        SET @i += 1;
                    END", cn))
                {
                    cmd.CommandTimeout = 120;
                    cmd.Parameters.AddWithValue("@Dias", RETENCION_DIAS);
                    cmd.Parameters.AddWithValue("@Batch", PURGA_BATCH);
                    cmd.Parameters.AddWithValue("@MaxIter", PURGA_MAX_ITER);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaNavegacionService.PurgarSiToca] {0}", ex.Message);
            }
        }

        // ── Lectura ─────────────────────────────────────────────────────────

        private const string SelectDetalle = @"
            SELECT n.Id, n.Fecha, n.IdUsuario, n.NombreUsuario, n.IdEmpresa, n.RolUsuario,
                   n.Controller, n.[Action], n.CodigoMenu, n.TituloPagina, n.IpAddress, n.UserAgent,
                   e.EmpNombre AS NombreEmpresa
            FROM dbo.AuditoriaNavegacion n
            LEFT JOIN dbo.Empresas e ON e.EmpId = n.IdEmpresa";

        public NavegacionResultado Consultar(NavegacionFiltro f)
        {
            f = f ?? new NavegacionFiltro();
            int pagina = f.Pagina < 1 ? 1 : f.Pagina;
            int tam = f.TamanoPagina < 1 ? 25 : (f.TamanoPagina > 200 ? 200 : f.TamanoPagina);

            var res = new NavegacionResultado { Pagina = pagina, TamanoPagina = tam };
            var (where, pars) = ConstruirFiltro(f);

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.AuditoriaNavegacion n" + where, cn))
                {
                    foreach (var p in pars) cmd.Parameters.Add(Clonar(p));
                    res.Total = Convert.ToInt32(cmd.ExecuteScalar());
                }

                string sql = SelectDetalle + where + @"
                    ORDER BY n.Fecha DESC, n.Id DESC
                    OFFSET @Offset ROWS FETCH NEXT @Tam ROWS ONLY";
                using (var cmd = new SqlCommand(sql, cn))
                {
                    foreach (var p in pars) cmd.Parameters.Add(Clonar(p));
                    cmd.Parameters.AddWithValue("@Offset", (pagina - 1) * tam);
                    cmd.Parameters.AddWithValue("@Tam", tam);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) res.Items.Add(Map(r));
                }
            }
            return res;
        }

        public List<RegistroNavegacion> ConsultarParaExport(NavegacionFiltro f, int maxFilas = 20000)
        {
            var lista = new List<RegistroNavegacion>();
            var (where, pars) = ConstruirFiltro(f ?? new NavegacionFiltro());

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                SelectDetalle.Replace("SELECT n.Id", "SELECT TOP (" + (maxFilas < 1 ? 1 : maxFilas) + ") n.Id")
                + where + " ORDER BY n.Fecha DESC, n.Id DESC", cn))
            {
                foreach (var p in pars) cmd.Parameters.Add(Clonar(p));
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) lista.Add(Map(r));
            }
            return lista;
        }

        public List<NavegacionResumenUsuario> ResumenPorUsuario(NavegacionFiltro f, int top = 500)
        {
            var lista = new List<NavegacionResumenUsuario>();
            var (where, pars) = ConstruirFiltro(f ?? new NavegacionFiltro());

            string sql = @"
                SELECT TOP (" + (top < 1 ? 1 : top) + @") n.IdUsuario,
                       MAX(n.NombreUsuario) AS NombreUsuario,
                       MAX(e.EmpNombre)     AS NombreEmpresa,
                       COUNT(*)             AS Visitas,
                       COUNT(DISTINCT COALESCE(n.CodigoMenu, n.Controller + '/' + n.[Action])) AS PaginasDistintas,
                       MIN(n.Fecha)         AS Primera,
                       MAX(n.Fecha)         AS Ultima
                FROM dbo.AuditoriaNavegacion n
                LEFT JOIN dbo.Empresas e ON e.EmpId = n.IdEmpresa" + where + @"
                GROUP BY n.IdUsuario
                ORDER BY Visitas DESC";

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, cn))
            {
                foreach (var p in pars) cmd.Parameters.Add(Clonar(p));
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(new NavegacionResumenUsuario
                        {
                            IdUsuario        = r["IdUsuario"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["IdUsuario"]),
                            NombreUsuario    = r["NombreUsuario"] == DBNull.Value ? null : r["NombreUsuario"].ToString(),
                            NombreEmpresa    = r["NombreEmpresa"] == DBNull.Value ? null : r["NombreEmpresa"].ToString(),
                            Visitas          = Convert.ToInt32(r["Visitas"]),
                            PaginasDistintas = Convert.ToInt32(r["PaginasDistintas"]),
                            Primera          = Convert.ToDateTime(r["Primera"]),
                            Ultima           = Convert.ToDateTime(r["Ultima"])
                        });
            }
            return lista;
        }

        public List<NavegacionResumenPagina> ResumenPorPagina(NavegacionFiltro f, int top = 500)
        {
            var lista = new List<NavegacionResumenPagina>();
            var (where, pars) = ConstruirFiltro(f ?? new NavegacionFiltro());

            string sql = @"
                SELECT TOP (" + (top < 1 ? 1 : top) + @") COALESCE(n.CodigoMenu, n.Controller + '/' + n.[Action]) AS Clave,
                       MAX(n.TituloPagina) AS Titulo,
                       MAX(n.Controller)   AS Controller,
                       MAX(n.[Action])     AS [Action],
                       COUNT(*)            AS Visitas,
                       COUNT(DISTINCT n.IdUsuario) AS UsuariosDistintos,
                       MAX(n.Fecha)        AS Ultima
                FROM dbo.AuditoriaNavegacion n" + where + @"
                GROUP BY COALESCE(n.CodigoMenu, n.Controller + '/' + n.[Action])
                ORDER BY Visitas DESC";

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(sql, cn))
            {
                foreach (var p in pars) cmd.Parameters.Add(Clonar(p));
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(new NavegacionResumenPagina
                        {
                            Clave             = r["Clave"] == DBNull.Value ? null : r["Clave"].ToString(),
                            Titulo            = r["Titulo"] == DBNull.Value ? null : r["Titulo"].ToString(),
                            Controller        = r["Controller"] == DBNull.Value ? null : r["Controller"].ToString(),
                            Action            = r["Action"] == DBNull.Value ? null : r["Action"].ToString(),
                            Visitas           = Convert.ToInt32(r["Visitas"]),
                            UsuariosDistintos = Convert.ToInt32(r["UsuariosDistintos"]),
                            Ultima            = Convert.ToDateTime(r["Ultima"])
                        });
            }
            return lista;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static (string where, List<SqlParameter> pars) ConstruirFiltro(NavegacionFiltro f)
        {
            var w = new StringBuilder(" WHERE 1 = 1 ");
            var pars = new List<SqlParameter>();

            void Add(string clausula, string nombre, object valor)
            {
                w.Append(clausula);
                pars.Add(new SqlParameter(nombre, valor ?? DBNull.Value));
            }

            if (f.IdUsuario.HasValue)                     Add(" AND n.IdUsuario = @IdUsuario", "@IdUsuario", f.IdUsuario.Value);
            if (f.IdEmpresa.HasValue)                     Add(" AND n.IdEmpresa = @IdEmpresa", "@IdEmpresa", f.IdEmpresa.Value);
            if (!string.IsNullOrWhiteSpace(f.CodigoMenu)) Add(" AND n.CodigoMenu = @CodigoMenu", "@CodigoMenu", f.CodigoMenu.Trim());
            if (f.Rol.HasValue)                           Add(" AND n.RolUsuario = @Rol", "@Rol", f.Rol.Value);
            if (f.Desde.HasValue)                         Add(" AND n.Fecha >= @Desde", "@Desde", f.Desde.Value.Date);
            if (f.Hasta.HasValue)                         Add(" AND n.Fecha < @Hasta", "@Hasta", f.Hasta.Value.Date.AddDays(1));

            if (f.IdsEmpresaPermitidas != null)
            {
                if (f.IdsEmpresaPermitidas.Count == 0)
                {
                    w.Append(" AND 1 = 0");
                }
                else
                {
                    var nombres = new List<string>();
                    for (int i = 0; i < f.IdsEmpresaPermitidas.Count; i++)
                    {
                        var n = "@EmpPerm" + i;
                        nombres.Add(n);
                        pars.Add(new SqlParameter(n, f.IdsEmpresaPermitidas[i]));
                    }
                    w.Append(" AND n.IdEmpresa IN (").Append(string.Join(",", nombres)).Append(")");
                }
            }

            return (w.ToString(), pars);
        }

        private static SqlParameter Clonar(SqlParameter p) => new SqlParameter(p.ParameterName, p.Value);

        private static RegistroNavegacion Map(SqlDataReader r) => new RegistroNavegacion
        {
            Id            = Convert.ToInt64(r["Id"]),
            Fecha         = Convert.ToDateTime(r["Fecha"]),
            IdUsuario     = r["IdUsuario"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["IdUsuario"]),
            NombreUsuario = r["NombreUsuario"] == DBNull.Value ? null : r["NombreUsuario"].ToString(),
            IdEmpresa     = r["IdEmpresa"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["IdEmpresa"]),
            RolUsuario    = r["RolUsuario"] == DBNull.Value ? (byte?)null : Convert.ToByte(r["RolUsuario"]),
            Controller    = r["Controller"] == DBNull.Value ? null : r["Controller"].ToString(),
            Action        = r["Action"] == DBNull.Value ? null : r["Action"].ToString(),
            CodigoMenu    = r["CodigoMenu"] == DBNull.Value ? null : r["CodigoMenu"].ToString(),
            TituloPagina  = r["TituloPagina"] == DBNull.Value ? null : r["TituloPagina"].ToString(),
            IpAddress     = r["IpAddress"] == DBNull.Value ? null : r["IpAddress"].ToString(),
            UserAgent     = r["UserAgent"] == DBNull.Value ? null : r["UserAgent"].ToString(),
            NombreEmpresa = r["NombreEmpresa"] == DBNull.Value ? null : r["NombreEmpresa"].ToString()
        };

        private static string Rec(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        private static int LeerInt(string clave, int porDefecto) =>
            int.TryParse(ConfigurationManager.AppSettings[clave], out int v) && v > 0 ? v : porDefecto;
    }
}
