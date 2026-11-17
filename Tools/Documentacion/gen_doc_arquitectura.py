# -*- coding: utf-8 -*-
"""
Genera la guía de arquitectura y código de BUFINS (solo español) en App_Data/Documentacion/Tecnica/.
Uso:  python Tools/Documentacion/gen_doc_arquitectura.py
Si cambia la estructura de carpetas, las capas o los números citados, ajustar doc_arquitectura.py (y FECHA) y volver a generar.
"""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from doc_lib import *
from doc_arquitectura import doc_arquitectura, FECHA

TITULO = 'Guía de arquitectura y código de BUFINS'
DESC = 'Cómo está construido el sitio: carpetas, capas, recorrido de una petición, datos, seguridad, cargue, informes, y un glosario de C# y .NET con ejemplos del propio código.'
# estilos que usa esta guía además de los del estilo único (bloques de código, diagrama de flujo, pasos numerados)
EXTRA_CSS = (
    'pre{margin:0;overflow-x:auto;background:#0f172a;color:#e2e8f0;border-radius:12px;padding:12px 14px;font:12.5px/1.55 Consolas,"Cascadia Mono",monospace}'
    'pre code{background:none;color:inherit;padding:0;font-size:inherit}'
    'figure{margin:12px 0}figcaption{font-size:12.5px;color:var(--ink2);margin-top:5px}'
    '.flow{display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;margin:12px 0 14px}'
    '.node{flex:1 1 150px;background:var(--card);border:1px solid var(--line);border-radius:12px;padding:10px 12px;font-size:13.5px;text-align:center}'
    '.node:not(:last-child)::after{content:"\2192";display:block;color:var(--ink2);font-weight:700;margin-top:2px}'
    'ol.steps{padding-left:22px;color:var(--ink)}ol.steps li{margin:6px 0}'
    '@media print{.node,pre{break-inside:avoid}}')

SALIDA = os.path.join(RAIZ, 'App_Data', 'Documentacion', 'Tecnica')
os.makedirs(SALIDA, exist_ok=True)
L = Idioma('es')
html = pagina(L, TITULO, DESC, doc_arquitectura())
html = html.replace('</style>', EXTRA_CSS + '</style>', 1)
ruta = os.path.join(SALIDA, 'Guia_Arquitectura_y_Codigo_BUFINS.html')
escribir(ruta, html)
print('%-48s %6.1f KB' % (os.path.basename(ruta), len(html.encode('utf-8')) / 1024.0))
