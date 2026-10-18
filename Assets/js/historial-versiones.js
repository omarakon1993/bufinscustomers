/* Pestaña «Versiones» del Historial de cargues: pinta las versiones (agrupadas o en lista), aplica filtros y
   maneja la confirmación de «Deshacer este cargue».
   Lee la configuración de window._hv, que arma la vista Razor:
     _hv.datos   → versiones [{ id, empresa, anio, modo, modoTexto, esc, version, fecha, hora, ts, usuario, archivo, filas, cero, estado, actual }]
     _hv.max / esSuper / puedeRollback / okMsg
     _hv.t       → textos ya traducidos */
(function ($) {
    'use strict';
    var H = window._hv;
    if (!H) return;

    $(function () {
        var vista = 'grupo';
        try { vista = localStorage.getItem('hv.vista') === 'lista' ? 'lista' : 'grupo'; } catch (e) { }
        var pendiente = null;

        function esc(s) { return $('<div>').text(s == null ? '' : s).html(); }
        function iniciales(n) {
            var p = String(n || '').trim().split(/\s+/);
            return ((p[0] || '').charAt(0) + (p.length > 1 ? p[p.length - 1].charAt(0) : '')).toUpperCase() || '?';
        }
        function claveEstado(e) {
            e = String(e || '').toLowerCase();
            return e === 'actual' ? 'actual' : (e === 'revertido' ? 'revertido' : 'anterior');
        }

        // ---- Cápsulas (mismas clases .cg-pill que usa la pestaña «Registro») ----
        function pillModo(f) {
            var ej = f.modo === 0;
            return '<span class="cg-pill cg-pill-' + (ej ? 'ejec' : 'hist') + '"><i class="fas ' + (ej ? 'fa-play' : 'fa-history') + '"></i>' + esc(f.modoTexto) + '</span>';
        }
        function pillEsc(f) {
            return '<span class="cg-pill cg-pill-esc"><i class="fas fa-layer-group"></i>' + esc(H.t.escCorto) + ' ' + f.esc + '</span>';
        }
        function pillEstado(f) {
            var k = claveEstado(f.estado);
            return '<span class="cg-pill cg-pill-estado cg-pill-' + k + '">' + esc(H.t['est_' + k]) + '</span>';
        }
        function avatar(n) { return '<span class="hv-avatar">' + esc(iniciales(n)) + '</span>'; }
        function btnDeshacer(f, corto) {
            return H.puedeRollback
                ? '<button type="button" class="cg-btn hv-restore" data-id="' + f.id + '" title="' + esc(H.t.deshacerTip) + '"><i class="fas fa-undo"></i>' + esc(corto ? H.t.deshacerCorto : H.t.restaurar) + '</button>'
                : '';
        }

        function filtrados() {
            var emp = $('#filterEmpresa').val() || '', anio = $('#filterAnio').val() || '',
                modo = $('#filterModo').val() || '', e = $('#filterEscenario').val() || '';
            return H.datos.filter(function (f) {
                return (!emp || f.empresa === emp) && (!anio || String(f.anio) === anio) &&
                       (modo === '' || String(f.modo) === modo) && (!e || String(f.esc) === e);
            });
        }
        function clave(f) { return f.empresa + '|' + f.anio + '|' + f.modo + '|' + f.esc; }

        // ---- Vista agrupada: tarjeta por empresa · año · modo · escenario, con línea de tiempo ----
        function versionHtml(f) {
            return '<li class="hv-ver' + (f.actual ? ' is-actual' : '') + '"><span class="hv-nodo"></span>' +
                '<div class="hv-ver-main">' +
                    '<div class="hv-ver-top"><span class="hv-ver-fecha">' + f.fecha + '</span><span class="hv-ver-hora">' + f.hora + '</span>' +
                        pillEstado(f) + '<span class="hv-ver-num" title="' + esc(H.t.versionTip) + '">' + esc(f.version) + '</span></div>' +
                    '<div class="hv-ver-meta">' +
                        '<span>' + avatar(f.usuario) + esc(f.usuario) + '</span>' +
                        '<span class="hv-file" title="' + esc(f.archivo) + '"><i class="fas fa-file-excel"></i><span>' + esc(f.archivo) + '</span></span>' +
                        '<span class="hv-ver-filas' + (f.cero ? ' cero' : '') + '"><b>' + esc(f.filas) + '</b> ' + esc(H.t.filasCopia) + '</span>' +
                    '</div>' +
                '</div>' +
                '<div class="hv-ver-act">' + btnDeshacer(f) + '</div></li>';
        }

        function renderGrupos(rows) {
            var grupos = {}, orden = [];
            rows.forEach(function (f) { var k = clave(f); if (!grupos[k]) { grupos[k] = []; orden.push(k); } grupos[k].push(f); });
            orden.sort(function (a, b) {
                var x = grupos[a][0], y = grupos[b][0];
                return x.empresa.localeCompare(y.empresa) || (y.anio - x.anio) || (x.modo - y.modo) || (x.esc - y.esc);
            });
            var html = '<div class="hv-grupos">';
            orden.forEach(function (k) {
                var items = grupos[k].slice().sort(function (a, b) { return b.ts - a.ts; }), g = items[0];
                var total = H.datos.filter(function (f) { return clave(f) === k; }).length;
                var lleno = total >= H.max, barras = '';
                for (var i = 0; i < H.max; i++) barras += '<span class="hv-slot' + (i < total ? ' on' : '') + '"></span>';
                html += '<article class="hv-grupo"><header class="hv-grupo-cab">' +
                    (H.esSuper ? '<span class="hv-grupo-emp">' + esc(g.empresa) + '</span>' : '') +
                    '<span class="hv-grupo-anio">' + g.anio + '</span>' + pillModo(g) + pillEsc(g) +
                    '<span class="hv-slots' + (lleno ? ' full' : '') + '" title="' + esc(lleno ? H.t.slotsLleno : H.t.slotsLibre) + '">' +
                        '<span class="hv-slots-bars">' + barras + '</span>' +
                        esc(H.t.slotsDe.replace('{0}', Math.min(total, H.max)).replace('{1}', H.max)) + '</span>' +
                    '</header><ol class="hv-linea">' + items.map(versionHtml).join('') + '</ol></article>';
            });
            return html + '</div>';
        }

        // ---- Vista en lista (tabla) ----
        function renderLista(rows) {
            var th = function (t, c) { return '<th' + (c ? ' class="' + c + '"' : '') + '>' + esc(t) + '</th>'; };
            // Año, modo y escenario van juntos en una sola columna (menos ancho; se lee de un vistazo).
            var head = '<thead><tr>' + (H.esSuper ? th(H.t.empresa) : '') + th(H.t.anio) +
                th(H.t.fecha) + th(H.t.usuario) + th(H.t.archivo) + th(H.t.filas, 'num') + th(H.t.estado) + (H.puedeRollback ? '<th></th>' : '') + '</tr></thead>';
            var body = '<tbody>' + rows.slice().sort(function (a, b) { return b.ts - a.ts; }).map(function (f) {
                return '<tr class="hv-row">' +
                    (H.esSuper ? '<td><b>' + esc(f.empresa) + '</b></td>' : '') +
                    '<td><span class="hv-comb"><span class="hv-grupo-anio">' + f.anio + '</span>' + pillModo(f) + pillEsc(f) + '</span></td>' +
                    '<td><span class="hv-date">' + f.fecha + '</span><span class="hv-time">' + f.hora + '</span></td>' +
                    '<td><div class="hv-user">' + avatar(f.usuario) + esc(f.usuario) + '</div></td>' +
                    '<td><div class="hv-file" title="' + esc(f.archivo) + '"><i class="fas fa-file-excel"></i><span>' + esc(f.archivo) + '</span></div></td>' +
                    '<td class="num' + (f.cero ? ' zero' : '') + '">' + esc(f.filas) + '</td>' +
                    '<td>' + pillEstado(f) + '</td>' +
                    (H.puedeRollback ? '<td class="hv-actions">' + btnDeshacer(f, true) + '</td>' : '') + '</tr>';
            }).join('') + '</tbody>';
            return '<div class="hv-card hv-tabla-card"><div class="hv-table-wrap"><table class="hv-table">' + head + body + '</table></div></div>';
        }

        function render() {
            var rows = filtrados();
            $('#hvCount').text(rows.length + ' ' + H.t.versiones);
            $('#hvVista button').removeClass('active').filter('[data-vista="' + vista + '"]').addClass('active');
            if (!rows.length) { $('#hvContenido').empty(); $('#hvEmpty').show(); return; }
            $('#hvEmpty').hide();
            $('#hvContenido').html(vista === 'lista' ? renderLista(rows) : renderGrupos(rows));
        }

        function toast(msg) {
            $('#hvToastMsg').text(msg); $('#hvToast').addClass('show');
            setTimeout(function () { $('#hvToast').removeClass('show'); }, 4200);
        }
        function cerrarModal() { $('#hvModalBackdrop').removeClass('open'); pendiente = null; }

        $('#filterEmpresa, #filterAnio, #filterModo, #filterEscenario').on('change', render);
        $('#btnLimpiar').on('click', function () {
            $('#filterEmpresa, #filterAnio, #filterModo, #filterEscenario').val('');
            render();
        });
        $('#hvVista').on('click', 'button', function () {
            vista = $(this).data('vista');
            try { localStorage.setItem('hv.vista', vista); } catch (e) { }
            render();
        });

        // «Deshacer este cargue»: resumen de lo que va a pasar antes de confirmar
        $('#hvContenido').on('click', '.hv-restore', function () {
            var id = $(this).data('id');
            pendiente = H.datos.filter(function (f) { return f.id === id; })[0];
            if (!pendiente) return;
            var f = pendiente;
            var resumen = '<dl class="hv-modal-resumen">' +
                (H.esSuper ? '<dt>' + esc(H.t.empresa) + '</dt><dd>' + esc(f.empresa) + '</dd>' : '') +
                '<dt>' + esc(H.t.anio) + '</dt><dd>' + f.anio + ' · ' + esc(f.modoTexto) + ' · ' + esc(H.t.escCorto) + ' ' + f.esc + '</dd>' +
                '<dt>' + esc(H.t.cargue) + '</dt><dd>' + f.fecha + ' ' + f.hora + ' · ' + esc(f.usuario) + '</dd>' +
                '<dt>' + esc(H.t.archivo) + '</dt><dd>' + esc(f.archivo) + '</dd>' +
                '<dt>' + esc(H.t.filasCopia.charAt(0).toUpperCase() + H.t.filasCopia.slice(1)) + '</dt><dd>' + esc(f.filas) + '</dd></dl>';
            $('#hvModalCuerpo').html(H.t.modalCuerpo + resumen);
            $('#hvModalBackdrop').addClass('open');
            $('#hvModalOk').trigger('focus');
        });
        $('#hvModalCancel').on('click', cerrarModal);
        $('#hvModalBackdrop').on('click', function (e) { if (e.target === this) cerrarModal(); });
        $(document).off('keydown.hv').on('keydown.hv', function (e) { if (e.key === 'Escape') cerrarModal(); });
        $('#hvModalOk').on('click', function () {
            if (!pendiente) return;
            $('#inp_idHistorial').val(pendiente.id);
            $('#formRestaurar').submit();   // acción de rollback existente (POST + redirect con TempData)
        });

        render();
        if (H.okMsg) toast(H.okMsg);
    });
})(jQuery);
