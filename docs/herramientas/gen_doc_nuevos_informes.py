# -*- coding: utf-8 -*-
"""
Genera la documentación HTML (solo lectura, con el estilo único de los documentos de BUFINS) de:
  - Informe_LineasNegocio.html      (Rentabilidad por línea de negocio)
  - Informe_GerenciaMensual.html    (Informe mensual de gerencia)
y ajusta los documentos ya existentes cuando cambian sus informes:
  - Informe_Indicadores.html        (umbrales propios, comparar con escenario, frente a su historia, comentarios)
  - Informe_HistPrecios_vs_Excel.html (estilos del informe / paleta de colores, comentarios no aplica)

Uso:   python docs/herramientas/gen_doc_nuevos_informes.py
Salida: App_Data/Documentacion/  y copia idéntica en docs/ (la tercera copia, en la carpeta de documentación técnica de
        Common, se hace a mano). El CSS se toma de Informe_Indicadores.html para que el estilo sea literalmente el mismo.
Los documentos nuevos no llevan capturas: hay que agregarlas (base64, <figure>) cuando se pueda correr la aplicación.
Es idempotente: los parches de los documentos existentes se omiten si ya están aplicados.
"""
import io, os, re, shutil

RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DOCS = os.path.join(RAIZ, 'App_Data', 'Documentacion')
COPIA = os.path.join(RAIZ, 'docs')


def leer(p):
    return io.open(p, encoding='utf-8').read()


def escribir(p, s):
    io.open(p, 'w', encoding='utf-8', newline='\n').write(s)


CSS = re.search(r'<style>(.*?)</style>', leer(os.path.join(DOCS, 'Informe_Indicadores.html')), re.S).group(1)


def pagina(titulo, descripcion, secciones):
    """secciones: lista de (id, nombre_corto, html_de_la_seccion)."""
    nav = ''.join('<a href="#%s">%s</a>' % (i, n) for i, n, _ in secciones)
    cuerpo = '\n\n'.join(h for _, _, h in secciones)
    return ('<!doctype html>\n<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">\n'
            '<title>Documentación: %s</title>\n<style>%s</style></head><body>\n'
            '<header><div class="w"><h1>%s</h1><p>%s</p></div></header>\n<nav><div class="w">%s</div></nav>\n<main class="w">\n\n%s\n\n</main>\n</body></html>\n'
            % (titulo, CSS, titulo, descripcion, nav, cuerpo))


def tabla(encabezados, filas):
    h = '<div class="tw"><table><tr>' + ''.join('<th>%s</th>' % e for e in encabezados) + '</tr>'
    for f in filas:
        h += '<tr>' + ''.join('<td>%s</td>' % c for c in f) + '</tr>'
    return h + '</table></div>'


def faq(items):
    return ''.join('<details><summary>%s</summary><p>%s</p></details>' % (q, a) for q, a in items)


