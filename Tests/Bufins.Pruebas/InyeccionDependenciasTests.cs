using System;
using System.Linq;
using bufinscustomers.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bufins.Pruebas
{
    /// <summary>
    /// Garantiza que el contenedor sabe construir TODOS los controladores: si alguien agrega un parámetro de
    /// constructor que no está registrado, esta prueba falla (en vez de fallar en producción al abrir la pantalla).
    /// No crea ningún servicio ni toca la base de datos: solo consulta qué tipos están registrados.
    /// </summary>
    public class InyeccionDependenciasTests
    {
        [Fact]
        public void TodosLosParametrosDeConstructorDeLosControladoresSonResolubles()
        {
            var servicios = new ServiceCollection();
            servicios.AddServiciosBufins();
            using (var proveedor = servicios.BuildServiceProvider())
            {
                var esServicio = proveedor.GetRequiredService<IServiceProviderIsService>();
                var controladores = typeof(ServiciosExtensions).Assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));
                var sinRegistrar = controladores
                    .SelectMany(c => c.GetConstructors().SelectMany(k => k.GetParameters().Select(p => new { c, p.ParameterType })))
                    .Where(x => !esServicio.IsService(x.ParameterType))
                    .Select(x => x.c.Name + " necesita " + x.ParameterType.Name)
                    .ToList();
                Assert.True(sinRegistrar.Count == 0, "Sin registrar: " + string.Join("; ", sinRegistrar));
            }
        }

        [Fact]
        public void LosServiciosRegistradosSeCreanConSuConstructorSinParametros()
        {
            Assert.All(ServiciosExtensions.TiposServicio(), t => Assert.NotNull(t.GetConstructor(Type.EmptyTypes)));
            Assert.True(ServiciosExtensions.TiposServicio().Length > 50);
        }
    }
}
