# -*- coding: utf-8 -*-
"""
Une los textos pendientes (en español) de un documento con su traducción al inglés y guarda el diccionario completo
en Tools/Documentacion/traducciones/<Documento>.json  ({innerHTML en español: innerHTML en inglés}).
Los textos que ya tienen traducción en Strings.en-US.resx se toman de ahí (mismo significado que usa la aplicación).
Uso:  python construir_map.py <Documento> <archivo_con_lista_en_json>
"""
import io, json, os, re, sys
sys.path.insert(0, os.path.dirname(__file__))
from traduccion_html import extraer, tiene_letras
RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))


def resx(f):
    t = io.open(f, encoding='utf-8-sig').read()
    return dict(re.findall(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', t, re.S))


def mapa_resx():
    es = resx(os.path.join(RAIZ, 'App_GlobalResources', 'Strings.resx'))
    en = resx(os.path.join(RAIZ, 'App_GlobalResources', 'Strings.en-US.resx'))
    m = {}
    for k, v in es.items():
        if k in en and v.strip() and v != en[k]:
            m.setdefault(v.strip(), en[k].strip())
    return m


def unicos(doc):
    ruta = os.path.join(RAIZ, 'App_Data', 'Documentacion', 'Informes', 'Informe_%s.html' % doc)
    html = io.open(ruta, encoding='utf-8').read()
    res = []
    for _, _, x in extraer(html):
        if tiene_letras(x) and x not in res:
            res.append(x)
    return res


def pendientes(doc, m):
    return [x for x in unicos(doc) if x not in m and x.replace('&amp;', '&') not in m]


if __name__ == '__main__':
    doc, lista = sys.argv[1], sys.argv[2]
    m = mapa_resx()
    pend = pendientes(doc, m)
    en = json.load(io.open(lista, encoding='utf-8'))
    if len(en) != len(pend):
        raise SystemExit('ERROR: %d traducciones para %d textos pendientes' % (len(en), len(pend)))
    res = {}
    for x in unicos(doc):
        if x in m: res[x] = m[x]
        elif x.replace('&amp;', '&') in m: res[x] = m[x.replace('&amp;', '&')]
    for es, ing in zip(pend, en):
        res[es] = ing
    out = os.path.join(os.path.dirname(__file__), 'traducciones', 'Informe_%s.json' % doc)
    io.open(out, 'w', encoding='utf-8', newline='\n').write(json.dumps(res, ensure_ascii=False, indent=1))
    print(doc, 'textos:', len(res), '(resx: %d, nuevos: %d)' % (len(res) - len(pend), len(pend)))
