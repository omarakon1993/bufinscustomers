# -*- coding: utf-8 -*-
"""Genera la documentación HTML del Informe de indicadores (autocontenida, con capturas incrustadas en base64).

Fuente ÚNICA de verdad (la documentación nunca diverge del código): los textos de cada ficha salen de App_GlobalResources/Strings.resx
(claves Ind_<Codigo>_*) y los rangos del semáforo de Services/IndicadoresCatalogo.cs. Solo las capturas se toman a mano.

uso:  python docs/herramientas/gen_doc_indicadores.py <raiz_proyecto> <salida.html> <img_filtros> <img_tarjetas> <img_ficha> <img_tabla>
      (usar `python` y no `python -I`: requiere Pillow del usuario)
luego copiar la salida a App_Data/Documentacion/Informe_Indicadores.html, docs/ y la carpeta de documentación técnica
(ver CLAUDE.md, "Documentación de informes — ESTÁNDAR").
"""
import sys, re, io, base64, html
import xml.etree.ElementTree as ET
from PIL import Image

RAIZ = sys.argv[1].replace('\\', '/')
SALIDA = sys.argv[2]
IMGS = sys.argv[3:7]

# Textos de las fichas: salen de Strings.resx (los mismos que ve el usuario en la ficha)
RES = {}
for el in ET.parse(RAIZ + '/App_GlobalResources/Strings.resx').getroot().findall('data'):
    v = el.find('value')
    RES[el.get('name')] = v.text if v is not None and v.text else ''
SUF = ['Nombre', 'Pregunta', 'Que', 'Formula', 'Mejorar', 'Regla']
IND = {}
for k in RES:
    m = re.match(r'Ind_(\w+)_Nombre$', k)
    if m and m.group(1) not in ('Fam', 'Doc'):
        c = m.group(1)
        IND[c] = (tuple(RES.get('Ind_%s_%s' % (c, x), '') for x in SUF),)

# Cortes del semáforo: se leen del catálogo C# para que la documentación nunca diverja del código
cat = open(RAIZ + '/Services/IndicadoresCatalogo.cs', encoding='utf-8-sig').read()
FAM = {'Rent': 'Rentabilidad', 'Ret': 'Retornos', 'Efi': 'Eficiencia', 'Liq': 'Liquidez', 'Sol': 'Solvencia', 'Cre': 'Crecimiento'}
orden, info = [], {}
for m in re.finditer(r'\bD\("(\w+)",\s*(\w+),\s*"(\w+)",\s*(true|false|null),\s*(-?[\d\.]+)m,\s*(-?[\d\.]+)m,\s*(-?[\d\.]+)m', cat):
    cod = m.group(1); orden.append(cod)
    info[cod] = dict(fam=FAM[m.group(2)], fmt=m.group(3), mas=m.group(4), c=[float(m.group(i)) for i in (5, 6, 7)])
# CapitalTrabajo (valor): definido aparte, sin cortes numéricos
idx = orden.index('RazonEfectivo') + 1
orden.insert(idx, 'CapitalTrabajo')
info['CapitalTrabajo'] = dict(fam='Liquidez', fmt='moneda', mas='true', c=[0, 0, 0])
assert set(orden) == set(IND.keys()), set(orden) ^ set(IND.keys())

def n(v, dec):
    return ('{:,.%df}' % dec).format(v).replace(',', '#').replace('.', ',').replace('#', '.')
def fv(fmt, v):
    if fmt == 'pct': return n(v * 100, 1) + ' %'
    if fmt == 'x': return n(v, 2) + 'x'
    if fmt == 'dias': return n(v, 0) + ' d'
    return n(v, 0)
def rango(cod):
    d = info[cod]
    if d['fmt'] == 'moneda': return 'Verde si es positivo; rojo si es cero o negativo'
    s = '≥ ' if d['mas'] == 'true' else '≤ '
    c = d['c']
    extra = ''
    if cod == 'CapitalTrabajoIngresos': extra = ' (negativo = rojo)'
    return ('Saludable %s%s · Atención %s%s · Riesgo %s%s · Crítico %s' % (s, fv(d['fmt'], c[0]), s, fv(d['fmt'], c[1]), s, fv(d['fmt'], c[2]),
            'por debajo' if d['mas'] == 'true' else 'por encima')) + extra

