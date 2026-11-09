# -*- coding: utf-8 -*-
from doc_lib import *


# ======================================================================================================================
# Estado de resultados (PYG)
# ======================================================================================================================
def doc_pyg(L):
    t = L.t
    s = []
    s.append(('para', t('Para qué sirve', 'What it is for'), seccion('para', 1, t('Para qué sirve', 'What it is for'),
        t('<p class="lead">Responde: <b>¿cómo va el negocio frente al presupuesto y frente al año anterior?, ¿de dónde viene el margen y dónde se va?</b> Muestra el estado de resultados de la empresa — de los ingresos a la utilidad neta — mes a mes, por trimestre o por año, con la comparación que se elija.</p>',
          '<p class="lead">It answers: <b>how is the business doing against the budget and against last year, where does the margin come from and where does it go?</b> It shows the company’s income statement — from revenue to net profit — by month, quarter or year, with the comparison you choose.</p>')
        + dos_tarjetas(t('Para el gerente o dueño', 'For the manager or owner'),
            [t('Cómo van los ingresos, el EBITDA y la utilidad neta frente a lo presupuestado.', 'How revenue, EBITDA and net profit compare with the budget.'),
             t('Una cascada que explica en qué se va cada peso de ingreso hasta la utilidad neta.', 'A waterfall that explains where each peso of revenue goes until it becomes net profit.'),
             t('Una lectura en tres líneas (logro, alerta, eficiencia) para entender el mes en segundos.', 'A three-line reading (achievement, alert, efficiency) to understand the month in seconds.')],
            t('Para el contador o analista financiero', 'For the accountant or financial analyst'),
            [t('El detalle completo por cuenta, con variaciones en pesos y en porcentaje.', 'The full detail by account, with variances in pesos and in percent.'),
             t('Período y acumulado del año lado a lado.', 'Period and year-to-date side by side.'),
             t('Exportación a Excel y PDF con las mismas cifras de la pantalla.', 'Export to Excel and PDF with the same figures as the screen.')]))))
    s.append(('pantalla', t('La pantalla', 'The screen'), seccion('pantalla', 2, t('La pantalla', 'The screen'),
        t('<p class="lead">Se elige la empresa, el escenario y un <b>rango de meses</b> (con los atajos de siempre: año en curso, últimos 6 meses, mes anterior, etc.). Luego se pulsa <b>Generar</b>; nada se consulta hasta entonces.</p>',
          '<p class="lead">Choose the company, the scenario and a <b>range of months</b> (with the usual shortcuts: current year, last 6 months, previous month, etc.). Then press <b>Generate</b>; nothing is queried until then.</p>')
        + '<ul>'
        + t('<li><b>Agrupar por:</b> mes, trimestre o año. Solo afecta a las tendencias; la tabla siempre muestra el período elegido y el acumulado del año.</li>',
            '<li><b>Group by:</b> month, quarter or year. It only affects the trends; the table always shows the chosen period and the year-to-date.</li>')
        + t('<li><b>Comparar contra:</b> el presupuesto (lo predeterminado), el presupuesto con ajuste o el forecast. Además se muestra siempre la variación contra el mismo período del año anterior (Var. A/A).</li>',
            '<li><b>Compare against:</b> the budget (default), the adjusted budget or the forecast. The variance against the same period of the previous year (YoY) is always shown as well.</li>')
        + t('<li><b>Nivel de detalle:</b> todas las cuentas o solo los subtotales.</li><li><b>Unidad:</b> pesos, miles o millones.</li>',
            '<li><b>Level of detail:</b> all accounts or subtotals only.</li><li><b>Unit:</b> pesos, thousands or millions.</li>')
        + t('<li><b>Comparar con otro escenario</b> (opcional): agrega las cifras de un segundo escenario.</li>',
            '<li><b>Compare with another scenario</b> (optional): adds the figures of a second scenario.</li>')
        + bloque_estilos(L) + '</ul>'
        + tabla([t('Bloque', 'Block'), t('Qué muestra', 'What it shows')], [
            [t('<b>Indicadores clave</b>', '<b>Key indicators</b>'), t('Ingresos y tres márgenes (bruto, EBITDA y neto), cada uno contra el presupuesto y contra el año anterior. Un conmutador permite ver el <b>Período</b>, el <b>Acumulado</b> del año o <b>Ambos</b>.', 'Revenue and three margins (gross, EBITDA and net), each against the budget and against last year. A switch lets you see the <b>Period</b>, the <b>Year-to-date</b> or <b>Both</b>.')],
            [t('<b>De ingresos a utilidad neta</b>', '<b>From revenue to net profit</b>'), t('La cascada: ingresos, costo de ventas, gastos operacionales, otros e impuestos y utilidad neta.', 'The waterfall: revenue, cost of sales, operating expenses, other items and taxes, and net profit.')],
            [t('<b>Tendencias</b>', '<b>Trends</b>'), t('La evolución de los principales conceptos en el rango, agrupada como se haya elegido.', 'The evolution of the main items over the range, grouped as chosen.')],
            [t('<b>Estado de resultados</b>', '<b>Income statement</b>'), t('La tabla completa: Real, presupuesto, variación en pesos, variación porcentual, margen sobre ingresos y variación contra el año anterior; en bloques de <b>Período</b> y <b>Acumulado del año</b>.', 'The full table: Actual, budget, variance in pesos, variance in percent, margin over revenue and variance against last year; in <b>Period</b> and <b>Year-to-date</b> blocks.')],
            [t('<b>Desglose por línea de negocio</b>', '<b>Breakdown by business line</b>'), t('Solo si la empresa maneja líneas de negocio: el mismo período repartido por línea.', 'Only if the company has business lines: the same period split by line.')],
            [t('<b>Lo más relevante</b> e <b>insights con IA</b>', '<b>Key takeaways</b> and <b>AI insights</b>'), t('Las variaciones de mayor impacto y la lectura en tres líneas.', 'The variances with the biggest impact and the three-line reading.')],
            [t('<b>Comentarios de gestión</b>', '<b>Management comments</b>'), t('Notas del equipo sobre el cierre (año y mes); las ven y escriben todos los usuarios con acceso a la empresa.', 'Team notes about the close (year and month); every user with access to the company can read and write them.')],
        ])
        + nota(L, '<b>Información modelo Bufins.</b> La cápsula del título recuerda que estos datos solo existen después de ejecutar el modelo.', '<b>Bufins model information.</b> The capsule in the title is a reminder that this data only exists after the model has been run.', 'info'))))
    s.append(('datos', t('De dónde salen las cifras', 'Where the figures come from'), seccion('datos', 3, t('De dónde salen las cifras', 'Where the figures come from'),
        t('<p class="lead">De la tabla del modelo <code>ModeloPYG</code>, que se llena al ejecutar el modelo de resultados en <b>Datos → Ejecución de modelos</b>. El informe <b>no ejecuta el modelo</b>: solo hay datos después de ejecutarlo.</p>',
          '<p class="lead">From the model table <code>ModeloPYG</code>, which is filled when the results model is run in <b>Data → Model execution</b>. The report <b>does not run the model</b>: there is data only after it has been run.</p>')
        + '<ul>'
        + t('<li>Cada línea del estado de resultados se une con su clasificación (<code>Rel_PYG</code>): las cuentas que suman y los <b>subtotales</b> que el modelo ya calcula (utilidad bruta, EBITDA, utilidad neta…). El informe no inventa subtotales: usa los del modelo, por eso siempre cuadran con él.</li>',
            '<li>Every line of the income statement is matched with its classification (<code>Rel_PYG</code>): the accounts that add up and the <b>subtotals</b> the model already computes (gross profit, EBITDA, net profit…). The report does not invent subtotals: it uses the model’s, so they always match it.</li>')
        + t('<li>Las columnas de comparación vienen del mismo modelo: <b>presupuesto</b>, <b>presupuesto con ajuste</b> y <b>forecast</b>, además del valor real.</li>',
            '<li>The comparison columns come from the same model: <b>budget</b>, <b>adjusted budget</b> and <b>forecast</b>, in addition to the actual value.</li>')
        + t('<li>Cada escenario es una copia paralela de los datos; el informe lee el que se elija.</li></ul>',
            '<li>Each scenario is a parallel copy of the data; the report reads the one you choose.</li></ul>'))))
    s.append(('calculo', t('Cómo se calcula', 'How it is calculated'), seccion('calculo', 4, t('Cómo se calcula', 'How it is calculated'),
        tabla([t('Concepto', 'Item'), t('Cómo se obtiene', 'How it is obtained')], [
            [t('Período', 'Period'), t('La suma de los meses del rango elegido.', 'The sum of the months in the chosen range.')],
            [t('Acumulado del año', 'Year-to-date'), t('La suma de enero al último mes del rango, dentro del mismo año.', 'The sum from January to the last month of the range, within the same year.')],
            [t('Var. $', 'Var. $'), t('Real − comparativo (presupuesto, ajuste o forecast).', 'Actual − comparison (budget, adjusted budget or forecast).')],
            [t('Var. %', 'Var. %'), t('Var. $ ÷ comparativo. Si el comparativo es cero no hay porcentaje.', 'Var. $ ÷ comparison. If the comparison is zero there is no percentage.')],
            [t('Margen %', 'Margin %'), t('Cada línea dividida por los ingresos del mismo período.', 'Each line divided by the revenue of the same period.')],
            [t('Var. A/A', 'YoY var.'), t('Valor actual contra el mismo período del año anterior. Si ese año no tiene datos cargados, la columna queda vacía y se avisa con una nota.', 'Current value against the same period of the previous year. If that year has no loaded data, the column stays empty and a note says so.')],
            [t('Cascada', 'Waterfall'), t('Cinco barras: ingresos → costo de ventas → gastos operacionales → otros e impuestos → utilidad neta. Cada tramo es la diferencia entre dos subtotales del modelo, así que <b>siempre cuadra</b> con la utilidad neta.', 'Five bars: revenue → cost of sales → operating expenses → other items and taxes → net profit. Each step is the difference between two model subtotals, so it <b>always adds up</b> to net profit.')],
            [t('Tendencias agrupadas', 'Grouped trends'), t('Con trimestre o año, cada punto es la suma de sus meses.', 'With quarter or year, each point is the sum of its months.')],
        ]))))
    s.append(('ia', t('Lectura con IA', 'AI reading'), seccion('ia', 5, t('Lectura con IA', 'AI reading'),
        bloque_ia(L, '<b>Logro</b> (lo mejor del período), <b>Alerta</b> (lo que más se aleja del presupuesto) y <b>Eficiencia</b> (qué pasa con costos y márgenes)',
                  '<b>Achievement</b> (the best of the period), <b>Alert</b> (what moves furthest from the budget) and <b>Efficiency</b> (what happens with costs and margins)',
                  'únicamente el resumen ya calculado del período, sus comparativos y los márgenes',
                  'only the already-calculated summary of the period, its comparisons and the margins'))))
    s.append(('exportar', t('Exportar', 'Export'), seccion('exportar', 6, t('Exportar', 'Export'),
        bloque_exportar(L,
            'hoja con el estado de resultados tal como se ve en pantalla (las columnas de porcentaje con formato de porcentaje) y el logo de la empresa.',
            'a sheet with the income statement as shown on screen (percent columns formatted as percentages) and the company logo.',
            'resumen ejecutivo, cascada y tendencias, y la tabla con el mismo nivel de detalle de la pantalla; pie con empresa, período y la leyenda de confidencialidad.',
            'executive summary, waterfall and trends, and the table with the same level of detail as the screen; footer with company, period and the confidentiality notice.'))))
    s.append(('faq', t('Preguntas frecuentes', 'FAQ'), '<section id="faq"><h2>7. ' + t('Preguntas frecuentes', 'Frequently asked questions') + '</h2>' + faq([
        (t('Dice que no hay datos, ¿qué hago?', 'It says there is no data, what do I do?'), t('Ejecute el modelo de resultados de la empresa en Datos → Ejecución de modelos y vuelva a generar el informe. Revise también que el escenario y el rango tengan información.', 'Run the company’s results model in Data → Model execution and generate the report again. Also check that the scenario and the range have data.')),
        (t('¿Por qué la columna Var. A/A está vacía?', 'Why is the YoY column empty?'), t('Porque el año anterior no tiene datos cargados para esa empresa y escenario. El informe lo avisa con una nota.', 'Because the previous year has no loaded data for that company and scenario. The report shows a note.')),
        (t('¿Por qué no tengo porcentaje de variación en una línea?', 'Why is there no variance percentage on a line?'), t('Porque el comparativo de esa línea es cero: dividir por cero no tiene sentido.', 'Because the comparison for that line is zero: dividing by zero makes no sense.')),
        (t('¿Cuál es la diferencia entre Período y Acumulado?', 'What is the difference between Period and Year-to-date?'), t('Período es el rango elegido; Acumulado es de enero hasta el último mes del rango, en el mismo año.', 'Period is the chosen range; Year-to-date runs from January to the last month of the range, in the same year.')),
        (t('¿Puedo comparar contra otro escenario?', 'Can I compare against another scenario?'), t('Sí, en «Comparar con otro escenario». Esa opción solo se habilita si el otro escenario tiene datos.', 'Yes, in “Compare with another scenario”. That option is enabled only if the other scenario has data.')),
        (t('¿Quién puede escribir comentarios?', 'Who can write comments?'), t('Todos los usuarios con acceso a la empresa; editar o eliminar un comentario solo puede hacerlo su autor o un administrador.', 'Every user with access to the company; only the author or an administrator can edit or delete a comment.')),
    ]) + '</section>'))
    return (t('Estado de resultados (PYG)', 'Income statement (P&L)'),
            t('Qué es, de dónde salen las cifras, cómo se calculan los márgenes y el EBITDA, cómo se compara contra el presupuesto y cómo leer la cascada.',
              'What it is, where the figures come from, how margins and EBITDA are calculated, how it compares with the budget and how to read the waterfall.'), s)


