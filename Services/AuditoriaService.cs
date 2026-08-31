/*
 * TABLA ÚNICA DE AUDITORÍA — ejecutar una vez en la BD:
 *
 *   CREATE TABLE Auditoria (
 *       Id             BIGINT         IDENTITY(1,1) PRIMARY KEY,
 *       Fecha          DATETIME       NOT NULL CONSTRAINT DF_Auditoria_Fecha DEFAULT GETDATE(),
 *       Tipo           NVARCHAR(40)   NOT NULL,     -- CONFIGURACION | SEGURIDAD | USUARIOS | EMPRESAS | ...
 *       Categoria      NVARCHAR(60)   NULL,
 *       Accion         NVARCHAR(30)   NOT NULL,     -- Crear | Editar | Eliminar | Login | LoginFallido | ...
 *       Entidad        NVARCHAR(80)   NULL,
 *       EntidadId      NVARCHAR(60)   NULL,
 *       Descripcion    NVARCHAR(400)  NULL,
 *       ValorAnterior  NVARCHAR(MAX)  NULL,
 *       ValorNuevo     NVARCHAR(MAX)  NULL,
 *       IdUsuario      INT            NULL,
 *       NombreUsuario  NVARCHAR(150)  NULL,
 *       IdEmpresa      INT            NULL,
 *       IpAddress      NVARCHAR(45)   NULL,
 *       UserAgent      NVARCHAR(300)  NULL
 *   );
 *   CREATE INDEX IX_Auditoria_Fecha   ON Auditoria (Fecha DESC);
 *   CREATE INDEX IX_Auditoria_Tipo    ON Auditoria (Tipo, Fecha DESC);
 *   CREATE INDEX IX_Auditoria_Usuario ON Auditoria (IdUsuario, Fecha DESC);
 *   CREATE INDEX IX_Auditoria_Empresa ON Auditoria (IdEmpresa, Fecha DESC);
 *
 * (Opcional) opción de menú para el visor — mismas columnas que usa MenuOpcionesService.CrearMenuOpcion.
 *   IdCategoria se resuelve por nombre; ajústalo si la categoría "Informes" se llama distinto,
 *   o crea la opción desde Configuración → Gestor de Menú.
 *   SoloSuperAdmin = 0 para poder asignar el permiso INFORMES_AUDITORIA_GENERAL también a un
 *   Admin de Empresa: en ese caso el visor se recorta solo a los inicios de sesión (Tipo=SEGURIDAD)
 *   de los usuarios de su empresa / grupo. El Super Admin ve todo. Otros roles no tienen acceso.
 *
 *   INSERT INTO MenuOpciones
 *       (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden,
 *        Controller, [Action], Activo, IdGrupo, IdCategoria,
 *        SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
 *   VALUES
 *       ('INFORMES_AUDITORIA_GENERAL', 'Auditoría General', 'General Audit Log',
 *        'Bitácora unificada de toda la auditoría del sistema', 'fas fa-shield-alt', 90,
 *        'Auditoria', 'Index', 1,
 *        NULL,
 *        (SELECT TOP 1 Id FROM CategoriasMenu WHERE Nombre = 'Informes'),
 *        0, 0, 0);
 */

