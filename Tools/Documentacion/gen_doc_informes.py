# -*- coding: utf-8 -*-
"""
Genera la documentación de cliente (HTML de solo lectura, bilingüe) de los informes:
PYG, Balance, Tesorería, Flujo de caja, Flujo de efectivo, Línea de tiempo e Informe de modelos.

Uso:    python Tools/Documentacion/gen_doc_informes.py
Salida: App_Data/Documentacion/Informes/Informe_<Nombre>.html     (español)
        App_Data/Documentacion/Informes/Informe_<Nombre>.en.html  (inglés)
Es idempotente. El CSS sale de Informe_Indicadores.html (estilo único de BUFINS). Los documentos no llevan capturas todavía:
agregarlas (base64, <figure>) cuando se pueda correr la aplicación. Si cambia un informe, ajustar el texto aquí y volver a generar.
"""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from doc_lib import *
from doc_pyg_balance import doc_pyg, doc_balance
from doc_tesoreria_flujos import doc_tesoreria, doc_flujo
from doc_lt_modelos import doc_linea_tiempo, doc_modelos

DOCUMENTOS = [
    ('Informe_PYG', doc_pyg),
    ('Informe_Balance', doc_balance),
    ('Informe_Tesoreria', doc_tesoreria),
    ('Informe_FlujoCaja', lambda L: doc_flujo(L, 'Caja')),
    ('Informe_FlujoEfectivo', lambda L: doc_flujo(L, 'Efectivo')),
    ('Informe_LineaTiempo', doc_linea_tiempo),
    ('Informe_Modelos', doc_modelos),
]

for nombre, fn in DOCUMENTOS:
    for codigo, sufijo in (('es', '.html'), ('en', '.en.html')):
        L = Idioma(codigo)
        titulo, desc, secciones = fn(L)
        html = pagina(L, titulo.replace('&', '&amp;'), desc, secciones)
        ruta = os.path.join(DOCS, nombre + sufijo)
        escribir(ruta, html)
        print('%-34s %6.1f KB' % (nombre + sufijo, len(html.encode('utf-8')) / 1024.0))