# ======================================================================================================================
# Rentabilidad por línea de negocio
# ======================================================================================================================
def doc_lineas():
    s = []
    s.append(('para', 'Para qué sirve', '''<section id="para"><h2>1. Para qué sirve</h2>
<p class="lead">Responde: <b>¿qué líneas de negocio generan el margen y cuáles lo consumen?, ¿de cuál depende la empresa?</b> Toma el resultado que Bufins ya calcula por línea y lo muestra como un estado de resultados por línea, con la mezcla de ingresos, los márgenes y la comparación con el año anterior.</p>
<div class="g2"><div class="card"><b>Para el gerente o dueño</b><ul><li>Qué línea es la líder y cuánto pesa en los ingresos.</li><li>Qué líneas aportan a la utilidad operacional y cuáles no.</li><li>La lectura con IA: rentabilidad, concentración y acción recomendada.</li></ul></div>
<div class="card"><b>Para el contador o analista financiero</b><ul><li>El estado de resultados completo de cada línea, de ingresos a utilidad neta.</li><li>La validación contra el Estado de resultados (PYG).</li><li>Exportación a Excel, PDF y correo.</li></ul></div></div></section>'''))
    s.append(('pantalla', 'La pantalla', '''<section id="pantalla"><h2>2. La pantalla</h2>
<p class="lead">Se elige la empresa, el escenario y un <b>rango de meses</b> (con los atajos de siempre: año en curso, últimos 6 meses, etc.). Las cifras del rango se suman y se comparan con el mismo rango del año anterior. También se puede elegir la <b>paleta de colores</b> del informe y de sus exportaciones.</p>
''' + tabla(['Bloque', 'Qué muestra'], [
        ['<b>Indicadores clave</b>', 'Ingresos y utilidad operacional (con su variación contra el año anterior), margen operacional y la línea líder con su peso en los ingresos.'],
        ['<b>Mezcla de ingresos por línea</b>', 'Barras apiladas mes a mes: cuánto aporta cada línea al ingreso y cómo cambia la mezcla.'],
        ['<b>Aporte a la utilidad operacional</b>', 'Las líneas ordenadas por su utilidad operacional, con su margen operacional.'],
        ['<b>Márgenes por línea</b>', 'Margen bruto, operacional y neto de cada línea y del total.'],
        ['<b>Estado de resultados por línea</b>', 'Ingresos, costo de ventas, utilidad bruta, gastos operacionales, utilidad operacional, EBITDA, resultado no operacional, utilidad antes de impuestos, impuesto y utilidad neta, una columna por línea más el total, y los márgenes.'],
        ['<b>Resumen por línea</b>', 'Participación en ingresos y en la utilidad operacional, y variación contra el año anterior.'],
        ['<b>Comentarios de gestión</b>', 'Notas del equipo sobre el cierre; se puede indicar a qué línea se refiere cada una.'],
    ]) + '</section>'))
    s.append(('datos', 'De dónde salen las cifras', '''<section id="datos"><h2>3. De dónde salen las cifras</h2>
<p class="lead">De la tabla del modelo <code>ModeloLineasNegocio</code>, resultado del modelo de Líneas de Negocio. El informe no ejecuta el modelo: <b>solo hay datos después de ejecutarlo</b> en Ejecución de Modelos (por eso lleva la cápsula «Información modelo Bufins»).</p>
<ul><li><b>Las líneas son propias de cada empresa.</b> El modelo guarda cada cuenta con el nombre de la línea al final (por ejemplo «Ingresos Exp» pertenece a la línea «Exp»); el informe descubre las líneas solo, sin listas fijas.</li>
<li>Cada línea trae los mismos 16 conceptos: ingresos, otros ingresos y financieros, gastos de administración y de ventas, depreciación y amortización (de costo y de gasto), provisiones, otros gastos, gastos financieros, diferencia en cambio, impuesto de renta y costo de ventas.</li>
<li>Solo se muestran las líneas con movimiento en el rango elegido.</li></ul></section>'''))
    s.append(('calculo', 'Cómo se calcula', '''<section id="calculo"><h2>4. Cómo se calcula</h2>
''' + tabla(['Concepto', 'Cómo se obtiene'], [
        ['Costo de ventas', 'Costo de ventas (CMV) + depreciación del costo + amortización del costo.'],
        ['Utilidad bruta', 'Ingresos − costo de ventas.'],
        ['Gastos operacionales', 'Administración + ventas + depreciación y amortización del gasto + provisiones de cartera e inventarios.'],
        ['Utilidad operacional', 'Utilidad bruta − gastos operacionales.'],
        ['EBITDA', 'Utilidad operacional + las cuatro depreciaciones y amortizaciones (costo y gasto). El modelo no trae una línea EBITDA.'],
        ['Resultado no operacional', 'Otros ingresos + ingresos financieros − otros gastos − gastos financieros − diferencia en cambio.'],
        ['Utilidad neta', 'Utilidad operacional + resultado no operacional − impuesto de renta.'],
        ['Márgenes', 'Cada utilidad dividida por los ingresos de la misma línea (sin ingresos no hay margen).'],
        ['Comparación', 'El mismo rango de meses del año anterior; si ese año no tiene datos de líneas no se muestran variaciones.'],
    ]) + '<p class="note" style="margin-top:12px"><b>Costos y gastos</b> vienen del modelo como valores positivos y el informe los resta; no se interpretan como signos negativos.</p></section>'))
    s.append(('cuadre', 'Cuadre con el PYG', '''<section id="cuadre"><h2>5. Cuadre con el Estado de resultados</h2>
<p class="lead">Una franja verde indica que, para el mismo rango, <b>los ingresos y la utilidad neta sumados de todas las líneas coinciden con el Estado de resultados (PYG)</b> con una tolerancia de un peso. Si no coinciden, la franja se pone en rojo y muestra la diferencia: es una señal para revisar cómo se asignaron los costos entre líneas o si el modelo de Líneas de Negocio y el de PYG se ejecutaron con los mismos datos.</p></section>'''))
    s.append(('ia', 'IA', '''<section id="ia"><h2>6. Lectura con IA</h2>
<ul><li>El botón <b>Generar</b> produce tres líneas: <b>Rentabilidad</b> (qué línea es más y menos rentable), <b>Concentración</b> (cuánto depende el negocio de una sola línea) y <b>Acción recomendada</b>. «Pregúntale a este informe» responde preguntas libres sobre estas cifras.</li>
<li><b>Qué se envía a la IA:</b> únicamente el resumen ya calculado por línea y el total; no se envían datos contables de detalle ni de otras tablas.</li>
<li>Cuenta contra el presupuesto de IA de la empresa y respeta el acceso por usuario; si la IA está apagada para la empresa, el bloque no aparece.</li></ul></section>'''))
    s.append(('exportar', 'Exportar', '''<section id="exportar"><h2>7. Exportar</h2>
<p class="lead">Botón <b>Exportar</b>, igual que en los demás informes, con el logo de la empresa y la paleta elegida:</p>
<ul><li><b>Excel:</b> hojas «Resumen por línea», «Estado por línea» y «Mezcla mensual».</li>
<li><b>PDF</b> horizontal: resumen con indicadores, lectura con IA y comentarios; mezcla y márgenes; y el estado de resultados por línea.</li>
<li><b>Copiar como imagen</b> y <b>Enviar por correo</b> (con el Excel y el PDF adjuntos; el envío queda en la auditoría).</li></ul></section>'''))
    s.append(('faq', 'Preguntas frecuentes', '<section id="faq"><h2>8. Preguntas frecuentes</h2>' + faq([
        ('No veo líneas, ¿qué hago?', 'Ejecute el modelo de Líneas de Negocio de la empresa en Ejecución de Modelos y elija un rango con datos. Si la empresa no usa líneas, el informe no tiene qué mostrar.'),
        ('¿Por qué una línea no aparece?', 'Porque no tuvo movimiento (todo en cero) en el rango elegido.'),
        ('¿Por qué la franja de cuadre sale en rojo?', 'Los totales de las líneas no coinciden con el PYG. Revise que ambos modelos se hayan ejecutado para el mismo escenario y la misma carga de datos.'),
        ('¿Por qué un margen aparece vacío?', 'Porque la línea no tuvo ingresos en el rango: sin ingresos no hay margen.'),
        ('¿Puedo comparar contra el presupuesto?', 'Hoy no: el modelo de líneas no trae presupuesto. Se compara contra el mismo rango del año anterior.'),
        ('¿Quién puede escribir comentarios?', 'Todos los usuarios con acceso a la empresa; editar o eliminar un comentario solo puede hacerlo su autor o un administrador.')]) + '</section>'))
    return pagina('Rentabilidad por línea de negocio',
                  'Qué es, de dónde salen las cifras, cómo se calcula cada concepto y cómo se valida contra el Estado de resultados.', s)


