/* Comentarios de gestión (tarjeta compartida de los informes). Ver Views/Shared/_ComentariosGestion.cshtml.
   Uso:
     ComentariosGestion.montar({ raiz: '#lnComentarios', informe: 'LineasNegocio' });               // una vez
     ComentariosGestion.cargar({ idEmpresa: 6, anio: 2026, mes: 9, claves: ['Exp','Nal'] });          // al pintar el informe
     ComentariosGestion.ultimos();                                                                   // lista en memoria (para PDF)
   Opciones de montar: todos:true → lista los comentarios de TODOS los informes del cierre (Informe mensual de gerencia). */
(function (w, $) {
    'use strict';
    var T = w.ComentariosGestionTextos || {};
    var cfg = null, $raiz = null, ctx = null, lista = [], editandoId = null;

    function esc(t) { return $('<span>').text(t == null ? '' : String(t)).html(); }
    function token() { return $('input[name="__RequestVerificationToken"]').first().val(); }
    function rol(r) { return $raiz.find('[data-role="' + r + '"]'); }

    function aviso(icono, texto) {
        if (w.Swal) w.Swal.fire({ icon: icono, text: texto, timer: icono === 'success' ? 1500 : undefined, showConfirmButton: icono !== 'success' });
    }

    function pintarLista() {
        var $l = rol('lista').empty();
        rol('contador').text(lista.length ? '(' + lista.length + ')' : '');
        if (!lista.length) { $l.append('<div class="cg-com-vacio"><i class="fas fa-comment-dots"></i><span>' + esc(T.vacio) + '</span></div>'); return; }
        lista.forEach(function (c) {
            var origen = (cfg.todos && c.Informe) ? '<span class="cg-com-chip inf">' + esc((T.informes || {})[c.Informe] || c.Informe) + '</span>' : '';
            var clave = c.Clave ? '<span class="cg-com-chip">' + esc(c.Clave) + '</span>' : '';
            var inicial = esc((c.NombreUsuario || '?').charAt(0).toUpperCase());
            var acciones = c.PuedeEditar
                ? '<span class="cg-com-acc"><button type="button" class="cg-com-btn" data-act="editar" data-id="' + c.Id + '" title="' + esc(T.editar) + '"><i class="fas fa-pen"></i></button>' +
                  '<button type="button" class="cg-com-btn del" data-act="eliminar" data-id="' + c.Id + '" title="' + esc(T.eliminar) + '"><i class="fas fa-trash"></i></button></span>' : '';
            var cuerpo = (editandoId === c.Id)
                ? '<textarea class="form-control cg-com-edit" rows="2" maxlength="1000">' + esc(c.Texto) + '</textarea>' +
                  '<div class="cg-com-edit-acc"><button type="button" class="btn btn-modern-gradient btn-sm" data-act="guardar" data-id="' + c.Id + '">' + esc(T.guardar) + '</button> ' +
                  '<button type="button" class="btn btn-modern-secondary btn-sm" data-act="cancelar">' + esc(T.cancelar) + '</button></div>'
                : '<div class="cg-com-texto">' + esc(c.Texto).replace(/\n/g, '<br>') + '</div>';
            $l.append('<div class="cg-com-item"><span class="cg-com-avatar">' + inicial + '</span><div class="cg-com-cuerpo">' +
                '<div class="cg-com-meta"><b>' + esc(c.NombreUsuario) + '</b><span class="f">' + esc(c.Fecha) + (c.FechaEdicion ? ' · ' + esc(T.editadoFmt.replace('{0}', c.FechaEdicion)) : '') + '</span>' + origen + clave + acciones + '</div>' +
                cuerpo + '</div></div>');
        });
    }

    function llenarClaves(claves) {
        var $s = rol('clave').empty().append($('<option>').val('').text(T.general));
        (claves || []).forEach(function (c) { $s.append($('<option>').val(c).text(c)); });
        rol('lblClave').toggle((claves || []).length > 0); $s.toggle((claves || []).length > 0);
    }

    function cargar(c) {
        if (!$raiz || !c || !c.idEmpresa) return Promise.resolve([]);
        ctx = c; editandoId = null;
        $raiz.show();
        llenarClaves(c.claves);
        rol('periodo').text(T.periodoFmt.replace('{0}', (T.meses[c.mes - 1] || c.mes) + ' ' + c.anio));
        return new Promise(function (ok) {
            $.post($raiz.data('url-listar'), { idEmpresa: c.idEmpresa, anio: c.anio, mes: c.mes, informe: cfg.todos ? '' : cfg.informe, __RequestVerificationToken: token() }, function (resp) {
                if (!resp || !resp.success) { lista = []; pintarLista(); ok(lista); return; }
                rol('nodisponible').toggle(!resp.disponible);
                rol('form').toggle(!!resp.disponible);
                lista = resp.comentarios || [];
                pintarLista(); ok(lista);
            }).fail(function () { lista = []; pintarLista(); ok(lista); });
        });
    }

    function publicar() {
        var texto = $.trim(rol('texto').val());
        if (!texto) { aviso('warning', T.textoVacio); return; }
        if (!ctx) return;
        var $b = rol('publicar').prop('disabled', true);
        $.post($raiz.data('url-crear'), { idEmpresa: ctx.idEmpresa, informe: cfg.informe, anio: ctx.anio, mes: ctx.mes, clave: rol('clave').val() || '', texto: texto, __RequestVerificationToken: token() }, function (resp) {
            $b.prop('disabled', false);
            if (!resp || !resp.success) { aviso('error', (resp && resp.message) || T.error); return; }
            rol('texto').val(''); rol('restantes').text('1000');
            cargar(ctx);
        }).fail(function () { $b.prop('disabled', false); aviso('error', T.error); });
    }

    function accion(act, id) {
        if (act === 'editar') { editandoId = id; pintarLista(); return; }
        if (act === 'cancelar') { editandoId = null; pintarLista(); return; }
        if (act === 'guardar') {
            var texto = $.trim($raiz.find('.cg-com-edit').val());
            if (!texto) { aviso('warning', T.textoVacio); return; }
            $.post($raiz.data('url-editar'), { id: id, texto: texto, __RequestVerificationToken: token() }, function (resp) {
                if (!resp || !resp.success) { aviso('error', (resp && resp.message) || T.error); return; }
                cargar(ctx);
            }).fail(function () { aviso('error', T.error); });
            return;
        }
        if (act === 'eliminar') {
            var borrar = function () {
                $.post($raiz.data('url-eliminar'), { id: id, __RequestVerificationToken: token() }, function (resp) {
                    if (!resp || !resp.success) { aviso('error', (resp && resp.message) || T.error); return; }
                    cargar(ctx);
                }).fail(function () { aviso('error', T.error); });
            };
            if (w.Swal) w.Swal.fire({ icon: 'question', text: T.confirmarEliminar, showCancelButton: true, confirmButtonText: T.si, cancelButtonText: T.cancelar }).then(function (r) { if (r.isConfirmed) borrar(); });
            else if (w.confirm(T.confirmarEliminar)) borrar();
        }
    }

    function montar(o) {
        cfg = o || {}; $raiz = $(cfg.raiz); T = w.ComentariosGestionTextos || T;
        rol('publicar').on('click', publicar);
        rol('texto').on('input', function () { rol('restantes').text(1000 - $(this).val().length); });
        $raiz.on('click', '[data-act]', function () { accion($(this).data('act'), Number($(this).data('id'))); });
    }

    w.ComentariosGestion = { montar: montar, cargar: cargar, ultimos: function () { return lista.slice(); } };
})(window, jQuery);
