/* Tema de colores de los informes — UNA sola implementación para todos (ver CLAUDE.md, «Tema de colores de los informes — ESTÁNDAR»).
   Requiere jQuery. El markup de la sección «Estilos» es Views/Shared/_TemaInformeSelector.cshtml.

   Uso típico en un informe:
     InformeTema.montar({ selector: '#pygTema', empresa: '#pygEmpresa' });      // una vez (carga las corporativas de la empresa)
     InformeTema.preparar().then(function (ok) { if (!ok) return; ...consultar... });   // al pulsar Generar
     InformeTema.aplicar(document.getElementById('pygResultado'));              // al pintar: variables CSS + InformeTema.actual
     $.extend(filtrosConsultados, InformeTema.parametros());                    // Excel y correo re-resuelven el tema en el servidor
     InformeTema.reset();                                                        // en Limpiar (vuelve a Bufins)
   Gráficos: InformeTema.actual.p / .a / .s / .tercero / .strong / .serie[i]. PDF: InformeTema.pdf(PDF_COL), InformeTema.pdfDegradado().
   Opciones: Bufins (por defecto) · Corporativa 1 y 2 de la empresa elegida (deshabilitadas con «Sin registrar» si no existen) · Personalizada.
   El algoritmo que deriva los colores vive SOLO en C# (Helpers/TemaInforme.cs): aquí no se reimplementa. DEFECTO es únicamente el
   respaldo idéntico al tema Bufins para que los informes funcionen aunque falle la petición. */
