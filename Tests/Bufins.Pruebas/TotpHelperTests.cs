using System;
using System.Text;
using bufinscustomers.Helpers;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>Pruebas de caracterización de TOTP con los vectores oficiales del RFC 6238.</summary>
    public class TotpHelperTests
    {
        private static readonly byte[] SecretoRfc = Encoding.ASCII.GetBytes("12345678901234567890");

        [Fact]
        public void Codigo_CoincideConVectorRfc6238()
        {
            // RFC 6238, T = 59 s -> paso 1 -> 94287082 (8 dígitos); los 6 últimos son 287082.
            Assert.Equal("287082", TotpHelper.Codigo(SecretoRfc, 1));
        }

        [Fact]
        public void ABase32_CodificaElSecretoDelRfc()
        {
            Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", TotpHelper.ABase32(SecretoRfc));
        }

        [Fact]
        public void PasoActual_EsElSegundoUnixDivididoEn30()
        {
            var t = new DateTime(1970, 1, 1, 0, 1, 0, DateTimeKind.Utc); // 60 s
            Assert.Equal(2, TotpHelper.PasoActual(t));
        }

        [Fact]
        public void Validar_AceptaCodigoCorrectoYRechazaIncorrecto()
        {
            long paso = TotpHelper.PasoActual();
            string bueno = TotpHelper.Codigo(SecretoRfc, paso);
            Assert.True(TotpHelper.Validar(SecretoRfc, bueno) >= 0);
            string malo = bueno == "000000" ? "000001" : "000000";
            Assert.Equal(-1, TotpHelper.Validar(SecretoRfc, malo));
        }

        [Fact]
        public void Validar_NoReaceptaUnPasoYaUsado()
        {
            long paso = TotpHelper.PasoActual();
            string codigo = TotpHelper.Codigo(SecretoRfc, paso);
            Assert.Equal(-1, TotpHelper.Validar(SecretoRfc, codigo, paso + 5));
        }
    }
}
