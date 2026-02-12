using bufinscustomers.Helpers;
using System.Web.Mvc;
using System.Web.Routing;

namespace bufinscustomers.Permisos
{
    /// <summary>
    /// Atributo personalizado para verificar si un usuario tiene un permiso específico
    /// antes de ejecutar una acción del controlador
    /// </summary>
    public class RequierePermisoAttribute : ActionFilterAttribute
    {
        public string CodigoPermiso { get; set; }

        /// <summary>
        /// Constructor que recibe el código del permiso requerido
        /// </summary>
        /// <param name="codigoPermiso">Código del permiso (ej: "PLANTILLA_CARGUE", "REPORTES_PBI")</param>
        public RequierePermisoAttribute(string codigoPermiso)
        {
            CodigoPermiso = codigoPermiso;
        }

        /// <summary>
        /// Se ejecuta antes de la acción del controlador
        /// Verifica si el usuario tiene el permiso requerido
        /// </summary>
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // Verificar si el usuario tiene el permiso
            if (!UsuarioSesionHelper.TienePermiso(CodigoPermiso))
            {
                // Redirigir al inicio silenciosamente
                filterContext.Result = new RedirectToRouteResult(
                    new RouteValueDictionary
                    {
                        { "controller", "Home" },
                        { "action", "Index" }
                    });
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
