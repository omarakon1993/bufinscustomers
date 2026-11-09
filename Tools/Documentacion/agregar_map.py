# -*- coding: utf-8 -*-
"""
Completa Tools/Documentacion/traducciones/Informe_<Documento>.json con los textos del HTML en español que todavía no tienen traducción
(incluye atributos alt/title). Con un solo argumento LISTA los pendientes; con el segundo (JSON con la lista en inglés, en el mismo orden) los agrega.
Uso:  python agregar_map.py <Documento> [lista_en.json]
"""
import io, json, os, re, sys
sys.path.insert(0, os.path.dirname(__file__))
from traduccion_html import extraer, atributos, tiene_letras
from construir_map import mapa_resx, RAIZ

doc = sys.argv[1]
ruta_json = os.path.join(os.path.dirname(__file__), 'traducciones', 'Informe_%s.json' % doc)
trad = json.load(io.open(ruta_json, encoding='utf-8'))
html = io.open(os.path.join(RAIZ, 'App_Data', 'Documentacion', 'Informes', 'Informe_%s.html' % doc), encoding='utf-8').read()
m = mapa_resx()
pend = []
for _, _, x in extraer(html):
    if tiene_letras(x) and x not in trad and x not in pend and not re.match(r'^(\s*<a [^>]*>[^<]*</a>\s*)+$', x):
        k = x if x in m else x.replace('&amp;', '&')
        if k in m: trad[x] = m[k]
        else: pend.append(x)
for a in atributos(html):
    k = 'attr:' + a
    if k not in trad and k not in pend: pend.append(k)
if len(sys.argv) < 3:
    for i, x in enumerate(pend): print('%d|%s' % (i, x))
    print('TOTAL', len(pend))
else:
    en = json.load(io.open(sys.argv[2], encoding='utf-8'))
    if len(en) != len(pend): raise SystemExit('ERROR: %d traducciones para %d pendientes' % (len(en), len(pend)))
    for es, ing in zip(pend, en): trad[es] = ing
io.open(ruta_json, 'w', encoding='utf-8', newline='\n').write(json.dumps(trad, ensure_ascii=False, indent=1))
