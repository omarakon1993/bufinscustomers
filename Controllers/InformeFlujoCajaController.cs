using bufinscustomers.Models;
using bufinscustomers.Permisos;

namespace bufinscustomers.Controllers
{
    /// <summary>Informe gerencial de Flujo de caja (dbo.ModeloFlujoCaja): generación de caja bruta, capital de trabajo, flujo de caja
    /// libre operativo, flujo financiero y no operacional, y el flujo neto. Toda la lógica vive en InformeFlujoBaseController.</summary>
    [ValidarSesion]
    public class InformeFlujoCajaController : InformeFlujoBaseController
    {
        protected override FlujoTipo TipoFlujo { get { return FlujoTipo.Caja; } }
        protected override string NombreControlador { get { return "InformeFlujoCaja"; } }
        protected override string FuncionIA { get { return IAFuncion.InsightsFlujoCaja; } }
        protected override string CodigoPrompt { get { return "RESUMEN_FLUJO_CAJA_GERENCIAL"; } }

        /// <summary>Prompt por defecto (cuando no existe RESUMEN_FLUJO_CAJA_GERENCIAL en GestorPrompts). La vista corta la respuesta
        /// por las etiquetas "Generación de caja:", "Uso del efectivo:" y "Acción recomendada:" — un prompt personalizado debe
        /// conservarlas.</summary>
        protected override string PromptPorDefecto { get { return PromptCaja; } }

        private const string PromptCaja =
            "Prepara el resumen ejecutivo del FLUJO DE CAJA para la gerencia.\n" +
            "Cómo leer los datos: es un flujo de caja gerencial. \"Kpis\" trae BrutoDespuesImpuestos (caja generada por la operación antes de " +
            "capital de trabajo e inversión, ya descontados impuestos), CapitalTrabajo (efecto de cartera, inventarios y proveedores; negativo = " +
            "consume caja), FclOperativo (flujo de caja libre operativo), FlujoNeto (FCL + financiero + no operacional = variación neta de la caja) " +
            "y ConversionCaja (FCL operativo / utilidad operacional; puede venir con NoAplica=true). \"Valor\" es la cifra de la medida elegida " +
            "(Medida: real = ejecutado; proy = real hasta el mes UltimoMesReal y proyección después), \"ValorPpto\" el presupuesto (solo si " +
            "HayPresupuesto) y \"ValorAnt\" el mismo rango del año anterior; las variaciones vienen como fracción (0,05 = 5%); en ConversionCaja " +
            "son diferencias en puntos. \"Puente\" lleva de la utilidad operacional al flujo neto. \"Meses\" da el flujo neto y su acumulado " +
            "mes a mes. Los valores de \"Filas\" con EsSubtotal agrupan líneas; en costos como impuestos o gastos financieros el valor es una magnitud que resta.\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Generación de caja: <qué tanta caja genera la operación y cuánto se convierte en caja libre, con cifras>\n" +
            "Uso del efectivo: <a dónde se va la caja (capital de trabajo, inversión, financiación) y qué rubro pesa más, con cifras>\n" +
            "Acción recomendada: <una acción concreta y accionable para la gerencia, apoyada en los datos>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; usa millones de pesos con formato colombiano (por ejemplo 170,0 M); " +
            "compara contra el presupuesto o el año anterior cuando existan; pon en **negrita** la cifra principal de cada línea; usa solo cifras " +
            "presentes en los datos y nunca inventes valores.";
    }
}
