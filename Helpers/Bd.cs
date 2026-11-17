using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Acceso a SQL Server sin repetir el patrón «abrir conexión → comando → parámetros → leer».
    /// Cada llamada abre y cierra su propia conexión (el pool de ADO.NET la reutiliza), igual que el código manual.
    /// Los parámetros se pasan como objeto anónimo: <c>new { Id = 5, Nombre = "x" }</c> → @Id, @Nombre (null → DBNull).
    /// No cubre parámetros OUTPUT, transacciones ni lectura de varios result sets: esos casos se siguen escribiendo a mano.
    /// </summary>
    public static class Bd
    {
        /// <summary>Ejecuta INSERT/UPDATE/DELETE (o un SP) y devuelve las filas afectadas.</summary>
        public static int Ejecutar(string cadena, string texto, object parametros = null, bool esSp = false, int? timeoutSeg = null)
        {
            using (var cn = new SqlConnection(cadena))
            using (var cmd = Crear(cn, texto, parametros, esSp, timeoutSeg))
            {
                cn.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Devuelve la primera columna de la primera fila convertida a T (default si no hay fila o es NULL).</summary>
        public static T Escalar<T>(string cadena, string texto, object parametros = null, bool esSp = false, int? timeoutSeg = null)
        {
            using (var cn = new SqlConnection(cadena))
            using (var cmd = Crear(cn, texto, parametros, esSp, timeoutSeg))
            {
                cn.Open();
                return Convertir<T>(cmd.ExecuteScalar());
            }
        }

        /// <summary>Lee todas las filas y las convierte con <paramref name="mapa"/> (que recibe el lector posicionado en la fila).</summary>
        public static List<T> Lista<T>(string cadena, string texto, object parametros, Func<SqlDataReader, T> mapa, bool esSp = false, int? timeoutSeg = null)
        {
            var lista = new List<T>();
            Leer(cadena, texto, parametros, r => lista.Add(mapa(r)), esSp, timeoutSeg);
            return lista;
        }

        /// <summary>Versión sin parámetros de <see cref="Lista{T}(string, string, object, Func{SqlDataReader, T}, bool, int?)"/>.</summary>
        public static List<T> Lista<T>(string cadena, string texto, Func<SqlDataReader, T> mapa) => Lista(cadena, texto, null, mapa);

        /// <summary>Como <c>Lista</c> pero solo la primera fila (default si no hay ninguna).</summary>
        public static T Primero<T>(string cadena, string texto, object parametros, Func<SqlDataReader, T> mapa, bool esSp = false, int? timeoutSeg = null)
        {
            using (var cn = new SqlConnection(cadena))
            using (var cmd = Crear(cn, texto, parametros, esSp, timeoutSeg))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? mapa(r) : default(T);
            }
        }

        /// <summary>Recorre las filas y ejecuta <paramref name="porFila"/> en cada una (para llenar listas/objetos propios).</summary>
        public static void Leer(string cadena, string texto, object parametros, Action<SqlDataReader> porFila, bool esSp = false, int? timeoutSeg = null)
        {
            using (var cn = new SqlConnection(cadena))
            using (var cmd = Crear(cn, texto, parametros, esSp, timeoutSeg))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) porFila(r);
            }
        }

        /// <summary>Si hay al menos una fila, ejecuta <paramref name="porFila"/> con la primera (para lecturas de un solo registro).</summary>
        public static void LeerPrimera(string cadena, string texto, object parametros, Action<SqlDataReader> porFila, bool esSp = false, int? timeoutSeg = null)
        {
            using (var cn = new SqlConnection(cadena))
            using (var cmd = Crear(cn, texto, parametros, esSp, timeoutSeg))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) porFila(r);
            }
        }

        /// <summary>Versión sin parámetros de <see cref="LeerPrimera(string, string, object, Action{SqlDataReader}, bool, int?)"/>.</summary>
        public static void LeerPrimera(string cadena, string texto, Action<SqlDataReader> porFila) => LeerPrimera(cadena, texto, null, porFila);

        /// <summary>Versión sin parámetros de <see cref="Leer(string, string, object, Action{SqlDataReader}, bool, int?)"/>.</summary>
        public static void Leer(string cadena, string texto, Action<SqlDataReader> porFila) => Leer(cadena, texto, null, porFila);

        // Crea el comando con su tipo (texto o SP), timeout opcional y parámetros ya convertidos.
        private static SqlCommand Crear(SqlConnection cn, string texto, object parametros, bool esSp, int? timeoutSeg)
        {
            var cmd = new SqlCommand(texto, cn);
            if (esSp) cmd.CommandType = CommandType.StoredProcedure;
            if (timeoutSeg.HasValue) cmd.CommandTimeout = timeoutSeg.Value;
            foreach (var p in Parametros(parametros)) cmd.Parameters.Add(p);
            return cmd;
        }

        /// <summary>
        /// Convierte un objeto anónimo (o un IDictionary) en parámetros SQL: el nombre recibe '@' si no lo trae y null pasa a DBNull.
        /// Público para poder probarlo sin base de datos.
        /// </summary>
        public static List<SqlParameter> Parametros(object parametros)
        {
            var res = new List<SqlParameter>();
            if (parametros == null) return res;

            if (parametros is IDictionary dic)
            {
                foreach (DictionaryEntry e in dic) res.Add(Nuevo(Convert.ToString(e.Key), e.Value));
                return res;
            }
            foreach (var prop in parametros.GetType().GetProperties())
                res.Add(Nuevo(prop.Name, prop.GetValue(parametros)));
            return res;
        }

        private static SqlParameter Nuevo(string nombre, object valor)
        {
            return new SqlParameter(nombre.StartsWith("@") ? nombre : "@" + nombre, valor ?? DBNull.Value);
        }

        private static T Convertir<T>(object valor)
        {
            if (valor == null || valor == DBNull.Value) return default(T);
            if (valor is T ya) return ya;
            var destino = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(valor, destino);
        }
    }
}
