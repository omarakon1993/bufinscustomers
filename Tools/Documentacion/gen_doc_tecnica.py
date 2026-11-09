# -*- coding: utf-8 -*-
"""
Genera la documentación técnica de BUFINS (español e inglés) en App_Data/Documentacion/Tecnica/.
Uso:  python Tools/Documentacion/gen_doc_tecnica.py
Si cambian versiones, módulos o controles de seguridad, ajustar doc_tecnica.py (y FECHA) y volver a generar.
"""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from doc_lib import *
from doc_tecnica import doc_tecnica

EXTRA_CSS = (
    '.flow{display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;margin:12px 0 14px}'
    '.node{flex:1 1 150px;background:var(--card);border:1px solid var(--line);border-radius:12px;padding:10px 12px;font-size:13.5px;text-align:center;font-weight:600}'
    '.node:not(:last-child)::after{content:"→";display:block;color:var(--ink2);font-weight:700;margin-top:2px}'
    '@media print{.node{break-inside:avoid}}')
SALIDA = os.path.join(RAIZ, 'App_Data', 'Documentacion', 'Tecnica')
os.makedirs(SALIDA, exist_ok=True)
for codigo, sufijo in (('es', '.html'), ('en', '.en.html')):
    L = Idioma(codigo)
    titulo, desc, secciones = doc_tecnica(L)
    html = pagina(L, titulo, desc, secciones)
    # estilo del diagrama de flujo (mismas reglas que Seguridad_Blindaje_Inicio_Sesion.html)
    html = html.replace('</style>', EXTRA_CSS + '</style>', 1)
    ruta = os.path.join(SALIDA, 'Documentacion_Tecnica_BUFINS' + sufijo)
    escribir(ruta, html)
    print('%-44s %6.1f KB' % (os.path.basename(ruta), len(html.encode('utf-8')) / 1024.0))
