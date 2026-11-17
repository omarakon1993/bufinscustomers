/*
 * Secciones de un informe: cada parte del informe lleva su título (el mismo estilo «TÍTULO ———» de siempre) y se puede colapsar o expandir con un clic
 * en el título; la barra «Secciones» de arriba tiene un botón por sección y ambos controles son uno solo: colapsar la sección apaga su botón y apagar
 * el botón colapsa la sección. Lo que está expandido es lo que se exporta (PDF, imagen, Excel y correo): el informe usa `visible(id)` / `visibles()`.
 *
 *   var secc = InformeSecciones.montar({
 *       barra: '#pygSecBarra', clave: 'pyg',
 *       textos: { etiqueta, todas, ninguna, sinSeleccion },
 *       secciones: [ { id: 'kpis', titulo: 'Indicadores clave', icono: 'fa-gauge-high',
 *                      cab: '#pygSecKpis .pyg2-section-title',      // fila del título (lleva un .t con el texto); el texto del botón sale de ahí
 *                      cuerpo: ['#pygKpiStrip'] }, ... ]            // lo que se oculta al colapsar (el título siempre queda a la vista)
 *   });
 *   secc.visible('kpis')       // true si está expandida y tiene datos
 *   secc.visibles()            // ["kpis", "cascada", ...]
 *   secc.disponible('lineas', false)   // sin datos para este informe: se oculta (título y botón) y no se exporta
 *   secc.refrescarTitulos()    // vuelve a leer los títulos (p. ej. «Tendencia mensual 2026» cambia con el rango)
 *
 * La elección se recuerda en el navegador (solo conveniencia; se guarda lo colapsado, así las secciones nuevas aparecen expandidas).
 * Sin dependencias salvo jQuery. Estilos: Assets/css/informe-secciones.css.
 */
