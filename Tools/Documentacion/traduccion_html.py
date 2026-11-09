# -*- coding: utf-8 -*-
"""
Utilidad para crear la variante en inglés (.en.html) de un documento HTML ya escrito en español sin duplicar su estructura:
  extraer(html)             -> lista de bloques de texto (innerHTML de párrafos, celdas, títulos, enlaces de navegación…) con su posición
  aplicar(html, trad, ...)  -> el mismo HTML con cada bloque reemplazado por su traducción (trad: dict {español: inglés})
Los bloques se identifican por su innerHTML EXACTO (con etiquetas en línea como <b> o <code>), así una traducción sirve a todos los
bloques idénticos. El CSS, las imágenes y la estructura no se tocan.
"""
import re
from html.parser import HTMLParser

BLOQUES = {'p', 'li', 'td', 'th', 'h1', 'h2', 'h3', 'h4', 'summary', 'figcaption', 'a', 'dt', 'dd', 'caption', 'label', 'button', 'title',
           'div', 'span', 'b', 'strong', 'small', 'i', 'em', 'footer'}   # los últimos solo cuentan si ningún bloque mayor los contiene (ver extraer)
# elementos que, si aparecen dentro de un bloque, lo hacen "no hoja" (sus hijos se traducen por separado)
ESTRUCTURA = {'p', 'li', 'td', 'th', 'h1', 'h2', 'h3', 'h4', 'summary', 'figcaption', 'ul', 'ol', 'table', 'tr', 'div', 'section', 'details', 'dl', 'dt', 'dd', 'figure', 'header', 'nav', 'main', 'tbody', 'thead'}


class _Extractor(HTMLParser):
    def __init__(self, texto):
        super().__init__(convert_charrefs=False)
        self.texto = texto
        self.lineas = [0]
        for m in re.finditer('\n', texto):
            self.lineas.append(m.end())
        self.pila = []        # [tag, inicio_interno, es_hoja, en_estilo]
        self.bloques = []     # (inicio, fin)
        self.en_style = 0

    def _off(self):
        l, c = self.getpos()
        return self.lineas[l - 1] + c

    def handle_starttag(self, tag, attrs):
        ini = self._off()
        fin_tag = ini + len(self.get_starttag_text())
        if tag in ('style', 'script'):
            self.en_style += 1
        if tag in ESTRUCTURA and self.pila:
            # un hijo estructural convierte a los bloques abiertos en "no hoja"
            for e in self.pila:
                e[2] = False
        if tag in BLOQUES or tag in ESTRUCTURA:
            self.pila.append([tag, fin_tag, True])

    def handle_endtag(self, tag):
        if tag in ('style', 'script'):
            self.en_style = max(0, self.en_style - 1)
        fin = self._off()
        for i in range(len(self.pila) - 1, -1, -1):
            if self.pila[i][0] == tag:
                t, ini, hoja = self.pila[i]
                del self.pila[i:]
                if hoja and tag in BLOQUES and not self.en_style:
                    self.bloques.append((ini, fin))
                break


def extraer(html):
    p = _Extractor(html)
    p.feed(html)
    p.close()
    cand = sorted(set(p.bloques))
    # se descarta todo bloque que esté contenido en otro (p. ej. un <b> dentro de un <p>): queda el más externo
    res = []
    for a, b in cand:
        if not any((a2 <= a and b <= b2 and (a2, b2) != (a, b)) for a2, b2 in cand):
            res.append((a, b))
    return [(a, b, html[a:b]) for a, b in res]


_ALT = re.compile(r'(alt|title|aria-label)="([^"]*)"')


def atributos(html):
    """Textos de los atributos alt/title/aria-label con letras (se guardan en el diccionario con el prefijo «attr:»)."""
    return [m.group(2) for m in _ALT.finditer(html) if tiene_letras(m.group(2)) and not m.group(2).startswith('data:')]


def tiene_letras(s):
    return re.search(r'[A-Za-zÁÉÍÓÚáéíóúñÑ]{2,}', re.sub(r'<[^>]+>', '', s)) is not None


def aplicar(html, trad, faltantes=None):
    """Reemplaza cada bloque por trad[bloque] cuando existe; los que no tienen traducción se anotan en `faltantes`."""
    res = []
    pos = 0
    for a, b, txt in extraer(html):
        res.append(html[pos:a])
        if txt in trad:
            res.append(trad[txt])
        elif re.match(r'^(\s*<a [^>]*>[^<]*</a>\s*)+$', txt):
            # barra de navegación: una sola fila de enlaces; cada texto se traduce por separado
            res.append(re.sub(r'(<a [^>]*>)([^<]*)(</a>)', lambda m: m.group(1) + trad.get(m.group(2), m.group(2)) + m.group(3), txt))
        else:
            if faltantes is not None and tiene_letras(txt):
                faltantes.append(txt)
            res.append(txt)
        pos = b
    res.append(html[pos:])
    salida = ''.join(res)
    return _ALT.sub(lambda m: '%s="%s"' % (m.group(1), trad.get('attr:' + m.group(2), m.group(2))), salida)
