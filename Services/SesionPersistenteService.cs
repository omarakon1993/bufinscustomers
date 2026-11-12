using System;
using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using bufinscustomers.Helpers;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Respaldo en SQL de las sesiones autenticadas (<c>dbo.SesionesActivas</c>, script manual <c>Sql/045</c>) para que un reinicio
    /// del sitio no saque a los usuarios: la sesión vive en memoria y, si el proceso se reinicia, <see cref="UsuarioSesionHelper"/>
    /// la reconstruye desde aquí cargando de nuevo el usuario desde la base.
    ///
    /// Seguridad: el identificador de la cookie NUNCA se guarda (solo su SHA-256); se exige la ventana normal de inactividad;
    /// si la contraseña cambió desde el inicio de sesión (huella del hash) no se reconstruye; cerrar sesión borra la fila.
    /// Nunca lanza: sin la tabla o con la base caída simplemente no hay respaldo (comportamiento anterior).
    /// </summary>
    public class SesionPersistenteService : BaseService
    {
        public sealed class Registro
        {
            public int IdUsuario;
            public string HuellaClave;
            public DateTime FechaLogin;
            public DateTime UltimaActividad;
        }

        private const int SEGUNDOS_ENTRE_ACTUALIZACIONES = 60;
        private const int MINUTOS_ENTRE_PURGAS = 60;
        private const int MINUTOS_REVISAR_TABLA = 5;

        private static volatile bool _disponible = true;
        private static DateTime _proximaRevisionTabla = DateTime.MinValue;
        private static DateTime _ultimaPurga = DateTime.MinValue;
        private static readonly object _lock = new object();
        // Última vez que se actualizó cada fila (por huella), para escribir como mucho una vez por minuto.
        private static readonly ConcurrentDictionary<string, DateTime> _ultimaEscritura = new ConcurrentDictionary<string, DateTime>();

        public static string Huella(string valor)
        {
            if (string.IsNullOrEmpty(valor)) return null;
            using (var sha = SHA256.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(valor));
                var sb = new StringBuilder(64);
                foreach (var x in b) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool Disponible()
        {
            if (_disponible) return true;
            // Si faltaba la tabla (script no ejecutado) se vuelve a probar cada pocos minutos.
            if (DateTime.Now < _proximaRevisionTabla) return false;
            _disponible = true;
            return true;
        }

        private static void MarcarNoDisponible(Exception ex, string ctx)
        {
            bool tablaFalta = ex is SqlException se && se.Number == 208; // objeto inexistente
            if (tablaFalta)
            {
                if (_disponible) AppLogger.Warn("Sesiones persistentes desactivadas: falta dbo.SesionesActivas (Sql/045).", "SesionPersistente");
                _disponible = false;
                _proximaRevisionTabla = DateTime.Now.AddMinutes(MINUTOS_REVISAR_TABLA);
            }
            else
            {
                AppLogger.Warn("SesionPersistente." + ctx + ": " + ex.GetType().Name + " " + ex.Message, "SesionPersistente");
            }
        }

        /// <summary>Crea o actualiza la fila de una sesión recién autenticada.</summary>
        public void Registrar(string idSesion, int idUsuario, string hashClave, DateTime fechaLogin)
        {
            string h = Huella(idSesion);
            if (h == null || idUsuario <= 0 || !Disponible()) return;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.SesionesActivas
                       SET IdUsuario = @IdUsuario, HuellaClave = @HuellaClave, FechaLogin = @FechaLogin, UltimaActividad = @Ahora
                     WHERE HuellaSesion = @Huella;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.SesionesActivas (HuellaSesion, IdUsuario, HuellaClave, FechaLogin, UltimaActividad)
                        VALUES (@Huella, @IdUsuario, @HuellaClave, @FechaLogin, @Ahora);", cn))
                {
                    var ahora = DateTime.Now;
                    cmd.Parameters.Add("@Huella", SqlDbType.Char, 64).Value = h;
                    cmd.Parameters.Add("@IdUsuario", SqlDbType.Int).Value = idUsuario;
                    cmd.Parameters.Add("@HuellaClave", SqlDbType.Char, 64).Value = (object)Huella(hashClave) ?? DBNull.Value;
                    cmd.Parameters.Add("@FechaLogin", SqlDbType.DateTime).Value = fechaLogin;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime).Value = ahora;
                    cn.Open();
                    cmd.ExecuteNonQuery();
                    _ultimaEscritura[h] = ahora;
                }
            }
            catch (Exception ex) { MarcarNoDisponible(ex, "Registrar"); }
            PurgarSiToca();
        }

        /// <summary>Marca actividad (como mucho una escritura por minuto y sesión).</summary>
        public void Tocar(string idSesion)
        {
            string h = Huella(idSesion);
            if (h == null || !Disponible()) return;
            var ahora = DateTime.Now;
            if (_ultimaEscritura.TryGetValue(h, out var ultima) && (ahora - ultima).TotalSeconds < SEGUNDOS_ENTRE_ACTUALIZACIONES) return;
            _ultimaEscritura[h] = ahora;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("UPDATE dbo.SesionesActivas SET UltimaActividad = @Ahora WHERE HuellaSesion = @Huella", cn))
                {
                    cmd.Parameters.Add("@Huella", SqlDbType.Char, 64).Value = h;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime).Value = ahora;
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { MarcarNoDisponible(ex, "Tocar"); }
        }

        /// <summary>Borra la fila (cierre de sesión, sesión abandonada o expirada).</summary>
        public void Eliminar(string idSesion)
        {
            string h = Huella(idSesion);
            if (h == null) return;
            _ultimaEscritura.TryRemove(h, out _);
            if (!Disponible()) return;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM dbo.SesionesActivas WHERE HuellaSesion = @Huella", cn))
                {
                    cmd.Parameters.Add("@Huella", SqlDbType.Char, 64).Value = h;
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { MarcarNoDisponible(ex, "Eliminar"); }
        }

        /// <summary>Fila vigente (dentro de la ventana de inactividad) de la sesión, o null.</summary>
        public Registro ObtenerVigente(string idSesion, int minutosInactividad)
        {
            string h = Huella(idSesion);
            if (h == null || !Disponible()) return null;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    SELECT IdUsuario, HuellaClave, FechaLogin, UltimaActividad
                      FROM dbo.SesionesActivas
                     WHERE HuellaSesion = @Huella AND UltimaActividad >= @Limite", cn))
                {
                    cmd.Parameters.Add("@Huella", SqlDbType.Char, 64).Value = h;
                    cmd.Parameters.Add("@Limite", SqlDbType.DateTime).Value = DateTime.Now.AddMinutes(-minutosInactividad);
                    cn.Open();
                    using (var rd = cmd.ExecuteReader())
                    {
                        if (!rd.Read()) return null;
                        return new Registro
                        {
                            IdUsuario = rd.GetInt32(0),
                            HuellaClave = rd.IsDBNull(1) ? null : rd.GetString(1),
                            FechaLogin = rd.GetDateTime(2),
                            UltimaActividad = rd.GetDateTime(3)
                        };
                    }
                }
            }
            catch (Exception ex) { MarcarNoDisponible(ex, "Obtener"); return null; }
        }

        private void PurgarSiToca()
        {
            lock (_lock)
            {
                if ((DateTime.Now - _ultimaPurga).TotalMinutes < MINUTOS_ENTRE_PURGAS) return;
                _ultimaPurga = DateTime.Now;
            }
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("DELETE FROM dbo.SesionesActivas WHERE UltimaActividad < @Limite", cn))
                {
                    // Margen amplio sobre los 20 min de inactividad: lo vencido nunca se reconstruye de todas formas.
                    cmd.Parameters.Add("@Limite", SqlDbType.DateTime).Value = DateTime.Now.AddHours(-2);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                foreach (var kv in _ultimaEscritura)
                    if ((DateTime.Now - kv.Value).TotalHours > 2) _ultimaEscritura.TryRemove(kv.Key, out _);
            }
            catch (Exception ex) { MarcarNoDisponible(ex, "Purgar"); }
        }
    }
}