window.InformeSecciones = (function () {
    'use strict';

    function leerColapsadas(clave) {
        try { var v = JSON.parse(localStorage.getItem('secciones.' + clave) || '[]'); return Array.isArray(v) ? v : []; } catch (e) { return []; }
    }
    function guardarColapsadas(clave, ids) { try { localStorage.setItem('secciones.' + clave, JSON.stringify(ids)); } catch (e) { } }
    function texto(s) { return $('<span>').text(s == null ? '' : s).html(); }

    function montar(cfg) {
        var $barra = $(cfg.barra), T = cfg.textos || {};
        var estado = {};
        var oyentes = [];
        var colapsadas = leerColapsadas(cfg.clave);

        cfg.secciones.forEach(function (s) { estado[s.id] = { mostrar: colapsadas.indexOf(s.id) < 0, disponible: true }; });

        function buscar(id) { return cfg.secciones.filter(function (x) { return x.id === id; })[0]; }
        function $cab(s) { return $(s.cab); }
        function $cuerpo(s) { return $($.isArray(s.cuerpo) ? s.cuerpo.join(',') : s.cuerpo); }
        function visible(id) { var e = estado[id]; return !!e && e.mostrar && e.disponible; }
        function visibles() { return cfg.secciones.filter(function (s) { return visible(s.id); }).map(function (s) { return s.id; }); }
        function etiqueta(s) {
            var t = $cab(s).find('.t').first().text().replace(/\s+/g, ' ').trim();
            return t || s.titulo || s.id;
        }

        function aplicar(s, aviso) {
            var e = estado[s.id], vis = visible(s.id);
            $cuerpo(s).toggleClass('sec-oculta', !vis);
            $cab(s).toggleClass('sec-oculta', !e.disponible).toggleClass('sec-colapsada', !e.mostrar)
                .find('.t').first().attr('aria-expanded', e.mostrar ? 'true' : 'false');
            $barra.find('.sec-chip[data-sec="' + s.id + '"]')
                .toggleClass('on', vis).attr('aria-pressed', vis ? 'true' : 'false').prop('hidden', !e.disponible);
            if (aviso) oyentes.forEach(function (fn) { try { fn(s.id, vis); } catch (err) { if (window.console) console.error(err); } });
        }
        function persistir() {
            guardarColapsadas(cfg.clave, cfg.secciones.filter(function (s) { return !estado[s.id].mostrar; }).map(function (s) { return s.id; }));
        }
        function actualizarAviso() { $barra.find('.sec-vacio').prop('hidden', visibles().length > 0); }
        function fijar(id, mostrar, aviso) {
            var s = buscar(id); if (!s) return;
            estado[id].mostrar = !!mostrar; aplicar(s, aviso !== false); persistir(); actualizarAviso();
            if (mostrar) $cuerpo(s).addClass('sec-aparece');
        }

        // ---- barra de chips ----
        var h = '<span class="sec-et"><i class="fas fa-table-list"></i>' + texto(T.etiqueta) + '</span><div class="sec-chips">';
        cfg.secciones.forEach(function (s) {
            h += '<button type="button" class="sec-chip" data-sec="' + s.id + '" aria-pressed="true"><i class="fas fa-check sec-chk"></i>' +
                (s.icono ? '<i class="fas ' + s.icono + ' sec-ico"></i>' : '') + '<span class="sec-chip-t">' + texto(s.titulo) + '</span></button>';
        });
        h += '</div><div class="sec-acc"><button type="button" class="sec-link" data-acc="todas">' + texto(T.todas) + '</button>' +
            '<button type="button" class="sec-link" data-acc="ninguna">' + texto(T.ninguna) + '</button></div>' +
            '<div class="sec-vacio" hidden>' + texto(T.sinSeleccion) + '</div>';
        $barra.addClass('secciones-bar').html(h);

        $barra.on('click', '.sec-chip', function () { var id = $(this).data('sec'); fijar(id, !estado[id].mostrar); });
        $barra.on('click', '.sec-link', function () {
            var todas = $(this).data('acc') === 'todas';
            cfg.secciones.forEach(function (s) { estado[s.id].mostrar = todas; aplicar(s, true); if (todas) $cuerpo(s).addClass('sec-aparece'); });
            persistir(); actualizarAviso();
        });

        // ---- títulos de sección: clic para colapsar / expandir ----
        cfg.secciones.forEach(function (s) {
            var $c = $cab(s).addClass('sec-cab');
            var $t = $c.find('.t').first().attr({ role: 'button', tabindex: '0', 'aria-expanded': 'true' });
            if (!$c.find('.sec-chev').length) $('<i class="fas fa-chevron-down sec-chev" aria-hidden="true"></i>').insertAfter($t);
            $c.on('click', function (ev) {
                // los controles propios del título (segmentados, leyendas, botones) no colapsan la sección
                if ($(ev.target).closest('.pyg2-seg, .pyg2-legend, button, a, input, select, label').length) return;
                fijar(s.id, !estado[s.id].mostrar);
            });
            $t.on('keydown', function (ev) { if (ev.which === 13 || ev.which === 32) { ev.preventDefault(); fijar(s.id, !estado[s.id].mostrar); } });
        });

        // quita la clase de entrada al terminar la animación (para que no se repita al repintar)
        $(document).on('animationend', '.sec-aparece', function () { $(this).removeClass('sec-aparece'); });

        function refrescarTitulos() { cfg.secciones.forEach(function (s) { $barra.find('.sec-chip[data-sec="' + s.id + '"] .sec-chip-t').text(etiqueta(s)); }); }

        cfg.secciones.forEach(function (s) { aplicar(s, false); });
        refrescarTitulos(); actualizarAviso();

        return {
            visible: visible,
            visibles: visibles,
            fijar: fijar,
            refrescarTitulos: refrescarTitulos,
            /** Marca si la sección tiene datos para el informe actual (sin datos: se oculta con su título y deja de ofrecerse). */
            disponible: function (id, hay) {
                var s = buscar(id); if (!s) return;
                estado[id].disponible = !!hay; aplicar(s, false); actualizarAviso();
            },
            /** Vuelve a aplicar las clases a los elementos (tras repintar el informe, por si el DOM se recreó). */
            reaplicar: function () { cfg.secciones.forEach(function (s) { aplicar(s, false); }); refrescarTitulos(); actualizarAviso(); },
            alCambiar: function (fn) { oyentes.push(fn); }
        };
    }

    return { montar: montar };
})();