# ======================================================================================================================
# Informe mensual de gerencia
# ======================================================================================================================
def doc_gerencia():
    s = []
    s.append(('para', 'Para qué sirve', '''<section id="para"><h2>1. Para qué sirve</h2>
<p class="lead">Es el <b>cierre del mes en un solo documento</b>: lo que la gerencia necesita ver cada mes sin abrir cinco informes. Reúne resultados contra presupuesto, balance, caja, indicadores que requieren atención, líneas de negocio y los comentarios del equipo, con un resumen escrito por IA arriba.</p>
<div class="g2"><div class="card"><b>Para el gerente o dueño</b><ul><li>Cómo cerró el mes frente al presupuesto y al año anterior.</li><li>La posición de caja y el foco del mes.</li></ul></div>
<div class="card"><b>Para quien prepara el cierre</b><ul><li>Un solo PDF o Excel para enviar a la junta o a los socios.</li><li>Los comentarios de todos los informes del cierre en un mismo lugar.</li></ul></div></div></section>'''))
    s.append(('pantalla', 'La pantalla', '''<section id="pantalla"><h2>2. La pantalla</h2>
<p class="lead">Se elige la empresa, el escenario y el <b>mes de cierre</b> (la lista trae los meses con resultados, el más reciente primero). No hay rango: es un solo mes. También se puede elegir la paleta de colores del informe.</p>
''' + tabla(['Sección', 'Qué muestra', 'De qué informe sale'], [
        ['<b>Resumen del mes (IA)</b>', 'Tres líneas: resultado, caja y balance, y foco del mes.', 'Todas las secciones'],
        ['<b>Resultados del mes</b>', 'Ingresos, EBITDA y utilidad neta del mes contra el presupuesto y el año anterior, y el acumulado del año.', 'Estado de resultados (PYG)'],
        ['<b>Balance al cierre</b>', 'Activo total, capital de trabajo, razón corriente y endeudamiento, contra el cierre del año anterior, y si el balance cuadra.', 'Balance general'],
        ['<b>Caja del mes</b>', 'Caja inicial, flujo neto del mes contra el presupuesto, flujo de caja libre operativo, caja final y si cuadra con el balance.', 'Flujo de caja'],
        ['<b>Indicadores que requieren atención</b>', 'Cuántos indicadores hay en cada color del semáforo y los rojos y naranjas más graves.', 'Informe de indicadores'],
        ['<b>Líneas de negocio</b>', 'Ingresos, participación y margen operacional de las líneas del mes.', 'Rentabilidad por línea de negocio'],
        ['<b>Comentarios de gestión</b>', 'Los comentarios del equipo sobre este cierre, de todos los informes.', 'Comentarios de gestión'],
    ]) + '</section>'))
    s.append(('datos', 'De dónde salen las cifras', '''<section id="datos"><h2>3. De dónde salen las cifras</h2>
<p class="lead">Este informe <b>no calcula nada propio</b>: usa exactamente los mismos cálculos de cada informe, aplicados a un solo mes. Por eso una cifra aquí coincide con la de la pantalla original. Todo sale de las tablas del modelo, así que solo hay datos después de ejecutar los modelos.</p>
<ul><li>Cada sección es independiente: si una no tiene datos (por ejemplo la empresa no usa líneas de negocio), aparece como «sin datos» y las demás se muestran normalmente.</li>
<li>Los resultados se comparan contra el <b>presupuesto</b> y el <b>año anterior</b>; el balance, contra el <b>cierre del año anterior</b>.</li></ul></section>'''))
    s.append(('lectura', 'Cómo se lee', '''<section id="lectura"><h2>4. Cómo se lee</h2>
<ul><li><b>Verde y rojo</b> en las variaciones: verde cuando el cambio es favorable (más ingresos, más utilidad, menos endeudamiento) y rojo cuando es desfavorable.</li>
<li><b>Acumulado del año</b>: lo que va del año hasta el mes de cierre, contra el presupuesto acumulado.</li>
<li><b>Caja final cuadra con el balance</b>: el flujo del mes explica el cambio de la caja del balance. Si no cuadra, revise las cargas de enero o las partidas de caja de los meses anteriores.</li>
<li><b>Indicadores en atención</b> usa los umbrales de la empresa; si un administrador los ajustó, el semáforo ya los refleja.</li></ul></section>'''))
    s.append(('comentarios', 'Comentarios', '''<section id="comentarios"><h2>5. Comentarios de gestión</h2>
<p class="lead">Esta tarjeta muestra los comentarios de <b>todos los informes</b> para el mes de cierre elegido, con el informe de origen de cada uno. Los comentarios nuevos que se escriben aquí quedan como «Informe mensual de gerencia». Los pueden escribir y leer todos los usuarios de la empresa; editar o eliminar solo su autor o un administrador. Los comentarios van incluidos en el PDF y en el Excel.</p></section>'''))
    s.append(('ia', 'IA', '''<section id="ia"><h2>6. Lectura con IA</h2>
<ul><li>El botón <b>Generar</b> produce tres líneas: <b>Resultado</b>, <b>Caja y balance</b> y <b>Foco del mes</b>. «Pregúntale a este informe» responde preguntas libres.</li>
<li><b>Qué se envía a la IA:</b> únicamente las cifras ya calculadas del cierre; no se envían datos contables de detalle ni de otras tablas.</li>
<li>Cuenta contra el presupuesto de IA de la empresa; si la IA está apagada para la empresa, el bloque no aparece.</li></ul></section>'''))
    s.append(('exportar', 'Exportar', '''<section id="exportar"><h2>7. Exportar</h2>
<ul><li><b>Excel:</b> hoja «Resumen» (resultados, balance y caja), «Atención y líneas» y «Comentarios».</li>
<li><b>PDF</b> horizontal de dos páginas: resumen con IA, resultados, balance y caja; e indicadores en atención, líneas y comentarios.</li>
<li><b>Copiar como imagen</b> y <b>Enviar por correo</b> (con el Excel y el PDF adjuntos; el envío queda en la auditoría). El logo y la paleta de la empresa se respetan en todas las salidas.</li></ul></section>'''))
    s.append(('faq', 'Preguntas frecuentes', '<section id="faq"><h2>8. Preguntas frecuentes</h2>' + faq([
        ('El mes que busco no aparece', 'La lista trae solo meses con ingresos en el Estado de resultados. Ejecute los modelos de la empresa y vuelva a elegir la empresa.'),
        ('Una sección dice «sin datos»', 'Esa parte del modelo (balance, flujo, indicadores o líneas) no tiene información para el mes. Las demás secciones no se afectan.'),
        ('¿Por qué no hay gráficos en el PDF?', 'Es un documento de lectura rápida: cifras y tablas. Los gráficos están en cada informe.'),
        ('¿Puedo elegir varios meses?', 'No: es el cierre de un mes. Para un rango use los informes individuales.'),
        ('¿Las cifras coinciden con los otros informes?', 'Sí, usan los mismos cálculos aplicados al mes elegido.')]) + '</section>'))
    return pagina('Informe mensual de gerencia',
                  'El cierre del mes en un solo documento: resultados, balance, caja, indicadores, líneas de negocio y comentarios.', s)


