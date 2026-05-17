/*
 * Tabla requerida en BD (ejecutar una vez):
 *
 *   CREATE TABLE Notificaciones (
 *       Id            INT           IDENTITY(1,1) PRIMARY KEY,
 *       IdUsuario     INT           NOT NULL,
 *       Titulo        NVARCHAR(100) NOT NULL,
 *       Mensaje       NVARCHAR(500) NULL,
 *       Tipo          NVARCHAR(20)  NOT NULL CONSTRAINT DF_Notif_Tipo DEFAULT 'info',
 *       Leida         BIT           NOT NULL CONSTRAINT DF_Notif_Leida DEFAULT 0,
 *       FechaCreacion DATETIME      NOT NULL CONSTRAINT DF_Notif_Fecha DEFAULT GETDATE()
 *   );
 *   CREATE INDEX IX_Notificaciones_Usuario
 *       ON Notificaciones (IdUsuario, Leida, FechaCreacion DESC);
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    public class NotificacionesService : BaseService
    {
        public void Crear(int idUsuario, string titulo, string mensaje = null, string tipo = "info")
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        INSERT INTO Notificaciones (IdUsuario, Titulo, Mensaje, Tipo)
                        VALUES (@IdUsuario, @Titulo, @Mensaje, @Tipo);
                        DELETE FROM Notificaciones
                        WHERE IdUsuario = @IdUsuario
                          AND FechaCreacion < DATEADD(DAY, -15, GETDATE())", cn);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cmd.Parameters.AddWithValue("@Titulo",    titulo);
                    cmd.Parameters.AddWithValue("@Mensaje",   (object)mensaje ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Tipo",      tipo);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch { /* las notificaciones no deben romper el flujo principal */ }
        }

        public (List<Notificacion> Items, int NoLeidas) ObtenerRecientes(int idUsuario)
        {
            var list = new List<Notificacion>();
            int noLeidas = 0;
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(@"
                        SELECT TOP 20 Id, Titulo, Mensaje, Tipo, Leida, FechaCreacion
                        FROM Notificaciones
                        WHERE IdUsuario = @IdUsuario
                        ORDER BY FechaCreacion DESC;

                        SELECT COUNT(*)
                        FROM Notificaciones
                        WHERE IdUsuario = @IdUsuario AND Leida = 0", cn);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) list.Add(Map(r));
                        if (r.NextResult() && r.Read()) noLeidas = r.GetInt32(0);
                    }
                }
            }
            catch { }
            return (list, noLeidas);
        }

        public void MarcarLeida(int id, int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "UPDATE Notificaciones SET Leida=1 WHERE Id=@Id AND IdUsuario=@IdUsuario", cn);
                    cmd.Parameters.AddWithValue("@Id",        id);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public void MarcarTodasLeidas(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "UPDATE Notificaciones SET Leida=1 WHERE IdUsuario=@IdUsuario AND Leida=0", cn);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public void LimpiarTodas(int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "DELETE FROM Notificaciones WHERE IdUsuario = @IdUsuario", cn);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public void Eliminar(int id, int idUsuario)
        {
            try
            {
                using (var cn = new SqlConnection(CadenaConexion))
                {
                    var cmd = new SqlCommand(
                        "DELETE FROM Notificaciones WHERE Id=@Id AND IdUsuario=@IdUsuario", cn);
                    cmd.Parameters.AddWithValue("@Id",        id);
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static Notificacion Map(SqlDataReader r) => new Notificacion
        {
            Id            = Convert.ToInt32(r["Id"]),
            Titulo        = r["Titulo"].ToString(),
            Mensaje       = r["Mensaje"] == DBNull.Value ? null : r["Mensaje"].ToString(),
            Tipo          = r["Tipo"].ToString(),
            Leida         = Convert.ToBoolean(r["Leida"]),
            FechaCreacion = Convert.ToDateTime(r["FechaCreacion"])
        };
    }
}
