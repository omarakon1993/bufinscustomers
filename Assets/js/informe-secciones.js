/*
 * Secciones de un informe: barra de chips para mostrar u ocultar cada parte del informe (Indicadores clave, Cascada, Tendencia, Tabla, ...).
 * Lo que está visible es lo que se exporta (PDF, imagen, Excel y correo): el informe llama a `visibles()` / `visible(id)`.
 *
 *   var secc = InformeSecciones.montar({
 *       barra: '#pygSecBarra', clave: 'pyg',
 *       textos: { etiqueta, todas, ninguna, sinSeleccion },
 *       secciones: [ { id: 'kpis', titulo: 'Indicadores clave', icono: 'fa-gauge-high', el: ['#pygSecKpis'] }, ... ]
 *   });
 *   secc.visible('kpis')        // true si el usuario la muestra y tiene datos
 *   secc.visibles()             // ["kpis", "cascada", ...]
 *   secc.disponible('lineas', false)   // la sección no tiene datos para este informe: se oculta y no se ofrece
 *   secc.alCambiar(function (id, mostrar) { ... })   // p. ej. para redibujar gráficos al mostrarlos
 *
 * La elección se recuerda en el navegador (solo conveniencia; lo oculto, no lo visible, para que las secciones nuevas aparezcan solas).
 * Sin dependencias salvo jQuery. Estilos: Assets/css/informe-secciones.css.
 */
window.InformeSecciones = (function () {
    'use strict';

    function leerOcultas(clave) {
        try { var v = JSON.parse(localStorage.getItem('secciones.' + clave) || '[]'); return Array.isArray(v) ? v : []; } catch (e) { return []; }
    }
    function guardarOcultas(clave, ids) { try { localStorage.setItem('secciones.' + clave, JSON.stringify(ids)); } catch (e) { } }

    function montar(cfg) {
        var $barra = $(cfg.barra), T = cfg.textos || {};
        var estado = {};        // id -> { mostrar: bool, disponible: bool }
        var oyentes = [];
        var ocultas = leerOcultas(cfg.clave);

        cfg.secciones.forEach(function (s) { estado[s.id] = { mostrar: ocultas.indexOf(s.id) < 0, disponible: true }; });

        function elementos(s) {
            var el = typeof s.el === 'function' ? s.el() : s.el;
            return $($.isArray(el) ? el.join(',') : el);
        }
        function visible(id) { var e = estado[id]; return !!e && e.mostrar && e.disponible; }
        function visibles() { return cfg.secciones.filter(function (s) { return visible(s.id); }).map(function (s) { return s.id; }); }

        function aplicar(s, aviso) {
            elementos(s).toggleClass('sec-oculta', !visible(s.id));
            $barra.find('.sec-chip[data-sec="' + s.id + '"]')
                .toggleClass('on', estado[s.id].mostrar && estado[s.id].disponible)
                .toggleClass('sin-datos', !estado[s.id].disponible)
                .attr('aria-pressed', visible(s.id) ? 'true' : 'false')
                .prop('hidden', !estado[s.id].disponible);
            if (aviso) oyentes.forEach(function (fn) { try { fn(s.id, visible(s.id)); } catch (e) { if (window.console) console.error(e); } });
        }
        function persistir() {
            guardarOcultas(cfg.clave, cfg.secciones.filter(function (s) { return !estado[s.id].mostrar; }).map(function (s) { return s.id; }));
        }
        function actualizarAviso() {
            $barra.find('.sec-vacio').prop('hidden', visibles().length > 0);
        }
        function fijar(id, mostrar, aviso) {
            var s = cfg.secciones.filter(function (x) { return x.id === id; })[0]; if (!s) return;
            estado[id].mostrar = !!mostrar; aplicar(s, aviso !== false); persistir(); actualizarAviso();
        }

        // ---- pintar la barra ----
        var h = '<span class="sec-et"><i class="fas fa-table-list"></i>' + $('<span>').text(T.etiqueta || '').html() + '</span><div class="sec-chips">';
        cfg.secciones.forEach(function (s) {
            h += '<button type="button" class="sec-chip" data-sec="' + s.id + '" aria-pressed="true"><i class="fas fa-check sec-chk"></i>' +
                (s.icono ? '<i class="fas ' + s.icono + ' sec-ico"></i>' : '') + '<span>' + $('<span>').text(s.titulo).html() + '</span></button>';
        });
        h += '</div><div class="sec-acc"><button type="button" class="sec-link" data-acc="todas">' + $('<span>').text(T.todas || '').html() + '</button>' +
            '<button type="button" class="sec-link" data-acc="ninguna">' + $('<span>').text(T.ninguna || '').html() + '</button></div>' +
            '<div class="sec-vacio" hidden>' + $('<span>').text(T.sinSeleccion || '').html() + '</div>';
        $barra.addClass('secciones-bar').html(h);

        $barra.on('click', '.sec-chip', function () {
            var id = $(this).data('sec');
            fijar(id, !estado[id].mostrar);
        });
        $barra.on('click', '.sec-link', function () {
            var todas = $(this).data('acc') === 'todas';
            cfg.secciones.forEach(function (s) { estado[s.id].mostrar = todas; aplicar(s, true); });
            persistir(); actualizarAviso();
        });

        cfg.secciones.forEach(function (s) { aplicar(s, false); });
        actualizarAviso();

        return {
            visible: visible,
            visibles: visibles,
            fijar: fijar,
            /** Marca si la sección tiene datos para el informe actual (sin datos: se oculta y deja de ofrecerse). */
            disponible: function (id, hay) {
                var s = cfg.secciones.filter(function (x) { return x.id === id; })[0]; if (!s) return;
                estado[id].disponible = !!hay; aplicar(s, false); actualizarAviso();
            },
            /** Vuelve a aplicar las clases a los elementos (llamar tras repintar el informe, por si el DOM se recreó). */
            reaplicar: function () { cfg.secciones.forEach(function (s) { aplicar(s, false); }); actualizarAviso(); },
            alCambiar: function (fn) { oyentes.push(fn); }
        };
    }

    return { montar: montar };
})();
