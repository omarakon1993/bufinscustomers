using System;
using bufinscustomers.Helpers;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>
    /// Pruebas de caracterización de las conversiones del cargue de Excel (se extrajeron tal cual del orquestador):
    /// fijan el comportamiento actual para que ningún cambio futuro altere cómo se leen las cifras de los clientes.
    /// </summary>
    public class CargueConversionTests
    {
        // ── SanitizarValorNumerico: heurística colombiana (coma = decimal, punto = miles) ──
        [Theory]
        [InlineData(null, "0")]
        [InlineData("", "0")]
        [InlineData("   ", "0")]
        [InlineData("-", "0")]
        [InlineData("—", "0")]
        [InlineData("abc", "0")]
        [InlineData("$ 1.234,56", "1234.56")]      // colombiano: punto miles, coma decimal
        [InlineData("15,549.00", "15549.00")]      // americano: coma miles, punto decimal
        [InlineData("86,751", "86.751")]           // una coma sola es siempre decimal
        [InlineData("15549,00", "15549.00")]
        [InlineData("1.554.900", "1554900")]       // varios puntos = miles
        [InlineData("15.549", "15549")]            // 1-2 dígitos + 3 → miles
        [InlineData("150.534", "150.534")]         // 3 dígitos antes → decimal
        [InlineData("15549.00", "15549.00")]
        [InlineData("-15.549", "-15549")]
        [InlineData("0.500", "0.500")]             // "0" antes del punto → decimal
        public void SanitizarValorNumerico_AplicaLaHeuristicaColombiana(string entrada, string esperado)
        {
            Assert.Equal(esperado, CargueConversion.SanitizarValorNumerico(entrada));
        }

        // ── ExtraerDecimal ──
        [Fact]
        public void ExtraerDecimal_NuloEsCero()
        {
            Assert.Equal(0m, CargueConversion.ExtraerDecimal(null));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void ExtraerDecimal_DoubleInvalidoEsCero(double valor)
        {
            Assert.Equal(0m, CargueConversion.ExtraerDecimal(valor));
        }

        [Fact]
        public void ExtraerDecimal_ConservaLosTiposNumericos()
        {
            Assert.Equal(12.5m, CargueConversion.ExtraerDecimal(12.5));
            Assert.Equal(7m, CargueConversion.ExtraerDecimal(7));
            Assert.Equal(9000000000L, CargueConversion.ExtraerDecimal(9000000000L));
            Assert.Equal(3.25m, CargueConversion.ExtraerDecimal(3.25m));
        }

        [Theory]
        [InlineData("1.234,56", 1234.56)]
        [InlineData("$ 2.000", 2000)]
        [InlineData("abc", 0)]
        [InlineData("", 0)]
        public void ExtraerDecimal_TextoPasaPorLaHeuristica(string texto, double esperado)
        {
            Assert.Equal((decimal)esperado, CargueConversion.ExtraerDecimal(texto));
        }

        // ── DetectarErrorNumerico ──
        [Theory]
        [InlineData("1,489E-05", "notación científica")]
        [InlineData("1E5", "notación científica")]
        [InlineData("12abc", "contiene texto")]
        [InlineData("1,2,3", "más de un separador decimal")]
        public void DetectarErrorNumerico_ReportaElMotivo(string valor, string fragmento)
        {
            var error = CargueConversion.DetectarErrorNumerico(valor);
            Assert.NotNull(error);
            Assert.Contains(fragmento, error);
        }

        [Theory]
        [InlineData("123,45")]
        [InlineData("1.234.567")]
        [InlineData("-50")]
        public void DetectarErrorNumerico_ValoresValidosNoTienenError(string valor)
        {
            Assert.Null(CargueConversion.DetectarErrorNumerico(valor));
        }

        // ── ConvertirCeldaBit ──
        [Theory]
        [InlineData("Sí", true)]
        [InlineData("si", true)]
        [InlineData("X", true)]
        [InlineData("verdadero", true)]
        [InlineData("1", true)]
        [InlineData("No", false)]
        [InlineData("falso", false)]
        [InlineData("0", false)]
        public void ConvertirCeldaBit_ReconoceTextos(string texto, bool esperado)
        {
            Assert.Equal(esperado, CargueConversion.ConvertirCeldaBit(texto));
        }

        [Fact]
        public void ConvertirCeldaBit_VacioEsDbNullYDesconocidoEsNull()
        {
            Assert.Equal(DBNull.Value, CargueConversion.ConvertirCeldaBit(null));
            Assert.Equal(DBNull.Value, CargueConversion.ConvertirCeldaBit("  "));
            Assert.Null(CargueConversion.ConvertirCeldaBit("quizas"));
            Assert.Equal(true, CargueConversion.ConvertirCeldaBit(1.0));
            Assert.Equal(false, CargueConversion.ConvertirCeldaBit(0.0));
        }

        // ── ConvertirCeldaFecha ──
        [Fact]
        public void ConvertirCeldaFecha_AceptaFormatosIsoYSerieDeExcel()
        {
            Assert.Equal(new DateTime(2025, 3, 31), CargueConversion.ConvertirCeldaFecha("2025-03-31"));
            Assert.Equal(new DateTime(2025, 3, 31, 14, 30, 0), CargueConversion.ConvertirCeldaFecha("2025-03-31 14:30"));
            Assert.Equal(new DateTime(2025, 3, 31), CargueConversion.ConvertirCeldaFecha("2025/03/31"));
            Assert.Equal(new DateTime(2025, 1, 1), CargueConversion.ConvertirCeldaFecha(45658.0)); // serie OA de Excel
            var fecha = new DateTime(2024, 12, 31);
            Assert.Equal(fecha, CargueConversion.ConvertirCeldaFecha(fecha));
        }

        [Fact]
        public void ConvertirCeldaFecha_VacioEsDbNullYBasuraEsNull()
        {
            Assert.Equal(DBNull.Value, CargueConversion.ConvertirCeldaFecha(null));
            Assert.Equal(DBNull.Value, CargueConversion.ConvertirCeldaFecha("   "));
            Assert.Null(CargueConversion.ConvertirCeldaFecha("no es fecha"));
        }
    }
}
