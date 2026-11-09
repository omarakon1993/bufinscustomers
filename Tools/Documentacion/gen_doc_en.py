# -*- coding: utf-8 -*-
"""
Genera la variante en inglés (Informe_<X>.en.html) de los documentos escritos primero en español, aplicando el diccionario
Tools/Documentacion/traducciones/Informe_<X>.json al HTML en español (la estructura, el CSS y las imágenes no se tocan).
Uso:  python Tools/Documentacion/gen_doc_en.py [Documento ...]
Si cambia el texto en español de un documento, los bloques nuevos aparecen como «SIN TRADUCCIÓN» y hay que agregarlos al .json.
"""
import io, json, os, re, sys
sys.path.insert(0, os.path.dirname(__file__))
from traduccion_html import aplicar
RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DOCS = os.path.join(RAIZ, 'App_Data', 'Documentacion', 'Informes')
TODOS = ['Indicadores', 'HistPrecios_vs_Excel', 'LineasNegocio', 'GerenciaMensual']

for doc in (sys.argv[1:] or TODOS):
    trad = json.load(io.open(os.path.join(os.path.dirname(__file__), 'traducciones', 'Informe_%s.json' % doc), encoding='utf-8'))
    html = io.open(os.path.join(DOCS, 'Informe_%s.html' % doc), encoding='utf-8').read()
    falt = []
    en = aplicar(html, trad, falt)
    en = en.replace('<html lang="es">', '<html lang="en">', 1)
    io.open(os.path.join(DOCS, 'Informe_%s.en.html' % doc), 'w', encoding='utf-8', newline='\n').write(en)
    print('%-22s bloques traducidos: %d | SIN TRADUCCIÓN: %d' % (doc, len(trad), len(set(falt))))
    for x in sorted(set(falt))[:10]:
        print('    -', x[:100])
