using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using bufinscustomers.Helpers;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Qué documentos de la biblioteca «Documentación BUFINS» son SOLO para Super Admin (<c>dbo.DocumentacionAcceso</c>, script Sql/044).
    /// Cada documento tiene un valor por defecto en el catálogo (<see cref="DocumentoBiblioteca.SoloSuperAdminPorDefecto"/>); una fila de la
    /// tabla lo reemplaza. Si la tabla aún no existe se usan solo los valores por defecto y <see cref="Guardar"/> avisa. Nunca lanza al leer.
    /// </summary>
    public class DocumentacionAccesoService : BaseService
    {
        private const string CACHE_KEY = "DocumentacionAcceso";

        /// <summary>Valores guardados (clave → solo Super Admin) y si la tabla existe. Cache de 5 min; se invalida al guardar.</summary>
        private static Dictionary<string, bool> Guardados(out bool tabla)
        {
            var cache = MemoryCache.Default;
            if (cache[CACHE_KEY] is Tuple<bool, Dictionary<string, bool>> t) { tabla = t.Item1; return t.Item2; }
            var d = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool existe = true;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand("SELECT Clave, SoloSuperAdmin FROM dbo.DocumentacionAcceso", cn))
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) d[r.GetString(0)] = r.GetBoolean(1);
                }
            }
            catch (SqlException ex)
            {
                existe = false;   // 208 = el objeto no existe (script 044 sin ejecutar)
                if (ex.Number != 208) AppLogger.Error(ex, "DocumentacionAccesoService.Guardados");
            }
            cache.Set(CACHE_KEY, Tuple.Create(existe, d), DateTimeOffset.UtcNow.AddMinutes(5));
            tabla = existe;
            return d;
        }

        public static bool TablaDisponible { get { bool t; Guardados(out t); return t; } }

        /// <summary>true si el documento de la biblioteca es solo para Super Admin (valor guardado o, si no hay, el del catálogo).</summary>
        public static bool EsSoloSuperAdmin(string claveBiblioteca)
        {
            if (string.IsNullOrEmpty(claveBiblioteca)) return false;
            bool t;
            var g = Guardados(out t);
            bool v;
            if (g.TryGetValue(claveBiblioteca, out v)) return v;
            var doc = DocumentacionBibliotecaCatalogo.Buscar(claveBiblioteca);
            return doc != null && doc.SoloSuperAdminPorDefecto;
        }

        /// <summary>Claves restringidas hoy (para pintar la biblioteca).</summary>
        public static HashSet<string> Restringidos()
        {
            return new HashSet<string>(DocumentacionBibliotecaCatalogo.Todos.Where(d => EsSoloSuperAdmin(d.Clave)).Select(d => d.Clave), StringComparer.Ordinal);
        }

        /// <summary>El usuario puede ver el documento de la biblioteca (Super Admin siempre; el resto solo si no está restringido).</summary>
        public static bool PuedeVer(string claveBiblioteca, Usuarios usuario)
        {
            if (usuario != null && usuario.Admin == 2) return true;
            return !EsSoloSuperAdmin(claveBiblioteca);
        }

        /// <summary>Guarda el valor de un documento. Devuelve false y la clave del mensaje de error (para que el controlador lo traduzca).</summary>
        public bool Guardar(string claveBiblioteca, bool soloSuperAdmin, int idUsuario, out string errorKey)
        {
            errorKey = null;
            if (DocumentacionBibliotecaCatalogo.Buscar(claveBiblioteca) == null) { errorKey = "DocBuf_AccesoDocInvalido"; return false; }
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                using (var cmd = new SqlCommand(@"
                    UPDATE dbo.DocumentacionAcceso SET SoloSuperAdmin = @S, FechaActualizacion = GETDATE(), IdUsuario = @U WHERE Clave = @C;
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.DocumentacionAcceso (Clave, SoloSuperAdmin, IdUsuario) VALUES (@C, @S, @U);", cn))
                {
                    cmd.Parameters.AddWithValue("@C", claveBiblioteca);
                    cmd.Parameters.AddWithValue("@S", soloSuperAdmin);
                    cmd.Parameters.AddWithValue("@U", idUsuario);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
                MemoryCache.Default.Remove(CACHE_KEY);
                return true;
            }
            catch (SqlException ex)
            {
                if (ex.Number == 208) { errorKey = "DocBuf_AccesoSinTabla"; return false; }
                AppLogger.Error(ex, "DocumentacionAccesoService.Guardar");
                errorKey = "Err_InternoRef";
                return false;
            }
        }
    }
}
