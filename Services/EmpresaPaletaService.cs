using bufinscustomers.Helpers;
using System;
using System.Collections.Generic;

namespace bufinscustomers.Services
{
    /// <summary>Paleta corporativa de una empresa (hasta 2 por empresa: Slot 1 y 2).</summary>
    public class EmpresaPaleta
    {
        public int IdEmpresa { get; set; }
        public byte Slot { get; set; }
        public string Nombre { get; set; }
        public string Primario { get; set; }
        public string Secundario { get; set; }
        public string Acento { get; set; }
    }

    /// <summary>
    /// Persistencia de los colores corporativos (<c>dbo.EmpresaPaletas</c>, <c>Sql/037_EmpresaPaletas.sql</c>, script manual).
    /// Si la tabla aún no existe (error 208) nada se rompe: no hay paletas y <see cref="Disponible"/> lo dice.
    /// </summary>
    public class EmpresaPaletaService : BaseService
    {
        private const int TablaInexistente = 208;

        public bool Disponible()
        {
            try
            {
                return Bd.Escalar<int>(CadenaConexion, "SELECT CASE WHEN OBJECT_ID('dbo.EmpresaPaletas','U') IS NULL THEN 0 ELSE 1 END") == 1;
            }
            catch (SqlException) { return false; }
        }

        /// <summary>Las paletas de la empresa (0, 1 o 2), ordenadas por Slot.</summary>
        public List<EmpresaPaleta> Obtener(int idEmpresa)
        {
            var l = new List<EmpresaPaleta>();
            try
            {
                Bd.Leer(CadenaConexion, @"SELECT IdEmpresa, Slot, Nombre, Primario, Secundario, Acento
                      FROM dbo.EmpresaPaletas WHERE IdEmpresa = @id ORDER BY Slot",
                    new { id = idEmpresa },
                    r => l.Add(new EmpresaPaleta
                        {
                            IdEmpresa = (int)r["IdEmpresa"],
                            Slot = (byte)r["Slot"],
                            Nombre = r["Nombre"] == DBNull.Value ? null : (string)r["Nombre"],
                            Primario = ((string)r["Primario"]).Trim(),
                            Secundario = ((string)r["Secundario"]).Trim(),
                            Acento = r["Acento"] == DBNull.Value ? null : ((string)r["Acento"]).Trim()
                        }));
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { /* sin script 037: sin paletas */ }
            return l;
        }

        public EmpresaPaleta Obtener(int idEmpresa, byte slot)
        {
            return Obtener(idEmpresa).Find(p => p.Slot == slot);
        }

        /// <summary>Crea o reemplaza la paleta del Slot (los colores ya llegan validados y normalizados a #RRGGBB).</summary>
        public void Guardar(EmpresaPaleta p, int? idUsuario)
        {
            Bd.Ejecutar(CadenaConexion, @"UPDATE dbo.EmpresaPaletas
                     SET Nombre = @n, Primario = @p, Secundario = @s, Acento = @a, FechaActualizacion = GETDATE(), IdUsuarioActualizo = @u
                   WHERE IdEmpresa = @id AND Slot = @slot;
                  IF @@ROWCOUNT = 0
                      INSERT INTO dbo.EmpresaPaletas (IdEmpresa, Slot, Nombre, Primario, Secundario, Acento, IdUsuarioActualizo)
                      VALUES (@id, @slot, @n, @p, @s, @a, @u);",
                new
                {
                    id = p.IdEmpresa,
                    slot = p.Slot,
                    n = string.IsNullOrWhiteSpace(p.Nombre) ? (object)DBNull.Value : p.Nombre.Trim(),
                    p = p.Primario,
                    s = p.Secundario,
                    a = string.IsNullOrWhiteSpace(p.Acento) ? (object)DBNull.Value : p.Acento,
                    u = idUsuario.HasValue ? (object)idUsuario.Value : DBNull.Value
                });
        }

        /// <summary>Quita la paleta del Slot. true si existía.</summary>
        public bool Eliminar(int idEmpresa, byte slot)
        {
            try
            {
                return Bd.Ejecutar(CadenaConexion, "DELETE FROM dbo.EmpresaPaletas WHERE IdEmpresa = @id AND Slot = @slot",
                    new { id = idEmpresa, slot = slot }) > 0;
            }
            catch (SqlException ex) when (ex.Number == TablaInexistente) { return false; }
        }
    }
}
