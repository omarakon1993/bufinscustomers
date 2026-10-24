using System;
using System.Collections.Generic;
using System.Linq;
using bufinscustomers.Helpers;
using bufinscustomers.Models;

namespace bufinscustomers.Services
{
    /// <summary>
    /// Resuelve qué <see cref="TemaInforme"/> aplicar a un informe a partir de lo que manda el navegador. NUNCA confía en los
    /// colores recibidos sin validarlos y NUNCA falla: cualquier valor inválido, ajeno o inexistente cae al tema Bufins.
    /// Identificadores: <c>bufins</c> | <c>preset:&lt;clave&gt;</c> | <c>corp:&lt;idEmpresa&gt;:&lt;1|2&gt;</c> | <c>custom</c>
    /// (este último con los tres colores).
    /// </summary>
    public class TemaInformeService : BaseService
    {
        public const string IdPersonalizada = "custom";

        public TemaInforme Resolver(Usuarios usuario, string tema, string principal, string secundario, string acento)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tema) || tema == TemaInforme.IdBufins) return TemaInforme.Bufins;

                if (tema.StartsWith("preset:", StringComparison.Ordinal))
                    return TemaInforme.PresetPorClave(tema.Substring(7)) ?? TemaInforme.Bufins;

                if (tema == IdPersonalizada)
                {
                    string aviso;
                    if (TemaInforme.Validar(principal, secundario, acento, out aviso) != null) return TemaInforme.Bufins;
                    string p, s, a;
                    TemaInforme.TryHex(principal, out p); TemaInforme.TryHex(secundario, out s); TemaInforme.TryHex(acento, out a);
                    return TemaInforme.Derivar(IdPersonalizada, "Tema_Personalizada", p, s, a);
                }

                if (tema.StartsWith("corp:", StringComparison.Ordinal))
                {
                    var partes = tema.Split(':');
                    int idEmpresa; byte slot;
                    if (partes.Length != 3 || !int.TryParse(partes[1], out idEmpresa) || !byte.TryParse(partes[2], out slot)) return TemaInforme.Bufins;
                    if (!EmpresaAccesoHelper.TieneAcceso(usuario, idEmpresa)) return TemaInforme.Bufins;   // paleta de una empresa ajena
                    var pal = new EmpresaPaletaService().Obtener(idEmpresa, slot);
                    return pal == null ? TemaInforme.Bufins : DesdePaleta(pal);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "TemaInformeService.Resolver");
            }
            return TemaInforme.Bufins;
        }

        /// <summary>Convierte una paleta corporativa guardada en tema (si los colores guardados no fueran válidos, Bufins).</summary>
        public TemaInforme DesdePaleta(EmpresaPaleta pal)
        {
            string aviso;
            if (TemaInforme.Validar(pal.Primario, pal.Secundario, pal.Acento, out aviso) != null) return TemaInforme.Bufins;
            return TemaInforme.Derivar("corp:" + pal.IdEmpresa + ":" + pal.Slot, NombreCorporativa(pal), pal.Primario, pal.Secundario, pal.Acento);
        }

        /// <summary>Nombre visible: «{Empresa} 1/2». El nombre NO es personalizable (la columna Nombre queda reservada para usos futuros).</summary>
        public static string NombreCorporativa(EmpresaPaleta pal)
        {
            return NombreCorporativa(pal.IdEmpresa, pal.Slot);
        }

        public static string NombreCorporativa(int idEmpresa, byte slot)
        {
            var emp = EmpresaCacheHelper.ObtenerEmpresasCacheadas().FirstOrDefault(e => e.Id == idEmpresa);
            return (emp != null ? emp.Nombre : "Empresa") + " " + slot;
        }

        /// <summary>Nombre para mostrar: los presets/personalizada guardan una CLAVE de recurso (<c>Tema_*</c>); las corporativas, el texto.</summary>
        public static string NombreVisible(TemaInforme t, Func<string, string> R)
        {
            return t.Nombre != null && t.Nombre.StartsWith("Tema_", StringComparison.Ordinal) ? R(t.Nombre) : t.Nombre;
        }
    }
}
