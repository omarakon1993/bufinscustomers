# -*- coding: utf-8 -*-
from doc_lib import *


# ======================================================================================================================
# Línea de tiempo dinámica
# ======================================================================================================================
def doc_linea_tiempo(L):
    t = L.t
    s = []
    s.append(('para', t('Para qué sirve', 'What it is for'), seccion('para', 1, t('Para qué sirve', 'What it is for'),
        t('<p class="lead">Responde: <b>¿cómo ha evolucionado esta cifra mes a mes?, ¿cómo se compara con otro escenario?</b> Es el informe más flexible: se elige <b>cualquier variable</b> de las tablas de resultados de los modelos y se ve su evolución en el tiempo, sin armar nada a mano.</p>',
          '<p class="lead">It answers: <b>how has this figure evolved month by month, how does it compare with another scenario?</b> It is the most flexible report: you pick <b>any variable</b> from the model result tables and see its evolution over time, without building anything by hand.</p>')
        + dos_tarjetas(t('Para el gerente o dueño', 'For the manager or owner'),
            [t('Seguir una cifra concreta (ventas, utilidad, caja…) y ver si va mejorando.', 'Follow a specific figure (sales, profit, cash…) and see whether it is improving.'),
             t('Poner hasta ocho variables en la misma gráfica para compararlas.', 'Put up to eight variables on the same chart to compare them.')],
            t('Para el contador o analista financiero', 'For the accountant or financial analyst'),
            [t('Comparar el Escenario 1 contra el Escenario 2 de una misma variable.', 'Compare Scenario 1 against Scenario 2 for the same variable.'),
             t('Exportar la serie completa a Excel con un gráfico editable.', 'Export the full series to Excel with an editable chart.')]))))
    s.append(('pantalla', t('La pantalla', 'The screen'), seccion('pantalla', 2, t('La pantalla', 'The screen'),
        t('<p class="lead">Los filtros están en dos filas: <b>Qué ver</b> y <b>Rango y agrupación</b>. Después se pulsa <b>Generar informe</b>; hasta entonces no se consulta nada.</p>',
          '<p class="lead">The filters are in two rows: <b>What to see</b> and <b>Range and grouping</b>. Then press <b>Generate report</b>; nothing is queried until then.</p>')
        + tabla([t('Filtro', 'Filter'), t('Qué hace', 'What it does')], [
            [t('<b>Empresa</b> y <b>Escenario</b>', '<b>Company</b> and <b>Scenario</b>'), t('De qué empresa y de cuál copia de datos se leen las cifras.', 'Which company and which copy of the data the figures are read from.')],
            [t('<b>Tabla a analizar</b>', '<b>Table to analyze</b>'), t('La tabla de resultados del modelo (estado de resultados, balance, flujo de caja, líneas de negocio, etc.).', 'The model result table (income statement, balance sheet, cash flow, business lines, etc.).')],
            [t('<b>Descripción / Indicador(es)</b>', '<b>Description / Indicator(s)</b>'), t('Abre un selector con buscador. Se pueden elegir <b>hasta 8 variables</b> y guardar <b>combinaciones</b> que se usan a menudo.', 'Opens a selector with search. You can choose <b>up to 8 variables</b> and save <b>combinations</b> you use often.')],
            [t('<b>Campo</b>', '<b>Field</b>'), t('Qué columna de la tabla se grafica (por ejemplo, el valor real o el presupuestado).', 'Which column of the table is plotted (for example, the actual or the budgeted value).')],
            [t('<b>Agrupar por</b>', '<b>Group by</b>'), t('Mes (un punto por mes), trimestre (suma T1–T4) o año (suma por año).', 'Month (one point per month), quarter (sum Q1–Q4) or year (sum per year).')],
            [t('<b>Rango de fechas</b>', '<b>Date range</b>'), t('Se arrastran los extremos del control, o se usan los atajos (año en curso, últimos 6 meses, mes anterior…). Si ambos extremos coinciden se muestra un solo mes.', 'Drag the ends of the control, or use the shortcuts (current year, last 6 months, previous month…). If both ends coincide a single month is shown.')],
            [t('<b>Comparar con otro escenario</b> (opcional)', '<b>Compare with another scenario</b> (optional)'), t('Agrega la misma variable del otro escenario. Solo está disponible cuando se elige <b>una única variable</b>.', 'Adds the same variable from the other scenario. It is available only when <b>a single variable</b> is chosen.')],
        ]) + '<ul>' + bloque_estilos(L) + '</ul>'
        + '<p class="lead" style="margin-top:12px">' + t('El resultado trae:', 'The result includes:') + '</p>'
        + tabla([t('Bloque', 'Block'), t('Qué muestra', 'What it shows')], [
            [t('<b>Indicadores</b>', '<b>Indicators</b>'), t('Variación del último período, promedio del rango y cuántos períodos incluye.', 'Variation of the last period, average of the range and how many periods it includes.')],
            [t('<b>Gráfico</b>', '<b>Chart</b>'), t('En <b>líneas</b> o <b>barras</b>, con etiquetas de valor opcionales; cada serie se puede ocultar o mostrar desde la leyenda.', 'As <b>lines</b> or <b>bars</b>, with optional value labels; each series can be hidden or shown from the legend.')],
            [t('<b>Tabla de datos</b>', '<b>Data table</b>'), t('Período, valor, comparación y variación de la serie.', 'Period, value, comparison and variation of the series.')],
        ])
        + nota(L, '<b>Información modelo Bufins.</b> Estos datos solo existen después de ejecutar los modelos en Datos → Ejecución de modelos.', '<b>Bufins model information.</b> This data only exists after the models have been run in Data → Model execution.', 'info'))))
    s.append(('datos', t('De dónde salen las cifras', 'Where the figures come from'), seccion('datos', 3, t('De dónde salen las cifras', 'Where the figures come from'),
        t('<p class="lead">De las tablas <code>Modelo*</code> que llena la ejecución de modelos (resultados, balance, balance presupuestado, flujo de caja, flujo de efectivo, líneas de negocio y tesorería). Es la <b>única fuente</b>: el informe nunca lee los datos de cargue originales.</p>',
          '<p class="lead">From the <code>Model*</code> tables filled by the model run (results, balance sheet, budgeted balance sheet, cash flow, statement of cash flows, business lines and treasury). It is the <b>only source</b>: the report never reads the original uploaded data.</p>')
        + '<ul>' + t('<li>Solo aparecen las tablas, variables y meses que ya tienen datos para la empresa y el escenario elegidos.</li><li>Si la tabla no tiene datos, el informe lo dice («Esta tabla todavía no tiene datos cargados en el modelo para esta empresa y escenario»).</li></ul>',
                    '<li>Only the tables, variables and months that already have data for the chosen company and scenario appear.</li><li>If the table has no data, the report says so (“This table has no data loaded in the model for this company and scenario yet”).</li></ul>'))))
    s.append(('calculo', t('Cómo se calcula', 'How it is calculated'), seccion('calculo', 4, t('Cómo se calcula', 'How it is calculated'),
        tabla([t('Concepto', 'Item'), t('Cómo se obtiene', 'How it is obtained')], [
            [t('Valor de un período', 'Value of a period'), t('Con «Mes», el valor del mes. Con «Trimestre» o «Año», la <b>suma</b> de los meses del grupo.', 'With “Month”, the month’s value. With “Quarter” or “Year”, the <b>sum</b> of the group’s months.')],
            [t('Variación del último período', 'Variation of the last period'), t('Último valor contra el valor del período anterior, en porcentaje.', 'Last value against the previous period’s value, in percent.')],
            [t('Promedio del rango', 'Average of the range'), t('El promedio de los valores de todos los períodos incluidos.', 'The average of the values of all the periods included.')],
            [t('Comparación', 'Comparison'), t('La misma variable del otro escenario, período a período, y su diferencia contra la serie principal.', 'The same variable from the other scenario, period by period, and its difference from the main series.')],
        ]) + nota(L, 'Una variable que es un <b>saldo</b> (como las del balance) no tiene sentido sumarla entre meses: para esas cifras conviene agrupar por «Mes» o mirar un solo corte.', 'A variable that is a <b>balance</b> (like those of the balance sheet) makes no sense when added across months: for those figures it is better to group by “Month” or look at a single cut-off.'))))
    s.append(('exportar', t('Exportar', 'Export'), seccion('exportar', 5, t('Exportar', 'Export'),
        bloque_exportar(L,
            'la serie que se está viendo, con un <b>gráfico nativo editable</b> (si se cambia un valor, el gráfico se actualiza) y las mismas opciones de la pantalla (tipo de gráfico, unidad, series ocultas).',
            'the series being viewed, with an <b>editable native chart</b> (if a value changes, the chart updates) and the same options as on screen (chart type, unit, hidden series).',
            'se pregunta qué incluir: <b>solo el gráfico</b> (con los indicadores clave) o <b>gráfico con datos</b> (además la tabla completa de la serie).',
            'you are asked what to include: <b>chart only</b> (with the key indicators) or <b>chart with data</b> (plus the full table of the series).'))))
    s.append(('faq', t('Preguntas frecuentes', 'FAQ'), '<section id="faq"><h2>6. ' + t('Preguntas frecuentes', 'Frequently asked questions') + '</h2>' + faq([
        (t('No me deja comparar con otro escenario', 'It will not let me compare with another scenario'), t('La comparación solo está disponible cuando hay <b>una sola variable</b> elegida, y el otro escenario debe tener datos.', 'The comparison is available only when <b>a single variable</b> is chosen, and the other scenario must have data.')),
        (t('¿Por qué no aparece la variable que busco?', 'Why doesn’t the variable I am looking for appear?'), t('Porque esa tabla no tiene datos para la empresa y el escenario elegidos, o el modelo no se ha ejecutado. Ejecútelo en Datos → Ejecución de modelos.', 'Because that table has no data for the chosen company and scenario, or the model has not been run. Run it in Data → Model execution.')),
        (t('¿Cuántas variables puedo graficar a la vez?', 'How many variables can I plot at once?'), t('Hasta ocho en la misma gráfica. Las combinaciones que use a menudo se pueden guardar.', 'Up to eight on the same chart. Combinations you use often can be saved.')),
        (t('¿Qué pasa si agrupo por trimestre o año?', 'What happens if I group by quarter or year?'), t('Cada punto es la suma de sus meses. Las cifras que son saldos conviene verlas por mes.', 'Each point is the sum of its months. Figures that are balances are best viewed by month.')),
        (t('¿Cómo cambio entre líneas y barras?', 'How do I switch between lines and bars?'), t('Con el selector del gráfico (Líneas / Barras); también hay un conmutador entre Gráfico y Tabla.', 'With the chart selector (Lines / Bars); there is also a switch between Chart and Table.')),
        (t('¿Qué incluye el PDF?', 'What does the PDF include?'), t('Al exportar se elige entre «Solo gráfico» y «Gráfico con datos».', 'When exporting you choose between “Chart only” and “Chart with data”.')),
    ]) + '</section>'))
    return (t('Línea de tiempo dinámica', 'Dynamic timeline'),
            t('Cómo elegir cualquier variable de los modelos, ver su evolución mes a mes, compararla entre escenarios y exportar el resultado.', 'How to pick any variable from the models, see its month-by-month evolution, compare it across scenarios and export the result.'), s)


