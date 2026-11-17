using bufinscustomers.Helpers;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>Pruebas de la política de contraseñas (sin consultar la red: comprobarFiltracion = false).</summary>
    public class PoliticaContrasenaTests
    {
        [Fact]
        public void ClaveFuerte_Cumple()
        {
            Assert.True(PoliticaContrasena.Validar("Zq7!kLm9#Xw2pR", "omar", out string error, false));
            Assert.Null(error);
        }

        [Theory]
        [InlineData("Ab1!", "Pwd_ErrorLongitud")]
        [InlineData("ZQ7!KLM9#XW2PR", "Pwd_ErrorMinuscula")]
        [InlineData("zq7!klm9#xw2pr", "Pwd_ErrorMayuscula")]
        [InlineData("Zq!!kLm!#XwpPR", "Pwd_ErrorNumero")]
        [InlineData("Zq7akLm9bXw2pR", "Pwd_ErrorEspecial")]
        public void ClaveDebil_DevuelveLaClaveDeError(string clave, string esperado)
        {
            Assert.False(PoliticaContrasena.Validar(clave, "omar", out string error, false));
            Assert.Equal(esperado, error);
        }

        [Fact]
        public void ClaveConElUsuario_SeRechaza()
        {
            Assert.False(PoliticaContrasena.Validar("Zq7!omarLm9#Xw", "omar", out string error, false));
            Assert.Equal("Pwd_ErrorContieneUsuario", error);
        }
    }
}
