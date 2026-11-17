/*
 * Secciones «PYG en una hoja» y «¿Cuánta plata me queda?» — pintado en pantalla y en PDF, compartido por el informe de PYG, el de Flujo de caja
 * y la versión para comité (Informe mensual de gerencia). Las cifras llegan listas del servidor (InformeEjecutivoService); aquí solo se pintan.
 *
 * Cada función recibe un contexto `k` que aporta lo propio de cada pantalla (formato de cifras según la unidad elegida, textos ya traducidos):
 *   k.fmt(v, conSufijo) · k.fmtPct(v) · k.fmtVar(v) · k.nombreMes(m) · k.abrevMes(m) · k.unidadBadge() · k.nombreTramo(codigo)
 *   k.T = { ph: {...textos de PYG en una hoja}, cd: {...textos de caja}, sinSeccion }
 * Solo para PDF además: k.Co (colores) · k.M · k.ANCHO · k.LIMITE_Y · k.nuevaPagina() (agrega página y devuelve la nueva y) · k.degradado(x,y,w,h)
 */
window.InformeVistas = (function () {
    'use strict';
    var MDASH = '—';

    function esc(t) { return $('<span>').text(t == null ? '' : String(t)).html(); }

    // =====================================================================================================================
    // Pantalla
    // =====================================================================================================================
    function claseVar(fila, v) {
        if (v === null || v === undefined || v === 0) return '';
        return (fila.EsGastoOCosto ? v < 0 : v > 0) ? 'pos' : 'neg';
    }

    /** Tabla «PYG en una hoja» (12 meses, acumulado, AV %, presupuesto y año anterior). */
    function htmlPyg(r, k) {
        var P = k.T.ph, h = '<div class="ej-hoja-wrap"><table class="ej-hoja"><thead><tr><th>' + esc(P.concepto) + '</th>';
        for (var m = 1; m <= 12; m++) h += '<th>' + esc(k.abrevMes(m)) + '</th>';
        h += '<th class="sep">' + esc(P.acum) + '</th><th>' + esc(P.av) + '</th>';
        if (r.HayPresupuesto) h += '<th class="sep">' + esc(P.ppto) + '</th><th>' + esc(P.varPpto) + '</th>';
        if (r.HayAnioAnterior) h += '<th class="sep">' + esc(P.anioAnt) + '</th><th>' + esc(P.varAnio) + '</th>';
        h += '</tr></thead><tbody>';
        r.Filas.forEach(function (f) {
            h += '<tr' + (f.EsSubtotal ? ' class="sub"' : '') + '><td>' + esc(f.Descripcion) + '</td>';
            f.Meses.forEach(function (v) { h += v === null ? '<td class="vacio">' + MDASH + '</td>' : '<td>' + esc(k.fmt(v)) + '</td>'; });
            h += '<td class="sep">' + esc(k.fmt(f.Acumulado)) + '</td><td>' + esc(k.fmtPct(f.Av)) + '</td>';
            if (r.HayPresupuesto) h += '<td class="sep">' + esc(k.fmt(f.AcumPresupuesto)) + '</td><td class="' + claseVar(f, f.VarPresupuesto) + '">' + esc(k.fmtVar(f.VarPresupuesto)) + '</td>';
            if (r.HayAnioAnterior) h += '<td class="sep">' + esc(f.AcumAnioAnterior === null ? MDASH : k.fmt(f.AcumAnioAnterior)) + '</td><td class="' + claseVar(f, f.VarAnioAnterior) + '">' + esc(k.fmtVar(f.VarAnioAnterior)) + '</td>';
            h += '</tr>';
        });
        return h + '</tbody></table></div><div class="ej-nota">' + esc(P.nota.replace('{0}', k.unidadBadge())) + '</div>';
    }

    function htmlHero(r, k) {
        var P = k.T.cd, h = '<div class="ej-hero">';
        h += '<div class="ej-hero-card"><div class="ej-hero-et">' + esc(P.inicio) + '</div><div class="ej-hero-val">' + esc(r.HayCajaInicial ? k.fmt(r.CajaInicial, true) : MDASH) + '</div><div class="ej-hero-sub">' + esc(P.inicioSub) + '</div></div>';
        h += '<div class="ej-hero-card principal"><div class="ej-hero-et">' + esc(P.hoyFmt.replace('{0}', k.nombreMes(r.MesCorte))) + '</div><div class="ej-hero-val' + (r.HayCajaCorte && r.CajaCorte < 0 ? ' neg' : '') + '">' + esc(r.HayCajaCorte ? k.fmt(r.CajaCorte, true) : MDASH) + '</div><div class="ej-hero-sub">' + esc(P.hoySub) + '</div></div>';
        h += '<div class="ej-hero-card"><div class="ej-hero-et">' + esc(P.proy) + '</div><div class="ej-hero-val' + (r.HayProyeccion && r.CajaProyectada < 0 ? ' neg' : '') + '">' + esc(r.HayProyeccion ? k.fmt(r.CajaProyectada, true) : MDASH) + '</div><div class="ej-hero-sub">' + esc(r.HayProyeccion ? P.proySub : P.sinProy) + '</div></div>';
        return h + '</div>';
    }

    function htmlEstadoCaja(r, k) {
        var P = k.T.cd, h = '';
        if (!r.HayCajaInicial) return '<div class="ej-estado aviso">' + esc(P.sinBalance) + '</div>';
        if (r.HayProyeccion) {
            if (r.MesesEnDeficit > 0) h += '<div class="ej-estado mal">' + esc(P.estadoDeficitFmt.replace('{0}', r.MesesEnDeficit).replace('{1}', k.fmt(r.CajaMinima, true)).replace('{2}', k.nombreMes(r.MesMinimo))) + '</div>';
            else h += '<div class="ej-estado ok">' + esc(P.estadoOk) + ' ' + esc(P.minimoFmt.replace('{0}', k.fmt(r.CajaMinima, true)).replace('{1}', k.nombreMes(r.MesMinimo))) + '</div>';
        }
        if (!r.Cuadra) h += '<div class="ej-estado aviso">' + esc(P.noCuadra) + '</div>';
        return h;
    }

    function htmlPuente(r, k) {
        var P = k.T.cd;
        function it(et, v) { return '<span class="it"><small>' + esc(et) + '</small>' + esc(k.fmt(v, true)) + '</span>'; }
        return '<div class="ej-puente">' + (r.HayCajaInicial ? it(P.inicio, r.CajaInicial) : '') + '<span class="op">+</span>' + it(P.entradas, r.TotalFuentes) +
            '<span class="op">&minus;</span>' + it(P.salidas, Math.abs(r.TotalUsos)) + (r.HayCajaCorte ? '<span class="op">=</span>' + it(P.hoyFmt.replace('{0}', k.nombreMes(r.MesCorte)), r.CajaCorte) : '') + '</div>';
    }

    function htmlTramos(r, k) {
        var P = k.T.cd, max = 1;
        r.Tramos.forEach(function (t) { max = Math.max(max, Math.abs(t.Valor)); });
        function col(tipo, titulo) {
            var h = '<div class="pyg2-card"><div class="pyg2-card-head"><div><div class="pyg2-card-title"><i class="fas fa-' + (tipo === 'fuente' ? 'arrow-down' : 'arrow-up') + '"></i>' + esc(titulo) + '</div></div></div>';
            var n = 0;
            r.Tramos.filter(function (t) { return t.Tipo === tipo; }).forEach(function (t) {
                n++;
                h += '<div class="ej-barra-fila"><div class="ej-barra-top"><span>' + esc(k.nombreTramo(t.Codigo)) + '</span><b>' + esc(k.fmt(Math.abs(t.Valor), true)) + '</b></div>' +
                    '<div class="ej-barra ' + tipo + '"><i style="width:' + Math.max(2, Math.round(Math.abs(t.Valor) / max * 100)) + '%"></i></div></div>';
            });
            if (!n) h += '<div class="pyg2-hl-empty">' + esc(k.T.sinSeccion) + '</div>';
            return h + '</div>';
        }
        return '<div class="ej-tramos">' + col('fuente', P.entradas) + col('uso', P.salidas) + '</div>';
    }

    function htmlMeses(r, k) {
        var P = k.T.cd;
        if (!r.Meses.length) return '';
        var h = '<div class="pyg2-card ej-bloque"><div class="pyg2-card-head"><div><div class="pyg2-card-title"><i class="fas fa-calendar-days"></i>' + esc(P.mesesTitulo) + '</div></div></div>' +
            '<table class="ej-meses"><thead><tr><th>' + esc(P.colMes) + '</th><th>' + esc(P.colFlujo) + '</th><th>' + esc(P.colCaja) + '</th><th>' + esc(P.colTipo) + '</th></tr></thead><tbody>';
        r.Meses.forEach(function (m) {
            h += '<tr' + (m.Proyectado ? ' class="proy"' : '') + '><td>' + esc(k.nombreMes(m.Mes)) + '</td><td>' + esc(k.fmt(m.Flujo)) + '</td><td' + (m.CajaFin < 0 ? ' class="neg"' : '') + '>' + esc(k.fmt(m.CajaFin)) + '</td><td>' + esc(m.Proyectado ? P.proyectado : P.real) + '</td></tr>';
        });
        return h + '</tbody></table></div>';
    }

    /** Sección completa «¿Cuánta plata me queda?». */
    function htmlCaja(r, k) { return htmlHero(r, k) + htmlPuente(r, k) + htmlEstadoCaja(r, k) + htmlTramos(r, k) + htmlMeses(r, k); }

    // =====================================================================================================================
    // PDF (jsPDF)
    // =====================================================================================================================
    // Colores mínimos que necesitan estas secciones; cada pantalla aporta los suyos (con el tema aplicado) y se completan con estos.
    var COL_DEF = {
        morado: [88, 58, 255], texto: [22, 18, 31], texto2: [91, 85, 112], muted: [139, 133, 160], linea: [228, 225, 240], suave: [244, 243, 251],
        bueno: [22, 163, 74], buenoBg: [236, 253, 243], malo: [220, 38, 38], maloBg: [254, 242, 242], aviso: [161, 98, 7], blanco: [255, 255, 255]
    };
    function colores(k) { return $.extend({}, COL_DEF, k.Co); }

    function utilesPdf(pdf, k) {
        var Co = colores(k), U = {};
        U.rgb = function (fn, c) { pdf[fn](c[0], c[1], c[2]); };
        U.fuente = function (est, tam, c) { pdf.setFont('helvetica', est); pdf.setFontSize(tam); U.rgb('setTextColor', c || Co.texto); };
        U.limpiar = function (t) { return String(t == null ? '' : t).replace(/\*\*|__/g, '').replace(/\s+/g, ' ').trim(); };
        U.tarjeta = function (x, yy, w, h) { U.rgb('setDrawColor', Co.linea); U.rgb('setFillColor', Co.blanco); pdf.setLineWidth(0.3); pdf.roundedRect(x, yy, w, h, 2.5, 2.5, 'FD'); };
        U.titulo = function (texto, yy) {
            var t = String(texto).toUpperCase();
            U.fuente('bold', 8.5, Co.morado); pdf.text(t, k.M, yy);
            U.rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.3); pdf.line(k.M + pdf.getTextWidth(t) + 3, yy - 1.1, k.M + k.ANCHO, yy - 1.1);
        };
        return U;
    }

    /** Dibuja «PYG en una hoja» desde la posición y; devuelve la y final. */
    function pdfPyg(pdf, k, p, y) {
        var Q = k.T.ph, Co = colores(k), U = utilesPdf(pdf, k), M = k.M, ANCHO = k.ANCHO;
        var enc = [{ t: Q.concepto, w: 44, izq: true }];
        for (var m = 1; m <= 12; m++) enc.push({ t: k.abrevMes(m), w: 11.5 });
        enc.push({ t: Q.acum, w: 17 }, { t: Q.av, w: 11 });
        if (p.HayPresupuesto) enc.push({ t: Q.ppto, w: 17 }, { t: Q.varPpto, w: 11 });
        if (p.HayAnioAnterior) enc.push({ t: Q.anioAnt, w: 17 }, { t: Q.varAnio, w: 11 });
        var total = enc.reduce(function (a, c) { return a + c.w; }, 0), esc2 = Math.min(1, ANCHO / total);
        enc.forEach(function (c) { c.w *= esc2; });
        function asegurar(h) { if (y + h > k.LIMITE_Y) y = k.nuevaPagina(); }

        asegurar(14);
        U.rgb('setFillColor', Co.morado); pdf.rect(M, y - 4.2, ANCHO, 6, 'F');
        U.fuente('bold', 6.6, Co.blanco);
        var x = M;
        enc.forEach(function (c) { if (c.izq) pdf.text(c.t.toUpperCase(), x + 1.5, y); else pdf.text(c.t.toUpperCase(), x + c.w - 1, y, { align: 'right' }); x += c.w; });
        y += 5.6;
        p.Filas.forEach(function (f) {
            asegurar(5.2);
            if (f.EsSubtotal) { U.rgb('setFillColor', Co.suave); pdf.rect(M, y - 3.6, ANCHO, 4.8, 'F'); }
            U.fuente(f.EsSubtotal ? 'bold' : 'normal', 6.8, f.EsSubtotal ? Co.texto : Co.texto2);
            var vals = [{ t: f.Descripcion, izq: true }];
            f.Meses.forEach(function (v) { vals.push({ t: v === null ? MDASH : k.fmt(v) }); });
            vals.push({ t: k.fmt(f.Acumulado) }, { t: k.fmtPct(f.Av) });
            if (p.HayPresupuesto) vals.push({ t: k.fmt(f.AcumPresupuesto) }, { t: k.fmtVar(f.VarPresupuesto), v: f.VarPresupuesto, f: f });
            if (p.HayAnioAnterior) vals.push({ t: f.AcumAnioAnterior === null ? MDASH : k.fmt(f.AcumAnioAnterior) }, { t: k.fmtVar(f.VarAnioAnterior), v: f.VarAnioAnterior, f: f });
            x = M;
            vals.forEach(function (c, i) {
                var col = null;
                if (c.f && c.v !== null && c.v !== undefined && c.v !== 0) col = (c.f.EsGastoOCosto ? c.v < 0 : c.v > 0) ? Co.bueno : Co.malo;
                if (col) U.rgb('setTextColor', col);
                if (c.izq) pdf.text(c.t, x + 1.5, y); else pdf.text(c.t, x + enc[i].w - 1, y, { align: 'right' });
                if (col) U.rgb('setTextColor', f.EsSubtotal ? Co.texto : Co.texto2);
                x += enc[i].w;
            });
            U.rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.1); pdf.line(M, y + 1.3, M + ANCHO, y + 1.3);
            y += 4.8;
        });
        U.fuente('italic', 7, Co.muted); pdf.text(U.limpiar(Q.nota.replace('{0}', k.unidadBadge())), M, y + 2);
        return y + 6;
    }

    /** Dibuja «¿Cuánta plata me queda?» desde la posición y; devuelve la y final. */
    function pdfCaja(pdf, k, c, y) {
        var Q = k.T.cd, Co = colores(k), U = utilesPdf(pdf, k), M = k.M, ANCHO = k.ANCHO;
        function asegurar(h) { if (y + h > k.LIMITE_Y) y = k.nuevaPagina(); }

        asegurar(40);
        var gap = 5, w3 = (ANCHO - gap * 2) / 3, h3 = 27;
        var tarjetas = [
            { et: Q.inicio, v: c.HayCajaInicial ? k.fmt(c.CajaInicial, true) : MDASH, sub: Q.inicioSub, neg: false },
            { et: Q.hoyFmt.replace('{0}', k.nombreMes(c.MesCorte)), v: c.HayCajaCorte ? k.fmt(c.CajaCorte, true) : MDASH, sub: Q.hoySub, neg: c.HayCajaCorte && c.CajaCorte < 0, principal: true },
            { et: Q.proy, v: c.HayProyeccion ? k.fmt(c.CajaProyectada, true) : MDASH, sub: c.HayProyeccion ? Q.proySub : Q.sinProy, neg: c.HayProyeccion && c.CajaProyectada < 0 }
        ];
        tarjetas.forEach(function (t, i) {
            var x = M + i * (w3 + gap);
            U.tarjeta(x, y, w3, h3); if (t.principal) k.degradado(x + 3, y, w3 - 6, 1.2);
            U.fuente('bold', 7.2, Co.texto2); pdf.text(t.et.toUpperCase(), x + 4.5, y + 7.5);
            U.fuente('bold', 17, t.neg ? Co.malo : Co.texto); pdf.text(t.v, x + 4.5, y + 17);
            U.fuente('normal', 7, Co.muted); pdf.text(pdf.splitTextToSize(t.sub, w3 - 9)[0] || '', x + 4.5, y + 23);
        });
        y += h3 + 6;
        U.fuente('bold', 8.3, Co.texto2);
        var puente = (c.HayCajaInicial ? Q.inicio + ' ' + k.fmt(c.CajaInicial, true) + '  +  ' : '') + Q.entradas + ' ' + k.fmt(c.TotalFuentes, true) + '  -  ' + Q.salidas + ' ' + k.fmt(Math.abs(c.TotalUsos), true) + (c.HayCajaCorte ? '  =  ' + k.fmt(c.CajaCorte, true) : '');
        pdf.text(U.limpiar(puente), M, y); y += 6;

        var msg = null, colorMsg = Co.bueno, bgMsg = Co.buenoBg;
        if (!c.HayCajaInicial) { msg = Q.sinBalance; colorMsg = Co.aviso; bgMsg = Co.suave; }
        else if (c.HayProyeccion && c.MesesEnDeficit > 0) { msg = Q.estadoDeficitFmt.replace('{0}', c.MesesEnDeficit).replace('{1}', k.fmt(c.CajaMinima, true)).replace('{2}', k.nombreMes(c.MesMinimo)); colorMsg = Co.malo; bgMsg = Co.maloBg; }
        else if (c.HayProyeccion) msg = Q.estadoOk + ' ' + Q.minimoFmt.replace('{0}', k.fmt(c.CajaMinima, true)).replace('{1}', k.nombreMes(c.MesMinimo));
        if (msg) {
            var lin = pdf.splitTextToSize(U.limpiar(msg), ANCHO - 8); asegurar(lin.length * 4.4 + 6);
            U.rgb('setFillColor', bgMsg); pdf.roundedRect(M, y - 3.5, ANCHO, lin.length * 4.4 + 3, 2, 2, 'F');
            U.fuente('bold', 8.3, colorMsg); pdf.text(lin, M + 4, y + 0.5); y += lin.length * 4.4 + 5;
        }
        if (!c.Cuadra) { U.fuente('italic', 7.5, Co.aviso); pdf.text(U.limpiar(Q.noCuadra), M, y); y += 5; }

        // entradas y salidas con barras
        var mitad = (ANCHO - 8) / 2, yTop = y, max = 1, yFin = y;
        c.Tramos.forEach(function (t) { max = Math.max(max, Math.abs(t.Valor)); });
        [['fuente', Q.entradas], ['uso', Q.salidas]].forEach(function (par, idx) {
            var x = M + idx * (mitad + 8), yy = yTop;
            U.fuente('bold', 8, Co.morado); pdf.text(par[1].toUpperCase(), x, yy); yy += 5;
            c.Tramos.filter(function (t) { return t.Tipo === par[0]; }).forEach(function (t) {
                U.fuente('normal', 7.6, Co.texto2); pdf.text(k.nombreTramo(t.Codigo), x, yy);
                U.fuente('bold', 7.6, Co.texto); pdf.text(k.fmt(Math.abs(t.Valor), true), x + mitad, yy, { align: 'right' });
                U.rgb('setFillColor', Co.suave); pdf.roundedRect(x, yy + 1, mitad, 1.8, 0.9, 0.9, 'F');
                U.rgb('setFillColor', par[0] === 'fuente' ? Co.morado : [245, 158, 11]); pdf.roundedRect(x, yy + 1, Math.max(1.5, mitad * Math.abs(t.Valor) / max), 1.8, 0.9, 0.9, 'F');
                yy += 7.2;
            });
            yFin = Math.max(yFin, yy);
        });
        y = yFin + 3;

        if (c.Meses.length) {
            asegurar(18 + c.Meses.length * 4.8);
            U.titulo(Q.mesesTitulo, y); y += 5;
            var cw = [60, 55, 55, 40];
            U.rgb('setFillColor', Co.morado); pdf.rect(M, y - 4.2, ANCHO, 6, 'F'); U.fuente('bold', 7, Co.blanco);
            pdf.text(Q.colMes.toUpperCase(), M + 2, y); pdf.text(Q.colFlujo.toUpperCase(), M + cw[0] + cw[1] - 2, y, { align: 'right' });
            pdf.text(Q.colCaja.toUpperCase(), M + cw[0] + cw[1] + cw[2] - 2, y, { align: 'right' }); pdf.text(Q.colTipo.toUpperCase(), M + cw[0] + cw[1] + cw[2] + cw[3] - 2, y, { align: 'right' });
            y += 5.6;
            c.Meses.forEach(function (m) {
                asegurar(5.5);
                U.fuente(m.Proyectado ? 'italic' : 'normal', 7.8, Co.texto2); pdf.text(k.nombreMes(m.Mes), M + 2, y);
                pdf.text(k.fmt(m.Flujo), M + cw[0] + cw[1] - 2, y, { align: 'right' });
                if (m.CajaFin < 0) U.rgb('setTextColor', Co.malo);
                pdf.text(k.fmt(m.CajaFin), M + cw[0] + cw[1] + cw[2] - 2, y, { align: 'right' });
                U.rgb('setTextColor', Co.texto2); pdf.text(m.Proyectado ? Q.proyectado : Q.real, M + cw[0] + cw[1] + cw[2] + cw[3] - 2, y, { align: 'right' });
                U.rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.1); pdf.line(M, y + 1.3, M + ANCHO, y + 1.3); y += 4.8;
            });
        }
        return y + 4;
    }

    return { htmlPyg: htmlPyg, htmlCaja: htmlCaja, htmlHero: htmlHero, htmlPuente: htmlPuente, htmlEstadoCaja: htmlEstadoCaja, pdfPyg: pdfPyg, pdfCaja: pdfCaja };
})();
