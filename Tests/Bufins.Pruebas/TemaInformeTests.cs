using bufinscustomers.Helpers;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>Pruebas de validación y utilidades de color del tema de informes.</summary>
    public class TemaInformeTests
    {
        [Theory]
        [InlineData("1a2b3c", "#1A2B3C")]
        [InlineData("#abcdef", "#ABCDEF")]
        [InlineData("  #00ff00 ", "#00FF00")]
        public void TryHex_NormalizaAMayusculasConAlmohadilla(string entrada, string esperado)
        {
            Assert.True(TemaInforme.TryHex(entrada, out string hex));
            Assert.Equal(esperado, hex);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("#12345")]
        [InlineData("zzzzzz")]
        public void TryHex_RechazaValoresInvalidos(string entrada)
        {
            Assert.False(TemaInforme.TryHex(entrada, out _));
        }

        [Fact]
        public void Validar_RechazaPrincipalClaroPorContrasteConBlanco()
        {
            Assert.Equal("Tema_Err_Contraste", TemaInforme.Validar("#FFFF00", "#003366", null, out _));
        }

        [Fact]
        public void Validar_RechazaHexInvalido()
        {
            Assert.Equal("Tema_Err_Hex", TemaInforme.Validar("rojo", "#003366", null, out _));
        }

        [Fact]
        public void Contraste_NegroContraBlancoEs21()
        {
            Assert.Equal(21.0, TemaInforme.Contraste("#000000", "#FFFFFF"), 1);
        }

        [Fact]
        public void Rgb_Hex_SonInversos()
        {
            var rgb = TemaInforme.Rgb("#1A2B3C");
            Assert.Equal("#1A2B3C", TemaInforme.Hex(rgb[0], rgb[1], rgb[2]));
        }
    }
}
