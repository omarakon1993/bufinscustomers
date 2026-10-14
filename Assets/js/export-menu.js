/*
 * Botón "Exportar" desplegable — comportamiento (estándar para informes). Mismo funcionamiento del botón de
 * Revisar cargue. Markup: Views/Shared/_ExportMenu.cshtml · estilos: Assets/css/export-menu.css.
 *
 *   ExportMenu.init('miExp', {
 *       acciones: { excel: fn, pdf: fn, imagen: fn, correo: fn },   // solo las que se usen (ver data-opciones del parcial)
 *       textos:   { generando, listo, imagenCopiada, imagenDescargada, errorGenerar }
 *   });
 *
 * Cada acción puede devolver una Promise: mientras se resuelve el botón muestra "Generando…" (spinner) y si
 * falla sale un aviso de error. "correo" abre un modal, así que no pone el botón en estado ocupado.
 * Utilidades expuestas: ExportMenu.aviso(icono, titulo), ExportMenu.descargarBlob(blob, nombre),
 * ExportMenu.copiarImagen(elemento, nombreArchivo, textos) (requiere html2canvas) y ExportMenu.correo(cfg)
 * (modal de envío por correo compartido; ver su comentario más abajo).
 */
window.ExportMenu = (function () {
    var ICONO = 'fas fa-arrow-up-from-bracket';

    function aviso(icono, titulo) {
        if (!window.Swal) return;
        Swal.fire({ toast: true, position: 'top-end', icon: icono, title: titulo, showConfirmButton: false, timer: 3200, timerProgressBar: true });
    }

    function descargarBlob(blob, nombre) {
        var url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url; a.download = nombre; document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 1500);
    }

    /** Captura `elemento` como PNG y lo copia al portapapeles; si el navegador no deja, lo descarga. */
    function copiarImagen(elemento, nombreArchivo, textos) {
        var t = textos || {};
        var escala = Math.min(2, 14000 / Math.max(elemento.scrollHeight, 1));
        // Los canvas (gráficos Chart.js) se leen AHORA y se pasan al clon como imágenes: así el contenido sale aunque
        // html2canvas no pueda dibujar el canvas clonado. También se compensa el scroll de la página.
        var imgs = Array.prototype.map.call(elemento.querySelectorAll('canvas'), function (c) {
            try { return c.toDataURL('image/png'); } catch (e) { return null; }
        });
        var blobP = html2canvas(elemento, {
            backgroundColor: '#ffffff', scale: escala, logging: false, useCORS: true,
            scrollX: -window.scrollX, scrollY: -window.scrollY,
            ignoreElements: function (n) { return n.classList && n.classList.contains('exp'); },
            onclone: function (doc, clon) {
                // html2canvas no soporta texto con degradado (background-clip:text): saldría un rectángulo de color.
                Array.prototype.forEach.call(clon.querySelectorAll('.bm-text'), function (n) {
                    n.style.background = 'none'; n.style.webkitBackgroundClip = 'border-box'; n.style.color = '#583AFF';
                });
                var cs = clon.querySelectorAll('canvas');
                Array.prototype.forEach.call(cs, function (c, i) {
                    if (!imgs[i]) return;
                    var im = doc.createElement('img');
                    im.src = imgs[i];
                    im.style.width = c.style.width || (c.clientWidth ? c.clientWidth + 'px' : '100%');
                    im.style.height = c.style.height || (c.clientHeight ? c.clientHeight + 'px' : 'auto');
                    im.style.display = 'block';
                    c.parentNode.replaceChild(im, c);
                });
            }
        }).then(function (c) { return new Promise(function (ok) { c.toBlob(ok, 'image/png'); }); });
        blobP.catch(function (e) { if (window.console) console.error('Copiar imagen:', e); aviso('error', t.imagenError || 'No se pudo generar la imagen'); });

        var respaldo = function () {
            return blobP.then(function (b) { descargarBlob(b, nombreArchivo); aviso('info', t.imagenDescargada); });
        };
        // ClipboardItem recibe la promesa en el mismo gesto del clic (requisito de Safari).
        if (window.isSecureContext && navigator.clipboard && navigator.clipboard.write && window.ClipboardItem) {
            try {
                return navigator.clipboard.write([new ClipboardItem({ 'image/png': blobP })])
                    .then(function () { aviso('success', t.imagenCopiada); }, respaldo);
            } catch (e) { return respaldo(); }
        }
        return respaldo();
    }

    function init(id, cfg) {
        cfg = cfg || {};
        var acciones = cfg.acciones || {}, t = cfg.textos || {};
        var $exp = $('#' + id), $btn = $exp.find('.exp-btn');
        if (!$exp.length) return null;

        // Solo las opciones pedidas (data-opciones="excel,pdf,imagen,correo").
        var activas = String($exp.data('opciones') || 'excel,pdf,imagen,correo').split(',').map(function (s) { return s.trim(); });
        $exp.find('.exp-opt').each(function () {
            if (activas.indexOf(String($(this).data('accion'))) === -1) $(this).hide();
        });
        $exp.find('.exp-fmt .xls').toggle(activas.indexOf('excel') !== -1);
        $exp.find('.exp-fmt .pdf').toggle(activas.indexOf('pdf') !== -1);
        $exp.find('.exp-fmt .mail').toggle(activas.indexOf('correo') !== -1);
        $exp.find('.exp-sep').toggle(activas.indexOf('correo') !== -1 && activas.length > 1);

        function abrirMenu(abrir) {
            $exp.toggleClass('open', abrir);
            $btn.attr('aria-expanded', abrir ? 'true' : 'false');
            if (abrir) setTimeout(function () { $exp.find('.exp-opt:visible').first().trigger('focus'); }, 30);
        }
        $btn.on('click', function (e) { e.stopPropagation(); abrirMenu(!$exp.hasClass('open')); });
        $(document).on('click.exp' + id, function (e) { if (!$(e.target).closest('#' + id).length) abrirMenu(false); });
        $(document).on('keydown.exp' + id, function (e) {
            if (!$exp.hasClass('open')) return;
            var $opts = $exp.find('.exp-opt:visible'), i = $opts.index(document.activeElement);
            if (e.key === 'Escape') { abrirMenu(false); $btn.trigger('focus'); }
            else if (e.key === 'ArrowDown') { e.preventDefault(); $opts.eq((i + 1) % $opts.length).trigger('focus'); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); $opts.eq((i - 1 + $opts.length) % $opts.length).trigger('focus'); }
        });

        // Estado "generando" en el propio botón (el menú se cierra al elegir una opción).
        var textoBtn = $exp.find('.exp-texto').text();
        function ocupado(si) {
            $exp.toggleClass('busy', si);
            $exp.find('.exp-icono i').attr('class', si ? 'fas fa-spinner fa-spin' : ICONO);
            $exp.find('.exp-texto').text(si ? (t.generando || textoBtn) : textoBtn);
        }

        $exp.on('click', '.exp-opt', function () {
            var accion = String($(this).data('accion')), fn = acciones[accion];
            abrirMenu(false);
            if (!fn) return;
            if (accion === 'correo') { fn(); return; }       // abre un modal: el botón no queda "generando"
            if ($exp.hasClass('busy')) return;
            ocupado(true);
            var p;
            try { p = Promise.resolve(fn()); } catch (e) { p = Promise.reject(e); }
            p.catch(function (e) { if (window.console) console.error(e); aviso('error', t.errorGenerar); })
             .then(function () { ocupado(false); });
        });

        return { ocupado: ocupado, abrir: abrirMenu };
    }

    /**
     * Modal "Enviar por correo" compartido (Views/Shared/_ExportCorreoModal.cshtml). Valida destinatarios, genera el PDF
     * en el navegador (si el informe lo tiene) y hace el POST; el servidor reconstruye el informe con `datos()`.
     *   var correo = ExportMenu.correo({
     *       url: '/Informe/EnviarPorCorreo',
     *       token: function () { return $('input[name="__RequestVerificationToken"]').first().val(); },
     *       datos: function () { return { ...filtros del informe consultado... }; },   // se serializa con traditional:true
     *       pdf:   function () { return doc.output('datauristring'); },                 // opcional ('' = solo Excel)
     *       pills: function () { return ['Empresa', 'Rango', ...]; },                    // contexto que se ve en el modal
     *       textos: { valDestino, valMaximo, enviando, error, exitoTitulo, aceptar }
     *   });
     *   ... acciones: { correo: correo.abrir }
     */
    function correo(cfg) {
        var t = cfg.textos || {};
        var rx = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
        var $modal = $('#expCorreoModal');
        function error(msg) { $('#expCorreoError').text(msg || '').toggleClass('d-none', !msg); }

        function abrir() {
            var pills = (cfg.pills ? cfg.pills() : []) || [];
            $('#expCorreoPills').html(pills.map(function (p) { return $('<span class="exp-pill">').text(p)[0].outerHTML; }).join(''));
            error('');
            $modal.modal('show');
        }

        $modal.off('shown.bs.modal.exp').on('shown.bs.modal.exp', function () {
            if (!$('#expCorreoYo').is(':checked')) $('#expCorreoOtros').trigger('focus');
        });

        $('#expCorreoEnviar').off('click.exp').on('click.exp', function () {
            var $b = $(this);
            var incluirme = $('#expCorreoYo').is(':checked');
            var otros = ($('#expCorreoOtros').val() || '').split(/[,;\s]+/).filter(function (c) { return c; });
            var invalidos = otros.filter(function (c) { return !rx.test(c); });
            if ((!incluirme && otros.length === 0) || invalidos.length) {
                error(t.valDestino + (invalidos.length ? ' (' + invalidos.join(', ') + ')' : ''));
                return;
            }
            if (otros.length + (incluirme ? 1 : 0) > 10) { error(t.valMaximo); return; }
            error('');

            var htmlBtn = $b.html();
            $b.prop('disabled', true).html('<i class="fas fa-spinner fa-spin mr-1"></i>' + $('<span>').text(t.enviando).html());
            var pdf = '';
            try { pdf = cfg.pdf ? (cfg.pdf() || '') : ''; } catch (e) { pdf = ''; }
            var token = typeof cfg.token === 'function' ? cfg.token() : cfg.token;

            $.ajax({
                url: cfg.url, type: 'POST', traditional: true,
                data: $.extend({ __RequestVerificationToken: token }, cfg.datos ? cfg.datos() : {},
                    { correos: otros.join(','), incluirmeAMi: incluirme, mensaje: $('#expCorreoMensaje').val() || '', pdfBase64: pdf })
            }).done(function (r) {
                if (r && r.exito) {
                    $modal.modal('hide');
                    Swal.fire({ icon: 'success', title: t.exitoTitulo, text: r.mensaje, confirmButtonText: t.aceptar });
                } else {
                    error((r && r.mensaje) || t.error);
                }
            }).fail(function () {
                error(t.error);
            }).always(function () {
                $b.prop('disabled', false).html(htmlBtn);
            });
        });

        return { abrir: abrir };
    }

    return { init: init, aviso: aviso, descargarBlob: descargarBlob, copiarImagen: copiarImagen, correo: correo };
})();
