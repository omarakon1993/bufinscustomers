using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using bufinscustomers.Helpers;

namespace bufinscustomers.Services
{
    public class SolicitudReinicioAuth
    {
        public int Id { get; set; }
        public int IdUsuario { get; set; }
        public DateTime Fecha { get; set; }
        public string Estado { get; set; }
        public string Mensaje { get; set; }
        public string Telefono { get; set; }
        public string IP { get; set; }
        public string UserAgent { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string NotaResolucion { get; set; }
        // Datos del usuario (JOIN)
        public string Usuario { get; set; }
        public string NombreCompleto { get; set; }
        public string Correo { get; set; }
        public string Empresa { get; set; }
        public string ResueltaPor { get; set; }
    }

    public class ResultadoCrearSolicitud
    {
        public bool Disponible { get; set; }   // false = falta el script 030
        public bool Creada { get; set; }       // false con Disponible=true => ya había una pendiente
    }

    /// <summary>
    /// Solicitudes de reinicio de métodos de autenticación (tabla <c>SolicitudesReinicioAuth</c>, Sql/030).
    /// Tolerante a que la tabla aún no exista (error SQL 208).
    /// </summary>
    public class SolicitudReinicioAuthService : BaseService
    {
        public const string Pendiente = "Pendiente";
        public const string Aprobada = "Aprobada";
        public const string Rechazada = "Rechazada";

        private static bool EsEsquemaFaltante(SqlException ex) { return ex.Number == 207 || ex.Number == 208; }

        /// <summary>Crea la solicitud salvo que el usuario ya tenga una pendiente (entonces no duplica).</summary>
        public ResultadoCrearSolicitud Crear(int idUsuario, string mensaje, string telefono, string ip, string userAgent)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    IF EXISTS (SELECT 1 FROM SolicitudesReinicioAuth WHERE IdUsuario = @U AND Estado = 'Pendiente')
                        SELECT 0;
                    ELSE
                    BEGIN
                        INSERT INTO SolicitudesReinicioAuth (IdUsuario, Mensaje, Telefono, IP, UserAgent)
                        VALUES (@U, @M, @T, @IP, @UA);
                        SELECT 1;
                    END", cn))
                {
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cmd.Parameters.AddWithValue("@M", string.IsNullOrWhiteSpace(mensaje) ? (object)DBNull.Value : mensaje);
                    cmd.Parameters.AddWithValue("@T", string.IsNullOrWhiteSpace(telefono) ? (object)DBNull.Value : telefono);
                    cmd.Parameters.AddWithValue("@IP", string.IsNullOrWhiteSpace(ip) ? (object)DBNull.Value : ip);
                    cmd.Parameters.AddWithValue("@UA", string.IsNullOrWhiteSpace(userAgent) ? (object)DBNull.Value : Corto(userAgent, 300));
                    cn.Open();
                    return new ResultadoCrearSolicitud { Disponible = true, Creada = Convert.ToInt32(cmd.ExecuteScalar()) == 1 };
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "SolicitudReinicioAuthService.Crear");
                return new ResultadoCrearSolicitud { Disponible = false };
            }
        }

        private const string SelectBase = @"
            SELECT TOP 300 s.Id, s.IdUsuario, s.Fecha, s.Estado, s.Mensaje, s.Telefono, s.IP, s.UserAgent,
                   s.FechaResolucion, s.NotaResolucion,
                   u.Usuario, u.Nombre, u.Apellidos, u.Correo, e.EmpNombre, r.Usuario AS ResueltaPor
              FROM SolicitudesReinicioAuth s
              JOIN Usuarios u ON u.Id = s.IdUsuario
              LEFT JOIN Empresas e ON e.EmpId = u.IdEmpresa
              LEFT JOIN Usuarios r ON r.Id = s.IdResuelve";

