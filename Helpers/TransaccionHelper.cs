using System;

namespace bufinscustomers.Helpers
{
    /// <summary>Utilidades para transacciones SQL que el resto de los servicios repiten.</summary>
    public static class TransaccionHelper
    {
        /// <summary>
        /// Deshace la transacción sin lanzar nunca: si el error original ya la dejó condenada o cerrada,
        /// el Rollback fallaría y taparía la excepción real, que ya se reporta en el catch del llamador.
        /// </summary>
        public static void RollbackSeguro(SqlTransaction tx)
        {
            try { tx?.Rollback(); }
            catch (Exception) { /* transacción ya cerrada o condenada: nada más que deshacer */ }
        }
    }
}