def img(ruta, ancho=1180):
    im = Image.open(ruta).convert('RGB')
    if im.width > ancho:
        im = im.resize((ancho, int(im.height * ancho / im.width)), Image.LANCZOS)
    b = io.BytesIO(); im.save(b, 'JPEG', quality=78, optimize=True)
    return 'data:image/jpeg;base64,' + base64.b64encode(b.getvalue()).decode('ascii')
I = [img(p) for p in IMGS]

def esc(t): return html.escape(t, quote=False)

familias = ['Rentabilidad', 'Retornos', 'Eficiencia', 'Liquidez', 'Solvencia', 'Crecimiento']
tablas = ''
for f in familias:
    filas = ''
    for cod in orden:
        if info[cod]['fam'] != f: continue
        es = IND[cod][0]
        filas += '<tr><td class="n">%s</td><td>%s</td><td class="f">%s</td><td class="r">%s</td></tr>' % (esc(es[0]), esc(es[1]), esc(es[3]), esc(rango(cod)))
    tablas += '<h3>%s</h3><div class="tw"><table><tr><th>Indicador</th><th>La pregunta que responde</th><th>Cómo se calcula</th><th>Rangos del semáforo (referencia general)</th></tr>%s</table></div>' % (f, filas)

fichas = ''
for f in familias:
    for cod in orden:
        if info[cod]['fam'] != f: continue
        es = IND[cod][0]
        fichas += ('<div class="ficha" id="f-%s"><h4>%s <span class="fam">%s</span></h4><p><b>Qué mide.</b> %s</p><p><b>Cómo mejorarlo.</b> %s</p><p class="regla">%s</p></div>'
                   % (cod, esc(es[0]), f, esc(es[2]), esc(es[4]), esc(es[5])))

