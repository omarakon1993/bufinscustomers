/*
 * Listas de los gestores (ver Assets/css/gestor-lista.css). Sin dependencias.
 *
 *   GestorLista.iniciar({
 *       tabla: '#tablaUsuarios',            // por defecto: la primera tabla .table-compact de una tarjeta .gl-lista
 *       buscar: true,                       // crea un buscador propio (omitir si la pantalla ya tiene el suyo)
 *       selects: [{ col: 7, placeholder: 'Todas las empresas' }],   // filtros por el texto de la columna `col` (0 = primera)
 *       externo: true,                      // la pantalla ya filtra y pagina: solo se agregan los selects; avisa con alCambiar() y consulta pasa(fila)
 *       alCambiar: function () { ... }
 *   });
 *
 * Siempre: pone `data-label` en las celdas (texto del encabezado) para el modo tarjeta y copia las clases `gl-sec` / `gl-ter` del encabezado a
 * toda la columna. Textos: window.GL_TXT (parcial _GestorListaConfig.cshtml).
 */
window.GestorLista = (function () {
    'use strict';
    var T = function () { return window.GL_TXT || {}; };

    function sinTildes(s) { return (s || '').toString().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(); }
    function texto(td) { return (td ? td.textContent : '').replace(/\s+/g, ' ').trim(); }

    function decorar(tabla) {
        var ths = Array.prototype.slice.call(tabla.querySelectorAll('thead th'));
        var filas = Array.prototype.slice.call(tabla.querySelectorAll('tbody tr'));
        filas.forEach(function (tr) {
            var tds = tr.children;
            if (tds.length === 1 && tds[0].hasAttribute('colspan')) return;
            ths.forEach(function (th, i) {
                var td = tds[i]; if (!td) return;
                if (!td.hasAttribute('data-label')) { var et = texto(th); if (et) td.setAttribute('data-label', et); }
                ['gl-sec', 'gl-ter'].forEach(function (c) { if (th.classList.contains(c)) td.classList.add(c); });
            });
        });
    }

    function filasDatos(tabla) {
        return Array.prototype.slice.call(tabla.querySelectorAll('tbody tr')).filter(function (tr) {
            return !(tr.children.length === 1 && tr.children[0].hasAttribute('colspan'));
        });
    }

    function iniciar(opts) {
        opts = opts || {};
        var tabla = typeof opts.tabla === 'string' ? document.querySelector(opts.tabla) : (opts.tabla || document.querySelector('.gl-lista .table-compact'));
        if (!tabla) return null;
        decorar(tabla);
        var tarjeta = tabla.closest('.gl-lista');
        var wrapper = tabla.closest('.table-wrapper') || tabla;
        var tx = T();

        var necesitaBarra = opts.buscar || (opts.selects && opts.selects.length);
        if (!necesitaBarra && !(tarjeta && tarjeta.getAttribute('data-gl-cuenta'))) return { pasa: function () { return true; }, aplicar: function () { } };

        var barra = tarjeta ? tarjeta.querySelector('.gestor-search-bar') : null;
        var inputPropio = null;
        if (necesitaBarra && !barra) {
            barra = document.createElement('div'); barra.className = 'gl-barra';
            wrapper.parentNode.insertBefore(barra, wrapper);
        }
        if (opts.buscar && barra && !barra.querySelector('.gestor-search-wrapper')) {
            var cont = document.createElement('div'); cont.className = 'gl-buscar';
            cont.innerHTML = '<i class="fas fa-search"></i>';
            inputPropio = document.createElement('input'); inputPropio.type = 'search'; inputPropio.autocomplete = 'off';
            inputPropio.placeholder = opts.placeholderBuscar || tx.buscarPH || ''; inputPropio.setAttribute('aria-label', inputPropio.placeholder);
            cont.appendChild(inputPropio); barra.appendChild(cont);
        }

        var selects = [];
        (opts.selects || []).forEach(function (cfg) {
            var valores = {};
            filasDatos(tabla).forEach(function (tr) { var v = texto(tr.children[cfg.col]); if (v) valores[v] = true; });
            var ordenados = Object.keys(valores).sort(function (a, b) { return a.localeCompare(b); });
            if (ordenados.length < 2) return;   // un solo valor: el filtro no aporta
            var sel = document.createElement('select'); sel.className = 'gl-sel'; sel.setAttribute('aria-label', cfg.placeholder || '');
            sel.innerHTML = '<option value="">' + (cfg.placeholder || '') + '</option>' + ordenados.map(function (v) { return '<option>' + v.replace(/&/g, '&amp;').replace(/</g, '&lt;') + '</option>'; }).join('');
            barra.appendChild(sel); selects.push({ el: sel, col: cfg.col });
        });

        var limpiar = null;
        if (necesitaBarra && (selects.length || inputPropio || (barra.querySelector('.gestor-search-input')))) {
            limpiar = document.createElement('button'); limpiar.type = 'button'; limpiar.className = 'gl-limpiar'; limpiar.hidden = true;
            limpiar.innerHTML = '<i class="fas fa-undo"></i><span>' + (tx.limpiar || '') + '</span>'; barra.appendChild(limpiar);
        }

        var cuenta = null, vacio = null;
        var plantillaCuenta = tarjeta ? tarjeta.getAttribute('data-gl-cuenta') : null;
        if (plantillaCuenta && !opts.externo) {
            var cab = document.createElement('div'); cab.className = 'gl-cab';
            cuenta = document.createElement('span'); cuenta.className = 'info-badge'; cab.appendChild(cuenta);
            wrapper.parentNode.insertBefore(cab, barra && barra.parentNode === wrapper.parentNode ? barra : wrapper);
        }
        if (!opts.externo) {
            vacio = document.createElement('div'); vacio.className = 'gl-vacio'; vacio.hidden = true;
            vacio.innerHTML = '<i class="fas fa-filter fa-2x mb-2" style="color:#c7d2fe;"></i><h5>' + (tx.sinResultados || '') + '</h5><p class="mb-0">' + (tx.sinResultadosAyuda || '') + '</p>';
            wrapper.parentNode.insertBefore(vacio, wrapper.nextSibling);
        }

        function valorBuscador() {
            var el = inputPropio || (barra ? barra.querySelector('.gestor-search-input') : null);
            return sinTildes((el ? el.value : '').trim());
        }
        function pasaSelects(tr) {
            return selects.every(function (s) { return s.el.value === '' || texto(tr.children[s.col]) === s.el.value; });
        }
        function pasa(tr) {
            if (!pasaSelects(tr)) return false;
            if (inputPropio) { var q = valorBuscador(); if (q && sinTildes(tr.textContent).indexOf(q) === -1) return false; }
            return true;
        }
        function activos() { return selects.some(function (s) { return s.el.value !== ''; }) || valorBuscador() !== ''; }

        function aplicar() {
            if (!opts.externo) {
                var filas = filasDatos(tabla), visibles = 0;
                filas.forEach(function (tr) { var ok = pasa(tr); tr.classList.toggle('gl-oculta', !ok); if (ok) visibles++; });
                if (cuenta) {
                    var hayFiltro = activos();
                    cuenta.textContent = hayFiltro && tx.cuentaFiltradaFmt ? tx.cuentaFiltradaFmt.replace('{0}', visibles).replace('{1}', filas.length) : (plantillaCuenta || '{0}').replace('{0}', filas.length);
                }
                if (vacio) { vacio.hidden = !(filas.length > 0 && visibles === 0); wrapper.hidden = filas.length > 0 && visibles === 0; }
            }
            if (limpiar) limpiar.hidden = !activos();
        }
        function cambio() { aplicar(); if (opts.alCambiar) opts.alCambiar(); }

        selects.forEach(function (s) { s.el.addEventListener('change', cambio); });
        if (inputPropio) inputPropio.addEventListener('input', cambio);
        else if (barra && barra.querySelector('.gestor-search-input')) barra.querySelector('.gestor-search-input').addEventListener('input', function () { if (limpiar) limpiar.hidden = !activos(); });
        if (limpiar) limpiar.addEventListener('click', function () {
            selects.forEach(function (s) { s.el.value = ''; });
            var el = inputPropio || (barra ? barra.querySelector('.gestor-search-input') : null); if (el) el.value = '';
            cambio();
        });
        aplicar();
        return { pasa: pasa, aplicar: aplicar };
    }

    return { iniciar: iniciar };
})();
