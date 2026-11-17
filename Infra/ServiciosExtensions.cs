using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace bufinscustomers.Infra
{
    /// <summary>Registro en el contenedor de dependencias de los servicios de negocio (carpeta Services).</summary>
    public static class ServiciosExtensions
    {
        /// <summary>
        /// Tipos de negocio que se registran: clases públicas no estáticas ni abstractas de bufinscustomers.Services
        /// cuyo nombre termina en «Service» y que tienen un constructor público sin parámetros.
        /// </summary>
        public static Type[] TiposServicio()
        {
            return typeof(ServiciosExtensions).Assembly.GetTypes()
                .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsGenericTypeDefinition
                            && t.Namespace == "bufinscustomers.Services" && t.Name.EndsWith("Service", StringComparison.Ordinal)
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .ToArray();
        }

        /// <summary>
        /// Registra cada servicio como transitorio (una instancia nueva por inyección, igual que el antiguo <c>new Servicio()</c>).
        /// Se crea siempre con el constructor sin parámetros aunque la clase tenga otros.
        /// </summary>
        public static IServiceCollection AddServiciosBufins(this IServiceCollection servicios)
        {
            foreach (var tipo in TiposServicio())
            {
                var t = tipo;
                servicios.AddTransient(t, _ => Activator.CreateInstance(t));
            }
            return servicios;
        }
    }
}
