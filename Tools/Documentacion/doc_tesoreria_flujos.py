# -*- coding: utf-8 -*-
from doc_lib import *


# ======================================================================================================================
# Tesorería del presupuesto
# ======================================================================================================================
def doc_tesoreria(L):
    t = L.t
    s = []
    s.append(('para', t('Para qué sirve', 'What it is for'), seccion('para', 1, t('Para qué sirve', 'What it is for'),
        t('<p class="lead">Responde: <b>¿alcanza la caja del presupuesto?, ¿en qué meses falta?, ¿cuánta financiación nueva se necesita?, ¿qué rubros lo explican?</b> Toma el presupuesto (o la proyección) del año y lo convierte en una lectura de caja mes a mes.</p>',
          '<p class="lead">It answers: <b>is the budget cash enough, in which months does it fall short, how much new funding is needed, which items explain it?</b> It takes the year’s budget (or projection) and turns it into a month-by-month cash reading.</p>')
        + dos_tarjetas(t('Para el gerente o dueño', 'For the manager or owner'),
            [t('Una franja de semáforo: la caja alcanza, queda justa o se requiere financiación.', 'A traffic-light strip: cash is enough, tight, or funding is required.'),
             t('El mes de menor caja y cuánta deuda nueva haría falta.', 'The month of lowest cash and how much new debt would be needed.')],
            t('Para el contador o analista financiero', 'For the accountant or financial analyst'),
            [t('El puente completo desde el capital y los ingresos hasta la caja neta del mes.', 'The full bridge from capital and revenue to the month’s net cash.'),
             t('El detalle por mes, con la opción de ver solo subtotales.', 'The monthly detail, with the option to see subtotals only.')]))))
    s.append(('pantalla', t('La pantalla', 'The screen'), seccion('pantalla', 2, t('La pantalla', 'The screen'),
        t('<p class="lead">Se elige la empresa, el escenario, las <b>cifras</b> y el rango de meses; por defecto se muestra todo el año. Luego se pulsa <b>Generar</b>.</p>',
          '<p class="lead">Choose the company, the scenario, the <b>figures</b> and the range of months; the whole year is shown by default. Then press <b>Generate</b>.</p>')
        + '<ul>'
        + t('<li><b>Cifras:</b> <b>presupuesto</b> o <b>proyección</b> (el presupuesto actualizado con lo ya ejecutado).</li>', '<li><b>Figures:</b> <b>budget</b> or <b>projection</b> (the budget updated with what has already been executed).</li>')
        + t('<li><b>Comparar contra</b> la otra medida (presupuesto vs proyección) y/o contra <b>otro escenario</b>.</li><li><b>Detalle de la tabla:</b> todas las líneas o solo subtotales. <b>Unidad:</b> pesos, miles o millones.</li>',
            '<li><b>Compare against</b> the other measure (budget vs projection) and/or against <b>another scenario</b>.</li><li><b>Table detail:</b> all lines or subtotals only. <b>Unit:</b> pesos, thousands or millions.</li>')
        + bloque_estilos(L) + '</ul>'
        + tabla([t('Bloque', 'Block'), t('Qué muestra', 'What it shows')], [
            [t('<b>Franja de estado</b>', '<b>Status strip</b>'), t('Verde: «la caja del período alcanza». Ámbar: «queda justa». Rojo: «se requiere financiación».', 'Green: “the period’s cash is enough”. Amber: “it is tight”. Red: “funding is required”.')],
            [t('<b>Cinco indicadores</b>', '<b>Five indicators</b>'), t('Caja neta del período, caja acumulada al cierre, saldo mínimo y su mes, meses con caja negativa y <b>deuda nueva requerida</b>.', 'Net cash of the period, accumulated cash at the end, minimum balance and its month, months with negative cash and <b>new debt required</b>.')],
            [t('<b>Caja mensual y acumulada</b>', '<b>Monthly and accumulated cash</b>'), t('Barras verdes y rojas con la caja de cada mes y una línea con la acumulada; la otra medida o el otro escenario se superponen si se eligió comparar.', 'Green and red bars with each month’s cash and a line with the accumulated cash; the other measure or the other scenario are overlaid if you chose to compare.')],
            [t('<b>Lo más relevante</b> y <b>mapa de calor</b>', '<b>Key takeaways</b> and <b>heat map</b>'), t('Los rubros que más pesan y una vista por colores de qué meses y conceptos aprietan la caja.', 'The items that weigh the most and a color view of which months and items squeeze cash.')],
            [t('<b>Tabla por mes</b> y <b>puente de caja</b>', '<b>Monthly table</b> and <b>cash bridge</b>'), t('El detalle de cada concepto por mes y el recorrido de la caja desde el punto de partida hasta la caja neta.', 'The detail of each item by month and the path of cash from the starting point to net cash.')],
            [t('<b>Comentarios de gestión</b>', '<b>Management comments</b>'), t('Notas del equipo sobre el cierre.', 'Team notes about the close.')],
        ])
        + nota(L, '<b>Solo el año de ejecución.</b> El modelo de tesorería guarda únicamente el año en ejecución, por eso el informe no compara entre años.', '<b>Execution year only.</b> The treasury model stores only the year being executed, so the report does not compare between years.', 'info'))))
    s.append(('datos', t('De dónde salen las cifras', 'Where the figures come from'), seccion('datos', 3, t('De dónde salen las cifras', 'Where the figures come from'),
        t('<p class="lead">De la tabla del modelo <code>ModeloTesoreriaPpto</code>, que se llena al ejecutar el modelo de tesorería en Datos → Ejecución de modelos. Ese modelo combina el estado de resultados, el cambio del balance y el balance del presupuesto. El informe <b>no ejecuta el modelo</b>: solo hay datos después de ejecutarlo.</p>',
          '<p class="lead">From the model table <code>ModeloTesoreriaPpto</code>, which is filled when the treasury model is run in Data → Model execution. That model combines the income statement, the balance sheet change and the budget balance sheet. The report <b>does not run the model</b>: there is data only after it has been run.</p>')
        + '<ul>'
        + t('<li><b>Presupuesto</b> es la medida base del modelo; <b>proyección</b> es el mismo presupuesto actualizado con lo ejecutado hasta hoy.</li>', '<li><b>Budget</b> is the model’s base measure; <b>projection</b> is the same budget updated with what has been executed so far.</li>')
        + t('<li>Las fórmulas de cada subtotal viven en el modelo (<code>Rel_TesoreriaPpto</code>); el informe las muestra tal cual.</li></ul>', '<li>The formula of each subtotal lives in the model (<code>Rel_TesoreriaPpto</code>); the report shows them as they are.</li></ul>'))))
    s.append(('calculo', t('Cómo se calcula', 'How it is calculated'), seccion('calculo', 4, t('Cómo se calcula', 'How it is calculated'),
        '<h3>' + t('La caja, paso a paso', 'Cash, step by step') + '</h3>'
        + tabla([t('Paso', 'Step'), t('Qué se suma o resta', 'What is added or subtracted')], [
            [t('1. Caja bruta', '1. Gross cash'), t('Capital social + deuda actual + ingresos − costo de ventas.', 'Share capital + current debt + revenue − cost of sales.')],
            [t('2. Caja antes de impuestos', '2. Cash before taxes'), t('− gastos operacionales + ingresos financieros − intereses − otros gastos no operacionales + otros ingresos no operacionales − diferencia en cambio.', '− operating expenses + financial income − interest − other non-operating expenses + other non-operating income − exchange difference.')],
            [t('3. Caja neta', '3. Net cash'), t('− impuesto de renta ± cambios en propiedad, planta y equipo, inversiones, flujo de efectivo neto de capital de trabajo y demás movimientos no operacionales.', '− income tax ± changes in property, plant and equipment, investments, working-capital net cash flow and other non-operating movements.')],
        ])
        + '<h3>' + t('Los indicadores', 'The indicators') + '</h3>'
        + tabla([t('Concepto', 'Item'), t('Cómo se obtiene', 'How it is obtained')], [
            [t('Caja acumulada', 'Accumulated cash'), t('La suma de la caja neta mes a mes; <b>se reinicia cada año</b>.', 'The month-by-month sum of net cash; it <b>restarts each year</b>.')],
            [t('Saldo mínimo', 'Minimum balance'), t('El menor valor de la caja acumulada en el rango, con su mes.', 'The lowest value of accumulated cash in the range, with its month.')],
            [t('Meses con caja negativa', 'Months with negative cash'), t('Cuántos meses del rango cierran con la caja acumulada por debajo de cero.', 'How many months in the range close with accumulated cash below zero.')],
            [t('Deuda nueva requerida', 'New debt required'), t('El <b>mayor déficit acumulado</b> del rango: lo que habría que financiar para que la caja nunca quede negativa.', 'The range’s <b>largest accumulated deficit</b>: what would have to be financed so that cash never goes negative.')],
            [t('Franja de estado', 'Status strip'), t('Rojo si hay déficit acumulado (se requiere financiación); ámbar si la caja alcanza pero queda justa; verde si alcanza con holgura.', 'Red if there is an accumulated deficit (funding required); amber if cash is enough but tight; green if it is comfortably enough.')],
        ])
        + nota(L, 'Las tres líneas de saldo del puente (acumulada, excedentes y deuda nueva) se <b>derivan de la caja neta mensual</b> en el propio informe: así son consistentes con la medida elegida y con el rango. Los valores menores a 0,5 se tratan como cero para evitar ruido numérico.', 'The three balance lines of the bridge (accumulated, surplus and new debt) are <b>derived from the monthly net cash</b> in the report itself, so they are consistent with the chosen measure and range. Values below 0.5 are treated as zero to avoid numeric noise.'))))
    s.append(('ia', t('Lectura con IA', 'AI reading'), seccion('ia', 5, t('Lectura con IA', 'AI reading'),
        bloque_ia(L, '<b>Posición de caja</b> (cómo queda la caja), <b>Financiación</b> (si hace falta y cuánta) y <b>Acción recomendada</b>',
                  '<b>Cash position</b> (where cash ends up), <b>Funding</b> (whether it is needed and how much) and <b>Recommended action</b>',
                  'únicamente el resumen ya calculado de caja, saldos y rubros principales',
                  'only the already-calculated summary of cash, balances and main items'))))
    s.append(('exportar', t('Exportar', 'Export'), seccion('exportar', 6, t('Exportar', 'Export'),
        bloque_exportar(L,
            'hojas «Resumen» (indicadores), «Caja mensual» (con gráfico editable), «Detalle por mes» y «Puente de caja», con el logo de la empresa.',
            'sheets “Summary” (indicators), “Monthly cash” (with an editable chart), “Monthly detail” and “Cash bridge”, with the company logo.',
            'página 1 con el estado, los indicadores, la lectura con IA y lo más relevante; página 2 con los gráficos; y la tabla por mes.',
            'page 1 with the status, indicators, AI reading and key takeaways; page 2 with the charts; and the monthly table.'))))
    s.append(('faq', t('Preguntas frecuentes', 'FAQ'), '<section id="faq"><h2>7. ' + t('Preguntas frecuentes', 'Frequently asked questions') + '</h2>' + faq([
        (t('Dice que no hay datos, ¿qué hago?', 'It says there is no data, what do I do?'), t('Ejecute el modelo de tesorería de la empresa en Datos → Ejecución de modelos y elija un escenario con información.', 'Run the company’s treasury model in Data → Model execution and choose a scenario with data.')),
        (t('¿Cuál es la diferencia entre presupuesto y proyección?', 'What is the difference between budget and projection?'), t('El presupuesto es la meta del año; la proyección es ese presupuesto actualizado con lo que ya se ejecutó.', 'The budget is the year’s target; the projection is that budget updated with what has already been executed.')),
        (t('¿Qué significa «deuda nueva requerida»?', 'What does “new debt required” mean?'), t('Es el mayor déficit acumulado del rango: el dinero que habría que conseguir para que la caja nunca quede por debajo de cero.', 'It is the range’s largest accumulated deficit: the money that would have to be raised so that cash never falls below zero.')),
        (t('¿Por qué la caja acumulada vuelve a empezar en enero?', 'Why does accumulated cash start over in January?'), t('Porque el acumulado se reinicia cada año.', 'Because the accumulated total restarts every year.')),
        (t('¿Puedo ver años anteriores?', 'Can I see previous years?'), t('No: el modelo de tesorería solo guarda el año de ejecución.', 'No: the treasury model only stores the execution year.')),
        (t('¿Se puede comparar contra otro escenario?', 'Can I compare against another scenario?'), t('Sí, con la misma medida (presupuesto o proyección). La opción se deshabilita si el otro escenario no tiene datos.', 'Yes, with the same measure (budget or projection). The option is disabled if the other scenario has no data.')),
    ]) + '</section>'))
    return (t('Tesorería del presupuesto', 'Budget treasury'),
            t('Si la caja del presupuesto alcanza, en qué meses falta, cuánta financiación se necesita y qué rubros lo explican.',
              'Whether the budget cash is enough, in which months it falls short, how much funding is needed and which items explain it.'), s)