# ======================================================================================================================
# Informe de modelos
# ======================================================================================================================
def doc_modelos(L):
    t = L.t
    s = []
    s.append(('para', t('Para qué sirve', 'What it is for'), seccion('para', 1, t('Para qué sirve', 'What it is for'),
        t('<p class="lead">Responde: <b>¿qué datos produjo la ejecución de modelos?</b> Es la consulta directa de las tablas de resultados (estado de resultados, balance, flujos, líneas de negocio, tesorería…): se elige la empresa y la tabla, se filtra y se descarga.</p>',
          '<p class="lead">It answers: <b>what data did the model run produce?</b> It is the direct query of the result tables (income statement, balance sheet, cash flows, business lines, treasury…): pick the company and the table, filter and download.</p>')
        + dos_tarjetas(t('Para el gerente o dueño', 'For the manager or owner'),
            [t('Ver los números exactos que alimentan los demás informes.', 'See the exact numbers that feed the other reports.'),
             t('Comprobar rápidamente que un modelo ya se ejecutó y qué contiene.', 'Quickly check that a model has already been run and what it contains.')],
            t('Para el contador o analista financiero', 'For the accountant or financial analyst'),
            [t('Filtrar por cualquier campo y exportar el resultado a Excel.', 'Filter by any field and export the result to Excel.'),
             t('Consultar varias tablas a la vez.', 'Query several tables at once.')]))))
    s.append(('pantalla', t('La pantalla', 'The screen'), seccion('pantalla', 2, t('La pantalla', 'The screen'),
        t('<p class="lead">Se elige la empresa, el escenario, el año (y, según la tabla, el mes) y <b>una o varias tablas</b>. El botón <b>Consultar</b> cambia de texto según lo elegido («Consultar 3 tablas seleccionadas», «Consultar todas las tablas»).</p>',
          '<p class="lead">Choose the company, the scenario, the year (and, depending on the table, the month) and <b>one or more tables</b>. The <b>Query</b> button changes its text according to your choice (“Query 3 selected tables”, “Query all tables”).</p>')
        + '<ul>'
        + t('<li><b>Varias tablas:</b> se consultan una tras otra y cada una aparece en su pestaña.</li>', '<li><b>Several tables:</b> they are queried one after another and each one appears in its own tab.</li>')
        + t('<li><b>Filtrar resultados:</b> sobre lo ya consultado se pueden agregar filtros por campo (igual a, contiene, empieza o termina con, mayor o menor que…) que se muestran como etiquetas y se pueden quitar una a una.</li>', '<li><b>Filter results:</b> on top of what was queried you can add field filters (equals, contains, starts or ends with, greater or less than…) shown as chips that can be removed one by one.</li>')
        + t('<li>Los números se muestran con separador de miles y decimales; los filtros numéricos comparan el valor real, no el texto formateado.</li>', '<li>Numbers are shown with thousands and decimal separators; numeric filters compare the real value, not the formatted text.</li>')
        + bloque_estilos(L) + '</ul>'
        + nota(L, '<b>Información modelo Bufins.</b> Estos datos solo existen después de ejecutar los modelos en Datos → Ejecución de modelos.', '<b>Bufins model information.</b> This data only exists after the models have been run in Data → Model execution.', 'info'))))
    s.append(('datos', t('De dónde salen las cifras', 'Where the figures come from'), seccion('datos', 3, t('De dónde salen las cifras', 'Where the figures come from'),
        t('<p class="lead">Únicamente de las tablas <code>Modelo*</code> (resultados, balance, balance presupuestado, diferencia del balance, flujo de caja, flujo de efectivo, líneas de negocio y tesorería). Es la regla de la plataforma: la información que se consulta aquí, y la que usa la IA, sale solo de los modelos, nunca de los datos de cargue originales.</p>',
          '<p class="lead">Only from the <code>Model*</code> tables (results, balance sheet, budgeted balance sheet, balance sheet difference, cash flow, statement of cash flows, business lines and treasury). This is a platform rule: the information queried here, and the one used by AI, comes only from the models, never from the original uploaded data.</p>')
        + '<ul>' + t('<li>Una tabla sin filas para la empresa y los filtros elegidos muestra «Sin datos».</li><li>Los usuarios solo consultan su empresa o las de su grupo empresarial.</li></ul>', '<li>A table without rows for the chosen company and filters shows “No data”.</li><li>Users can only query their own company or those of their business group.</li></ul>'))))
    s.append(('exportar', t('Exportar', 'Export'), seccion('exportar', 4, t('Exportar', 'Export'),
        t('<p class="lead">Botón <b>Exportar</b>, igual que en los demás informes, con el logo de la empresa y la paleta elegida:</p>'
          '<ul><li><b>Excel:</b> la tabla consultada, con los filtros aplicados.</li>'
          '<li><b>Enviar por correo:</b> a usted y/o a otras personas (hasta 10), con el Excel adjunto; el envío queda en la auditoría.</li></ul>'
          '<p class="note">Este informe es una grilla de datos, por eso no tiene salida en PDF ni imagen.</p>',
          '<p class="lead"><b>Export</b> button, the same as in the other reports, with the company logo and the chosen palette:</p>'
          '<ul><li><b>Excel:</b> the queried table, with the filters applied.</li>'
          '<li><b>Send by email:</b> to you and/or other people (up to 10), with the Excel attached; the sending is recorded in the audit log.</li></ul>'
          '<p class="note">This report is a data grid, so it has no PDF or image output.</p>'))))
    s.append(('faq', t('Preguntas frecuentes', 'FAQ'), '<section id="faq"><h2>5. ' + t('Preguntas frecuentes', 'Frequently asked questions') + '</h2>' + faq([
        (t('Sale «Sin datos», ¿qué hago?', 'It says “No data”, what do I do?'), t('Ejecute el modelo correspondiente en Datos → Ejecución de modelos para la empresa y el escenario elegidos, o amplíe los filtros.', 'Run the corresponding model in Data → Model execution for the chosen company and scenario, or widen the filters.')),
        (t('¿Puedo consultar varias tablas a la vez?', 'Can I query several tables at once?'), t('Sí: elija varias en el selector o «Todas las tablas».', 'Yes: choose several in the selector or “All tables”.')),
        (t('¿Por qué no puedo ver la empresa de otro cliente?', 'Why can’t I see another client’s company?'), t('Porque cada usuario solo accede a su empresa y a las de su grupo empresarial.', 'Because each user only has access to their own company and those of their business group.')),
        (t('¿El Excel incluye los filtros que apliqué?', 'Does the Excel include the filters I applied?'), t('Sí, exporta la tabla consultada con sus filtros.', 'Yes, it exports the queried table with its filters.')),
        (t('¿Qué diferencia hay con la Línea de tiempo?', 'What is the difference with the Timeline?'), t('El Informe de modelos muestra las filas tal cual; la Línea de tiempo grafica una variable a lo largo de los meses y permite compararla entre escenarios.', 'The Models report shows the rows as they are; the Timeline plots a variable over the months and lets you compare it across scenarios.')),
    ]) + '</section>'))
    return (t('Informe de modelos', 'Models report'),
            t('Cómo consultar, filtrar y exportar las tablas que genera la ejecución de modelos.', 'How to query, filter and export the tables produced by running the models.'), s)
