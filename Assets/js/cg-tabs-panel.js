/* Pestañas con contenido en la MISMA página (sin recargar): «Historial de cargues» (Versiones | Registro) y el hub de Auditoría.
   Solo cambia el contenido: el anterior se desvanece, se pide el nuevo por AJAX, se reemplaza y entra con un fundido; el marcador
   de la pestaña activa se desliza de una a otra. Una sola pestaña vive en el DOM a la vez (así no chocan ids ni scripts).

   Uso:  CgTabs.init({
             nav:   '#cgTabs',       // contenedor .cg-tabs.cg-tabs-js con el marcador .cg-tab-marca y los botones .cg-tab[data-key][data-url]
             panel: '#cgPanel',      // donde se inyecta el contenido
             spin:  '#cgSpin',       // indicador de carga (aparece solo si tarda)
             intro: '#cgIntro',      // opcional: recibe el data-intro (HTML) de la pestaña activa
             error: '<div>…</div>',  // HTML si falla la carga
             inicial: 'versiones',   // opcional: clave de la pestaña inicial
             storageKey: 'clave',    // opcional: recuerda la última pestaña en sessionStorage
             urlInicial: function ($btn) { … },   // opcional: URL de la primera carga (p. ej. reenviar la cadena de consulta)
             alActivar:  function ($btn) { … }    // opcional: se llama al activar una pestaña (p. ej. actualizar la URL)
         }); */
(function ($) {
    'use strict';

    function init(o) {
        var $seg = $(o.nav), $panel = $(o.panel), $spin = $(o.spin), $intro = o.intro ? $(o.intro) : $();
        var $tabs = $seg.find('.cg-tab'), $marca = $seg.find('.cg-tab-marca');
        var cargando = false, spinTimer = null;

        // Coloca el marcador exactamente sobre la pestaña activa (medición con getBoundingClientRect).
        function moverMarca(btn, sinAnim) {
            if (!btn || !$marca.length) return;
            var cont = $seg[0], cr = cont.getBoundingClientRect(), br = btn.getBoundingClientRect();
            if (sinAnim) $marca.addClass('no-anim');
            $marca.css({
                width: br.width + 'px', height: br.height + 'px',
                transform: 'translate(' + (br.left - cr.left - cont.clientLeft) + 'px,' + (br.top - cr.top - cont.clientTop) + 'px)'
            });
            if (sinAnim) { void $marca[0].offsetWidth; $marca.removeClass('no-anim'); }
        }

        function activar($btn, primera) {
            if (!$btn || !$btn.length) return;
            if (cargando && !primera) return;
            cargando = true;

            $tabs.removeClass('is-active').removeAttr('aria-current');
            $btn.addClass('is-active').attr('aria-current', 'page');
            moverMarca($btn[0], !!primera);
            if ($intro.length) $intro.html($btn.data('intro') || '');
            if (o.storageKey) { try { sessionStorage.setItem(o.storageKey, $btn.data('key')); } catch (e) { } }
            if (o.alActivar) o.alActivar($btn, !!primera);

            // El contenido actual se desvanece; el indicador de carga solo aparece si tarda más de 220 ms.
            $panel.addClass('is-loading');
            clearTimeout(spinTimer);
            spinTimer = setTimeout(function () { $spin.addClass('on'); }, 220);

            var inicio = Date.now();
            function fin() {
                clearTimeout(spinTimer);
                $spin.removeClass('on');
                // El contenido nuevo entra con un fundido corto (no aparece de golpe).
                $panel.addClass('is-entering').removeClass('is-loading');
                void $panel[0].offsetWidth;
                requestAnimationFrame(function () { $panel.removeClass('is-entering'); });
                cargando = false;
            }
            function mostrar(html) {
                // Espera a que el contenido anterior termine de desvanecerse antes de cambiarlo.
                setTimeout(function () { $panel.html(html); fin(); }, (primera ? 0 : Math.max(0, 180 - (Date.now() - inicio))));
            }
            var url = (primera && o.urlInicial) ? o.urlInicial($btn) : $btn.data('url');
            $.ajax({ url: url, dataType: 'html' }).done(mostrar).fail(function () { mostrar(o.error || ''); });
        }

        $tabs.on('click', function () {
            var $b = $(this);
            if (!$b.hasClass('is-active')) activar($b);
        });

        var rz;
        $(window).on('resize', function () {
            clearTimeout(rz);
            rz = setTimeout(function () { moverMarca($tabs.filter('.is-active')[0], true); }, 120);
        });

        var clave = o.inicial || null;
        if (!clave && o.storageKey) { try { clave = sessionStorage.getItem(o.storageKey); } catch (e) { } }
        var $ini = clave ? $tabs.filter('[data-key="' + clave + '"]') : $();
        if (!$ini.length) $ini = $tabs.first();

        activar($ini, true);
        // Reajuste tras cargar fuentes / layout definitivo.
        $(window).on('load', function () { moverMarca($tabs.filter('.is-active')[0], true); });
        requestAnimationFrame(function () { moverMarca($tabs.filter('.is-active')[0], true); });
    }

    window.CgTabs = { init: init };
})(jQuery);
