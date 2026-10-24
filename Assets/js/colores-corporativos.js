/* Modal «Colores corporativos» (hasta 2 paletas por empresa) — una sola implementación para todas las pantallas.
   Requiere jQuery (Bootstrap modal) y, para confirmar al quitar, SweetAlert2. El markup es Views/Shared/_ColoresCorporativosModal.cshtml.
   Uso: ColoresCorp.abrir(idEmpresa, nombreEmpresa).
   La vista previa NO reimplementa la derivación de colores: pregunta a InformeTemaController.Resolver (misma clase C# que usan
   los informes, Excel y correo), así lo que se ve aquí es exactamente lo que saldrá en el informe. */
(function () {
    var HEX = /^#?[0-9A-Fa-f]{6}$/;
    var modal, cfg, T, idEmpresa = 0, disponible = true;

    function $m(sel, ctx) { return (ctx || modal).querySelector(sel); }
    function todos(sel, ctx) { return Array.prototype.slice.call((ctx || modal).querySelectorAll(sel)); }
    function norm(v) { v = (v || '').trim(); if (!v) return ''; if (v.charAt(0) !== '#') v = '#' + v; return v.toUpperCase(); }
    function token() { var t = modal.querySelector('input[name="__RequestVerificationToken"]'); return t ? t.value : ''; }

    function aviso(texto) {
        var a = $m('#coloresCorpAviso');
        a.textContent = texto || '';
        a.hidden = !texto;
    }

    function tarjeta(slot) { return modal.querySelector('.pc-card[data-slot="' + slot + '"]'); }
    function campo(card, nombre) { return card.querySelector('.pc-hex[data-campo="' + nombre + '"]'); }
    function picker(card, nombre) { return card.querySelector('.pc-picker[data-campo="' + nombre + '"]'); }
    function msg(card, texto, tipo) { var m = card.querySelector('.pc-msg'); m.textContent = texto || ''; m.className = 'pc-msg' + (tipo ? ' ' + tipo : ''); }

    function leer(card) {
        return {
            primario: norm(campo(card, 'primario').value),
            secundario: norm(campo(card, 'secundario').value),
            acento: norm(campo(card, 'acento').value)
        };
    }

    function pintarEstado(card, registrada) {
        var e = card.querySelector('[data-estado]');
        e.textContent = registrada ? T.registrada : T.vacia;
        e.classList.toggle('ok', !!registrada);
        card.querySelector('.pc-quitar').hidden = !registrada;
    }

    function ponerColores(card, p) {
        ['primario', 'secundario', 'acento'].forEach(function (c) {
            var v = p && p[c] ? norm(p[c]) : '';
            campo(card, c).value = v;
            if (v) picker(card, c).value = v.toLowerCase();
        });
        pintarEstado(card, !!p);
        msg(card, '');
        vistaPrevia(card);
    }

    // ----- vista previa (debounce) -----
    var timers = {};
    function vistaPrevia(card) {
        var slot = card.getAttribute('data-slot');
        clearTimeout(timers[slot]);
        timers[slot] = setTimeout(function () { pedirVistaPrevia(card); }, 220);
    }
    function limpiarPrevia(card) {
        card.querySelector('.pc-prev-barra').style.background = '';
        card.querySelector('.pc-prev-puntos').innerHTML = '';
        card.querySelector('.pc-prev-texto').style.color = '';
    }
    function pedirVistaPrevia(card) {
        var v = leer(card);
        if (!HEX.test(v.primario) || !HEX.test(v.secundario) || (v.acento && !HEX.test(v.acento))) {
            limpiarPrevia(card);
            if (v.primario || v.secundario) msg(card, (v.primario && !HEX.test(v.primario)) || (v.secundario && !HEX.test(v.secundario)) ? T.faltan : '', 'err');
            else msg(card, '');
            return;
        }
        var q = new URLSearchParams({ tema: 'custom', p: v.primario, s: v.secundario, a: v.acento || '' });
        fetch(cfg.resolver + '?' + q.toString(), { credentials: 'same-origin', cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (j) {
                if (!j.exito) { limpiarPrevia(card); msg(card, j.mensaje, 'err'); card.dataset.invalida = '1'; return; }
                delete card.dataset.invalida;
                var t = j.tema;
                card.querySelector('.pc-prev-barra').style.background = t.muestra;
                card.querySelector('.pc-prev-puntos').innerHTML = t.serie.slice(0, 5).map(function (c) { return '<i style="background:' + c + '"></i>'; }).join('');
                card.querySelector('.pc-prev-texto').style.color = t.strong;
                msg(card, j.aviso || '', j.aviso ? 'aviso' : '');
            })
            .catch(function () { msg(card, T.errorGeneral, 'err'); });
    }

    // ----- sincronía selector de color <-> código hex -----
    function enlazar(card) {
        ['primario', 'secundario', 'acento'].forEach(function (c) {
            var h = campo(card, c), p = picker(card, c);
            p.addEventListener('input', function () { h.value = p.value.toUpperCase(); vistaPrevia(card); });
            h.addEventListener('input', function () {
                var v = norm(h.value);
                if (HEX.test(v)) p.value = v.toLowerCase();
                vistaPrevia(card);
            });
            h.addEventListener('blur', function () { if (HEX.test(h.value.trim())) h.value = norm(h.value); });
        });
        card.querySelector('.pc-guardar').addEventListener('click', function () { guardar(card); });
        card.querySelector('.pc-quitar').addEventListener('click', function () { quitar(card); });
    }

    // ----- guardar / quitar -----
    function post(url, datos) {
        var fd = new FormData();
        fd.append('__RequestVerificationToken', token());
        fd.append('idEmpresa', idEmpresa);
        Object.keys(datos).forEach(function (k) { fd.append(k, datos[k]); });
        return fetch(url, { method: 'POST', body: fd, credentials: 'same-origin' }).then(function (r) { return r.json(); });
    }

    function guardar(card) {
        var v = leer(card);
        if (!HEX.test(v.primario) || !HEX.test(v.secundario) || (v.acento && !HEX.test(v.acento))) { msg(card, T.faltan, 'err'); return; }
        var btn = card.querySelector('.pc-guardar');
        btn.disabled = true;
        post(cfg.guardar, { slot: card.getAttribute('data-slot'), primario: v.primario, secundario: v.secundario, acento: v.acento })
            .then(function (j) {
                if (!j.exito) { msg(card, j.mensaje, 'err'); return; }
                pintarEstado(card, true);
                msg(card, j.aviso || j.mensaje, j.aviso ? 'aviso' : 'ok');
            })
            .catch(function () { msg(card, T.errorGeneral, 'err'); })
            .then(function () { btn.disabled = !disponible; });
    }

    function quitar(card) {
        var hacer = function () {
            post(cfg.eliminar, { slot: card.getAttribute('data-slot') })
                .then(function (j) {
                    if (!j.exito) { msg(card, j.mensaje, 'err'); return; }
                    ponerColores(card, null);
                    msg(card, j.mensaje, 'ok');
                })
                .catch(function () { msg(card, T.errorGeneral, 'err'); });
        };
        if (window.Swal) {
            Swal.fire({ icon: 'warning', title: T.confirmarTitulo, text: T.confirmarTexto, showCancelButton: true, confirmButtonText: T.confirmarTitulo, cancelButtonText: T.cancelar })
                .then(function (r) { if (r.isConfirmed) hacer(); });
        } else if (window.confirm(T.confirmarTexto)) hacer();
    }

    // ----- API -----
    window.ColoresCorp = {
        abrir: function (id, nombre) {
            modal = modal || document.getElementById('coloresCorpModal');
            if (!modal) return;
            if (!cfg) {
                cfg = { obtener: modal.dataset.urlObtener, guardar: modal.dataset.urlGuardar, eliminar: modal.dataset.urlEliminar, resolver: modal.dataset.urlResolver };
                T = window.__coloresCorpTextos || {};
                todos('.pc-card').forEach(enlazar);
            }
            idEmpresa = parseInt(id, 10) || 0;
            $m('#coloresCorpSub').textContent = nombre || '';
            aviso('');
            todos('.pc-card').forEach(function (c) { ponerColores(c, null); c.querySelector('.pc-guardar').disabled = true; });
            window.jQuery(modal).modal('show');

            fetch(cfg.obtener + '?idEmpresa=' + idEmpresa, { credentials: 'same-origin', cache: 'no-store' })
                .then(function (r) { return r.json(); })
                .then(function (j) {
                    if (!j.exito) { aviso(j.mensaje); return; }
                    disponible = !!j.disponible;
                    if (!disponible) aviso(T.sinTabla);
                    todos('.pc-card').forEach(function (c) { c.querySelector('.pc-guardar').disabled = !disponible; });
                    (j.paletas || []).forEach(function (p) {
                        var c = tarjeta(p.slot);
                        if (c) ponerColores(c, p);
                    });
                })
                .catch(function () { aviso(T.errorGeneral); });
        }
    };
})();