using bufinscustomers.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Servicio central de auditoría. Todas las auditorías del sistema se escriben en la
    /// tabla <c>Auditoria</c>, diferenciadas por <see cref="RegistroAuditoria.Tipo"/>.
    /// Nunca lanza excepciones: si el registro falla, cae a <c>Trace</c> y sigue.
    ///
    /// Para auditar algo nuevo: elegir/crear un código en <see cref="AuditoriaTipo"/> y llamar
    /// <see cref="RegistrarCambio"/> desde el controlador tras la operación exitosa.
    /// </summary>
    public class AuditoriaService : BaseService
    {
        // ── Escritura ────────────────────────────────────────────────────────

        /// <summary>Registro de bajo nivel. Autocompleta Fecha, usuario, IP y user-agent desde el contexto si vienen vacíos.</summary>
        public void Registrar(RegistroAuditoria r)
        {
            try
            {
                if (r == null) return;
                if (r.Fecha == default(DateTime)) r.Fecha = DateTime.Now;
                if (string.IsNullOrWhiteSpace(r.Categoria)) r.Categoria = r.Tipo;

                CompletarDesdeContexto(r);

                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        INSERT INTO Auditoria
                            (Fecha, Tipo, Categoria, Accion, Entidad, EntidadId, Descripcion,
                             ValorAnterior, ValorNuevo, IdUsuario, NombreUsuario, IdEmpresa, IpAddress, UserAgent)
                        VALUES
                            (@Fecha, @Tipo, @Categoria, @Accion, @Entidad, @EntidadId, @Descripcion,
                             @ValorAnterior, @ValorNuevo, @IdUsuario, @NombreUsuario, @IdEmpresa, @IpAddress, @UserAgent)", cn);

                    cmd.Parameters.AddWithValue("@Fecha",         r.Fecha);
                    cmd.Parameters.AddWithValue("@Tipo",          Recorte(r.Tipo, 40) ?? "GENERAL");
                    cmd.Parameters.AddWithValue("@Categoria",     (object)Recorte(r.Categoria, 60) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Accion",        Recorte(r.Accion, 30) ?? "-");
                    cmd.Parameters.AddWithValue("@Entidad",       (object)Recorte(r.Entidad, 80) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EntidadId",     (object)Recorte(r.EntidadId, 60) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Descripcion",   (object)Recorte(r.Descripcion, 400) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ValorAnterior", (object)r.ValorAnterior ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ValorNuevo",    (object)r.ValorNuevo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdUsuario",     (object)r.IdUsuario ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@NombreUsuario", (object)Recorte(r.NombreUsuario, 150) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdEmpresa",     (object)r.IdEmpresa ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IpAddress",     (object)Recorte(r.IpAddress, 45) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserAgent",     (object)Recorte(r.UserAgent, 300) ?? DBNull.Value);

                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[AuditoriaService.Registrar] {0}", ex.Message);
            }
        }

        /// <summary>Atajo para cambios de configuración/CRUD. Serializa <paramref name="valorAnterior"/>/<paramref name="valorNuevo"/> a JSON.</summary>
        public void RegistrarCambio(string tipo, string accion, string entidad, string entidadId,
            string descripcion, object valorAnterior = null, object valorNuevo = null,
            int? idEmpresa = null, string categoria = null)
        {
            Registrar(new RegistroAuditoria
            {
                Tipo          = tipo,
                Categoria     = categoria,
                Accion        = accion,
                Entidad       = entidad,
                EntidadId     = entidadId,
                Descripcion   = descripcion,
                ValorAnterior = Serializar(valorAnterior),
                ValorNuevo    = Serializar(valorNuevo),
                IdEmpresa     = idEmpresa
            });
        }

        /// <summary>
        /// Eventos de seguridad (login OK/fallido, bloqueo). El usuario y la empresa se pasan
        /// explícitos porque en el login todavía no hay sesión establecida.
        /// </summary>
        public void RegistrarSeguridad(string accion, string descripcion, int? idUsuario,
            string nombreUsuario, int? idEmpresa = null)
        {
            Registrar(new RegistroAuditoria
            {
                Tipo          = AuditoriaTipo.Seguridad,
                Accion        = accion,
                Entidad       = "Sesion",
                Descripcion   = descripcion,
                IdUsuario     = idUsuario,
                NombreUsuario = nombreUsuario,
                IdEmpresa     = idEmpresa
            });
        }

        // ── Lectura (visor, paginación server-side) ──────────────────────────

        public AuditoriaResultado Consultar(AuditoriaFiltro f)
        {
            f = f ?? new AuditoriaFiltro();
            int pagina = f.Pagina < 1 ? 1 : f.Pagina;
            int tam    = f.TamanoPagina < 1 ? 25 : (f.TamanoPagina > 200 ? 200 : f.TamanoPagina);

            var res = new AuditoriaResultado { Pagina = pagina, TamanoPagina = tam };
            var (filtro, parametros) = ConstruirFiltro(f);

            using (var cn = new SqlConnection(CadenaConexion))
            {
                cn.Open();

                using (var cmdCount = new SqlCommand("SELECT COUNT(*) FROM Auditoria a" + filtro, cn))
                {
                    foreach (var p in parametros) cmdCount.Parameters.Add(Clonar(p));
                    res.Total = Convert.ToInt32(cmdCount.ExecuteScalar());
                }

                string sql = SelectBase + filtro + @"
                    ORDER BY a.Fecha DESC, a.Id DESC
                    OFFSET @Offset ROWS FETCH NEXT @Tam ROWS ONLY";

                using (var cmd = new SqlCommand(sql, cn))
                {
                    foreach (var p in parametros) cmd.Parameters.Add(Clonar(p));
                    cmd.Parameters.AddWithValue("@Offset", (pagina - 1) * tam);
                    cmd.Parameters.AddWithValue("@Tam", tam);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            res.Items.Add(Map(r));
                }
            }
            return res;
        }

        /// <summary>Mismos filtros que <see cref="Consultar"/> pero sin paginar, con tope de filas (para exportar).</summary>
        public List<RegistroAuditoria> ConsultarParaExport(AuditoriaFiltro f, int maxFilas = 20000)
        {
            var lista = new List<RegistroAuditoria>();
            var (filtro, parametros) = ConstruirFiltro(f ?? new AuditoriaFiltro());

            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(
                "SELECT TOP (@Max) " + SelectColumnas + " FROM dbo.Auditoria a LEFT JOIN dbo.Empresas e ON e.EmpId = a.IdEmpresa"
                + filtro + " ORDER BY a.Fecha DESC, a.Id DESC", cn))
            {
                foreach (var p in parametros) cmd.Parameters.Add(Clonar(p));
                cmd.Parameters.AddWithValue("@Max", maxFilas < 1 ? 1 : maxFilas);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(Map(r));
            }
            return lista;
        }

        private const string SelectColumnas =
            @"a.Id, a.Fecha, a.Tipo, a.Categoria, a.Accion, a.Entidad, a.EntidadId,
              a.Descripcion, a.ValorAnterior, a.ValorNuevo, a.IdUsuario, a.NombreUsuario,
              a.IdEmpresa, e.EmpNombre AS NombreEmpresa, a.IpAddress, a.UserAgent";

        private const string SelectBase =
            "SELECT " + SelectColumnas + " FROM dbo.Auditoria a LEFT JOIN dbo.Empresas e ON e.EmpId = a.IdEmpresa";

        private static (string filtro, List<SqlParameter> parametros) ConstruirFiltro(AuditoriaFiltro f)
        {
            var where = new StringBuilder(" WHERE 1 = 1 ");
            var parametros = new List<SqlParameter>();

            void Add(string clausula, string nombre, object valor)
            {
                where.Append(clausula);
                parametros.Add(new SqlParameter(nombre, valor ?? DBNull.Value));
            }

            if (!string.IsNullOrWhiteSpace(f.Tipo))    Add(" AND a.Tipo = @Tipo",       "@Tipo", f.Tipo.Trim());
            if (!string.IsNullOrWhiteSpace(f.Accion))  Add(" AND a.Accion = @Accion",   "@Accion", f.Accion.Trim());
            if (f.IdUsuario.HasValue)                  Add(" AND a.IdUsuario = @IdUsuario", "@IdUsuario", f.IdUsuario.Value);
            if (f.IdEmpresa.HasValue)                  Add(" AND a.IdEmpresa = @IdEmpresa", "@IdEmpresa", f.IdEmpresa.Value);
            if (!string.IsNullOrWhiteSpace(f.Entidad)) Add(" AND a.Entidad = @Entidad", "@Entidad", f.Entidad.Trim());
            if (f.Desde.HasValue)                      Add(" AND a.Fecha >= @Desde",    "@Desde", f.Desde.Value.Date);
            if (f.Hasta.HasValue)                      Add(" AND a.Fecha < @Hasta",     "@Hasta", f.Hasta.Value.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(f.Texto))
                Add(" AND (a.Descripcion LIKE @Texto OR a.NombreUsuario LIKE @Texto OR a.EntidadId LIKE @Texto)",
                    "@Texto", "%" + f.Texto.Trim() + "%");

            // Alcance por empresa (usuarios que no son Super Admin).
            if (f.IdsEmpresaPermitidas != null)
            {
                if (f.IdsEmpresaPermitidas.Count == 0)
                {
                    where.Append(" AND 1 = 0");
                }
                else
                {
                    var nombres = new List<string>();
                    for (int i = 0; i < f.IdsEmpresaPermitidas.Count; i++)
                    {
                        var n = "@EmpPerm" + i;
                        nombres.Add(n);
                        parametros.Add(new SqlParameter(n, f.IdsEmpresaPermitidas[i]));
                    }
                    where.Append(" AND a.IdEmpresa IN (").Append(string.Join(",", nombres)).Append(")");
                }
            }

            return (where.ToString(), parametros);
        }

        public RegistroAuditoria ObtenerPorId(long id)
        {
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand(@"
                SELECT a.Id, a.Fecha, a.Tipo, a.Categoria, a.Accion, a.Entidad, a.EntidadId,
                       a.Descripcion, a.ValorAnterior, a.ValorNuevo, a.IdUsuario, a.NombreUsuario,
                       a.IdEmpresa, e.EmpNombre AS NombreEmpresa, a.IpAddress, a.UserAgent
                FROM dbo.Auditoria a LEFT JOIN dbo.Empresas e ON e.EmpId = a.IdEmpresa
                WHERE a.Id = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) return Map(r);
            }
            return null;
        }

        /// <summary>
        /// Borra filas de auditoría (solo Super Admin). <paramref name="mesesConservar"/> null/&lt;=0 = todo;
        /// en caso contrario conserva los últimos N meses. <paramref name="idEmpresa"/> null = todas las
        /// empresas; con valor = solo esa empresa. Devuelve las filas eliminadas.
        /// </summary>
        public int Limpiar(int? mesesConservar, int? idEmpresa)
        {
            var sql = new StringBuilder("DELETE FROM dbo.Auditoria WHERE 1 = 1");
            using (var cn = new SqlConnection(CadenaConexion))
            using (var cmd = new SqlCommand())
            {
                if (idEmpresa.HasValue)
                {
                    sql.Append(" AND IdEmpresa = @IdEmpresa");
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa.Value);
                }
                if (mesesConservar.HasValue && mesesConservar.Value > 0)
                {
                    sql.Append(" AND Fecha < DATEADD(MONTH, -@Meses, GETDATE())");
                    cmd.Parameters.AddWithValue("@Meses", mesesConservar.Value);
                }
                cmd.Connection = cn;
                cmd.CommandText = sql.ToString();
                cn.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Valores distintos de <c>Tipo</c> presentes, para poblar el filtro del visor.</summary>
        public List<string> ObtenerTiposUsados()
        {
            var lista = new List<string>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT DISTINCT Tipo FROM Auditoria ORDER BY Tipo", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) lista.Add(r.GetString(0));
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("[AuditoriaService.ObtenerTiposUsados] {0}", ex.Message); }
            return lista;
        }

        // ── Helpers privados ────────────────────────────────────────────────

        private static void CompletarDesdeContexto(RegistroAuditoria r)
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx == null) return;

                if (string.IsNullOrEmpty(r.IpAddress)) r.IpAddress = ctx.Request?.UserHostAddress;
                if (string.IsNullOrEmpty(r.UserAgent)) r.UserAgent = ctx.Request?.UserAgent;

                if (!r.IdUsuario.HasValue || string.IsNullOrEmpty(r.NombreUsuario))
                {
                    var u = ctx.Session?["UsuarioCompleto"] as Usuarios;
                    if (u != null)
                    {
                        if (!r.IdUsuario.HasValue) r.IdUsuario = u.Id;
                        if (string.IsNullOrEmpty(r.NombreUsuario))
                            r.NombreUsuario = ((u.Nombre ?? "") + " " + (u.Apellidos ?? "")).Trim();
                        if (!r.IdEmpresa.HasValue) r.IdEmpresa = u.IdEmpresa;
                    }
                }
            }
            catch { /* contexto no disponible */ }
        }

        private static string Serializar(object o)
        {
            if (o == null) return null;
            if (o is string s) return s;
            try { return JsonConvert.SerializeObject(o, new JsonSerializerSettings { DateFormatString = "yyyy-MM-ddTHH:mm:ss", NullValueHandling = NullValueHandling.Ignore }); }
            catch { return o.ToString(); }
        }

        private static string Recorte(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        private static SqlParameter Clonar(SqlParameter p) => new SqlParameter(p.ParameterName, p.Value);

        private static RegistroAuditoria Map(SqlDataReader r) => new RegistroAuditoria
        {
            Id            = Convert.ToInt64(r["Id"]),
            Fecha         = Convert.ToDateTime(r["Fecha"]),
            Tipo          = r["Tipo"].ToString(),
            Categoria     = r["Categoria"] == DBNull.Value ? null : r["Categoria"].ToString(),
            Accion        = r["Accion"].ToString(),
            Entidad       = r["Entidad"] == DBNull.Value ? null : r["Entidad"].ToString(),
            EntidadId     = r["EntidadId"] == DBNull.Value ? null : r["EntidadId"].ToString(),
            Descripcion   = r["Descripcion"] == DBNull.Value ? null : r["Descripcion"].ToString(),
            ValorAnterior = r["ValorAnterior"] == DBNull.Value ? null : r["ValorAnterior"].ToString(),
            ValorNuevo    = r["ValorNuevo"] == DBNull.Value ? null : r["ValorNuevo"].ToString(),
            IdUsuario     = r["IdUsuario"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["IdUsuario"]),
            NombreUsuario = r["NombreUsuario"] == DBNull.Value ? null : r["NombreUsuario"].ToString(),
            IdEmpresa     = r["IdEmpresa"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["IdEmpresa"]),
            NombreEmpresa = r["NombreEmpresa"] == DBNull.Value ? null : r["NombreEmpresa"].ToString(),
            IpAddress     = r["IpAddress"] == DBNull.Value ? null : r["IpAddress"].ToString(),
            UserAgent     = r["UserAgent"] == DBNull.Value ? null : r["UserAgent"].ToString()
        };
    }
}
