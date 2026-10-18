using bufinscustomers.Helpers;
using bufinscustomers.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Mide qué le haría un cargue a los datos reales de su llave (empresa, año, modo, escenario) ANTES de
    /// confirmarlo: cuántas filas hay hoy en cada tabla Ini_ y cuántas trae el staging del lote. Es la base de
    /// la alerta de borrado grande y del detalle de auditoría. También lee los parámetros configurables
    /// (Configuraciones generales BUFINS → «Cargues de datos»).
    /// </summary>
    public class CargueImpactoService : BaseService
    {
        public const string ClaveUmbral = "Cargue_UmbralReemplazoPct";
        public const string ClaveArchivos = "Cargue_ArchivosConservados";
        public const int UmbralPorDefecto = 50;
        public const int ArchivosPorDefecto = 5;
        public const int ArchivosMaximo = 50;

        /// <summary>Porcentaje (1-100) bajo el cual una tabla se considera «reducción fuerte». Default 50.</summary>
        public static int LeerUmbralPct() => LeerEntero(ClaveUmbral, UmbralPorDefecto, 1, 100);

        /// <summary>Cuántos archivos de cargue confirmados se conservan por empresa (0 = ninguno). Default 5.</summary>
        public static int LeerArchivosConservados() => LeerEntero(ClaveArchivos, ArchivosPorDefecto, 0, ArchivosMaximo);

        public const string ClaveBloqueo = "Cargue_UmbralBloqueoPct";
        public const int BloqueoPorDefecto = 10;

        /// <summary>
        /// Porcentaje (1-50) bajo el cual una tabla queda «no permitida» (default 10). Nunca es mayor que el umbral de
        /// confirmación reforzada, para que los dos niveles tengan sentido.
        /// </summary>
        public static int LeerUmbralBloqueoPct() => Math.Min(LeerEntero(ClaveBloqueo, BloqueoPorDefecto, 1, 50), LeerUmbralPct());

        /// <summary>
        /// Evalúa un reemplazo (cargue, deshacer una versión o recuperar un respaldo) comparando, por tabla, las filas que hay hoy
        /// con las que quedarían. Es la ÚNICA regla de «nunca dejar un año vacío / reducido en extremo» de toda la aplicación.
        /// </summary>
        public static ImpactoCargue Evaluar(IEnumerable<(string Tabla, int Existentes, int Nuevas)> tablas)
        {
            var impacto = new ImpactoCargue { UmbralPct = LeerUmbralPct(), UmbralBloqueoPct = LeerUmbralBloqueoPct() };
            foreach (var t in tablas)
                if (t.Existentes > 0 || t.Nuevas > 0)
                    impacto.Tablas.Add(new ImpactoTabla { Tabla = t.Tabla, Existentes = t.Existentes, Nuevas = t.Nuevas });
            return impacto;
        }

        public const string ClaveRespaldos = "Cargue_RespaldosRestauracion";
        public const int RespaldosPorDefecto = 3;

        /// <summary>Respaldos automáticos previos a una restauración que se conservan por llave de cargue (1-10, default 3).</summary>
        public static int LeerRespaldosRestauracion() => LeerEntero(ClaveRespaldos, RespaldosPorDefecto, 1, 10);

        private static int LeerEntero(string clave, int porDefecto, int min, int max)
        {
            try
            {
                string v = new ConfiguracionSistemaService().ObtenerValor(clave);
                if (int.TryParse(v?.Trim(), out int n)) return Math.Max(min, Math.Min(max, n));
            }
            catch (Exception ex) { AppLogger.Warn($"CargueImpactoService: no se pudo leer '{clave}': {ex.Message}"); }
            return porDefecto;
        }

        private static string SiExiste(string tabla, string sentencia, string siNo) =>
            $"IF OBJECT_ID(N'dbo.{tabla}', N'U') IS NOT NULL {sentencia} ELSE {siNo}";

        /// <summary>
        /// Filas que hoy hay en cada tabla Ini_ de la empresa y que el cargue reemplazaría. Usa EXACTAMENTE el mismo
        /// criterio que los borrados de DatosController (EliminarEjecucionDeIni / EliminarAnosHistoricosDeIni).
        /// Si se pasa tx, cuenta dentro de la transacción del llamador (para registrar lo que de verdad se borra).
        /// </summary>
        public List<(string Tabla, int Filas)> ContarExistentes(SqlConnection conn, SqlTransaction tx,
            int idEmpresa, int anio, byte modo, int idEscenario)
        {
            var res = new List<(string, int)>();
            string where = "IdEmpresa_Log = @IdEmpresa AND Año = @Anio" +
                           (modo == 0 ? " AND Historico_Log = 0" : "") +
                           " AND ISNULL(IdEscenario,1) = @IdEscenario";

            foreach (var tabla in TablasCargueHelper.TablasIniParaEmpresa(idEmpresa))
            {
                using (var cmd = new SqlCommand(SiExiste(tabla, $"SELECT COUNT(*) FROM dbo.[{tabla}] WHERE {where}", "SELECT 0"), conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IdEmpresa", idEmpresa);
                    cmd.Parameters.AddWithValue("@Anio", anio);
                    cmd.Parameters.AddWithValue("@IdEscenario", idEscenario);
                    res.Add((tabla, Convert.ToInt32(cmd.ExecuteScalar())));
                }
            }
            return res;
        }

        /// <summary>Compara lo que hay hoy contra lo que trae el staging del lote, tabla por tabla.</summary>
        public ImpactoCargue Analizar(CargueLote lote)
        {
            var impacto = new ImpactoCargue { UmbralPct = LeerUmbralPct(), UmbralBloqueoPct = LeerUmbralBloqueoPct() };

            using (var conn = new SqlConnection(CadenaConexion))
            {
                conn.Open();
                var existentes = ContarExistentes(conn, null, lote.IdEmpresa, lote.Anio, lote.Modo, lote.IdEscenario);

                foreach (var (tabla, filas) in existentes)
                {
                    string staging = TablasCargueHelper.NombreStaging(tabla);
                    int nuevas;
                    using (var cmd = new SqlCommand(SiExiste(staging, $"SELECT COUNT(*) FROM dbo.[{staging}] WHERE IdLote = @IdLote", "SELECT 0"), conn))
                    {
                        cmd.Parameters.AddWithValue("@IdLote", lote.IdLote);
                        nuevas = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    // Tablas sin datos ni antes ni después no aportan nada a la vista.
                    if (filas > 0 || nuevas > 0)
                        impacto.Tablas.Add(new ImpactoTabla { Tabla = tabla, Existentes = filas, Nuevas = nuevas });
                }
            }
            return impacto;
        }
    }
}