# ======================================================================================================================
# Parches de los documentos existentes
# ======================================================================================================================
def parche_indicadores():
    p = os.path.join(DOCS, 'Informe_Indicadores.html')
    s = leer(p)
    if 'id="umbrales"' in s:
        return 'Indicadores: ya estaba'
    # 2. La pantalla: opciones nuevas
    nuevo_pantalla = '''<h3>Opciones adicionales</h3>
<ul><li><b>Comparar con escenario:</b> evalúa cada indicador en el mismo mes de corte de otro escenario; aparece una columna en la tabla y un dato en la ficha.</li>
<li><b>Umbrales:</b> botón para que un administrador ajuste los rangos del semáforo de la empresa (ver la sección 6).</li>
<li><b>Frente a su historia:</b> en la ficha de cada indicador, qué porcentaje de los demás meses del rango estuvo peor que el mes de corte (se muestra con al menos 5 meses con valor).</li>
<li><b>Estilos del informe:</b> paleta de colores para la pantalla y las exportaciones.</li>
<li><b>Comentarios de gestión:</b> notas del equipo sobre el cierre, que escriben y leen todos los usuarios de la empresa.</li></ul>
<p class="note">Las capturas de esta sección se tomaron antes de agregar estas opciones.</p>
'''
    ancla = '<p class="lead">Se elige la empresa, el escenario, contra qué mes comparar'
    i = s.index(ancla)
    fin = s.index('</p>', i) + len('</p>')
    s = s[:fin] + '\n' + nuevo_pantalla + s[fin:]
    # 6. semáforo: umbrales propios
    viejo = 'En una próxima fase se podrán calibrar con datos del sector.</p></section>'
    assert viejo in s, 'ancla del semáforo no encontrada'
    s = s.replace(viejo, 'Si la empresa necesita otros rangos, un administrador puede ajustarlos (ver abajo).</p>\n'
        '<h3 id="umbrales">Umbrales propios de la empresa</h3>\n'
        '<p class="lead">El <b>Super Admin</b> y el <b>Admin de Empresa</b> ven el botón <b>Umbrales</b>. Allí, para cada indicador que tiene dirección (más alto es mejor o más bajo es mejor), se escriben los tres cortes: verde, amarillo y naranja. Por debajo (o por encima, según la dirección) del tercero el indicador queda en rojo.</p>\n'
        '<ul><li>Los cortes deben ir en orden: con «más alto es mejor», verde ≥ amarillo ≥ naranja; con «más bajo es mejor», al revés. Si no, no se guardan.</li>\n'
        '<li>Los indicadores que no se cambian siguen usando los rangos generales; los que sí se cambiaron se marcan como <b>Propio</b> y la ficha lo indica.</li>\n'
        '<li><b>Restablecer</b> devuelve un indicador a los rangos generales.</li>\n'
        '<li>Los umbrales son de la empresa: no afectan a las demás. Cada cambio queda registrado en la auditoría.</li>\n'
        '<li>No tienen umbrales propios los indicadores monetarios (como el capital de trabajo, que es verde si es positivo) ni los que no tienen dirección.</li></ul></section>', 1)
    # 9. FAQ
    viejo = '<details><summary>No veo datos, ¿qué hago?</summary>'
    assert viejo in s
    s = s.replace(viejo,
        '<details><summary>¿Se puede comparar contra el presupuesto?</summary><p>Hoy no. Se puede comparar contra otro mes (cierre anterior, mes anterior o mismo mes del año anterior), contra otro escenario y contra la propia historia de la empresa dentro del rango.</p></details>\n'
        '<details><summary>¿Qué significa «Mejor que el X % de los meses anteriores del rango»?</summary><p>Es la posición del indicador frente a la propia historia de la empresa en el rango elegido: de los demás meses con valor, en cuántos estuvo peor. Necesita al menos cinco meses con valor; amplíe el rango para tener más historia.</p></details>\n'
        '<details><summary>Cambié un umbral y el semáforo se movió, ¿es normal?</summary><p>Sí: el semáforo se calcula con los rangos de la empresa. Puede volver a los generales con «Restablecer».</p></details>\n' + viejo, 1)
    escribir(p, s)
    return 'Indicadores: parchado'


