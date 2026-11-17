using System;
using System.Collections.Generic;
using bufinscustomers.Helpers;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>
    /// El mensaje que llega al navegador nunca debe traer el texto de una falla técnica (SQL, nulos, red…),
    /// pero sí conserva las reglas de negocio que la aplicación escribió a propósito.
    /// </summary>
    public class MensajeErrorHelperTests
    {
        private static readonly Func<string, string> Traduce = clave => "Error interno (ref {0})";

        [Theory]
        [MemberData(nameof(ErroresTecnicos))]
        public void FallaTecnica_NoFiltraSuTextoYDevuelveCodigoDeReferencia(Exception ex)
        {
            string msg = MensajeErrorHelper.Crear(ex, "Prueba", Traduce);
            Assert.DoesNotContain("SECRETO", msg);
            Assert.StartsWith("Error interno (ref ", msg);
            Assert.True(MensajeErrorHelper.EsTecnica(ex));
        }

        public static IEnumerable<object[]> ErroresTecnicos()
        {
            yield return new object[] { new NullReferenceException("SECRETO tabla dbo.Usuarios") };
            yield return new object[] { new System.IO.IOException("SECRETO ruta interna del servidor") };
            yield return new object[] { new TimeoutException("SECRETO tiempo") };
            // una excepción envolvente cuyo origen es técnico también cuenta como técnica
            yield return new object[] { new Exception("Error al consultar datos", new KeyNotFoundException("SECRETO clave")) };
        }

        [Fact]
        public void ReglaDeNegocio_ConservaSuMensaje()
        {
            var ex = new InvalidOperationException("El correo ya existe");
            Assert.Equal("El correo ya existe", MensajeErrorHelper.Crear(ex, "Prueba", Traduce));
            Assert.False(MensajeErrorHelper.EsTecnica(ex));
        }

        [Fact]
        public void CadaFallaTecnicaRecibeUnCodigoDistinto()
        {
            var ex = new NullReferenceException();
            Assert.NotEqual(MensajeErrorHelper.Crear(ex, "A", Traduce), MensajeErrorHelper.Crear(ex, "A", Traduce));
        }
    }
}
