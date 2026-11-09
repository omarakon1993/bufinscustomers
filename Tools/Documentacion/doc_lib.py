# -*- coding: utf-8 -*-
"""
Librería común del generador de documentación de informes (HTML de solo lectura, estilo único de BUFINS).

Cada documento se escribe UNA vez con textos en los dos idiomas (T(es, en)) y se genera como:
    App_Data/Documentacion/Informes/Informe_<Nombre>.html      (español)
    App_Data/Documentacion/Informes/Informe_<Nombre>.en.html   (inglés; lo sirve DocumentacionProtegidaHelper cuando la interfaz está en inglés)
El CSS se toma de Informe_Indicadores.html para que el estilo sea literalmente el mismo.
"""
import io, os, re

RAIZ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DOCS = os.path.join(RAIZ, 'App_Data', 'Documentacion', 'Informes')


def leer(p):
    return io.open(p, encoding='utf-8').read()


def escribir(p, s):
    io.open(p, 'w', encoding='utf-8', newline='\n').write(s)


CSS = re.search(r'<style>(.*?)</style>', leer(os.path.join(DOCS, 'Informe_Indicadores.html')), re.S).group(1)


class Idioma(object):
    """L('es') / L('en'): L.t(es, en) devuelve el texto del idioma activo."""
    def __init__(self, codigo):
        self.codigo = codigo

    def t(self, es, en):
        return es if self.codigo == 'es' else en


def pagina(L, titulo, descripcion, secciones):
    """secciones: lista de (id, nombre_corto, html_de_la_seccion)."""
    nav = ''.join('<a href="#%s">%s</a>' % (i, n) for i, n, _ in secciones)
    cuerpo = '\n\n'.join(h for _, _, h in secciones)
    pref = L.t('Documentación', 'Documentation')
    return ('<!doctype html>\n<html lang="%s"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">\n'
            '<title>%s: %s</title>\n<style>%s</style></head><body>\n'
            '<header><div class="w"><h1>%s</h1><p>%s</p></div></header>\n<nav><div class="w">%s</div></nav>\n<main class="w">\n\n%s\n\n</main>\n</body></html>\n'
            % (L.codigo, pref, titulo, CSS, titulo, descripcion, nav, cuerpo))


def tabla(encabezados, filas):
    h = '<div class="tw"><table><tr>' + ''.join('<th>%s</th>' % e for e in encabezados) + '</tr>'
    for f in filas:
        h += '<tr>' + ''.join('<td>%s</td>' % c for c in f) + '</tr>'
    return h + '</table></div>'


def faq(items):
    return ''.join('<details><summary>%s</summary><p>%s</p></details>' % (q, a) for q, a in items)


def seccion(id_, n, titulo, cuerpo):
    return '<section id="%s"><h2>%d. %s</h2>\n%s</section>' % (id_, n, titulo, cuerpo)


def dos_tarjetas(titulo1, items1, titulo2, items2):
    def tarjeta(t, items):
        return '<div class="card"><b>%s</b><ul>%s</ul></div>' % (t, ''.join('<li>%s</li>' % i for i in items))
    return '<div class="g2">%s%s</div>' % (tarjeta(titulo1, items1), tarjeta(titulo2, items2))


# ---------- bloques que se repiten en todos los informes ----------
def bloque_estilos(L):
    return L.t(
        '<li><b>Estilos del informe:</b> permite elegir la paleta de colores del informe (por defecto, la de Bufins; también los colores corporativos de la empresa o una paleta propia). La elección se aplica al pulsar <b>Generar</b> y llega a todas las salidas (pantalla, PDF, imagen, Excel y correo). El logo de la empresa se muestra junto a su nombre y no depende de la paleta.</li>',
        '<li><b>Report styles:</b> lets you choose the color palette of the report (Bufins by default; also the company\'s corporate colors or a custom palette). The choice is applied when you press <b>Generate</b> and reaches every output (screen, PDF, image, Excel and email). The company logo is shown next to its name and does not depend on the palette.</li>')


def bloque_ia(L, et_es, et_en, env_es, env_en):
    es = ('<ul><li>El botón <b>Generar</b> produce tres líneas: %s.</li>'
          '<li><b>Qué se envía a la IA:</b> %s; no se envían datos contables de detalle ni de otras tablas.</li>'
          '<li>Cuenta contra el presupuesto de IA de la empresa y respeta el acceso por usuario; si la IA está apagada para la empresa, el bloque no aparece.</li>'
          '<li>La lectura es un apoyo: siempre se contrasta con las cifras de la pantalla.</li></ul>') % (et_es, env_es)
    en = ('<ul><li>The <b>Generate</b> button produces three lines: %s.</li>'
          '<li><b>What is sent to the AI:</b> %s; no detailed accounting data and nothing from other tables is sent.</li>'
          '<li>It counts against the company’s AI budget and respects per-user access; if AI is turned off for the company, the block does not appear.</li>'
          '<li>The reading is a support tool: always check it against the figures on screen.</li></ul>') % (et_en, env_en)
    return L.t(es, en)


def bloque_exportar(L, excel_es, excel_en, pdf_es, pdf_en, extra_es='', extra_en=''):
    es = ('<p class="lead">Botón <b>Exportar</b>, igual que en los demás informes, con el logo de la empresa y la paleta elegida:</p>'
          '<ul><li><b>Excel:</b> %s</li><li><b>PDF</b> horizontal: %s</li>'
          '<li><b>Copiar como imagen</b> y <b>Enviar por correo</b> (con el Excel y el PDF adjuntos; el envío queda en la auditoría).</li>%s</ul>') % (excel_es, pdf_es, extra_es)
    en = ('<p class="lead"><b>Export</b> button, the same as in the other reports, with the company logo and the chosen palette:</p>'
          '<ul><li><b>Excel:</b> %s</li><li><b>Landscape PDF:</b> %s</li>'
          '<li><b>Copy as image</b> and <b>Send by email</b> (with the Excel and PDF attached; the sending is recorded in the audit log).</li>%s</ul>') % (excel_en, pdf_en, extra_en)
    return L.t(es, en)


def nota(L, es, en, clase=''):
    return '<p class="note%s">%s</p>' % ((' ' + clase) if clase else '', L.t(es, en))