# ======================================================================================================================
# Balance general
# ======================================================================================================================
def doc_balance(L):
    t = L.t
    s = []
    s.append(('para', t('Para qué sirve', 'What it is for'), seccion('para', 1, t('Para qué sirve', 'What it is for'),
        t('<p class="lead">Responde: <b>¿qué tiene y qué debe la empresa a una fecha?, ¿está sana su estructura financiera?</b> Muestra el balance general (activos, pasivos y patrimonio) al mes de corte que se elija, comparado con otra fecha de referencia.</p>',
          '<p class="lead">It answers: <b>what does the company own and owe at a given date, is its financial structure healthy?</b> It shows the balance sheet (assets, liabilities and equity) at the cut-off month you choose, compared with another reference date.</p>')
        + nota(L, '<b>El balance es una foto, no un flujo.</b> Cada valor es el saldo al mes de corte; nunca se suman meses. El resto del rango solo alimenta las tendencias.', '<b>The balance sheet is a snapshot, not a flow.</b> Each value is the balance at the cut-off month; months are never added up. The rest of the range only feeds the trends.', 'info')
        + dos_tarjetas(t('Para el gerente o dueño', 'For the manager or owner'),
            [t('Cuánto tiene la empresa en activos, cuánto debe y cuánto es patrimonio.', 'How much the company has in assets, how much it owes and how much is equity.'),
             t('Si la liquidez y el endeudamiento mejoran o empeoran frente al cierre del año anterior.', 'Whether liquidity and leverage improve or worsen against last year’s close.')],
            t('Para el contador o analista financiero', 'For the accountant or financial analyst'),
            [t('Cada cuenta con su saldo, su peso sobre el activo total y su variación.', 'Each account with its balance, its weight over total assets and its variance.'),
             t('La comprobación de que Activo = Pasivo + Patrimonio.', 'The check that Assets = Liabilities + Equity.')]))))
    s.append(('pantalla', t('La pantalla', 'The screen'), seccion('pantalla', 2, t('La pantalla', 'The screen'),
        t('<p class="lead">Se elige la empresa, el escenario y el rango de meses; el <b>mes de corte</b> es el extremo derecho del rango. Luego se pulsa <b>Generar</b>.</p>',
          '<p class="lead">Choose the company, the scenario and the range of months; the <b>cut-off month</b> is the right end of the range. Then press <b>Generate</b>.</p>')
        + '<ul>'
        + t('<li><b>Comparar contra:</b> el cierre del año anterior (31 de diciembre; lo predeterminado, es el estándar contable), el mes anterior, el mismo mes del año anterior, el <b>presupuesto</b> o la <b>proyección</b> del mismo mes de corte.</li>',
            '<li><b>Compare against:</b> the previous year-end (December 31; the default, the accounting standard), the previous month, the same month of the previous year, the <b>budget</b> or the <b>projection</b> for the same cut-off month.</li>')
        + t('<li><b>Agrupar por</b> (solo tendencias) y <b>Unidad</b> (pesos, miles o millones).</li>', '<li><b>Group by</b> (trends only) and <b>Unit</b> (pesos, thousands or millions).</li>')
        + bloque_estilos(L) + '</ul>'
        + tabla([t('Bloque', 'Block'), t('Qué muestra', 'What it shows')], [
            [t('<b>Indicadores clave</b>', '<b>Key indicators</b>'), t('Activo total, capital de trabajo, razón corriente y endeudamiento total, cada uno contra el comparativo.', 'Total assets, working capital, current ratio and total leverage, each against the comparison.')],
            [t('<b>Cuadre</b>', '<b>Balance check</b>'), t('Una etiqueta indica si «Activo = Pasivo + Patrimonio» o, si no, el valor del descuadre.', 'A badge says whether “Assets = Liabilities + Equity” or, if not, the amount of the mismatch.')],
            [t('<b>Estructura</b>', '<b>Structure</b>'), t('Dos barras apiladas: de qué está hecho el activo y cómo se financia (pasivo y patrimonio).', 'Two stacked bars: what the assets are made of and how they are financed (liabilities and equity).')],
            [t('<b>Balance general</b>', '<b>Balance sheet</b>'), t('La tabla: saldo al corte, <b>% vertical</b> (sobre el activo total), comparativo, variación en pesos y en porcentaje.', 'The table: balance at cut-off, <b>vertical %</b> (over total assets), comparison, variance in pesos and in percent.')],
            [t('<b>Tendencias</b>', '<b>Trends</b>'), t('Capital de trabajo, razón corriente y endeudamiento a lo largo del rango.', 'Working capital, current ratio and leverage over the range.')],
            [t('<b>Lo más relevante</b>', '<b>Key takeaways</b>'), t('Las cuatro variaciones de mayor impacto, con su color según sean favorables o no.', 'The four variances with the biggest impact, colored by whether they are favorable or not.')],
            [t('<b>Comentarios de gestión</b>', '<b>Management comments</b>'), t('Notas del equipo sobre el cierre.', 'Team notes about the close.')],
        ]))))
    s.append(('datos', t('De dónde salen las cifras', 'Where the figures come from'), seccion('datos', 3, t('De dónde salen las cifras', 'Where the figures come from'),
        t('<p class="lead">De la tabla del modelo <code>ModeloBalance</code> (y, para comparar contra el presupuesto o la proyección, de <code>ModeloBalancePpto</code>). El informe <b>no ejecuta el modelo</b>: solo hay datos después de ejecutarlo en Datos → Ejecución de modelos.</p>',
          '<p class="lead">From the model table <code>ModeloBalance</code> (and, to compare against the budget or the projection, from <code>ModeloBalancePpto</code>). The report <b>does not run the model</b>: there is data only after it has been run in Data → Model execution.</p>')
        + '<ul>'
        + t('<li>Cada línea se une con su clasificación (<code>REL_Balance</code>). Los totales (activo corriente, pasivo, patrimonio…) son los que calcula el modelo.</li>', '<li>Each line is matched with its classification (<code>REL_Balance</code>). The totals (current assets, liabilities, equity…) are the ones the model computes.</li>')
        + t('<li>Al comparar contra el presupuesto o la proyección, las líneas se cruzan por su descripción (no por su número de orden, que cambia de un mes a otro).</li>', '<li>When comparing against the budget or the projection, lines are matched by their description (not by their order number, which changes from one month to another).</li>')
        + t('<li>Si el comparativo no tiene datos, el saldo se muestra sin variación («—»).</li></ul>', '<li>If the comparison has no data, the balance is shown without variance (“—”).</li></ul>'))))
    s.append(('calculo', t('Cómo se calcula', 'How it is calculated'), seccion('calculo', 4, t('Cómo se calcula', 'How it is calculated'),
        tabla([t('Concepto', 'Item'), t('Cómo se obtiene', 'How it is obtained')], [
            [t('% vertical', 'Vertical %'), t('Saldo de la línea ÷ activo total del mismo corte.', 'Line balance ÷ total assets at the same cut-off.')],
            [t('Capital de trabajo', 'Working capital'), t('Activo corriente − pasivo corriente.', 'Current assets − current liabilities.')],
            [t('Razón corriente', 'Current ratio'), t('Activo corriente ÷ pasivo corriente, mostrada como «1,8x». «No aplica» si el pasivo corriente es cero.', 'Current assets ÷ current liabilities, shown as “1.8x”. “Not applicable” if current liabilities are zero.')],
            [t('Endeudamiento total', 'Total leverage'), t('Pasivo total ÷ activo total. «No aplica» si el activo es cero.', 'Total liabilities ÷ total assets. “Not applicable” if assets are zero.')],
            [t('Cuadre', 'Balance check'), t('Activo − (Pasivo + Patrimonio). Se tolera una diferencia de hasta 10 pesos por redondeos del modelo.', 'Assets − (Liabilities + Equity). A difference of up to 10 pesos is tolerated for model rounding.')],
            [t('Color de las variaciones', 'Variance colors'), t('En activo y patrimonio, subir es favorable; en pasivo, subir es desfavorable. El indicador de endeudamiento sigue la misma lógica.', 'In assets and equity, going up is favorable; in liabilities, going up is unfavorable. The leverage indicator follows the same logic.')],
            [t('Tendencias agrupadas', 'Grouped trends'), t('Con trimestre o año, cada punto es la foto del <b>último mes con datos</b> del grupo (no una suma).', 'With quarter or year, each point is the snapshot of the group’s <b>last month with data</b> (not a sum).')],
        ]) + nota(L, 'Hay un segundo control opcional: la utilidad neta acumulada del estado de resultados contra la línea de resultado del ejercicio del balance. Si el balance no trae esa línea, el control se omite sin afectar nada más.', 'There is an optional second check: the year-to-date net profit of the income statement against the “profit for the year” line of the balance sheet. If the balance sheet has no such line, the check is skipped without affecting anything else.'))))
    s.append(('ia', t('Lectura con IA', 'AI reading'), seccion('ia', 5, t('Lectura con IA', 'AI reading'),
        bloque_ia(L, '<b>Liquidez</b>, <b>Solvencia</b> y <b>Capital de trabajo</b>', '<b>Liquidity</b>, <b>Solvency</b> and <b>Working capital</b>',
                  'únicamente el resumen ya calculado del balance al corte, sus comparativos y los indicadores',
                  'only the already-calculated summary of the balance sheet at cut-off, its comparisons and the indicators'))))
    s.append(('exportar', t('Exportar', 'Export'), seccion('exportar', 6, t('Exportar', 'Export'),
        bloque_exportar(L,
            'el balance tal como se ve en pantalla, con el logo de la empresa.',
            'the balance sheet as shown on screen, with the company logo.',
            'resumen con los indicadores y la lectura con IA, la estructura y la tabla del balance; pie con empresa, corte y la leyenda de confidencialidad.',
            'summary with the indicators and the AI reading, the structure and the balance sheet table; footer with company, cut-off and the confidentiality notice.'))))
    s.append(('faq', t('Preguntas frecuentes', 'FAQ'), '<section id="faq"><h2>7. ' + t('Preguntas frecuentes', 'Frequently asked questions') + '</h2>' + faq([
        (t('¿Por qué no hay «Período» y «Acumulado» como en el estado de resultados?', 'Why are there no “Period” and “Year-to-date” as in the income statement?'), t('Porque el balance es una foto al corte: sumar saldos de varios meses no tendría sentido.', 'Because the balance sheet is a snapshot at cut-off: adding balances of several months would make no sense.')),
        (t('Sale «Descuadre de $X», ¿qué significa?', 'It says “Mismatch of $X”, what does it mean?'), t('Que Activo no es igual a Pasivo + Patrimonio con la tolerancia de 10 pesos. Revise que el modelo se haya ejecutado completo para ese escenario y esa carga de datos.', 'That Assets are not equal to Liabilities + Equity within the 10-peso tolerance. Check that the model was run completely for that scenario and data load.')),
        (t('¿Por qué la razón corriente o el endeudamiento dicen «No aplica»?', 'Why do the current ratio or leverage say “Not applicable”?'), t('Porque el denominador es cero (pasivo corriente o activo total).', 'Because the denominator is zero (current liabilities or total assets).')),
        (t('¿Con qué se compara por defecto?', 'What is it compared with by default?'), t('Con el cierre del año anterior (31 de diciembre), que es la referencia habitual de las normas contables. Puede cambiarla en «Comparar contra».', 'With the previous year-end (December 31), the usual reference in accounting standards. You can change it in “Compare against”.')),
        (t('¿Por qué una variación de pasivo sale en rojo si subió?', 'Why is a liability variance red when it went up?'), t('Porque más pasivo es desfavorable; en activos y patrimonio, subir se pinta como favorable.', 'Because more liabilities is unfavorable; in assets and equity, going up is shown as favorable.')),
        (t('¿Puedo comparar contra el presupuesto?', 'Can I compare against the budget?'), t('Sí: «Comparar contra → Presupuesto» o «Proyección» usa el balance presupuestado o proyectado del mismo mes de corte.', 'Yes: “Compare against → Budget” or “Projection” uses the budgeted or projected balance sheet for the same cut-off month.')),
    ]) + '</section>'))
    return (t('Balance general', 'Balance sheet'),
            t('Qué es, por qué es una foto al mes de corte, de dónde salen las cifras, cómo se calculan los indicadores y cómo se comprueba que el balance cuadra.',
              'What it is, why it is a snapshot at the cut-off month, where the figures come from, how the indicators are calculated and how the balance check works.'), s)
