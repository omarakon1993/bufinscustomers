/* Visor de documentación (ver Views/Shared/_DocumentacionVisor.cshtml).
   - DocVisor.init({ boton: '#id', url: '...' })          -> un botón abre SIEMPRE el mismo documento (informes).
   - DocVisor.abrirUrl(url, titulo, $elementoFoco)       -> abre el documento que se indique (biblioteca Documentación BUFINS). */
(function (w, $) {
    'use strict';
    var ligado = false, $foco = null, dinamico = false;

    function abrir() {
        $('#docvModal').addClass('open');
        document.body.style.overflow = 'hidden';
        $('#docvCerrar').trigger('focus');
    }
    function cerrar() {
        if (document.fullscreenElement) document.exitFullscreen();
        $('#docvModal').removeClass('open');
        document.body.style.overflow = '';
        // En modo dinámico se vacía el visor para que el siguiente documento no muestre el anterior.
        if (dinamico) $('#docvFrame').attr('src', 'about:blank');
        if ($foco && $foco.length) $foco.trigger('focus');
    }
    function ligar() {
        if (ligado) return;
        ligado = true;
        var $modal = $('#docvModal');
        $('#docvCerrar').on('click', cerrar);
        $modal.on('click', function (e) { if (e.target === this) cerrar(); });
        $(document).on('keydown', function (e) { if (e.key === 'Escape' && $modal.hasClass('open') && !document.fullscreenElement) cerrar(); });
        $('#docvFull').on('click', function () {
            var card = document.querySelector('#docvModal .docv-card');
            if (document.fullscreenElement) document.exitFullscreen(); else if (card.requestFullscreen) card.requestFullscreen();
        });
    }

    w.DocVisor = {
        init: function (op) {
            ligar();
            var $frame = $('#docvFrame'), $btn = $(op.boton);
            $btn.on('click', function () {
                $foco = $btn; dinamico = false;
                if (!$frame.attr('src')) $frame.attr('src', op.url);   // se carga una sola vez, al abrir
                abrir();
            });
        },
        abrirUrl: function (url, titulo, $elemento) {
            ligar();
            $foco = $elemento || null; dinamico = true;
            if (titulo) {
                $('#docvTitulo').empty().append('<i class="fas fa-book"></i>').append(document.createTextNode(titulo));
                $('#docvFrame').attr('title', titulo);
            }
            $('#docvFrame').attr('src', url);
            abrir();
        }
    };
})(window, window.jQuery);
