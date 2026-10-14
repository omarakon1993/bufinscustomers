/* Visor de documentación (ver Views/Shared/_DocumentacionVisor.cshtml). DocVisor.init({ boton: '#id', url: '...' }). */
(function (w, $) {
    'use strict';
    w.DocVisor = {
        init: function (op) {
            var $modal = $('#docvModal'), $frame = $('#docvFrame'), $btn = $(op.boton);
            function abrir() {
                if (!$frame.attr('src')) $frame.attr('src', op.url);   // se carga una sola vez, al abrir
                $modal.addClass('open');
                document.body.style.overflow = 'hidden';
                $('#docvCerrar').trigger('focus');
            }
            function cerrar() {
                if (document.fullscreenElement) document.exitFullscreen();
                $modal.removeClass('open');
                document.body.style.overflow = '';
                $btn.trigger('focus');
            }
            $btn.on('click', abrir);
            $('#docvCerrar').on('click', cerrar);
            $modal.on('click', function (e) { if (e.target === this) cerrar(); });
            $(document).on('keydown', function (e) { if (e.key === 'Escape' && $modal.hasClass('open') && !document.fullscreenElement) cerrar(); });
            $('#docvFull').on('click', function () {
                var card = document.querySelector('#docvModal .docv-card');
                if (document.fullscreenElement) document.exitFullscreen(); else if (card.requestFullscreen) card.requestFullscreen();
            });
        }
    };
})(window, window.jQuery);