        /// <summary>Lista solicitudes; estado null/vacío = todas. Las pendientes primero.</summary>
        public List<SolicitudReinicioAuth> Listar(string estado)
        {
            var lista = new List<SolicitudReinicioAuth>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(SelectBase +
                    " WHERE (@E IS NULL OR s.Estado = @E) ORDER BY CASE WHEN s.Estado = 'Pendiente' THEN 0 ELSE 1 END, s.Fecha DESC", cn))
                {
                    cmd.Parameters.AddWithValue("@E", string.IsNullOrWhiteSpace(estado) ? (object)DBNull.Value : estado);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) lista.Add(Leer(r));
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "SolicitudReinicioAuthService.Listar");
            }
            return lista;
        }

        public SolicitudReinicioAuth Obtener(int id)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(SelectBase.Replace("TOP 300 ", "") + " WHERE s.Id = @Id", cn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        return r.Read() ? Leer(r) : null;
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "SolicitudReinicioAuthService.Obtener");
                return null;
            }
        }

        public int ContarPendientes()
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM SolicitudesReinicioAuth WHERE Estado = 'Pendiente'", cn))
                {
                    cn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (SqlException) { return 0; }
        }

        /// <summary>Marca la solicitud como Aprobada/Rechazada; solo si seguía Pendiente (evita doble resolución).</summary>
        public bool Resolver(int id, string estado, int idResuelve, string nota)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(
                    "UPDATE SolicitudesReinicioAuth SET Estado = @E, IdResuelve = @R, FechaResolucion = GETDATE(), NotaResolucion = @N " +
                    "WHERE Id = @Id AND Estado = 'Pendiente'", cn))
                {
                    cmd.Parameters.AddWithValue("@E", estado);
                    cmd.Parameters.AddWithValue("@R", idResuelve);
                    cmd.Parameters.AddWithValue("@N", string.IsNullOrWhiteSpace(nota) ? (object)DBNull.Value : Corto(nota, 500));
                    cmd.Parameters.AddWithValue("@Id", id);
                    cn.Open();
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
            catch (SqlException ex)
            {
                if (!EsEsquemaFaltante(ex)) AppLogger.Error(ex, "SolicitudReinicioAuthService.Resolver");
                return false;
            }
        }

        /// <summary>Super Admin (Id, Nombre, Correo) a quienes se avisa de una solicitud nueva.</summary>
        public List<KeyValuePair<int, string>> ObtenerSuperAdminsConCorreo()
        {
            var lista = new List<KeyValuePair<int, string>>();
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Id, Correo FROM Usuarios WHERE Admin = 2", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            lista.Add(new KeyValuePair<int, string>(Convert.ToInt32(r["Id"]), r["Correo"] == DBNull.Value ? null : r["Correo"].ToString()));
                }
            }
            catch (SqlException ex) { AppLogger.Error(ex, "SolicitudReinicioAuthService.ObtenerSuperAdminsConCorreo"); }
            return lista;
        }

        private static SolicitudReinicioAuth Leer(SqlDataReader r)
        {
            string nombre = ((r["Nombre"] == DBNull.Value ? "" : r["Nombre"].ToString()) + " " +
                             (r["Apellidos"] == DBNull.Value ? "" : r["Apellidos"].ToString())).Trim();
            return new SolicitudReinicioAuth
            {
                Id = Convert.ToInt32(r["Id"]),
                IdUsuario = Convert.ToInt32(r["IdUsuario"]),
                Fecha = Convert.ToDateTime(r["Fecha"]),
                Estado = r["Estado"].ToString(),
                Mensaje = r["Mensaje"] == DBNull.Value ? null : r["Mensaje"].ToString(),
                Telefono = r["Telefono"] == DBNull.Value ? null : r["Telefono"].ToString(),
                IP = r["IP"] == DBNull.Value ? null : r["IP"].ToString(),
                UserAgent = r["UserAgent"] == DBNull.Value ? null : r["UserAgent"].ToString(),
                FechaResolucion = r["FechaResolucion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FechaResolucion"]),
                NotaResolucion = r["NotaResolucion"] == DBNull.Value ? null : r["NotaResolucion"].ToString(),
                Usuario = r["Usuario"] == DBNull.Value ? "" : r["Usuario"].ToString(),
                NombreCompleto = nombre,
                Correo = r["Correo"] == DBNull.Value ? "" : r["Correo"].ToString(),
                Empresa = r["EmpNombre"] == DBNull.Value ? "" : r["EmpNombre"].ToString(),
                ResueltaPor = r["ResueltaPor"] == DBNull.Value ? "" : r["ResueltaPor"].ToString()
            };
        }

        private static string Corto(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