(function () {
    // Respaldo = tema Bufins (mismos valores que TemaInforme.Bufins en C#).
    var DEFECTO = {
        id: 'bufins', nombre: 'Bufins', p: '#6366F1', s: '#01E1D7', a: '#D97706', strong: '#583AFF', strongOscuro: '#3B2FBF', tercero: '#0EA5E9',
        soft: '#EEF2FF', soft2: '#F4F3FB', soft3: '#F8F7FD', t1: '#E0E7FF', t2: '#C7D2FE', t3: '#A5B4FC', t4: '#818CF8', dk: '#3B2FBF', pRgb: [99, 102, 241],
        muestra: 'linear-gradient(90deg,#6366F1 0%,#3B82F6 35%,#06B6D4 70%,#01E1D7 100%)',
        comp: '#D97706', sText: '#0891B2', sOn: '#160933',
        grad: ['#6366F1', '#3B82F6', '#06B6D4', '#01E1D7'],
        gradCss: 'linear-gradient(90deg,#6366F1 0%,#3B82F6 35%,#06B6D4 70%,#01E1D7 100%)',
        serie: ['#6366F1', '#D97706', '#06B6D4', '#16A34A', '#DC2626', '#8B5CF6', '#0EA5E9', '#F59E0B'],
        pdf: {
            marca: [99, 102, 241], morado: [88, 58, 255], totalTx: [59, 47, 191], totalBg: [238, 242, 255], suave: [244, 243, 251], suave2: [248, 247, 253],
            ambar: [217, 119, 6], cian: [14, 165, 233], degradado: [[99, 102, 241], [59, 130, 246], [6, 182, 212], [1, 225, 215]]
        }
    };
    var VARS = ['--tm-p', '--tm-p-rgb', '--tm-strong', '--tm-strong-rgb', '--tm-s', '--tm-s-rgb', '--tm-c', '--tm-a', '--tm-grad', '--tm-soft', '--tm-soft2', '--tm-soft3', '--tm-t1', '--tm-t2', '--tm-t3', '--tm-t4', '--tm-dk', '--tm-wash', '--tm-wash2', '--tm-comp', '--tm-s-text', '--tm-icon-bg', '--tm-icon-on', '--tm-wash-s', '--tm-line-h'];
    var HEX = /^#?[0-9A-Fa-f]{6}$/;

    var raiz = null, cfg = null;
    var opciones = { presets: [DEFECTO], corporativas: [], sinEmpresa: true };
    var sel = { tema: 'bufins', tokens: DEFECTO, p: '', s: '', a: '' };      // lo elegido en la sección
    var pendiente = DEFECTO;                                                  // tema listo para aplicar al pintar
    var aplicado = { tema: 'bufins', p: '', s: '', a: '' };                  // lo que se ve en pantalla (y se exporta)
    var actual = DEFECTO;
    var timer = null, turnoOpciones = 0;

    function hexRgb(h) { h = h.replace('#', ''); return [parseInt(h.substr(0, 2), 16), parseInt(h.substr(2, 2), 16), parseInt(h.substr(4, 2), 16)]; }
    function norm(v) { v = (v || '').trim(); if (!v) return ''; if (v.charAt(0) !== '#') v = '#' + v; return v.toUpperCase(); }
    function $r(s) { return raiz.querySelector(s); }
    function todos(s) { return Array.prototype.slice.call(raiz.querySelectorAll(s)); }

    // ---------------------------------------------------------------- variables CSS
    function fijarVariables(el, t) {
        if (!el) return;
        if (t.id === 'bufins') { VARS.forEach(function (v) { el.style.removeProperty(v); }); return; }   // Bufins = valores de siempre (fallbacks del CSS)
        var st = el.style;
        st.setProperty('--tm-p', t.p); st.setProperty('--tm-p-rgb', t.pRgb.join(','));
        st.setProperty('--tm-strong', t.strong); st.setProperty('--tm-strong-rgb', hexRgb(t.strong).join(','));
        st.setProperty('--tm-s', t.s); st.setProperty('--tm-s-rgb', hexRgb(t.s).join(','));
        st.setProperty('--tm-c', t.tercero); st.setProperty('--tm-a', t.a);
        st.setProperty('--tm-grad', t.gradCss);
        st.setProperty('--tm-soft', t.soft); st.setProperty('--tm-soft2', t.soft2); st.setProperty('--tm-soft3', t.soft3);
        st.setProperty('--tm-t1', t.t1); st.setProperty('--tm-t2', t.t2); st.setProperty('--tm-t3', t.t3); st.setProperty('--tm-t4', t.t4); st.setProperty('--tm-dk', t.dk);
        // Fondos suaves decorativos: color FIJO (tinte del principal) en lugar del degradado principal→secundario de Bufins.
        // Papel del SECUNDARIO: iconos y títulos de tarjetas, líneas de sección, serie de comparación (leyendas) y etiquetas de familia.
        st.setProperty('--tm-comp', t.comp); st.setProperty('--tm-s-text', t.sText);
        st.setProperty('--tm-line-h', '2px'); st.setProperty('--tm-icon-bg', t.s); st.setProperty('--tm-icon-on', t.sOn);
        st.setProperty('--tm-wash-s', 'rgba(' + hexRgb(t.s).join(',') + ',.16)');
        st.setProperty('--tm-wash', 'rgba(' + t.pRgb.join(',') + ',.12)'); st.setProperty('--tm-wash2', 'rgba(' + t.pRgb.join(',') + ',.24)');
    }

    // ---------------------------------------------------------------- sección «Estilos» (UI)
    function puntos(t) { return t.serie.slice(0, 3).map(function (c) { return '<i style="background:' + c + '"></i>'; }).join(''); }

    function nuevaFicha(id, nombre, tema, deshabilitada, etiqueta, tip) {
        var b = document.createElement('button');
        b.type = 'button'; b.className = 'ti-ficha'; b.setAttribute('data-id', id);
        if (tema) b.innerHTML = '<span class="ti-mini" style="background:' + tema.muestra + '"></span><span class="ti-puntos">' + puntos(tema) + '</span><span class="ti-nom"></span>';
        else b.innerHTML = '<span class="ti-mini ti-mini-vacio"></span><span class="ti-nom"></span>';
        b.querySelector('.ti-nom').textContent = nombre;
        if (etiqueta) { var e = document.createElement('span'); e.className = 'ti-tag'; e.textContent = etiqueta; b.appendChild(e); }
        if (tip) b.title = tip;
        if (deshabilitada) { b.disabled = true; b.classList.add('ti-ficha-off'); }
        else b.addEventListener('click', function () { elegir(id); });
        return b;
    }

    function buscar(id) {
        if (id === 'bufins') return opciones.presets[0] || DEFECTO;
        for (var i = 0; i < opciones.corporativas.length; i++) {
            var c = opciones.corporativas[i];
            if (c.registrada && c.tema && c.tema.id === id) return c.tema;
        }
        return null;
    }

    function pintarFichas() {
        var cont = $r('[data-grupo="todas"]');
        cont.innerHTML = '';
        var bufins = opciones.presets[0] || DEFECTO;
        cont.appendChild(nuevaFicha('bufins', bufins.nombre, bufins, false));

        opciones.corporativas.forEach(function (c) {
            if (c.registrada && c.tema) cont.appendChild(nuevaFicha(c.tema.id, c.nombre, c.tema, false));
            else cont.appendChild(nuevaFicha('corp-vacia-' + c.slot, c.nombre, null, true, cfg.txtSinRegistrar, opciones.sinEmpresa ? cfg.txtElegirEmpresa : cfg.txtSinRegistrarTip));
        });

        var cust = document.createElement('button');
        cust.type = 'button'; cust.className = 'ti-ficha'; cust.setAttribute('data-id', 'custom');
        cust.innerHTML = '<i class="fas fa-eye-dropper ti-ico"></i><span class="ti-nom"></span>';
        cust.querySelector('.ti-nom').textContent = cfg.txtPersonalizada;
        cust.addEventListener('click', function () { elegir('custom'); });
        cont.appendChild(cust);
        marcar();
    }

    function marcar() {
        todos('.ti-ficha').forEach(function (b) { b.setAttribute('aria-pressed', b.getAttribute('data-id') === sel.tema ? 'true' : 'false'); });
        $r('.ti-custom').hidden = sel.tema !== 'custom';
    }

    function elegir(id) {
        sel.tema = id;
        if (id === 'custom') {
            var i = todos('.ti-custom .pc-hex');
            if (!i[0].value) { i[0].value = '#583AFF'; i[1].value = '#01E1D7'; sincronizarPickers(); }
            sel.tokens = null;
            validarPersonalizada(true);
        } else {
            sel.tokens = buscar(id) || DEFECTO;
            if (!buscar(id)) sel.tema = 'bufins';
        }
        marcar();
    }

    function sincronizarPickers() {
        todos('.ti-custom .pc-hex').forEach(function (h) {
            var p = raiz.querySelector('.ti-custom .pc-picker[data-campo="' + h.getAttribute('data-campo') + '"]');
            if (HEX.test(h.value.trim())) p.value = norm(h.value).toLowerCase();
        });
    }

    function leerCustom() {
        function v(c) { return norm(raiz.querySelector('.ti-custom .pc-hex[data-campo="' + c + '"]').value); }
        return { p: v('primario'), s: v('secundario'), a: v('acento') };
    }

    function mensajeCustom(t, tipo) { var m = $r('.ti-custom-msg'); m.textContent = t || ''; m.className = 'ti-custom-msg' + (tipo ? ' ' + tipo : ''); }

    // Pide a C# los tokens de la paleta personalizada. Devuelve Promise<tokens|null> (null = colores inválidos, ya se avisó).
    function validarPersonalizada(inmediato) {
        clearTimeout(timer);
        return new Promise(function (resolve) {
            function ir() {
                var c = leerCustom();
                sel.p = c.p; sel.s = c.s; sel.a = c.a;
                if (!HEX.test(c.p) || !HEX.test(c.s) || (c.a && !HEX.test(c.a))) {
                    sel.tokens = null; mensajeCustom(cfg.txtHex, 'err'); return resolve(null);
                }
                var q = new URLSearchParams({ tema: 'custom', p: c.p, s: c.s, a: c.a || '' });
                fetch(cfg.urlResolver + '?' + q.toString(), { credentials: 'same-origin', cache: 'no-store' })
                    .then(function (r) { return r.json(); })
                    .then(function (j) {
                        if (!j.exito) { sel.tokens = null; mensajeCustom(j.mensaje, 'err'); return resolve(null); }
                        sel.tokens = j.tema; mensajeCustom(j.aviso || '', j.aviso ? 'aviso' : ''); resolve(j.tema);
                    })
                    .catch(function () { sel.tokens = null; mensajeCustom(cfg.txtError, 'err'); resolve(null); });
            }
            if (inmediato) ir(); else timer = setTimeout(ir, 250);
        });
    }

    function enlazarCustom() {
        todos('.ti-custom .pc-picker').forEach(function (p) {
            p.addEventListener('input', function () {
                raiz.querySelector('.ti-custom .pc-hex[data-campo="' + p.getAttribute('data-campo') + '"]').value = p.value.toUpperCase();
                validarPersonalizada(false);
            });
        });
        todos('.ti-custom .pc-hex').forEach(function (h) {
            h.addEventListener('input', function () { sincronizarPickers(); validarPersonalizada(false); });
            h.addEventListener('blur', function () { if (HEX.test(h.value.trim())) h.value = norm(h.value); });
        });
    }

    function cargarOpciones() {
        var yo = ++turnoOpciones;
        var id = cfg.empresa ? (window.jQuery(cfg.empresa).val() || '') : '';
        var url = cfg.urlOpciones + (id ? '?idEmpresa=' + encodeURIComponent(id) : '');
        return fetch(url, { credentials: 'same-origin', cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (j) {
                if (yo !== turnoOpciones) return;
                opciones = { presets: j.presets && j.presets.length ? j.presets : [DEFECTO], corporativas: j.corporativas || [], sinEmpresa: !id };
                // Si estaba elegida una corporativa que ya no existe (otra empresa), vuelve a Bufins.
                if (sel.tema !== 'custom') {
                    sel.tokens = buscar(sel.tema);
                    if (!sel.tokens) { sel.tema = 'bufins'; sel.tokens = buscar('bufins') || DEFECTO; }
                }
                pintarFichas();
            })
            .catch(function () { opciones = { presets: [DEFECTO], corporativas: [], sinEmpresa: !id }; pintarFichas(); });
    }

    // ---------------------------------------------------------------- API
    window.InformeTema = {
        get actual() { return actual; },
        defecto: DEFECTO,

        montar: function (opts) {
            raiz = document.querySelector(opts.selector);
            if (!raiz) return;
            cfg = {
                empresa: opts.empresa || null,
                urlOpciones: raiz.getAttribute('data-url-opciones'), urlResolver: raiz.getAttribute('data-url-resolver'),
                txtPersonalizada: raiz.getAttribute('data-txt-personalizada'), txtHex: raiz.getAttribute('data-txt-hex'), txtError: raiz.getAttribute('data-txt-error'),
                txtSinRegistrar: raiz.getAttribute('data-txt-sin-registrar'), txtSinRegistrarTip: raiz.getAttribute('data-txt-sin-registrar-tip'),
                txtElegirEmpresa: raiz.getAttribute('data-txt-elegir-empresa')
            };
            enlazarCustom();
            if (cfg.empresa && window.jQuery) window.jQuery(cfg.empresa).on('change', cargarOpciones);
            return cargarOpciones();
        },

        /** Al pulsar Generar. Promise<boolean>: false = la paleta personalizada no es válida (ya se avisó): no consultar. */
        preparar: function () {
            if (!raiz) { pendiente = DEFECTO; aplicado = { tema: 'bufins', p: '', s: '', a: '' }; return Promise.resolve(true); }
            if (sel.tema === 'custom') {
                return validarPersonalizada(true).then(function (t) {
                    if (!t) return false;
                    pendiente = t; return true;
                });
            }
            pendiente = sel.tokens || DEFECTO;
            return Promise.resolve(true);
        },

        /** Al pintar el resultado: variables CSS en el contenedor y tema «actual» para gráficos y PDF. */
        aplicar: function (contenedor) {
            actual = pendiente || DEFECTO;
            aplicado = sel.tema === 'custom'
                ? { tema: 'custom', p: sel.p, s: sel.s, a: sel.a }
                : { tema: actual.id === 'bufins' ? 'bufins' : actual.id, p: '', s: '', a: '' };
            fijarVariables(contenedor, actual);
            return actual;
        },

        /** Campos para Excel y correo (el servidor re-resuelve y valida; nunca confía en colores del navegador). */
        parametros: function () { return { Tema: aplicado.tema, TemaP: aplicado.p, TemaS: aplicado.s, TemaA: aplicado.a }; },

        /** Limpiar: vuelve a Bufins (la elección no se recuerda). */
        reset: function () {
            sel = { tema: 'bufins', tokens: buscar('bufins') || DEFECTO, p: '', s: '', a: '' };
            if (raiz) { mensajeCustom(''); marcar(); }
        },

        /** Copia de los colores base del PDF con las claves de marca sustituidas por las del tema actual. */
        pdf: function (base) {
            var o = {}, k, t = actual.pdf;
            for (k in base) o[k] = base[k];
            ['marca', 'morado', 'totalTx', 'totalBg', 'suave', 'suave2', 'ambar', 'cian'].forEach(function (c) { if (t[c] && (c in base)) o[c] = t[c]; });
            return o;
        },
        pdfDegradado: function () { return actual.pdf.degradado; },

        /** rgba() de un #RRGGBB (para rellenos suaves de gráficos). */
        alfa: function (hex, a) { var c = hexRgb(hex); return 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + a + ')'; }
    };
})();
