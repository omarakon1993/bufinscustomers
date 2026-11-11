/* Atiende los eventos que antes estaban escritos en el HTML (onclick="funcion(1, 'x')").
   La política de seguridad estricta (CSP) no ejecuta código en atributos, así que el servidor los convierte
   (Infra/CspTagHelpers.cs) en  data-h-click='{"s":[{"f":"funcion","a":[1,"x"]}]}'  y este archivo, con UN solo listener por tipo de
   evento, llama a la función indicada. No usa eval ni new Function.

   Formato del atributo:  {"s":[{"f":"ruta.de.funcion","a":[args],"r":1?}],"p":1?}
     f  = nombre de una función global (admite objetos: "Acceso.togglePwd")
     a  = argumentos: números, textos, true/false/null, {"$":"this"} (el elemento), {"$":"event"}, {"$":"value"} (this.value),
          {"$attr":"nombre"} (this.getAttribute)
     r  = si la función devuelve false se cancela el evento (equivale a «return funcion()»)
     p  = cancelar siempre el evento (equivale a «return false»)
   Atajos de HTML (sin JavaScript):
     data-h-href="url"          al hacer clic navega a esa dirección
     data-filtro="letras|numeros|alfanumerico"   en un campo, descarta al escribir los caracteres no permitidos */
(function (w, d) {
    'use strict';
    var EVENTOS = ['click', 'change', 'input', 'submit', 'keyup', 'keydown', 'keypress', 'mouseover', 'mouseout', 'dblclick'];

    function resolver(ruta) {
        var partes = String(ruta).split('.'), ctx = w, o = w;
        for (var i = 0; i < partes.length; i++) {
            ctx = o;
            o = o == null ? undefined : o[partes[i]];
        }
        return typeof o === 'function' ? { fn: o, ctx: ctx } : null;
    }

    function valorArg(a, el, ev) {
        if (a && typeof a === 'object') {
            if (a.$ === 'this') return el;
            if (a.$ === 'event') return ev;
            if (a.$ === 'value') return el.value;
            if (a.$attr !== undefined) return el.getAttribute(a.$attr);
        }
        return a;
    }

    function ejecutar(el, ev, spec) {
        var cancelar = !!spec.p;
        (spec.s || []).forEach(function (s) {
            var r = resolver(s.f);
            if (!r) { if (w.console) console.error('Manejador no encontrado: ' + s.f); return; }
            var res = r.fn.apply(r.ctx, (s.a || []).map(function (a) { return valorArg(a, el, ev); }));
            if (s.r && res === false) cancelar = true;
        });
        if (cancelar) ev.preventDefault();
    }

    EVENTOS.forEach(function (tipo) {
        var attr = 'data-h-' + tipo, sel = '[' + attr + ']';
        d.addEventListener(tipo, function (ev) {
            var origen = ev.target && ev.target.nodeType === 1 ? ev.target : (ev.target && ev.target.parentElement);
            var nodo = origen && origen.closest ? origen.closest(sel) : null;
            while (nodo) {
                var spec = null;
                try { spec = JSON.parse(nodo.getAttribute(attr)); } catch (e) { if (w.console) console.error('Atributo ' + attr + ' inválido', e); }
                if (spec) ejecutar(nodo, ev, spec);
                nodo = nodo.parentElement ? nodo.parentElement.closest(sel) : null;
            }
        }, false);
    });

    // Navegación por atributo (antes: onclick="location.href='...'")
    d.addEventListener('click', function (ev) {
        var el = ev.target && ev.target.closest ? ev.target.closest('[data-h-href]') : null;
        if (el) { w.location.href = el.getAttribute('data-h-href'); }
    }, false);

    // Filtros de teclado (antes: oninput="this.value = this.value.replace(/[^0-9]/g, '')")
    var PERMITIDOS = { letras: /[^A-Za-z]/g, numeros: /[^0-9]/g, alfanumerico: /[^A-Za-z0-9]/g };
    d.addEventListener('input', function (ev) {
        var el = ev.target;
        if (!el || !el.getAttribute) return;
        var f = el.getAttribute('data-filtro');
        if (f && PERMITIDOS[f] && typeof el.value === 'string') {
            var limpio = el.value.replace(PERMITIDOS[f], '');
            if (limpio !== el.value) el.value = limpio;
        }
    }, false);

    // Fragmentos HTML pedidos por AJAX (pestañas del hub de Auditoría, Historial de cargues…): se envía el nonce de ESTA página para
    // que el servidor lo use en los <script> del fragmento; con otro nonce la CSP no los ejecutaría (Infra/CabecerasSeguridad.cs).
    function nonceDePagina() {
        var s = d.querySelector('script[nonce]');
        return s ? (s.nonce || s.getAttribute('nonce') || '') : '';
    }
    if (w.jQuery) {
        w.jQuery.ajaxPrefilter(function (opciones, original, xhr) {
            if (opciones.crossDomain) return;
            var n = nonceDePagina();
            if (n) xhr.setRequestHeader('X-Csp-Nonce', n);
        });
    }
})(window, document);
