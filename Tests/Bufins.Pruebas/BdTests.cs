using System;
using System.Collections.Generic;
using bufinscustomers.Helpers;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>Pruebas de la conversión de parámetros de Bd (no abren conexiones).</summary>
    public class BdTests
    {
        [Fact]
        public void Parametros_ObjetoAnonimo_AgregaArrobaYConservaValores()
        {
            var ps = Bd.Parametros(new { Id = 5, Nombre = "x" });
            Assert.Equal(2, ps.Count);
            Assert.Equal("@Id", ps[0].ParameterName);
            Assert.Equal(5, ps[0].Value);
            Assert.Equal("@Nombre", ps[1].ParameterName);
            Assert.Equal("x", ps[1].Value);
        }

        [Fact]
        public void Parametros_Null_SeConvierteEnDbNull()
        {
            var ps = Bd.Parametros(new { Valor = (string)null });
            Assert.Equal(DBNull.Value, ps[0].Value);
        }

        [Fact]
        public void Parametros_Diccionario_RespetaArrobaExistente()
        {
            var ps = Bd.Parametros(new Dictionary<string, object> { { "@A", 1 }, { "B", 2 } });
            Assert.Equal("@A", ps[0].ParameterName);
            Assert.Equal("@B", ps[1].ParameterName);
        }

        [Fact]
        public void Parametros_SinParametros_DevuelveListaVacia()
        {
            Assert.Empty(Bd.Parametros(null));
        }
    }
}