def parche_hist_precios():
    p = os.path.join(DOCS, 'Informe_HistPrecios_vs_Excel.html')
    s = leer(p)
    if 'Estilos del informe' in s:
        return 'Hist. precios: ya estaba'
    bloque = '''  <h2>6 · Estilos del informe <small>paleta de colores</small></h2>
  <div class="box">
    <p style="margin-top:0">Antes de generar el informe se puede elegir la <b>paleta de colores</b> en la sección «Estilos del informe»: <b>Bufins</b> (la de siempre, por defecto), los <b>colores corporativos 1 y 2</b> de la empresa (si se registraron en el gestor de empresas) o una <b>personalizada</b>.</p>
    <ul class="clean">
      <li>La elección se aplica al pulsar <b>Generar</b> y cambia las series de los gráficos, el mapa de calor, la banda del PDF y los encabezados y gráficos del Excel y del correo.</li>
      <li>No se recuerda: cada vez que se abre el informe vuelve a Bufins.</li>
      <li>El logo de la empresa, la marca Bufins y los colores de alza (verde) y baja (rojo) no cambian.</li>
    </ul>
  </div>

'''
    marca = '  <div class="note">\n    <b>Reglas de cálculo que conviene conocer</b>'
    assert marca in s
    s = s.replace(marca, bloque + marca, 1)
    escribir(p, s)
    return 'Hist. precios: parchado'


if __name__ == '__main__':
    escribir(os.path.join(DOCS, 'Informe_LineasNegocio.html'), doc_lineas())
    escribir(os.path.join(DOCS, 'Informe_GerenciaMensual.html'), doc_gerencia())
    print(parche_indicadores())
    print(parche_hist_precios())
    for f in ('Informe_LineasNegocio.html', 'Informe_GerenciaMensual.html', 'Informe_Indicadores.html', 'Informe_HistPrecios_vs_Excel.html'):
        shutil.copyfile(os.path.join(DOCS, f), os.path.join(COPIA, f))
    print('copias en docs/ listas')
