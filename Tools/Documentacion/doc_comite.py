# -*- coding: utf-8 -*-
"""Documentación del Informe para comité (bilingüe). Se genera con gen_doc_informes.py."""
from doc_lib import *


def doc_comite(L):
    t = L.t
    s = []

    s.append(("para", t("Para qué sirve", "What it is for"), seccion("para", 1, t("Para qué sirve", "What it is for"),
        t("<p class=\"lead\">Arma el <b>cierre de un mes como documento para presentar, decidir y firmar</b> en un comité: cómo cerró el mes, cuánta plata queda, cómo va el año mes a mes, qué indicadores requieren atención y un espacio para dejar escritas las decisiones y las firmas.</p>",
          "<p class=\"lead\">It builds the <b>month-end close as a document to present, decide on and sign</b> in a committee: how the month closed, how much money is left, how the year is going month by month, which indicators need attention, and a space to write down the decisions and the signatures.</p>")
        + dos_tarjetas(t("Para el comité o la gerencia", "For the committee or management"),
            [t("Una sola página de lectura con lo que importa del cierre, sin tener que abrir varios informes.", "A single reading page with what matters from the close, without opening several reports."),
             t("Resultados contra presupuesto, caja disponible y proyectada, y el PYG del año en una hoja.", "Results against budget, available and projected cash, and the year P&amp;L on one sheet."),
             t("Se imprime o se envía por correo, con recuadro de decisiones y firmas.", "It can be printed or sent by email, with a decisions box and signatures.")],
            t("Para el equipo financiero", "For the finance team"),
            [t("No calcula cifras nuevas: reutiliza las del Informe mensual de gerencia, el PYG y el Flujo de caja.", "It calculates no new figures: it reuses those of the Monthly management report, the P&amp;L and the Cash flow."),
             t("Cada parte es una sección que se colapsa; lo que queda expandido es lo que se exporta.", "Each part is a section that can be collapsed; what stays expanded is what gets exported."),
             t("Los comentarios de gestión del cierre se incluyen y se pueden escribir desde aquí.", "The close management comments are included and can be written from here.")]))))

    s.append(("pantalla", t("La pantalla", "The screen"), seccion("pantalla", 2, t("La pantalla", "The screen"),
        tabla([t("Elemento", "Item"), t("Qué hace", "What it does")], [
            [t("Filtros", "Filters"), t("Empresa, escenario y <b>mes de corte</b> (los meses con ingresos en el estado de resultados). Al pulsar <b>Generar</b> se arma el documento.", "Company, scenario and <b>cutoff month</b> (months with revenue in the income statement). Pressing <b>Generate</b> builds the document.")],
            [t("Barra «Secciones»", "“Sections” bar"), t("Un botón por cada sección, arriba de la tarjeta de la empresa. «Mostrar todas» y «Ocultar todas» cambian todas a la vez.", "One button per section, above the company card. “Show all” and “Hide all” change all of them at once.")],
            [t("Títulos de sección", "Section titles"), t("Un clic en el título colapsa o expande la sección y es el mismo control que su botón de la barra. Su elección se recuerda en este navegador.", "One click on the title collapses or expands the section and it is the same control as its button in the bar. Your choice is remembered in this browser.")],
            [t("Unidad", "Unit"), t("Pesos, miles o millones; aplica a todas las cifras del documento.", "Pesos, thousands or millions; it applies to every figure in the document.")],
            [t("Estilos del informe", "Report styles"), t("Paleta de colores de las pantallas y de todas las exportaciones; por defecto, la de Bufins.", "Color palette of the screens and of every export; Bufins by default.")]])
        + t("<p class=\"note\">Si una parte del modelo no tiene datos para el mes (por ejemplo, no hay balance), esa sección no aparece y las demás se muestran igual.</p>",
            "<p class=\"note\">If a part of the model has no data for the month (for example, there is no balance sheet), that section does not appear and the others are shown anyway.</p>"))))

    s.append(("secciones", t("Las secciones", "The sections"), seccion("secciones", 3, t("Las secciones, en su orden", "The sections, in order"),
        tabla([t("Sección", "Section"), t("Qué contiene y de dónde sale", "What it contains and where it comes from")], [
            [t("Resumen del cierre", "Close summary"), t("Ingresos, EBITDA y utilidad neta del mes contra el presupuesto y el acumulado del año (Informe mensual de gerencia).", "The month revenue, EBITDA and net profit against the budget and the year to date (Monthly management report).")],
            [t("Balance y caja del mes", "Balance sheet and cash of the month"), t("Indicadores del balance al cierre y la caja del mes: caja inicial, flujo neto, flujo contra presupuesto, flujo de caja libre operativo y caja final.", "Balance sheet indicators at the close and the month cash: opening cash, net flow, flow against budget, operating free cash flow and closing cash.")],
            [t("¿Cuánta plata queda?", "How much money is left?"), t("Caja al iniciar el año, caja disponible al mes de corte y caja proyectada a diciembre, con el puente de entradas y salidas y avisos si algún mes queda en negativo (Flujo de caja).", "Cash at the start of the year, cash available at the cutoff month and projected cash at December, with the bridge of money in and out and warnings if any month turns negative (Cash flow).")],
            [t("Estado de resultados en una hoja", "Income statement on one sheet"), t("El PYG del año mes a mes con acumulado, AV %, presupuesto y año anterior (PYG).", "The year P&amp;L month by month with year-to-date, AV %, budget and prior year (P&amp;L).")],
            [t("Indicadores en atención", "Indicators needing attention"), t("El semáforo del mes y los indicadores que más requieren atención (Indicadores).", "The month traffic light and the indicators that most need attention (Indicators).")],
            [t("Líneas de negocio", "Business lines"), t("Ingresos, participación y margen operacional de cada línea (Rentabilidad por línea de negocio).", "Revenue, share and operating margin of each line (Business line profitability).")],
            [t("Comentarios de gestión", "Management comments"), t("Los comentarios de todos los informes del cierre; los nuevos que se escriban aquí quedan como «Informe para comité».", "The comments from all the reports of the close; new ones written here are saved as “Committee report”.")],
            [t("Decisiones y compromisos", "Decisions and commitments"), t("Un recuadro en blanco para anotar decisiones, responsables y fechas a mano en el documento impreso.", "A blank box to write down decisions, owners and dates by hand on the printed document.")],
            [t("Firmas y aprobación", "Signatures and approval"), t("Tres espacios: Elaboró, Revisó y Aprobó, cada uno con nombre, cargo y fecha.", "Three spaces: Prepared by, Reviewed by and Approved by, each with name, position and date.")]]))))

    s.append(("datos", t("De dónde salen las cifras", "Where the figures come from"), seccion("datos", 4, t("De dónde salen las cifras y cómo se leen", "Where the figures come from and how to read them"),
        t("<p class=\"lead\">Todo sale de las tablas materializadas del modelo (<code>Modelo*</code>); este informe no ejecuta los modelos ni calcula cifras propias.</p>",
          "<p class=\"lead\">Everything comes from the model materialized tables (<code>Modelo*</code>); this report does not run the models or calculate figures of its own.</p>")
        + tabla([t("Cifra", "Figure"), t("Cómo se obtiene", "How it is obtained")], [
            [t("Caja al iniciar el año", "Cash at the start of the year"), t("La línea «Caja y bancos» del balance al cierre del año anterior. Si ese balance no existe, no se puede calcular la caja y se muestra solo el flujo.", "The “Cash and banks” balance sheet line at the prior year-end. If that balance sheet does not exist, cash cannot be computed and only the flow is shown.")],
            [t("Caja disponible al corte", "Cash available at the cutoff"), t("La «Caja y bancos» del balance del mes de corte; si no existe, la caja calculada (caja inicial más el flujo neto real). Si el corte es posterior al último mes ejecutado, se usa ese último mes.", "The “Cash and banks” of the cutoff month balance sheet; if it does not exist, the calculated cash (opening cash plus actual net flow). If the cutoff is after the last executed month, that last month is used.")],
            [t("Caja proyectada a diciembre", "Projected cash at December"), t("La caja del corte más el flujo neto proyectado de los meses siguientes, según el modelo.", "The cutoff cash plus the projected net flow of the following months, per the model.")],
            [t("AV % del PYG", "AV % of the P&amp;L"), t("Participación de cada línea sobre los ingresos acumulados. En costos y gastos, bajar contra el presupuesto o el año anterior es favorable.", "Share of each line over year-to-date revenue. For costs and expenses, going down against the budget or the prior year is favorable.")],
            [t("EBITDA", "EBITDA"), t("No es una línea del modelo: utilidad operacional más depreciaciones y amortizaciones.", "It is not a model line: operating profit plus depreciation and amortization.")]]))))

    s.append(("exportar", t("Exportar", "Export"), seccion("exportar", 5, t("Exportar", "Export"),
        bloque_exportar(L,
            "solo las secciones expandidas: hoja «Resumen» (resultados, balance y caja del mes, indicadores, líneas, decisiones y firmas), «PYG», «Caja disponible» y «Comentarios».",
            "only the expanded sections: “Summary” sheet (results, balance sheet and cash of the month, indicators, lines, decisions and signatures), “P&amp;L”, “Available cash” and “Comments”.",
            "varias páginas horizontales: portada con el resumen, balance y caja; el PYG en una página aparte; y al final los indicadores, las líneas, los comentarios, el recuadro de decisiones y las firmas, para imprimir y firmar.",
            "several landscape pages: cover with the summary, balance sheet and cash; the P&amp;L on its own page; and at the end the indicators, the lines, the comments, the decisions box and the signatures, to print and sign.",
            bloque_estilos(L)))))

    s.append(("faq", t("Preguntas frecuentes", "FAQ"), "<section id=\"faq\"><h2>6. " + t("Preguntas frecuentes", "Frequently asked questions") + "</h2>" + faq([
        (t("El mes que busco no aparece", "The month I am looking for does not appear"), t("La lista trae solo los meses con ingresos en el estado de resultados. Ejecute los modelos de la empresa y vuelva a elegir la empresa.", "The list only brings months with revenue in the income statement. Run the company models and choose the company again.")),
        (t("Una sección no aparece", "A section does not appear"), t("Esa parte del modelo (balance, flujo, indicadores o líneas) no tiene información para el mes. Las demás secciones no se afectan.", "That part of the model (balance sheet, flow, indicators or lines) has no information for the month. The other sections are not affected.")),
        (t("¿En qué se diferencia del Informe mensual de gerencia?", "How is it different from the Monthly management report?"), t("Usa las mismas cifras, pero lo arma como documento de comité: agrega la caja disponible y proyectada, el PYG en una hoja y los espacios de decisiones y firmas. Son opciones de menú distintas, con permisos distintos.", "It uses the same figures, but lays them out as a committee document: it adds available and projected cash, the one-page P&amp;L and the decisions and signatures spaces. They are different menu options, with different permissions.")),
        (t("¿Tiene lectura con IA?", "Does it have an AI reading?"), t("No. Para una lectura con IA use el Informe mensual de gerencia o cada informe.", "No. For an AI reading use the Monthly management report or each individual report.")),
        (t("¿Puedo elegir varios meses?", "Can I choose several months?"), t("No: es el cierre de un mes. Para un rango use los informes individuales.", "No: it is the close of one month. For a range use the individual reports."))
    ]) + "</section>"))

    return (t("Informe para comité", "Committee report"),
            t("El cierre del mes como documento para presentar, decidir y firmar: resultados, caja, PYG, indicadores, comentarios, decisiones y firmas.",
              "The month-end close as a document to present, decide on and sign: results, cash, P&amp;L, indicators, comments, decisions and signatures."), s)
