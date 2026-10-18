/* Pestaña «Portal público» de Auditoría (solo Super Admin). Pide el análisis a AuditoriaPortal/Consultar y lo dibuja.
   Configuración y textos en window._ap (los arma la vista). Se vuelve a ejecutar cada vez que se abre la pestaña: no deja eventos
   enganchados al documento. Todo lo que muestra es anónimo y agregado. */
(function ($) {
    'use strict';
    var A = window._ap;
    if (!A) return;
    var T = A.t, chart = null, ultimo = null;

    function esc(s) { return $('<div>').text(s == null ? '' : s).html(); }
    function attr(s) { return esc(s).replace(/"/g, '&quot;'); }
    function num(n) { return AudKit.num(n); }
    function tx(prefijo, clave) { var v = T[prefijo + clave]; return v != null ? v : clave; }
    function dur(seg) { seg = Math.round(seg || 0); return Math.floor(seg / 60) + ':' + ('0' + (seg % 60)).slice(-2); }
    function pct(n, max) { return Math.max(2, Math.round((n / (max || 1)) * 100)); }

    function valores() {
        var r = AudKit.periodo.rango();
        return {
            desde: r.desde || '', hasta: r.hasta || '',
            dispositivo: $('#fDispositivo').val() || '', origen: $('#fOrigen').val() || '', idioma: $('#fIdioma').val() || '',
            texto: $.trim($('#fTexto').val())
        };
    }

    // ---- piezas de dibujo ----
    function filas(items, o) {
        o = o || {};
        if (!items || !items.length) return '<p class="ap-vacio">' + esc(T.N_Nada) + '</p>';
        var max = Math.max.apply(null, items.map(function (i) { return i.N; })) || 1;
        return '<div class="ap-lista">' + items.map(function (i) {
            var et = o.etiqueta ? o.etiqueta(i) : esc(i.Nombre || i.Clave);
            var sub = o.sub ? o.sub(i) : '';
            return '<div class="ap-fila"><span class="ap-et" title="' + attr(i.Nombre || i.Clave) + '">' + et + (sub ? '<small>' + sub + '</small>' : '') + '</span>' +
                '<span class="ap-barra"><i style="width:' + pct(i.N, max) + '%"></i></span><b>' + num(i.N) + '</b></div>';
        }).join('') + '</div>';
    }
    function tarjeta(titulo, cuerpo, ancho) {
        return '<section class="ap-card' + (ancho ? ' ap-' + ancho : '') + '"><h4>' + esc(titulo) + '</h4>' + cuerpo + '</section>';
    }
    function seccion(icono, titulo, tarjetas) {
        return '<h3 class="ap-sec"><i class="fas ' + icono + '"></i>' + esc(titulo) + '</h3><div class="ap-grid">' + tarjetas.join('') + '</div>';
    }

    // ---- KPIs ----
    function pintarKpis(r) {
        var k = r.Kpis;
        AudKit.kpis.render($('#axKpis'), [
            { label: T.K_Visitas, valor: num(k.Visitas) },
            { label: T.K_Vistas, valor: num(k.Vistas) },
            { label: T.K_Visitantes, valor: num(k.Visitantes) },
            { label: T.K_VisitantesDia, valor: String(k.VisitantesPorDia).replace('.', ',') },
            { label: T.K_PagVisita, valor: String(k.PaginasPorVisita).replace('.', ',') },
            { label: T.K_Duracion, valor: dur(k.DuracionSeg), texto: true },
            { label: T.K_Rebote, valor: String(k.RebotePct).replace('.', ',') + ' %', texto: true },
            { label: T.K_Descargas, valor: num(k.Descargas) },
            { label: T.K_Contactos, valor: num(k.Contactos) },
            { label: T.K_Busquedas, valor: num(k.Busquedas) }
        ]);
    }

    // ---- mapa de calor día × hora ----
    function mapa(r) {
        var dias = String(T.Dias || '').split(',');
        var max = 0; r.Mapa.forEach(function (f) { f.forEach(function (v) { if (v > max) max = v; }); });
        var h = '<div class="ap-mapa"><span></span>';
        for (var hh = 0; hh < 24; hh++) h += '<span class="ap-mh">' + (hh % 3 === 0 ? hh : '') + '</span>';
        r.Mapa.forEach(function (fila, d) {
            h += '<span class="ap-md">' + esc(dias[d] || '') + '</span>';
            fila.forEach(function (v, hora) {
                var a = max ? (v / max) : 0;
                h += '<span class="ap-celda" title="' + attr((dias[d] || '') + ' ' + hora + ':00 · ' + num(v)) + '" style="background:' +
                    (v ? 'rgba(88,58,255,' + (0.12 + a * 0.88).toFixed(2) + ')' : '#f1f2f8') + '"></span>';
            });
        });
        return h + '</div>';
    }

    // ---- gráfico diario ----
    function grafico(r) {
        var cv = document.getElementById('apChart');
        if (!cv || !window.Chart) return;
        if (chart) { try { chart.destroy(); } catch (e) { } chart = null; }
        var etiquetas = r.Serie.map(function (d) { return d.Dia.slice(5); });
        chart = new Chart(cv.getContext('2d'), {
            data: {
                labels: etiquetas,
                datasets: [
                    { type: 'bar', label: T.Serie_Vistas, data: r.Serie.map(function (d) { return d.Vistas; }), backgroundColor: 'rgba(88,58,255,.22)', borderRadius: 4, order: 3 },
                    { type: 'line', label: T.Serie_Visitantes, data: r.Serie.map(function (d) { return d.Visitantes; }), borderColor: '#06B6D4', backgroundColor: '#06B6D4', tension: .3, pointRadius: 2, order: 1 },
                    { type: 'line', label: T.Serie_Visitas, data: r.Serie.map(function (d) { return d.Visitas; }), borderColor: '#583AFF', backgroundColor: '#583AFF', tension: .3, pointRadius: 2, order: 2 }
                ]
            },
            options: {
                responsive: true, maintainAspectRatio: false, interaction: { mode: 'index', intersect: false },
                plugins: { legend: { position: 'bottom', labels: { boxWidth: 12 } } },
                scales: { y: { beginAtZero: true, ticks: { precision: 0 } }, x: { ticks: { maxTicksLimit: 14 } } }
            }
        });
    }

    // ---- render principal ----
    function render(r) {
        $('#axInicial').hide(); $('#apAviso').hide();
        if (!r.Disponible) {
            $('#apResultados').hide(); AudKit.kpis.render($('#axKpis'), []);
            var clave = r.Motivo === 'SinCarpeta' ? T.SinCarpeta : T.SinDatos;
            $('#apAviso').show().find('h4').text(A.nombre).end().find('p').text(clave);
            return;
        }
        if (!r.Eventos) {
            AudKit.kpis.render($('#axKpis'), []); $('#apResultados').hide();
            $('#apAviso').show().find('h4').text(A.nombre).end().find('p').text(T.SinEventos);
            return;
        }
        pintarKpis(r);
        $('#apAhora').html('<i class="fas fa-circle"></i>' + esc(T.Ahora.replace('{0}', num(r.VisitantesAhora)).replace('{1}', num(r.VistasAhora))) +
            (r.PrimerEvento ? ' <em>' + esc(T.Desde.replace('{0}', r.PrimerEvento)) + '</em>' : ''));

        var h = '';
        h += '<h3 class="ap-sec"><i class="fas fa-chart-line"></i>' + esc(T.S_Tendencia) + '</h3>' +
             '<div class="ap-grid"><section class="ap-card ap-ancho"><div class="ap-chart-wrap"><canvas id="apChart"></canvas></div></section>' +
             tarjeta(T.S_Mapa, mapa(r), 'ancho') + '</div>';

        h += seccion('fa-search', T.S_Consultan, [
            tarjeta(T.S_Empresas, tablaEmpresas(r.Empresas), 'ancho'),
            tarjeta(T.S_Sectores, filas(r.Sectores, { sub: function (i) { return num(i.N2) + ' ' + T.K_Visitantes.toLowerCase(); } })),
            tarjeta(T.S_Paginas, filas(r.Paginas, { etiqueta: function (i) { return esc(tx('P_', i.Clave)); } })),
            tarjeta(T.S_Rankings, filas(r.Rankings, { etiqueta: function (i) { return esc(String(i.Clave).replace(/-/g, ' ')); } })),
            tarjeta(T.S_RankSeg, filas(r.RankingsSegmento)),
            tarjeta(T.S_Busquedas, filas(r.Busquedas, { etiqueta: function (i) { return esc(i.Nombre || ('NIT ' + i.Clave)); }, sub: function (i) { return i.Nombre ? 'NIT ' + esc(i.Clave) : ''; } })),
            tarjeta(T.S_Comparaciones, filas(r.Comparaciones)),
            tarjeta(T.S_FiltrosSector, filas(r.FiltrosSector))
        ]);

        h += seccion('fa-route', T.S_Llegan, [
            tarjeta(T.S_Origen, filas(r.Origen, { etiqueta: function (i) { return esc(tx('O_', i.Clave)); } })),
            tarjeta(T.S_Sitios, filas(r.Sitios)),
            tarjeta(T.S_Campanas, filas(r.Campanas))
        ]);

        h += seccion('fa-users', T.S_Quienes, [
            tarjeta(T.S_Dispositivos, filas(r.Dispositivos, { etiqueta: function (i) { return esc(tx('D_', i.Clave)); } })),
            tarjeta(T.S_Navegadores, filas(r.Navegadores)),
            tarjeta(T.S_Sistemas, filas(r.Sistemas)),
            tarjeta(T.S_Idiomas, filas(r.Idiomas)),
            tarjeta(T.S_Zonas, filas(r.Zonas))
        ]);

        function accion(i) { return esc(String(i.Clave).split(':').map(function (p) { return tx('A_', p); }).join(' · ')); }
        h += seccion('fa-mouse-pointer', T.S_Hacen, [
            tarjeta(T.S_Cta, filas(r.Cta)),
            tarjeta(T.S_Descargas, filas(r.Exportaciones, { etiqueta: accion, sub: function (i) { return num(i.N2) + ' ' + T.K_Visitantes.toLowerCase(); } })),
            tarjeta(T.S_Otras, filas(r.OtrasAcciones, { etiqueta: accion })),
            tarjeta(T.S_Salidas, filas(r.Salidas))
        ]);

        h += seccion('fa-sitemap', T.S_Recorridos, [
            tarjeta(T.S_Embudo, embudo(r.Embudo), 'ancho'),
            tarjeta(T.S_Entradas, filas(r.Entradas, { etiqueta: function (i) { return esc(tx('P_', i.Clave)); } })),
            tarjeta(T.S_SalidasPag, filas(r.SalidasPagina, { etiqueta: function (i) { return esc(tx('P_', i.Clave)); } })),
            tarjeta(T.S_Pasos, filas((r.Transiciones || []).map(function (x) { return { Clave: x.De + '>' + x.A, Nombre: tx('P_', x.De) + ' → ' + tx('P_', x.A), N: x.N }; }))),
            tarjeta(T.S_Profundidad, filas(r.Profundidad)),
            tarjeta(T.S_Lectura, tablaLectura(r.Lectura), 'ancho')
        ]);

        h += seccion('fa-heartbeat', T.S_Salud, [
            tarjeta(T.S_Bots, bots(r.Bots)),
            tarjeta(T.S_404, filas(r.Errores404, { etiqueta: function (i) { return esc(tx('P_', i.Clave)); } })),
            tarjeta(T.S_Limites, filas(r.Limites))
        ]);

        $('#apContenido').html(h);
        $('#apResultados').show();
        grafico(r);
    }

    function tablaEmpresas(items) {
        if (!items || !items.length) return '<p class="ap-vacio">' + esc(T.N_Nada) + '</p>';
        var max = Math.max.apply(null, items.map(function (i) { return i.N; })) || 1;
        return '<div class="ap-tw"><table class="ap-tabla"><thead><tr><th>' + esc(T.Hdr_Empresa) + '</th><th class="n">' + esc(T.Hdr_Vistas) + '</th><th class="n">' +
            esc(T.Hdr_Visitantes) + '</th><th class="n">' + esc(T.Hdr_Informes) + '</th></tr></thead><tbody>' + items.map(function (i) {
                var p = String(i.Extra || '').split('|');
                return '<tr><td><strong>' + esc(i.Nombre || ('NIT ' + i.Clave)) + '</strong><small>NIT ' + esc(i.Clave) + (p[0] ? ' · ' + esc(p[0]) : '') + '</small>' +
                    '<span class="ap-barra ap-fina"><i style="width:' + pct(i.N, max) + '%"></i></span></td><td class="n">' + num(i.N) + '</td><td class="n">' + num(i.N2) + '</td><td class="n">' + num(p[1] || 0) + '</td></tr>';
            }).join('') + '</tbody></table></div>';
    }

    function tablaLectura(items) {
        if (!items || !items.length) return '<p class="ap-vacio">' + esc(T.N_Nada) + '</p>';
        return '<div class="ap-tw"><table class="ap-tabla"><thead><tr><th>' + esc(T.X_Pagina) + '</th><th class="n">' + esc(T.X_Veces) + '</th><th class="n">' + esc(T.X_PermSeg) + '</th><th class="n">' +
            esc(T.X_ScrollPct) + '</th></tr></thead><tbody>' + items.map(function (i) {
                return '<tr><td>' + esc(tx('P_', i.Pagina)) + '</td><td class="n">' + num(i.N) + '</td><td class="n">' + num(i.PermanenciaSeg) + '</td><td class="n">' + num(i.ScrollPct) + '</td></tr>';
            }).join('') + '</tbody></table></div>';
    }

    function embudo(items) {
        if (!items || !items.length) return '<p class="ap-vacio">' + esc(T.N_Nada) + '</p>';
        var max = items[0].N || 1;
        return '<div class="ap-emb">' + items.map(function (i) {
            return '<div class="ap-emb-fila"><span class="ap-emb-et">' + esc(tx('E_', i.Clave)) + '</span><span class="ap-emb-bar"><i style="width:' + pct(i.N, max) + '%"></i></span>' +
                '<b>' + num(i.N) + '</b><em>' + i.N2 + ' %</em></div>';
        }).join('') + '</div>';
    }

    function bots(items) {
        if (!items || !items.length) return '<p class="ap-vacio">' + esc(T.N_Nada) + '</p>';
        return filas(items.map(function (b) { return { Clave: b.Familia, Nombre: tx('B_', b.Familia), N: b.N, Paginas: b.Paginas }; }), {
            sub: function (i) { return esc((i.Paginas || []).slice(0, 3).map(function (p) { return tx('P_', p.Clave) + ' ' + p.N; }).join(' · ')); }
        });
    }

    // ---- consultas ----
    function consultar() {
        var v = valores();
        AudKit.filtros.chips(consultar);
        $.getJSON(A.urlConsultar, v).done(function (r) {
            if (!r || r.error) { Swal.fire({ icon: 'error', title: A.errTit, text: (r && r.error) || A.error }); return; }
            ultimo = v;
            render(r);
        }).fail(function () { Swal.fire({ icon: 'error', title: A.errTit, text: A.error }); });
    }

    function inicial() {
        ultimo = null;
        if (chart) { try { chart.destroy(); } catch (e) { } chart = null; }
        $('#apContenido').empty(); $('#apResultados').hide(); $('#apAviso').hide();
        AudKit.kpis.render($('#axKpis'), []); AudKit.chips.render($('#axChips'), []);
        $('#axInicial').show();
    }

    AudKit.periodo.init();
    AudKit.masFiltros();
    AudKit.enter(consultar);
    $('#btnConsultar').off('click').on('click', consultar);
    $('#btnLimpiar').off('click').on('click', function () { AudKit.filtros.limpiar(); inicial(); });

    // Exportar a Excel: mismos filtros que la pantalla (POST con token antiforgery)
    $('#btnExportar').off('click').on('click', function () {
        var v = ultimo || valores();
        var $f = $('<form method="post" style="display:none">').attr('action', A.urlExportar);
        $f.append($('<input type="hidden" name="__RequestVerificationToken">').val($('input[name="__RequestVerificationToken"]').first().val()));
        Object.keys(v).forEach(function (k) { $f.append($('<input type="hidden">').attr('name', k).val(v[k])); });
        $('body').append($f); $f.trigger('submit'); setTimeout(function () { $f.remove(); }, 1500);
    });
})(jQuery);
