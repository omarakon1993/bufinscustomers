using bufinscustomers.Models;
using bufinscustomers.Permisos;

namespace bufinscustomers.Controllers
{
    /// <summary>Informe gerencial de Flujo de efectivo (dbo.ModeloFlujoEfectivo): estado de flujos de efectivo por el método indirecto
    /// (operación, inversión y deuda neta hasta el aumento neto del efectivo). La lógica vive en InformeFlujoBaseController.</summary>
    [ValidarSesion]
    public class InformeFlujoEfectivoController : InformeFlujoBaseController
    {
        protected override FlujoTipo TipoFlujo { get { return FlujoTipo.Efectivo; } }
        protected override string NombreControlador { get { return "InformeFlujoEfectivo"; } }
        protected override string FuncionIA { get { return IAFuncion.InsightsFlujoEfectivo; } }
        protected override string CodigoPrompt { get { return "RESUMEN_FLUJO_EFECTIVO_GERENCIAL"; } }

        /// <summary>Prompt por defecto (cuando no existe RESUMEN_FLUJO_EFECTIVO_GERENCIAL en GestorPrompts). La vista corta la
        /// respuesta por las etiquetas "Generación de caja:", "Uso del efectivo:" y "Acción recomendada:".</summary>
        protected override string PromptPorDefecto { get { return PromptEfectivo; } }

        private const string PromptEfectivo =
            "Prepara el resumen ejecutivo del FLUJO DE EFECTIVO (método indirecto) para la gerencia.\n" +
            "Cómo leer los datos: parte de la utilidad neta, suma partidas que no mueven caja (depreciación, amortización, provisiones) y los cambios " +
            "de capital de trabajo hasta el flujo de operación, y le suma inversión y deuda neta hasta el aumento neto del efectivo. \"Kpis\" trae " +
            "EfectivoOperacion (utilidad neta + partidas no monetarias), FlujoOperacion (después de capital de trabajo), FlujoInversion, DeudaNeta " +
            "(cambio en deuda neta: positivo = más deuda) y AumentoNeto (variación del efectivo). \"Valor\" es la cifra de la medida elegida " +
            "(Medida: real = ejecutado; proy = real hasta el mes UltimoMesReal y proyección después), \"ValorPpto\" el presupuesto (solo si " +
            "HayPresupuesto) y \"ValorAnt\" el mismo rango del año anterior; las variaciones vienen como fracción (0,05 = 5%). \"Puente\" lleva de la " +
            "utilidad neta al aumento neto del efectivo. \"Meses\" da el flujo neto y su acumulado mes a mes. Un flujo negativo significa que sale más " +
            "efectivo del que entra.\n\n" +
            "Responde EXACTAMENTE con estas 3 líneas, en este orden, sin títulos, viñetas, introducción ni conclusión:\n" +
            "Generación de caja: <cuánto efectivo genera la operación frente a la utilidad neta y el peso del capital de trabajo, con cifras>\n" +
            "Uso del efectivo: <cuánto se va a inversión y deuda, y de dónde se financia el aumento o la caída del efectivo, con cifras>\n" +
            "Acción recomendada: <una acción concreta y accionable para la gerencia, apoyada en los datos>\n\n" +
            "Reglas: cada línea tiene 1 o 2 frases y máximo 45 palabras; usa millones de pesos con formato colombiano (por ejemplo 170,0 M); " +
            "compara contra el presupuesto o el año anterior cuando existan; pon en **negrita** la cifra principal de cada línea; usa solo cifras " +
            "presentes en los datos y nunca inventes valores.";
    }
}
