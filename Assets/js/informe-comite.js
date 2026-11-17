/*
 * Versión para comité del Informe mensual de gerencia: el cierre de un mes armado como documento para presentar, decidir y firmar.
 * Lo configura la vista Views/Informes/InformeComite.cshtml con window.EJ_CFG (urls, textos ya traducidos).
 * Las secciones «PYG en una hoja» y «¿Cuánta plata me queda?» se pintan con Assets/js/informe-vistas.js (las mismas del informe de PYG y del Flujo de caja).
 * Las cifras llegan listas del servidor (InformeEjecutivoService); Excel y correo se reconstruyen en el servidor (nunca se confía en cifras del navegador).
 */
(function () {
    'use strict';
    var C = window.EJ_CFG, T = C.t;
    var MDASH = '—', MIDDOT = '·';
    var S = { meses: [], reporte: null, filtros: null, unidad: 'millones' };
    var $emp = $('#ejEmpresa'), $esc = $('#ejEscenario');

    // ---------- utilidades ----------
    function esc(t) { return $('<span>').text(t == null ? '' : String(t)).html(); }
    function empresaId() { var v = $emp.val(); return v ? parseInt(v, 10) : null; }
    function escenarioId() { var v = $esc.val(); return v ? parseInt(v, 10) : 1; }
    function escalar(v) { return S.unidad === 'miles' ? v / 1000 : (S.unidad === 'millones' ? v / 1000000 : v); }
    function sufijo() { return S.unidad === 'miles' ? ' ' + T.unidadSufijoMiles : (S.unidad === 'millones' ? ' ' + T.unidadSufijoMillones : ''); }
    function dec() { return S.unidad === 'pesos' ? 0 : 1; }
    function fmt(v, conSufijo) {
        if (v === null || v === undefined) return MDASH;
        try { return new Intl.NumberFormat(C.cultura, { minimumFractionDigits: dec(), maximumFractionDigits: dec() }).format(escalar(v)) + (conSufijo ? sufijo() : ''); }
        catch (e) { return escalar(v) + (conSufijo ? sufijo() : ''); }
    }
    function fmtPct(v) { if (v === null || v === undefined) return MDASH; try { return new Intl.NumberFormat(C.cultura, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(v * 100) + '%'; } catch (e) { return (Math.round(v * 1000) / 10) + '%'; } }
    function fmtVar(v) {
        if (v === null || v === undefined) return MDASH;
        try { return new Intl.NumberFormat(C.cultura, { minimumFractionDigits: 1, maximumFractionDigits: 1, signDisplay: 'exceptZero' }).format(v * 100) + '%'; }
        catch (e) { var n = Math.round(v * 1000) / 10; return (n >= 0 ? '+' : '') + n + '%'; }
    }
    function fmtRatio(v) { if (v === null || v === undefined) return MDASH; try { return new Intl.NumberFormat(C.cultura, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v) + 'x'; } catch (e) { return (Math.round(v * 100) / 100) + 'x'; } }
    function fmtIndicador(a) {
        if (a.Valor === null || a.Valor === undefined) return MDASH;
        if (a.Formato === 'pct') return fmtPct(a.Valor);
        if (a.Formato === 'x') return fmtRatio(a.Valor);
        if (a.Formato === 'dias') return Math.round(a.Valor) + ' d';
        return fmt(a.Valor, true);
    }
    function nombreMes(m) { return C.meses[m - 1] || String(m); }
    function abrevMes(m) { return nombreMes(m).substring(0, 3); }
    function mostrarError(m) {
        if (window.console) console.error('[' + C.controlador + ']', m);
        if (typeof Swal !== 'undefined') Swal.fire({ icon: 'error', title: T.error, text: m }); else alert(m);
    }
    function nombreTramo(codigo) { return T.cd.tramos[codigo] || codigo; }
    function mesCorteSel() { return S.meses[parseInt($('#ejMes').val() || 0, 10)]; }

    // ---------- filtros / consulta ----------
    function actualizarBoton() { $('#btnConsultarEJ').prop('disabled', !((!C.esAdmin || !!empresaId()) && !!$esc.val() && S.meses.length > 0)); }

    function cargarMeses() {
        S.meses = []; $('#ejMes').empty().prop('disabled', true); $('#ejSinMeses').hide();
        $('#ejResultado, #ejSinDatos').hide(); $('#ejEstadoInicial').show(); actualizarBoton();
        var id = empresaId();
        if (C.esAdmin && !id) return;
        $.post(C.urls.meses, { idEmpresa: id, idEscenario: escenarioId(), __RequestVerificationToken: C.token }, function (resp) {
            if (!resp || !resp.success) { mostrarError((resp && resp.message) || T.errorServidor); return; }
            S.meses = resp.meses || [];
            var $s = $('#ejMes').empty();
            S.meses.forEach(function (m, i) { $s.append($('<option>').val(i).text(m.etiqueta)); });
            $s.prop('disabled', !S.meses.length).trigger('change.select2');
            $('#ejSinMeses').toggle(!S.meses.length);
            actualizarBoton();
        }).fail(function (xhr) { mostrarError(T.errorServidor + ' (HTTP ' + xhr.status + ')'); });
    }

    function consultar() {
        if (!S.meses.length) return;
        var m = mesCorteSel();
        var filtros = { IdEmpresa: empresaId() || '', IdEscenario: escenarioId(), Anio: m.anio, Mes: m.mes };
        InformeTema.preparar().then(function (ok) { if (ok) consultarConTema(filtros); });
    }

    function consultarConTema(filtros) {
        $('#overlayConsultandoEJ').css('display', 'flex');
        $.post(C.urls.reporte, $.extend({ __RequestVerificationToken: C.token }, filtros), function (resp) {
            $('#overlayConsultandoEJ').hide();
            if (!resp || !resp.success) { mostrarError((resp && resp.message) || T.errorServidor); return; }
            var r = resp.reporte;
            if (!r || r.SinDatos) { $('#ejEstadoInicial, #ejResultado').hide(); $('#ejSinDatos').show(); return; }
            pintar(r, filtros);
        }).fail(function (xhr) { $('#overlayConsultandoEJ').hide(); mostrarError(T.errorServidor + ' (HTTP ' + xhr.status + ')'); });
    }

    function etiquetaCorte(f) { return nombreMes(f.Mes) + ' ' + f.Anio; }

    function pintar(r, filtros) {
        S.reporte = r; S.filtros = filtros;
        $('#ejTituloEmpresa').text($emp.find(':selected').text() || '');
        EmpresaLogo.mostrar($emp.val(), document.getElementById('ejLogo'));
        $('#ejChipCorte').text(T.corteFmt.replace('{0}', etiquetaCorte(filtros)));
        $('#ejChipEscenario').text($esc.find(':selected').text() || '');
        try { $('#ejFechaGenerado').text(new Date().toLocaleDateString(C.cultura, { day: 'numeric', month: 'long', year: 'numeric' })); } catch (e) { $('#ejFechaGenerado').text(''); }
        $('#ejEstadoInicial, #ejSinDatos').hide();
        InformeTema.aplicar(document.getElementById('ejResultado'));
        $.extend(S.filtros, InformeTema.parametros());
        $('#ejResultado').show();
        repintar();
        if (window.ComentariosGestion) ComentariosGestion.cargar({ idEmpresa: filtros.IdEmpresa || empresaId(), anio: filtros.Anio, mes: filtros.Mes, claves: [] });
    }

    function repintar() {
        var r = S.reporte; if (!r) return;
        $('#ejUnidadLbl').text(T.unidades[S.unidad]);
        $('#ejMenuUnidad button').each(function () { var on = $(this).data('unidad') === S.unidad; $(this).toggleClass('on', on).attr('aria-checked', on ? 'true' : 'false'); });
        $('#ejCuerpo').html(htmlComite(r));
    }

    // ---------- Secciones compartidas (PYG en una hoja, caja) ----------
    function ctxVistas(extra) {
        return $.extend({
            fmt: fmt, fmtPct: fmtPct, fmtVar: fmtVar, nombreMes: nombreMes, abrevMes: abrevMes, nombreTramo: nombreTramo,
            unidadBadge: function () { return T.unidadBadge[S.unidad]; },
            T: { ph: T.ph, cd: T.cd, sinSeccion: T.sinSeccion }
        }, extra || {});
    }
    function htmlPyg(r) { return InformeVistas.htmlPyg(r, ctxVistas()); }
    function htmlHero(r) { return InformeVistas.htmlHero(r, ctxVistas()); }
    function htmlPuente(r) { return InformeVistas.htmlPuente(r, ctxVistas()); }
    function htmlEstadoCaja(r) { return InformeVistas.htmlEstadoCaja(r, ctxVistas()); }

    // ---------- Informe para comité ----------
    function nombreIndicador(c) { return T.co.indNombres[c] || c; }
    function seccion(titulo, cuerpo) { return '<div class="pyg2-section ej-bloque"><div class="pyg2-section-title"><span class="t">' + esc(titulo) + '</span><span class="line"></span></div>' + cuerpo + '</div>'; }

    function fmtKpiBalance(k) {
        if (k.NoAplica) return T.co.noAplica;
        if (k.Codigo === 'RazonCorriente') return fmtRatio(k.Valor);
        if (k.Codigo === 'Endeudamiento') return fmtPct(k.Valor);
        return fmt(k.Valor, true);
    }
    function filasBalance(g) {
        return g.Balance.map(function (k) { return [T.co.bal[k.Codigo] || k.Codigo, fmtKpiBalance(k), k.NoAplica ? null : k.Variacion]; });
    }
    function filasCajaMes(g) {
        var f = [];
        if (g.HayCajaInicial) f.push([T.co.cajaInicial, fmt(g.CajaInicial, true), null]);
        f.push([T.co.flujoNeto, fmt(g.FlujoNeto, true), null], [T.co.flujoNetoPpto, fmt(g.FlujoNetoPresupuesto, true), null], [T.co.fcl, fmt(g.FclOperativo, true), null]);
        if (g.HayCajaFinal) f.push([T.co.cajaFinal, fmt(g.CajaFinal, true), null]);
        return f;
    }
    function tarjetaFilas(titulo, filas) {
        var h = '<div class="pyg2-card"><div class="pyg2-card-head"><div><div class="pyg2-card-title">' + esc(titulo) + '</div></div></div>';
        filas.forEach(function (f) { h += '<div class="gm-fila"><span class="gm-et">' + esc(f[0]) + '</span><span class="gm-val">' + esc(f[1]) + (f[2] !== null && f[2] !== undefined ? ' <small>' + esc(fmtVar(f[2])) + ' ' + esc(T.co.vsCierre) + '</small>' : '') + '</span></div>'; });
        return h + '</div>';
    }

    function htmlComite(r) {
        var P = T.co, g = r.Gerencia, h = '';
        if (g && g.HayResultados) {
            var c3 = '<div class="gm-grid3">';
            g.Resultados.forEach(function (c) {
                c3 += '<div class="ej-hero-card"><div class="ej-hero-et">' + esc(P.res[c.Codigo] || c.Codigo) + '</div><div class="ej-hero-val">' + esc(fmt(c.Real, true)) + '</div>' +
                    '<div class="ej-hero-sub">' + esc(T.vsPpto) + ' ' + esc(fmt(c.Presupuesto, true)) + ' &middot; ' + esc(fmtVar(c.VarPresupuesto)) + '</div>' +
                    '<div class="ej-hero-sub">' + esc(P.acumulado) + ' ' + esc(fmt(c.AcumReal, true)) + ' &middot; ' + esc(fmtVar(c.AcumVarPresupuesto)) + '</div></div>';
            });
            h += seccion(P.secResumen, c3 + '</div>');
        }
        if (g && (g.HayBalance || g.HayCaja)) {
            var bc = '<div class="gm-grid2">';
            if (g.HayBalance) bc += tarjetaFilas(P.secBalance, filasBalance(g));
            if (g.HayCaja) bc += tarjetaFilas(P.secCajaMes, filasCajaMes(g));
            h += seccion(P.secBalanceCaja, bc + '</div>');
        }
        if (r.Pyg && !r.Pyg.SinDatos) h += seccion(P.secPyg, '<div class="pyg2-card">' + htmlPyg(r.Pyg) + '</div>');
        if (r.Caja && !r.Caja.SinDatos) h += seccion(P.secCaja, htmlHero(r.Caja) + htmlPuente(r.Caja) + htmlEstadoCaja(r.Caja));
        if (g && g.HayIndicadores) {
            var a = '<div class="pyg2-card"><div class="gm-semaforo">';
            [['verde', g.TotalVerde], ['amarillo', g.TotalAmarillo], ['naranja', g.TotalNaranja], ['rojo', g.TotalRojo]].forEach(function (x) { a += '<span class="gm-sem ' + x[0] + '">' + x[1] + ' ' + esc(P.nivel[x[0]]) + '</span>'; });
            a += '</div>';
            if (!g.Atencion.length) a += '<div class="pyg2-hl-empty">' + esc(P.sinAtencion) + '</div>';
            g.Atencion.forEach(function (x) { a += '<div class="gm-fila"><span class="gm-et">' + esc(nombreIndicador(x.Codigo)) + '</span><span class="gm-val">' + esc(fmtIndicador(x)) + ' <span class="gm-sem ' + esc(x.Nivel) + '">' + esc(P.nivel[x.Nivel] || x.Nivel) + '</span></span></div>'; });
            h += seccion(P.secAtencion, a + '</div>');
        }
        if (g && g.HayLineas) {
            var l = '<div class="pyg2-card"><div class="pyg2-table-wrap"><table class="pyg2-table"><thead><tr><th>' + esc(P.colLinea) + '</th><th>' + esc(P.colIngresos) + '</th><th>' + esc(P.colPct) + '</th><th>' + esc(P.colMargen) + '</th></tr></thead><tbody>';
            g.Lineas.forEach(function (x) { l += '<tr><td>' + esc(x.Nombre) + '</td><td>' + esc(fmt(x.Ingresos, true)) + '</td><td>' + esc(fmtPct(x.PctIngresos)) + '</td><td>' + esc(fmtPct(x.MargenOperacional)) + '</td></tr>'; });
            h += seccion(P.secLineas, l + '</tbody></table></div></div>');
        }
        h += seccion(P.decisiones, '<div class="ej-decisiones">' + esc(P.decisionesAyuda) + '</div>');
        h += seccion(P.firmas, '<div class="ej-firmas">' + [P.elaboro, P.reviso, P.aprobo].map(function (t) {
            return '<div class="ej-firma"><div class="linea">' + esc(t) + '</div><div class="campo">' + esc(P.nombre) + '</div><div class="campo">' + esc(P.cargo) + '</div><div class="campo">' + esc(P.fecha) + '</div></div>';
        }).join('') + '</div>');
        return h;
    }

    // ---------- PDF (A4 horizontal, jsPDF puro) ----------
    var PDF_COL = {
        marca: [99, 102, 241], morado: [88, 58, 255], texto: [22, 18, 31], texto2: [91, 85, 112], muted: [139, 133, 160],
        linea: [228, 225, 240], suave: [244, 243, 251], totalBg: [238, 242, 255],
        bueno: [22, 163, 74], buenoBg: [236, 253, 243], malo: [220, 38, 38], maloBg: [254, 242, 242], aviso: [161, 98, 7], blanco: [255, 255, 255]
    };

    function construirPdf(r) {
        var P = T.pdf, Co = InformeTema.pdf(PDF_COL), DEG = InformeTema.pdfDegradado();
        var pdf = new window.jspdf.jsPDF('l', 'mm', 'a4');
        var W = pdf.internal.pageSize.getWidth(), H = pdf.internal.pageSize.getHeight();
        var M = 12, ANCHO = W - M * 2, LIMITE_Y = H - 15, y = 0;
        var empresa = $('#ejTituloEmpresa').text() || '', corte = etiquetaCorte(S.filtros);

        function rgb(fn, c) { pdf[fn](c[0], c[1], c[2]); }
        function fuente(est, tam, c) { pdf.setFont('helvetica', est); pdf.setFontSize(tam); rgb('setTextColor', c || Co.texto); }
        function limpiar(t) { return String(t == null ? '' : t).replace(/\*\*|__/g, '').replace(/\s+/g, ' ').trim(); }
        function ancho(t) { return pdf.getTextWidth(String(t)); }
        function degradado(x, yy, w, h) {
            var pasos = 90, n = DEG.length - 1;
            for (var i = 0; i < pasos; i++) {
                var t = i / (pasos - 1), seg = Math.min(n - 1, Math.floor(t * n)), f = t * n - seg, a = DEG[seg], b = DEG[seg + 1];
                pdf.setFillColor(Math.round(a[0] + (b[0] - a[0]) * f), Math.round(a[1] + (b[1] - a[1]) * f), Math.round(a[2] + (b[2] - a[2]) * f));
                pdf.rect(x + (w * i) / pasos, yy, w / pasos + 0.15, h, 'F');
            }
        }
        function pildora(texto, x, yy, fg, bg, negrita, tam, der) {
            fuente(negrita ? 'bold' : 'normal', tam || 7.5, fg);
            var w = ancho(texto) + 5, h = (tam || 7.5) * 0.62 + 1, x0 = der ? x - w : x;
            rgb('setFillColor', bg); pdf.roundedRect(x0, yy - h + 1.4, w, h, h / 2, h / 2, 'F'); pdf.text(texto, x0 + 2.5, yy); return w;
        }
        function tituloSeccion(texto, yy) {
            var t = String(texto).toUpperCase();
            fuente('bold', 8.5, Co.morado); pdf.text(t, M, yy);
            rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.3); pdf.line(M + ancho(t) + 3, yy - 1.1, M + ANCHO, yy - 1.1);
        }
        function tarjeta(x, yy, w, h) { rgb('setDrawColor', Co.linea); rgb('setFillColor', Co.blanco); pdf.setLineWidth(0.3); pdf.roundedRect(x, yy, w, h, 2.5, 2.5, 'FD'); }
        function encabezadoPagina() {
            degradado(0, 0, W, 2.4); fuente('bold', 10, Co.texto); pdf.text(empresa, M, 11);
            fuente('normal', 8, Co.muted); pdf.text(T.titulo + '  ' + MIDDOT + '  ' + corte, W - M, 11, { align: 'right' }); y = 20;
        }
        function asegurar(h) { if (y + h > LIMITE_Y) { pdf.addPage(); encabezadoPagina(); } }

        // Portada
        degradado(0, 0, W, 34);
        fuente('bold', 10, Co.blanco);
        var wMarca = window.BufinsPdfMarca ? BufinsPdfMarca(pdf, M, 10) : 0;
        if (!wMarca) { pdf.text('Bufins', M, 10); wMarca = ancho('Bufins'); }
        fuente('normal', 8.5, Co.blanco); pdf.text(MIDDOT + '  ' + T.subtitulo, M + wMarca + 2.5, 10);
        fuente('bold', 19, Co.blanco); pdf.text(empresa, M, 22);
        fuente('normal', 9.5, Co.blanco); pdf.text(T.titulo, M, 29);
        fuente('normal', 8, Co.blanco); pdf.text(P.generadoEl + ' ' + ($('#ejFechaGenerado').text() || ''), W - M, 10, { align: 'right' });
        fuente('bold', 8, Co.blanco); pdf.text(P.confidencial.toUpperCase(), W - M, 29, { align: 'right' });
        EmpresaLogo.pdf(pdf, { xDerecha: W - M, y: 12.5, alto: 14 });
        y = 43;
        var xc = M;
        [{ t: $('#ejChipCorte').text(), fg: Co.morado, bg: Co.totalBg, b: true }, { t: $('#ejChipEscenario').text() }, { t: T.unidadBadge[S.unidad] }].forEach(function (c) {
            if (!c.t) return; xc += pildora(c.t, xc, y, c.fg || Co.texto2, c.bg || Co.suave, !!c.b) + 3;
        });
        y += 10;

        // ----- secciones compartidas (Assets/js/informe-vistas.js) -----
        function kv() {
            return ctxVistas({ Co: Co, M: M, ANCHO: ANCHO, LIMITE_Y: LIMITE_Y, degradado: degradado, nuevaPagina: function () { pdf.addPage(); encabezadoPagina(); return y; } });
        }
        function pdfPyg(p) { y = InformeVistas.pdfPyg(pdf, kv(), p, y); }
        function pdfCaja(c) { y = InformeVistas.pdfCaja(pdf, kv(), c, y); }

        function pdfComentarios() {
            var Q = T.co, coms = window.ComentariosGestion ? ComentariosGestion.ultimos() : [];
            if (!coms.length) return;
            asegurar(20); tituloSeccion(Q.comentarios, y); y += 5;
            coms.slice(0, 6).forEach(function (c) {
                fuente('normal', 8.2, Co.texto2);
                var lineas = pdf.splitTextToSize(limpiar(((c.Clave ? c.Clave + ': ' : '') + c.Texto) + '  - ' + c.NombreUsuario), ANCHO - 10).slice(0, 3), hC = lineas.length * 4 + 5;
                asegurar(hC + 2); tarjeta(M, y, ANCHO, hC); pdf.text(lineas, M + 5, y + 6, { lineHeightFactor: 1.3 }); y += hC + 2;
            });
            y += 4;
        }
        function pdfBloqueFilas(x, w, titulo, filas) {
            tituloSeccionX(titulo, y, x, w);
            var yy = y + 5;
            filas.forEach(function (f) {
                fuente('normal', 8.5, Co.texto2); pdf.text(f[0], x + 1, yy);
                fuente('bold', 8.5, Co.texto); pdf.text(f[1] + (f[2] !== null && f[2] !== undefined ? '  (' + fmtVar(f[2]) + ' ' + T.co.vsCierre + ')' : ''), x + w - 1, yy, { align: 'right' });
                rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.15); pdf.line(x, yy + 1.8, x + w, yy + 1.8); yy += 6;
            });
            return yy;
        }
        function tituloSeccionX(texto, yy, x0, w) {
            var tt = String(texto).toUpperCase();
            fuente('bold', 8.5, Co.morado); pdf.text(tt, x0, yy);
            rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.3); pdf.line(x0 + ancho(tt) + 3, yy - 1.1, x0 + w, yy - 1.1);
        }

        // ----- contenido por tipo -----
        {
            var Q = T.co, g = r.Gerencia;
            if (g && g.HayResultados) {
                tituloSeccion(Q.secResumen, y); y += 4;
                var n = g.Resultados.length, gp = 5, wc = (ANCHO - gp * (n - 1)) / n, hc = 36;
                g.Resultados.forEach(function (c, i) {
                    var x = M + i * (wc + gp), bien = (c.VarPresupuesto || 0) >= 0;
                    tarjeta(x, y, wc, hc); degradado(x + 3, y, wc - 6, 1);
                    fuente('bold', 7.5, Co.texto2); pdf.text((Q.res[c.Codigo] || c.Codigo).toUpperCase(), x + 4.5, y + 8);
                    fuente('bold', 16, Co.texto); pdf.text(fmt(c.Real, true), x + 4.5, y + 17);
                    fuente('normal', 7, Co.muted); pdf.text(T.vsPpto + ' ' + fmt(c.Presupuesto, true), x + 4.5, y + 24);
                    pildora(fmtVar(c.VarPresupuesto), x + wc - 4.5, y + 24.5, bien ? Co.bueno : Co.malo, bien ? Co.buenoBg : Co.maloBg, true, 7, true);
                    fuente('normal', 7, Co.muted); pdf.text(Q.acumulado + ' ' + fmt(c.AcumReal, true), x + 4.5, y + 31);
                });
                y += hc + 9;
            }
            if (g && (g.HayBalance || g.HayCaja)) {
                asegurar(50);
                var mit = (ANCHO - 6) / 2, yIni = y, y1 = y, y2 = y;
                if (g.HayBalance) y1 = pdfBloqueFilas(M, mit, Q.secBalance, filasBalance(g));
                y = yIni;
                if (g.HayCaja) y2 = pdfBloqueFilas(M + mit + 6, mit, Q.secCajaMes, filasCajaMes(g));
                y = Math.max(y1, y2) + 4;
            }
            if (r.Caja && !r.Caja.SinDatos) { asegurar(60); tituloSeccion(Q.secCaja, y); y += 6; pdfCaja(r.Caja); y += 4; }
            if (r.Pyg && !r.Pyg.SinDatos) { pdf.addPage(); encabezadoPagina(); tituloSeccion(Q.secPyg, y); y += 6; pdfPyg(r.Pyg); }
            pdf.addPage(); encabezadoPagina();
            if (g && g.HayIndicadores) {
                tituloSeccion(Q.secAtencion, y); y += 6;
                var xs = M;
                ['verde', 'amarillo', 'naranja', 'rojo'].forEach(function (nv) {
                    var cant = g['Total' + nv.charAt(0).toUpperCase() + nv.slice(1)];
                    xs += pildora(cant + ' ' + Q.nivel[nv], xs, y, nv === 'verde' ? Co.bueno : (nv === 'rojo' ? Co.malo : Co.texto2), nv === 'verde' ? Co.buenoBg : (nv === 'rojo' ? Co.maloBg : Co.suave), true, 7.5) + 3;
                });
                y += 7;
                if (!g.Atencion.length) { fuente('italic', 8.5, Co.texto2); pdf.text(Q.sinAtencion, M + 1, y); y += 6; }
                g.Atencion.forEach(function (a) {
                    asegurar(7);
                    fuente('normal', 8.5, Co.texto2); pdf.text(nombreIndicador(a.Codigo), M + 1, y);
                    fuente('bold', 8.5, Co.texto); pdf.text(fmtIndicador(a) + '   ' + (Q.nivel[a.Nivel] || a.Nivel), W - M - 1, y, { align: 'right' });
                    rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.15); pdf.line(M, y + 1.8, W - M, y + 1.8); y += 6;
                });
                y += 4;
            }
            if (g && g.HayLineas) {
                asegurar(24); tituloSeccion(Q.secLineas, y); y += 6;
                fuente('bold', 7.5, Co.blanco); rgb('setFillColor', Co.marca); pdf.rect(M, y - 4.3, ANCHO, 6.3, 'F');
                pdf.text(Q.colLinea.toUpperCase(), M + 3, y);
                pdf.text(Q.colIngresos.toUpperCase(), M + 95, y, { align: 'right' }); pdf.text(Q.colPct.toUpperCase(), M + 130, y, { align: 'right' }); pdf.text(Q.colMargen.toUpperCase(), M + 170, y, { align: 'right' });
                y += 6;
                g.Lineas.forEach(function (l) {
                    asegurar(7);
                    fuente('normal', 8.3, Co.texto2); pdf.text(l.Nombre, M + 3, y);
                    pdf.text(fmt(l.Ingresos, true), M + 95, y, { align: 'right' }); pdf.text(fmtPct(l.PctIngresos), M + 130, y, { align: 'right' }); pdf.text(fmtPct(l.MargenOperacional), M + 170, y, { align: 'right' });
                    rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.15); pdf.line(M, y + 1.8, W - M, y + 1.8); y += 6;
                });
                y += 4;
            }
            pdfComentarios();
            // Decisiones y firmas (espacio para escribir y firmar)
            asegurar(70);
            tituloSeccion(Q.decisiones, y); y += 3;
            rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.3); pdf.roundedRect(M, y, ANCHO, 26, 2.5, 2.5, 'S');
            fuente('italic', 7.5, Co.muted); pdf.text(limpiar(Q.decisionesAyuda), M + 4, y + 6); y += 34;
            tituloSeccion(Q.firmas, y); y += 22;
            var wf = (ANCHO - 20) / 3;
            [Q.elaboro, Q.reviso, Q.aprobo].forEach(function (t, i) {
                var x = M + i * (wf + 10);
                rgb('setDrawColor', Co.texto); pdf.setLineWidth(0.4); pdf.line(x, y, x + wf, y);
                fuente('bold', 8.3, Co.texto); pdf.text(t, x, y + 5);
                fuente('normal', 7.3, Co.muted); pdf.text(Q.nombre + ':', x, y + 11); pdf.text(Q.cargo + ':', x, y + 17); pdf.text(Q.fecha + ':', x, y + 23);
            });
        }

        var tot = pdf.internal.getNumberOfPages();
        for (var p = 1; p <= tot; p++) {
            pdf.setPage(p); rgb('setDrawColor', Co.linea); pdf.setLineWidth(0.3); pdf.line(M, H - 10, W - M, H - 10);
            fuente('normal', 7, Co.muted); pdf.text(T.titulo + '  ' + MIDDOT + '  ' + empresa + '  ' + MIDDOT + '  ' + corte, M, H - 6);
            pdf.text(P.confidencial, W / 2, H - 6, { align: 'center' });
            fuente('bold', 7, Co.texto2); pdf.text(P.paginaFmt.replace('{0}', p).replace('{1}', tot), W - M, H - 6, { align: 'right' });
        }
        pdf.__nombre = C.controlador + '_' + empresa.replace(/[^a-z0-9]+/gi, '_') + '_' + S.filtros.Anio + ('0' + S.filtros.Mes).slice(-2) + '.pdf';
        return pdf;
    }

    function generarPdf() {
        if (!S.reporte) return Promise.resolve();
        return new Promise(function (ok, ko) {
            setTimeout(function () {
                try { var doc = construirPdf(S.reporte); doc.save(doc.__nombre); ExportMenu.aviso('success', C.exp.listo); ok(); }
                catch (e) { if (window.console) console.error('[' + C.controlador + '] PDF', e); ko(e); }
            }, 30);
        });
    }
    function copiarImagen() {
        var empresa = ($('#ejTituloEmpresa').text() || C.controlador).replace(/[^A-Za-z0-9]+/g, '_');
        return ExportMenu.copiarImagen(document.getElementById('ejResultado'), C.controlador + '_' + empresa + '_' + new Date().toISOString().slice(0, 10) + '.png',
            { imagenCopiada: C.exp.imagenCopiada, imagenDescargada: C.exp.imagenDescargada });
    }
    function exportarExcel() {
        if (!S.filtros) return;
        var $c = $('#ejExpCampos').empty();
        Object.keys(S.filtros).forEach(function (k) { $c.append($('<input type="hidden">').attr('name', k).val(S.filtros[k] == null ? '' : S.filtros[k])); });
        $('#frmExportarExcelEJ').submit();
    }

    // ---------- arranque ----------
    $(document).ready(function () {
        var s2lang = { noResults: function () { return T.select2NoResults; }, searching: function () { return T.select2Searching; } };
        $('#ejEmpresa, #ejEscenario, #ejMes').select2({ width: '100%', language: s2lang, dropdownAutoWidth: false, minimumResultsForSearch: 10 });
        $('#ejEmpresa, #ejEscenario').on('change', cargarMeses);

        try { var pr = JSON.parse(localStorage.getItem('ej.prefs') || '{}'); if (['pesos', 'miles', 'millones'].indexOf(pr.unidad) >= 0) S.unidad = pr.unidad; } catch (e) { }
        $('#ejBtnUnidad').on('click', function (e) { e.stopPropagation(); $(this).attr('aria-expanded', $('#ejMenuUnidad').toggleClass('open').hasClass('open') ? 'true' : 'false'); });
        $('#ejMenuUnidad button').on('click', function (e) {
            e.stopPropagation(); S.unidad = $(this).data('unidad');
            $('#ejMenuUnidad').removeClass('open'); $('#ejBtnUnidad').attr('aria-expanded', 'false');
            try { localStorage.setItem('ej.prefs', JSON.stringify({ unidad: S.unidad })); } catch (e2) { }
            repintar();
        });
        $(document).on('click', function () { $('#ejMenuUnidad').removeClass('open'); $('#ejBtnUnidad').attr('aria-expanded', 'false'); });

        $('#btnConsultarEJ').on('click', consultar);
        InformeTema.montar({ selector: '#ejTema', empresa: '#ejEmpresa' });
        $('#btnLimpiarEJ').on('click', function () { InformeTema.reset(); if (C.esAdmin) $emp.val('').trigger('change.select2'); cargarMeses(); });

        ComentariosGestion.montar({ raiz: '#ejComentarios', informe: 'Comite', todos: true });

        var correo = ExportMenu.correo({
            url: C.urls.correo,
            token: function () { return C.token; },
            datos: function () { return $.extend({}, S.filtros); },
            pdf: function () { var d = construirPdf(S.reporte); return d ? d.output('datauristring') : ''; },
            pills: function () { return [$('#ejTituloEmpresa').text()].concat($('#ejPanelResultado .pyg2-chip').map(function () { return $(this).text(); }).get()); },
            textos: C.exp
        });
        ExportMenu.init('ejExp', {
            acciones: { excel: exportarExcel, pdf: generarPdf, imagen: copiarImagen, correo: function () { if (S.reporte) correo.abrir(); } },
            textos: C.exp
        });

        cargarMeses();
    });
})();
