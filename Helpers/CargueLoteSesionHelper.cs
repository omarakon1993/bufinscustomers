using bufinscustomers.Services;
using System;
using System.Collections.Generic;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Recuerda en la sesión los lotes de staging (dbo.CarguesLotes) que ESA sesión abrió y aún no
    /// confirmó ni descartó, para descartarlos cuando la sesión termina: logout
    /// (<see cref="UsuarioSesionHelper.LimpiarSesion"/>) o expiración por inactividad
    /// (Global.asax Session_End — sessionState InProc). Solo toca los lotes de esta sesión, así que no
    /// afecta otra pestaña/navegador del mismo usuario ni, por supuesto, a otras empresas.
    /// </summary>
    public static class CargueLoteSesionHelper
    {
        private const string SessionKey = "CarguesLotesAbiertos";

        public static void Registrar(long idLote)
        {
            var session = AppHttp.Current?.Session;
            if (session == null || idLote <= 0) return;
            var lista = session[SessionKey] as List<long> ?? new List<long>();
            if (!lista.Contains(idLote)) lista.Add(idLote);
            session[SessionKey] = lista;
        }

        public static void Quitar(long idLote)
        {
            var lista = AppHttp.Current?.Session?[SessionKey] as List<long>;
            lista?.Remove(idLote);
        }

        /// <summary>Descarta los lotes abiertos de la sesion actual (logout). Nunca lanza.</summary>
        public static void DescartarAbiertos(SesionCompat session)
        {
            DescartarLista(session?[SessionKey] as List<long>);
        }

        /// <summary>Descarta los lotes abiertos de una sesion que acaba de terminar (expiracion o abandono). Nunca lanza.</summary>
        public static void DescartarAbiertos(SesionDatos session)
        {
            object v = null;
            session?.Valores.TryGetValue(SessionKey, out v);
            DescartarLista(v as List<long>);
        }

        private static void DescartarLista(List<long> lista)
        {
            try
            {
                if (lista == null || lista.Count == 0) return;
                int n = new CargueStagingService().DescartarLotes(lista);
                lista.Clear();
                if (n > 0) AppLogger.Info($"[CargueLoteSesionHelper] Fin de sesión: {n} lote(s) de staging descartado(s).");
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "CargueLoteSesionHelper.DescartarAbiertos");
            }
        }
    }
}