doc = '''<!doctype html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Documentación: Informe de indicadores</title>
<style>
:root{--bg:#f6f5fb;--card:#fff;--ink:#1c1738;--ink2:#5b5678;--line:#e3e0f1;--brand:#583aff;--chip:#efecff;--ok:#15803d;--warn:#a16207;--mid:#c2410c;--bad:#b91c1c;--grad:linear-gradient(90deg,#6366F1 0%,#3B82F6 35%,#06B6D4 70%,#01E1D7 100%)}
@media (prefers-color-scheme:dark){:root{--bg:#120a2b;--card:#1a1240;--ink:#efecff;--ink2:#a9a3cc;--line:#2d2460;--brand:#8f7bff;--chip:#271d5c;--ok:#3ddc97;--warn:#fbbf24;--mid:#fb923c;--bad:#ff7a6b}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 "Segoe UI",system-ui,sans-serif}
header{background:var(--grad);color:#fff;padding:34px 18px 28px}.w{max-width:1040px;margin:0 auto;padding:0 18px}
header h1{margin:0 0 6px;font-size:27px}header p{margin:0;opacity:.95;max-width:720px}
nav{position:sticky;top:0;z-index:5;background:var(--card);border-bottom:1px solid var(--line)}nav .w{display:flex;gap:6px;overflow-x:auto;padding-top:8px;padding-bottom:8px}
nav a{white-space:nowrap;text-decoration:none;color:var(--ink2);font-size:13px;padding:5px 10px;border-radius:999px}nav a:hover{background:var(--chip);color:var(--ink)}
section{margin:34px 0}h2{font-size:21px;margin:0 0 6px}h3{font-size:16px;margin:20px 0 8px;color:var(--brand)}
.lead{color:var(--ink2);margin:0 0 12px}.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:14px 18px}
.g2{display:grid;gap:12px;grid-template-columns:repeat(2,minmax(0,1fr))}@media(max-width:760px){.g2{grid-template-columns:1fr}}
.tw{overflow-x:auto;border:1px solid var(--line);border-radius:12px;background:var(--card)}table{border-collapse:collapse;width:100%;font-size:13.5px}
th{background:var(--grad);color:#fff;text-align:left;padding:8px 10px;white-space:nowrap}td{padding:8px 10px;border-top:1px solid var(--line);vertical-align:top}
td.n{font-weight:700;white-space:nowrap}td.f{font-family:Consolas,monospace;font-size:12.5px}td.r{color:var(--ink2);font-size:12.5px}
figure{margin:12px 0}figure img{width:100%;border:1px solid var(--line);border-radius:12px;display:block}figcaption{font-size:12.5px;color:var(--ink2);margin-top:5px}
.note{border-left:4px solid #d97706;background:var(--card);padding:10px 14px;border-radius:8px;color:var(--ink2)}
.dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:6px}.dot.v{background:#16a34a}.dot.a{background:#eab308}.dot.o{background:#f97316}.dot.r{background:#dc2626}.dot.gr{background:#94a3b8}
.ficha{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:10px 16px;margin:10px 0}.ficha h4{margin:0 0 4px;font-size:15px}.ficha .fam{font-size:11px;color:var(--brand);font-weight:700;margin-left:8px;text-transform:uppercase}
.ficha p{margin:4px 0;color:var(--ink2);font-size:13.5px}.regla{border-left:3px solid var(--brand);padding-left:10px;color:var(--ink)!important}
code{background:var(--chip);padding:1px 6px;border-radius:6px;font-size:12.5px}ul{padding-left:20px;color:var(--ink2)}li{margin:3px 0}
details{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:8px 14px;margin:8px 0}summary{cursor:pointer;font-weight:700}
</style></head><body>
<header><div class="w"><h1>Informe de indicadores</h1><p>Qué es, de dónde salen las cifras, cómo se calcula cada indicador y cómo leer el semáforo.</p></div></header>
<nav><div class="w"><a href="#para">Para qué sirve</a><a href="#pantalla">La pantalla</a><a href="#datos">De dónde salen las cifras</a><a href="#calculo">Cómo se calcula</a><a href="#indicadores">Los 27 indicadores</a><a href="#semaforo">El semáforo</a><a href="#ia">IA</a><a href="#exportar">Exportar</a><a href="#faq">Preguntas frecuentes</a><a href="#fichas">Glosario</a></div></nav>
<main class="w">

<section id="para"><h2>1. Para qué sirve</h2>
<p class="lead">Responde en una pantalla: <b>¿cómo está la empresa hoy y qué debo mirar primero?</b> Toma las cifras que ya calcula Bufins (Estado de resultados, Balance y Flujo de caja) y las convierte en 27 indicadores con un semáforo, una comparación y una ficha que explica cada uno en lenguaje de negocio.</p>
<div class="g2"><div class="card"><b>Para el gerente o dueño</b><ul><li>El resumen del semáforo y los tres indicadores que más requieren atención.</li><li>La lectura con IA: fortaleza, riesgo y prioridad.</li></ul></div>
<div class="card"><b>Para el contador o analista financiero</b><ul><li>La fórmula, las líneas del modelo y los rangos de cada indicador.</li><li>Tendencia mensual y exportación a Excel, PDF y correo.</li></ul></div></div></section>

<section id="pantalla"><h2>2. La pantalla</h2>
<p class="lead">Se elige la empresa, el escenario, contra qué mes comparar y el <b>mes de corte</b> (el extremo derecho del rango). El resto del rango alimenta las mini-tendencias de cada tarjeta.</p>
<figure><img alt="Filtros y encabezado del informe" src="@@I0@@"><figcaption>Filtros, mes de corte, resumen del semáforo y los indicadores que requieren atención.</figcaption></figure>
<figure><img alt="Tarjetas por familia" src="@@I1@@"><figcaption>Una tarjeta por indicador: valor, nivel del semáforo, variación contra el comparativo y tendencia de los últimos meses.</figcaption></figure>
<figure><img alt="Ficha de un indicador" src="@@I2@@"><figcaption>Al pasar el cursor por el ícono «i» de una tarjeta aparece «Ver detalle»; al hacer clic se abre la ficha en un modal: la pregunta, el valor actual con su nivel, el comparado y la variación, qué mide, cómo se calcula, los rangos del semáforo (con el nivel actual resaltado), cómo mejorarlo y una regla para recordar. El botón Glosario abre las fichas de todos los indicadores.</figcaption></figure>
<figure><img alt="Tabla de indicadores" src="@@I3@@"><figcaption>La tabla muestra todos los indicadores con su referencia. El filtro «Mostrar» permite ver solo las alertas (naranja y rojo).</figcaption></figure></section>

<section id="datos"><h2>3. De dónde salen las cifras</h2>
<p class="lead">El informe no ejecuta ningún modelo: lee las tablas ya materializadas, por eso solo hay datos después de ejecutar el modelo en <i>Ejecución de Modelos</i>.</p>
<div class="tw"><table><tr><th>Tabla</th><th>Líneas que se usan</th><th>Qué aporta</th></tr>
<tr><td class="n">ModeloPYG</td><td>Ingresos, CMV, Utilidad bruta, Gastos operacionales, Depreciación y amortización (costo y gasto), Utilidad operacional, Gastos financieros, Utilidad neta</td><td>Flujos del mes (se suman 12 meses)</td></tr>
<tr><td class="n">ModeloBalance</td><td>Caja y bancos, Cuentas por cobrar, Inventarios, Proveedores corrientes, Total activo corriente, Total pasivo corriente, Total Activo, Total pasivo, Total patrimonio, Obligaciones financieras (corrientes y no corrientes)</td><td>Saldos del mes de corte</td></tr>
<tr><td class="n">ModeloFlujoCaja</td><td>Subtotal Flujo de caja libre operativo</td><td>Margen de flujo libre y conversión a caja</td></tr></table></div>
<p class="note" style="margin-top:12px"><b>EBITDA.</b> El modelo no trae una línea «EBITDA»: se calcula como Utilidad operacional + depreciaciones y amortizaciones (costo y gasto), la misma regla del informe de Estado de resultados.</p></section>

<section id="calculo"><h2>4. Cómo se calcula</h2>
<div class="g2"><div class="card"><b>Flujos: 12 meses móviles</b><p>Los indicadores que usan el PYG o el Flujo de caja suman los 12 meses que terminan en el mes de corte. Así se evita que un mes bueno o malo, o la estacionalidad, distorsione el resultado.</p></div>
<div class="card"><b>Saldos: foto del mes de corte</b><p>Los indicadores que usan el Balance (liquidez, endeudamiento, rotaciones) toman el saldo de ese mes. Los días y las rotaciones combinan el saldo del corte con los flujos de 12 meses.</p></div></div>
<h3>Comparativo y variación</h3>
<ul><li>El mes de comparación se evalúa <b>igual que el corte</b>, con sus propios 12 meses. Opciones: cierre del año anterior (diciembre, por defecto), mes anterior o mismo mes del año anterior.</li>
<li>La variación se muestra en la unidad natural: <b>puntos porcentuales (pp)</b> para porcentajes, <b>veces</b> para razones y <b>días</b> para plazos. Verde = el cambio favorece al negocio; rojo = lo desfavorece.</li></ul>
<h3>Cuando faltan datos</h3>
<ul><li><b>Menos de 12 meses de PYG:</b> los indicadores que anualizan (días, rotaciones, retornos y crecimiento) <b>no se calculan</b> y se marcan «Requiere 12 meses de datos». Los márgenes se muestran pero sin semáforo (gris). Nunca se anualiza.</li>
<li><b>Crecimiento:</b> necesita 24 meses de historia (12 contra los 12 anteriores).</li>
<li><b>Sin Balance en el mes de corte:</b> no hay liquidez, endeudamiento ni rotaciones; se avisa en pantalla.</li>
<li><b>Casos especiales:</b> deuda neta con EBITDA no positivo se muestra como alerta roja explícita (no hay número); cobertura de intereses sin gastos financieros se marca «Sin gastos financieros»; conversión a caja con EBITDA no positivo no aplica.</li></ul></section>

<section id="indicadores"><h2>5. Los 27 indicadores</h2>
<p class="lead">Seis familias. Los rangos del semáforo salen del mismo catálogo que usa el informe.</p>
@@TABLAS@@</section>

<section id="semaforo"><h2>6. El semáforo</h2>
<div class="tw"><table><tr><th>Nivel</th><th>Qué significa</th></tr>
<tr><td class="n"><span class="dot v"></span>Saludable</td><td>Cumple el rango más exigente de referencia.</td></tr>
<tr><td class="n"><span class="dot a"></span>Atención</td><td>Aceptable, pero conviene vigilarlo.</td></tr>
<tr><td class="n"><span class="dot o"></span>Riesgo</td><td>Por debajo de lo recomendable; requiere un plan.</td></tr>
<tr><td class="n"><span class="dot r"></span>Crítico</td><td>Fuera de los rangos; requiere acción prioritaria.</td></tr>
<tr><td class="n"><span class="dot gr"></span>Sin dato</td><td>No hay información suficiente (parcial, sin balance o no aplica).</td></tr></table></div>
<p class="note" style="margin-top:12px"><b>Importante.</b> Los rangos son <b>referencias generales de mercado</b>: dependen de la industria, del tamaño de la empresa y del país. Un indicador en rojo es una señal para revisar, no un veredicto. En una próxima fase se podrán calibrar con datos del sector.</p></section>

<section id="ia"><h2>7. Lectura con IA</h2>
<ul><li>El botón <b>Generar resumen</b> produce tres líneas: <b>Fortaleza</b>, <b>Riesgo</b> y <b>Prioridad</b>. «Pregúntale a este informe» responde preguntas libres sobre estas cifras.</li>
<li><b>Qué se envía a la IA:</b> únicamente el reporte ya calculado (valores, comparativos, semáforos y los últimos meses de cada indicador), a partir de las tablas del modelo. No se envían datos contables de detalle ni de otras tablas.</li>
<li>Cuenta contra el presupuesto de IA de la empresa y respeta el acceso por usuario; si la IA está apagada para la empresa, el bloque no aparece.</li></ul></section>

<section id="exportar"><h2>8. Exportar</h2>
<p class="lead">Botón <b>Exportar</b>, igual que en los demás informes:</p>
<ul><li><b>Excel:</b> hoja Resumen (todos los indicadores con valor, comparativo, variación, semáforo y referencia), hoja Tendencia mensual (con gráfico de los márgenes) y hoja Fichas.</li>
<li><b>PDF</b> horizontal: resumen del semáforo, indicadores que requieren atención, lectura con IA (si se generó) y la tabla por familia.</li>
<li><b>Copiar como imagen</b> y <b>Enviar por correo</b> (con el Excel y el PDF adjuntos; el envío queda en la auditoría).</li></ul></section>

<section id="faq"><h2>9. Preguntas frecuentes</h2>
<details><summary>¿Por qué un indicador sale «Requiere 12 meses de datos»?</summary><p>Porque la empresa tiene menos de 12 meses de PYG en la ventana que termina en el corte. Para no engañar con cifras anualizadas, esos indicadores no se calculan hasta completar los 12 meses.</p></details>
<details><summary>¿Por qué el margen se ve pero sin color?</summary><p>Con menos de 12 meses el margen es válido pero sus rangos están pensados para un año completo, así que se muestra en gris.</p></details>
<details><summary>¿Por qué «Conversión de EBITDA a caja» puede pasar de 100 %?</summary><p>El flujo libre incluye capital de trabajo e inversiones: un mes con cobros atrasados o ventas de activos puede dejar más caja que EBITDA. Mira la tendencia, no un solo corte.</p></details>
<details><summary>¿El semáforo es una calificación de la empresa?</summary><p>No. Son rangos generales de referencia; la lectura final depende de la industria y del contexto de cada empresa.</p></details>
<details><summary>¿Los números coinciden con el informe de Estado de resultados o el Balance?</summary><p>Sí: salen de las mismas tablas. Aquí se miden sobre 12 meses móviles, mientras que esos informes muestran el período elegido.</p></details>
<details><summary>No veo datos, ¿qué hago?</summary><p>Ejecuta el modelo de la empresa en Ejecución de Modelos y elige un mes que tenga cifras de PYG.</p></details></section>

<section id="fichas"><h2>10. Glosario de indicadores</h2>
<p class="lead">El mismo contenido de las fichas que se abren en el informe.</p>
@@FICHAS@@</section>
</main></body></html>
'''
for k in range(4): doc = doc.replace('@@I%d@@' % k, I[k])
doc = doc.replace('@@TABLAS@@', tablas).replace('@@FICHAS@@', fichas)

open(SALIDA, 'w', encoding='utf-8').write(doc)
print('ok', len(doc))