# ======================================================================================================================
# Flujo de caja / Flujo de efectivo (misma estructura)
# ======================================================================================================================
def doc_flujo(L, tipo):
    t = L.t
    caja = (tipo == 'Caja')
    s = []
    if caja:
        titulo = t('Flujo de caja', 'Cash flow')
        pregunta_es = '¿cuánta caja genera el negocio?, ¿cuánto se va en impuestos, capital de trabajo e inversión?, ¿qué tan bien se convierte la utilidad en caja?'
        pregunta_en = 'how much cash does the business generate, how much goes to taxes, working capital and investment, how well is profit converted into cash?'
        desc_es = 'Es la <b>vista gerencial de la caja</b>: parte de la utilidad operacional y llega al flujo neto, pasando por la caja bruta, el capital de trabajo, la inversión y el flujo financiero.'
        desc_en = 'It is the <b>management view of cash</b>: it starts from operating profit and reaches net flow, passing through gross cash, working capital, investment and financing flow.'
    else:
        titulo = t('Flujo de efectivo', 'Statement of cash flows')
        pregunta_es = '¿cuánto efectivo generó la operación?, ¿cuánto se invirtió?, ¿cuánta deuda neta se tomó o se pagó?, ¿cuánto cambió el efectivo?'
        pregunta_en = 'how much cash did operations generate, how much was invested, how much net debt was taken or repaid, how much did cash change?'
        desc_es = 'Es el <b>estado de flujo de efectivo</b> por actividades: <b>operación</b>, <b>inversión</b> y <b>financiación</b>, hasta el cambio neto del efectivo.'
        desc_en = 'It is the <b>cash flow statement</b> by activity: <b>operating</b>, <b>investing</b> and <b>financing</b>, down to the net change in cash.'
    s.append(('para', t('Para qué sirve', 'What it is for'), seccion('para', 1, t('Para qué sirve', 'What it is for'),
        t('<p class="lead">Responde: <b>%s</b> %s</p>' % (pregunta_es, desc_es), '<p class="lead">It answers: <b>%s</b> %s</p>' % (pregunta_en, desc_en))
        + dos_tarjetas(t('Para el gerente o dueño', 'For the manager or owner'),
            [t('Una franja que dice si el negocio genera caja suficiente para sostener la operación.', 'A strip that tells whether the business generates enough cash to sustain operations.'),
             t('De dónde viene la caja y a dónde va, con una cascada de la caja inicial a la final.', 'Where cash comes from and where it goes, with a waterfall from opening to closing cash.')],
            t('Para el contador o analista financiero', 'For the accountant or financial analyst'),
            [t('El detalle por mes, con vista mensual o acumulada.', 'The monthly detail, with a monthly or accumulated view.'),
             t('La validación de que el flujo concilia con la caja y bancos del balance.', 'The validation that the flow reconciles with the balance sheet cash and banks.')]))))
    s.append(('pantalla', t('La pantalla', 'The screen'), seccion('pantalla', 2, t('La pantalla', 'The screen'),
        t('<p class="lead">Se elige la empresa, el escenario, las <b>cifras</b> y el rango de meses. Por defecto el rango va <b>desde enero hasta el último mes ejecutado</b>. Luego se pulsa <b>Generar</b>.</p>',
          '<p class="lead">Choose the company, the scenario, the <b>figures</b> and the range of months. By default the range runs <b>from January to the last executed month</b>. Then press <b>Generate</b>.</p>')
        + '<ul>'
        + t('<li><b>Cifras:</b> <b>real</b> (solo meses ejecutados) o <b>cierre proyectado</b> (real hasta el último mes ejecutado y proyección de ahí en adelante; los meses proyectados se marcan con * y se ven más claros).</li>',
            '<li><b>Figures:</b> <b>actual</b> (executed months only) or <b>projected close</b> (actual up to the last executed month and projection from there on; projected months are marked with * and look lighter).</li>')
        + t('<li><b>Detalle de la tabla:</b> todas las líneas o solo subtotales; <b>vista</b> mensual o acumulada; <b>unidad</b> en pesos, miles o millones. La unidad y la vista se recuerdan en su navegador.</li><li><b>Comparar con otro escenario</b> (opcional).</li>',
            '<li><b>Table detail:</b> all lines or subtotals only; <b>view</b> monthly or accumulated; <b>unit</b> in pesos, thousands or millions. The unit and the view are remembered in your browser.</li><li><b>Compare with another scenario</b> (optional).</li>')
        + bloque_estilos(L) + '</ul>'
        + tabla([t('Bloque', 'Block'), t('Qué muestra', 'What it shows')], [
            [t('<b>Franja de estado</b>', '<b>Status strip</b>'), t('Verde, ámbar o rojo según el flujo libre u operativo frente al flujo neto.', 'Green, amber or red depending on free or operating flow against net flow.')],
            [t('<b>Cinco indicadores</b>', '<b>Five indicators</b>'),
             t('Caja bruta después de impuestos, efecto del capital de trabajo, flujo de caja libre operativo, flujo neto y <b>conversión a caja</b>.', 'Gross cash after taxes, working capital effect, operating free cash flow, net flow and <b>cash conversion</b>.') if caja
             else t('Efectivo generado en la operación, flujo de operación, inversión, deuda neta y aumento neto del efectivo.', 'Cash generated by operations, operating flow, investment, net debt and net increase in cash.')],
            [t('<b>Flujo mensual</b>', '<b>Monthly flow</b>'), t('Barras con el flujo de cada mes; si se compara, el otro escenario se superpone.', 'Bars with each month’s flow; if you compare, the other scenario is overlaid.')],
            [t('<b>Cascada de caja</b>', '<b>Cash waterfall</b>'), t('De la <b>caja inicial</b> a la <b>caja final</b> pasando por cada fuente (+) y cada uso (−).', 'From <b>opening cash</b> to <b>closing cash</b> through each source (+) and each use (−).')],
            [t('<b>Fuentes y usos</b>', '<b>Sources and uses</b>'), t('El flujo del rango por actividad: positivo es una fuente de caja, negativo es un uso.', 'The range’s flow by activity: positive is a source of cash, negative is a use.')],
            [t('<b>Validación contra el balance</b>', '<b>Validation against the balance sheet</b>'), t('Una franja verde o roja dice si el flujo concilia con la «Caja y bancos» del balance.', 'A green or red strip says whether the flow reconciles with the “Cash and banks” line of the balance sheet.')],
            [t('<b>Puente y tabla por mes</b>', '<b>Bridge and monthly table</b>'), t('El recorrido paso a paso y el detalle de cada concepto por mes.', 'The step-by-step path and the detail of each item by month.')],
            [t('<b>Comentarios de gestión</b>', '<b>Management comments</b>'), t('Notas del equipo sobre el cierre.', 'Team notes about the close.')],
        ]))))
    s.append(('datos', t('De dónde salen las cifras', 'Where the figures come from'), seccion('datos', 3, t('De dónde salen las cifras', 'Where the figures come from'),
        t('<p class="lead">De la tabla del modelo <code>%s</code>, que se llena al ejecutar el modelo en Datos → Ejecución de modelos. El informe <b>no ejecuta el modelo</b>: solo hay datos después de ejecutarlo. A diferencia de la tesorería, esta tabla trae <b>varios años</b>.</p>' % ('ModeloFlujoCaja' if caja else 'ModeloFlujoEfectivo'),
          '<p class="lead">From the model table <code>%s</code>, which is filled when the model is run in Data → Model execution. The report <b>does not run the model</b>: there is data only after it has been run. Unlike treasury, this table holds <b>several years</b>.</p>' % ('ModeloFlujoCaja' if caja else 'ModeloFlujoEfectivo'))
        + '<ul>'
        + t('<li>Cada línea trae el valor <b>real</b> (solo meses ejecutados), la <b>proyección</b> y el <b>presupuesto</b> (este último, solo del año de ejecución).</li>', '<li>Each line carries the <b>actual</b> value (executed months only), the <b>projection</b> and the <b>budget</b> (the latter only for the execution year).</li>')
        + t('<li>La <b>caja inicial</b> y la validación usan la línea «Caja y bancos» del balance (el mismo modelo del Informe de indicadores).</li></ul>', '<li>The <b>opening cash</b> and the validation use the “Cash and banks” line of the balance sheet (the same model as the Indicators report).</li></ul>'))))
    if caja:
        filas_calc = [
            [t('Caja bruta antes de impuestos', 'Gross cash before taxes'), t('Utilidad operacional + depreciación, amortización y provisiones.', 'Operating profit + depreciation, amortization and provisions.')],
            [t('Caja bruta después de impuestos', 'Gross cash after taxes'), t('Caja bruta antes de impuestos − impuesto de renta.', 'Gross cash before taxes − income tax.')],
            [t('Efecto del capital de trabajo', 'Working capital effect'), t('El cambio en cuentas por cobrar, inventarios y cuentas por pagar: si consume caja, resta; si la libera, suma.', 'The change in receivables, inventories and payables: if it consumes cash it subtracts; if it releases cash it adds.')],
            [t('Flujo de caja libre operativo', 'Operating free cash flow'), t('Caja bruta después de impuestos ± capital de trabajo − inversión en propiedad, planta y equipo, intangibles y otras inversiones.', 'Gross cash after taxes ± working capital − investment in property, plant and equipment, intangibles and other investments.')],
            [t('Flujo financiero y no operacional', 'Financing and non-operating flow'), t('Ingresos y gastos financieros, diferencia en cambio y otros movimientos fuera de la operación.', 'Financial income and expenses, exchange difference and other movements outside operations.')],
            [t('Flujo neto', 'Net flow'), t('Flujo de caja libre operativo + flujo financiero + flujo no operacional. Es el cambio de la caja del período.', 'Operating free cash flow + financing flow + non-operating flow. It is the change in cash for the period.')],
            [t('Conversión a caja', 'Cash conversion'), t('Flujo de caja libre operativo ÷ utilidad operacional. «No aplica» si la utilidad operacional es cero o negativa.', 'Operating free cash flow ÷ operating profit. “Not applicable” if operating profit is zero or negative.')],
        ]
    else:
        filas_calc = [
            [t('Efectivo generado en la operación', 'Cash generated by operations'), t('Utilidad operacional ajustada por partidas que no son caja (depreciación, amortización, provisiones) y por impuestos.', 'Operating profit adjusted for non-cash items (depreciation, amortization, provisions) and taxes.')],
            [t('Flujo de operación', 'Operating flow'), t('Efectivo generado en la operación ± cambio en el capital de trabajo.', 'Cash generated by operations ± change in working capital.')],
            [t('Inversión', 'Investing'), t('Compras y ventas de propiedad, planta y equipo, intangibles e inversiones. El modelo incluye aquí los movimientos de patrimonio.', 'Purchases and sales of property, plant and equipment, intangibles and investments. The model includes equity movements here.')],
            [t('Deuda neta', 'Net debt'), t('Obligaciones financieras nuevas − pagos de deuda: positiva si la empresa se endeuda, negativa si paga.', 'New financial obligations − debt repayments: positive if the company borrows, negative if it repays.')],
            [t('Cambio neto del efectivo', 'Net change in cash'), t('Flujo de operación + inversión + deuda neta. Es el último renglón del estado.', 'Operating flow + investing + net debt. It is the last line of the statement.')],
        ]
    filas_calc += [
        [t('Cifras «cierre proyectado»', '“Projected close” figures'), t('Valor real hasta el último mes ejecutado de cada año y proyección de ahí en adelante.', 'Actual value up to each year’s last executed month and projection from there on.')],
        [t('Caja inicial', 'Opening cash'), t('«Caja y bancos» del balance al cierre del mes anterior al rango.', '“Cash and banks” from the balance sheet at the close of the month before the range.')],
        [t('Caja final calculada', 'Computed closing cash'), t('Caja inicial + flujo neto de los meses reales del rango.', 'Opening cash + net flow of the range’s actual months.')],
    ]
    s.append(('calculo', t('Cómo se calcula', 'How it is calculated'), seccion('calculo', 4, t('Cómo se calcula', 'How it is calculated'),
        tabla([t('Concepto', 'Item'), t('Cómo se obtiene', 'How it is obtained')], filas_calc)
        + nota(L, 'Cuando una cifra no tiene datos en la medida o el rango elegidos, el informe lo dice (por ejemplo, «real» de un mes que todavía no se ejecutó) en lugar de mostrar ceros.', 'When a figure has no data in the chosen measure or range, the report says so (for example, “actual” of a month not yet executed) instead of showing zeros.'))))
    s.append(('cuadre', t('Caja y validación', 'Cash and validation'), seccion('cuadre', 5, t('Caja inicial, caja final y validación', 'Opening cash, closing cash and validation'),
        t('<p class="lead">La cascada arranca en la <b>caja inicial</b>, suma las fuentes, resta los usos y termina en la <b>caja final</b>. Una franja verifica que ese resultado <b>coincida con la «Caja y bancos» del balance</b> del último mes real del rango.</p>',
          '<p class="lead">The waterfall starts at <b>opening cash</b>, adds the sources, subtracts the uses and ends at <b>closing cash</b>. A strip checks that this result <b>matches the “Cash and banks” of the balance sheet</b> for the last actual month of the range.</p>')
        + '<ul>'
        + t('<li><b>Franja verde:</b> concilia, con una tolerancia de unos pocos pesos por redondeos (crece con el número de meses).</li>', '<li><b>Green strip:</b> it reconciles, within a tolerance of a few pesos for rounding (it grows with the number of months).</li>')
        + t('<li><b>Franja roja:</b> muestra la diferencia, y la cascada agrega una barra ámbar «Sin conciliar» para que cierre en el balance y no esconda el hueco.</li>', '<li><b>Red strip:</b> shows the difference, and the waterfall adds an amber “Unreconciled” bar so it closes at the balance sheet and does not hide the gap.</li>')
        + t('<li><b>Sin caja inicial:</b> si el rango empieza en el primer mes que tiene balance, no hay cierre anterior; el informe lo avisa y omite la cascada (las fuentes y usos sí se muestran).</li>', '<li><b>No opening cash:</b> if the range starts at the first month that has a balance sheet, there is no previous close; the report says so and skips the waterfall (sources and uses are still shown).</li>')
        + t('<li><b>Enero:</b> en algunos años el modelo arranca enero con una caja distinta al cierre de diciembre; un rango que cruza esos meses puede mostrar una diferencia que no es un error del informe.</li></ul>', '<li><b>January:</b> in some years the model starts January with a cash balance different from the December close; a range that crosses those months can show a difference that is not a report error.</li></ul>'))))
    s.append(('ia', t('Lectura con IA', 'AI reading'), seccion('ia', 6, t('Lectura con IA', 'AI reading'),
        bloque_ia(L, '<b>Generación de caja</b> (cuánta genera el negocio), <b>Uso del efectivo</b> (a dónde se va) y <b>Acción recomendada</b>',
                  '<b>Cash generation</b> (how much the business produces), <b>Use of cash</b> (where it goes) and <b>Recommended action</b>',
                  'únicamente el resumen ya calculado del flujo, las fuentes y usos y la conciliación',
                  'only the already-calculated summary of the flow, the sources and uses and the reconciliation'))))
    s.append(('exportar', t('Exportar', 'Export'), seccion('exportar', 7, t('Exportar', 'Export'),
        bloque_exportar(L,
            'hojas «Resumen», «Flujo mensual» (con gráfico editable), «Detalle por mes», «Puente» y «Fuentes, usos y caja», con el logo de la empresa.',
            'sheets “Summary”, “Monthly flow” (with an editable chart), “Monthly detail”, “Bridge” and “Sources, uses and cash”, with the company logo.',
            'página 1 con el resumen, página 2 con los gráficos, página 3 con la caja, las fuentes y los usos, y la tabla por mes.',
            'page 1 with the summary, page 2 with the charts, page 3 with cash, sources and uses, and the monthly table.'))))
    s.append(('faq', t('Preguntas frecuentes', 'FAQ'), '<section id="faq"><h2>8. ' + t('Preguntas frecuentes', 'Frequently asked questions') + '</h2>' + faq([
        (t('Dice que no hay datos, ¿qué hago?', 'It says there is no data, what do I do?'), t('Ejecute el modelo de la empresa en Datos → Ejecución de modelos. Si elige «real» y el rango tiene meses sin ejecutar, esos meses no tienen cifra.', 'Run the company’s model in Data → Model execution. If you choose “actual” and the range has months not yet executed, those months have no figure.')),
        (t('¿Qué significa el asterisco (*) en un mes?', 'What does the asterisk (*) on a month mean?'), t('Que ese mes es proyectado, no real.', 'That the month is projected, not actual.')),
        (t('La franja de validación sale en rojo, ¿qué hago?', 'The validation strip is red, what do I do?'), t('Revise que el modelo del balance y el de flujo se hayan ejecutado para el mismo escenario y la misma carga de datos. Si el rango cruza un enero, puede que el modelo arranque el año con otra caja.', 'Check that the balance sheet model and the flow model were run for the same scenario and data load. If the range crosses a January, the model may start the year with a different cash balance.')),
        (t('¿Por qué no veo la cascada de caja?', 'Why don’t I see the cash waterfall?'), t('Porque el rango empieza en el primer mes con balance y no hay cierre anterior de donde tomar la caja inicial.', 'Because the range starts at the first month with a balance sheet and there is no previous close to take the opening cash from.')),
        (t('¿Cuál es la diferencia entre flujo de caja y flujo de efectivo?', 'What is the difference between the cash flow and the statement of cash flows?'),
         t('El <b>flujo de caja</b> es la vista gerencial (de la utilidad operacional al flujo neto); el <b>flujo de efectivo</b> es el estado por actividades (operación, inversión, financiación). Los dos terminan en el mismo cambio de caja.', 'The <b>cash flow</b> is the management view (from operating profit to net flow); the <b>statement of cash flows</b> is the by-activity statement (operating, investing, financing). Both end at the same change in cash.')),
        (t('¿Se puede comparar contra el presupuesto?', 'Can I compare against the budget?'), t('Hoy se compara contra otro escenario; el presupuesto está disponible como cifra solo para el año de ejecución.', 'Today you compare against another scenario; the budget is available as a figure only for the execution year.')),
    ]) + '</section>'))
    return (titulo,
            t('La vista gerencial de la caja: de la utilidad operacional al flujo neto, la conversión a caja y la validación contra el balance.', 'The management view of cash: from operating profit to net flow, cash conversion and validation against the balance sheet.') if caja
            else t('El estado de flujo de efectivo: operación, inversión y financiación, y cómo concilia con la caja del balance.', 'The cash flow statement: operating, investing and financing activities, and how it reconciles with the balance sheet cash.'), s)
