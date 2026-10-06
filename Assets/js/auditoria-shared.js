/* =====================================================================================
   Auditoría — utilidades compartidas (window.AudKit). Las 4 pestañas del módulo lo usan:
   período, chips de filtros activos, KPIs, paginador, impresión, esqueleto.
   Los textos NO viven aquí: se leen de atributos data-t-* del panel .ax-filtros (los pone el parcial
   _AudFiltros.cshtml desde los .resx), así el JS no hardcodea nada visible.
   Las pestañas se inyectan con $.load(): este archivo se evalúa de nuevo en cada carga, por eso se protege.
   ===================================================================================== */
(function (w, $) {
    'use strict';
    if (w.AudKit && w.AudKit.__v === 1) { return; }

    function T(k) { return $('.ax-filtros').attr('data-t-' + k) || ''; }
    function esc(s) { return $('<div>').text(s == null ? '' : s).html(); }
    function pad(n) { return ('0' + n).slice(-2); }
    function fmtISO(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }

    var AK = { __v: 1, esc: esc, T: T };

    AK.num = function (n) { return Number(n || 0).toLocaleString(); };

    /* ── Período (cápsula Hoy · 7 d · 30 d · Mes · personalizado) ─────────────────────── */
    AK.periodo = {
        init: function (onChange) {
            var $cap = $('#fPeriodo');
            if (!$cap.length) return;
            var self = this;
            $cap.off('click.ax').on('click.ax', '.ax-chip-per', function () {
                $cap.find('.ax-chip-per').removeClass('on');
                $(this).addClass('on');
                $('#wrapFechas').prop('hidden', $(this).attr('data-range') !== 'custom');
                self.pintar();
                if (onChange) onChange();
            });
            $('#fDesde, #fHasta').off('change.ax').on('change.ax', function () {
                if (self.actual() === 'custom') { self.pintar(); if (onChange) onChange(); }
            });
            this.pintar();
        },
        actual: function () { return $('#fPeriodo .ax-chip-per.on').attr('data-range') || $('#fPeriodo').attr('data-defecto') || '30'; },
        /** { desde, hasta } en yyyy-MM-dd. Personalizado con ambos campos vacíos = sin límite. */
        rango: function () {
            var r = this.actual();
            if (r === 'custom') return { desde: $('#fDesde').val() || '', hasta: $('#fHasta').val() || '' };
            var hoy = new Date();
            var d = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
            if (r === '7') d.setDate(d.getDate() - 6);
            else if (r === '30') d.setDate(d.getDate() - 29);
            else if (r === 'mes') d = new Date(hoy.getFullYear(), hoy.getMonth(), 1);
            return { desde: fmtISO(d), hasta: fmtISO(hoy) };
        },
        /** Texto corto del período para chips/Excel ("30 d", "Personalizado"…). */
        etiqueta: function () { return $('#fPeriodo .ax-chip-per.on').attr('title') || $('#fPeriodo .ax-chip-per.on').text().trim(); },
        pintar: function () {
            var $box = $('#audRango');
            var r = this.rango();
            if (!r.desde || !r.hasta) { $box.prop('hidden', true); return; }
            var meses = ($('#fPeriodo').attr('data-meses') || '').split(',');
            function corto(s) { var p = s.split('-'); return parseInt(p[2], 10) + ' ' + (meses[parseInt(p[1], 10) - 1] || ''); }
            var dias = Math.round((new Date(r.hasta) - new Date(r.desde)) / 86400000) + 1;
            if (isNaN(dias) || dias < 1) { $box.prop('hidden', true); return; }
            $('#audRangoTxt').text(r.desde === r.hasta ? corto(r.hasta) : corto(r.desde) + ' – ' + corto(r.hasta));
            $('#audRangoDias').text(dias + ' ' + (dias === 1 ? $('#fPeriodo').attr('data-dia') : $('#fPeriodo').attr('data-dias')));
            $box.prop('hidden', false);
        },
        reset: function () {
            var def = $('#fPeriodo').attr('data-defecto') || '30';
            $('#fPeriodo .ax-chip-per').removeClass('on');
            $('#fPeriodo .ax-chip-per[data-range="' + def + '"]').addClass('on');
            $('#fDesde, #fHasta').val('');
            $('#wrapFechas').prop('hidden', true);
            this.pintar();
        }
    };

    /** Enter dentro del buscador dispara la consulta. */
    AK.enter = function (fn) {
        $('#fTexto').off('keydown.ax').on('keydown.ax', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); fn(); }
        });
    };

    /* ── "Más filtros" ───────────────────────────────────────────────────────────────── */
    AK.masFiltros = function () {
        $('#axMas').off('click.ax').on('click.ax', function () {
            var abierto = $(this).attr('aria-expanded') === 'true';
            $(this).attr('aria-expanded', abierto ? 'false' : 'true');
            $('#axAvanzados').prop('hidden', abierto);
        });
    };

    /* ── Filtros: limpiar y chips de filtros activos ─────────────────────────────────── */
    AK.filtros = {
        limpiar: function () {
            $('.ax-filtros select').each(function () { $(this).val(''); });
            $('#fTexto').val('');
            AK.periodo.reset();
        },
        /** Chips de los selectores/buscador con valor (+ extras propios de la pestaña). Cada chip se quita con un clic. */
        chips: function (onChange, extras) {
            var items = [];
            $('.ax-filtros select').each(function () {
                var $s = $(this), v = $s.val();
                if (!v) return;
                var id = this.id;
                items.push({
                    label: $('label[for="' + id + '"]').text().trim(),
                    texto: $s.find('option:selected').text(),
                    quitar: function () { $s.val('').trigger('change'); onChange(); }
                });
            });
            var t = $.trim($('#fTexto').val());
            if (t) items.push({ label: T('buscar'), texto: t, quitar: function () { $('#fTexto').val(''); onChange(); } });
            (extras || []).forEach(function (i) { items.push(i); });
            AK.chips.render($('#axChips'), items);
        }
    };

    AK.chips = {
        render: function ($c, items) {
            $c.empty();
            if (!items || !items.length) { $c.prop('hidden', true); return; }
            $c.append($('<span class="ax-chips-t">').text(T('activos')));
            items.forEach(function (it) {
                var $ch = $('<span class="ax-chip">').text(it.label + ': ' + it.texto);
                $('<button type="button">').html('&times;')
                    .attr('aria-label', T('quitar') + ' ' + it.label)
                    .on('click', it.quitar).appendTo($ch);
                $c.append($ch);
            });
            $c.prop('hidden', false);
        }
    };

    /* ── KPIs ────────────────────────────────────────────────────────────────────────── */
    AK.kpis = {
        /** items: [{ label, valor, alerta?:bool, texto?:bool }] */
        render: function ($c, items) {
            $c.empty();
            if (!items || !items.length) { $c.prop('hidden', true); return; }
            items.forEach(function (i) {
                var $k = $('<div class="ax-kpi">').toggleClass('is-alerta', !!i.alerta);
                $k.append($('<small>').text(i.label));
                $k.append($('<b>').toggleClass('is-texto', !!i.texto).text(i.valor == null || i.valor === '' ? '—' : i.valor));
                $c.append($k);
            });
            $c.prop('hidden', false);
        }
    };

    /* ── Paginador (mismas clases .btn-pagination del sitio) ─────────────────────────── */
    AK.pager = {
        render: function ($el, actual, totalPags, onGo) {
            $el.empty();
            if (totalPags <= 1) return;
            function btn(label, dest, activo, disabled) {
                return $('<button type="button">').addClass('btn-pagination' + (activo ? ' active' : ''))
                    .prop('disabled', !!disabled).html(label).on('click', function () { onGo(dest); });
            }
            $el.append(btn('<i class="fas fa-chevron-left"></i>', actual - 1, false, actual === 1));
            var desde = Math.max(1, actual - 2), hasta = Math.min(totalPags, actual + 2);
            if (desde > 1) {
                $el.append(btn('1', 1, false, false));
                if (desde > 2) $el.append($('<span class="pagination-dots">').text('…'));
            }
            for (var p = desde; p <= hasta; p++) $el.append(btn(p, p, p === actual, false));
            if (hasta < totalPags) {
                if (hasta < totalPags - 1) $el.append($('<span class="pagination-dots">').text('…'));
                $el.append(btn(totalPags, totalPags, false, false));
            }
            $el.append(btn('<i class="fas fa-chevron-right"></i>', actual + 1, false, actual === totalPags));
        },
        /** "Mostrando 1–25 de 128" */
        info: function (pagina, tam, total) {
            if (!total) return '';
            var ini = (pagina - 1) * tam + 1, fin = Math.min(pagina * tam, total);
            return T('mostrando') + ' ' + ini + '–' + fin + ' ' + T('de') + ' ' + AK.num(total);
        }
    };

    /* ── Esqueleto mientras carga ────────────────────────────────────────────────────── */
    AK.esqueleto = function ($tbody, columnas, filas) {
        $tbody.empty();
        for (var r = 0; r < (filas || 6); r++) {
            var $tr = $('<tr class="skeleton-row">');
            for (var c = 0; c < columnas; c++) {
                $tr.append($('<td>').append($('<div class="skeleton-bar">').css('width', (45 + Math.floor(Math.random() * 45)) + '%')));
            }
            $tbody.append($tr);
        }
    };

    /* ── Imprimir el contenedor de resultados ────────────────────────────────────────── */
    AK.imprimir = function (selector, titulo) {
        var cont = document.querySelector(selector);
        if (!cont) return;
        var win = window.open('', '_blank');
        if (!win) return;
        win.document.write(
            '<!doctype html><html><head><meta charset="utf-8"><title>' + esc(titulo || document.title) + '</title>' +
            '<style>body{font:12px system-ui,-apple-system,sans-serif;padding:22px;color:#111;}' +
            'h1{font-size:15px;margin:0 0 12px;}table{border-collapse:collapse;width:100%;}' +
            'th,td{border:1px solid #bbb;padding:5px 7px;text-align:left;font-size:10.5px;vertical-align:top;}' +
            'th{background:#eee;} .info-badge,button,.btn,.pagination-controls,.ax-toolbar{display:none!important;}</style></head><body>' +
            '<h1>' + esc(titulo || document.title) + '</h1>' + cont.outerHTML + '</body></html>');
        win.document.close();
        win.focus();
        win.setTimeout(function () { win.print(); }, 300);
    };

    w.AudKit = AK;
})(window, jQuery);
