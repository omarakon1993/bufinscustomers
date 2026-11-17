/*
 * Selector de vistas de una familia de informes (parcial Views/Shared/_VistasInforme.cshtml).
 * Al pulsar otra vista se navega a ella conservando la empresa y el escenario elegidos en esta pagina (?idEmpresa=...&idEscenario=...);
 * la vista de destino los preselecciona. La salida es un desvanecido corto (clase vista-saliendo, ver vistas-informe.css) y la
 * pagina nueva aparece con un fundido, para que el cambio no se sienta brusco. Sin dependencias salvo el DOM.
 */
(function () {
    'use strict';
    var ESPERA_MS = 180;   // un poco menos que la transicion de salida del CSS (.2 s)

    document.addEventListener('click', function (e) {
        var a = e.target.closest ? e.target.closest('.vistas-informe-btn') : null;
        if (!a) return;
        e.preventDefault();
        if (a.classList.contains('on')) return;
        var cont = a.closest('.vistas-informe');
        var q = [];
        function valor(sel) { var el = sel ? document.querySelector(sel) : null; return el && el.value ? el.value : ''; }
        var emp = valor(cont.getAttribute('data-emp')), esc = valor(cont.getAttribute('data-esc'));
        if (emp) q.push('idEmpresa=' + encodeURIComponent(emp));
        if (esc) q.push('idEscenario=' + encodeURIComponent(esc));
        var base = a.getAttribute('href');
        var destino = q.length ? base + (base.indexOf('?') >= 0 ? '&' : '?') + q.join('&') : base;
        document.documentElement.classList.add('vista-saliendo');
        setTimeout(function () { window.location.href = destino; }, ESPERA_MS);
    });

    // Si el usuario vuelve con el boton Atras (pagina en cache), el contenido no debe quedar oculto.
    window.addEventListener('pageshow', function (ev) { if (ev.persisted) document.documentElement.classList.remove('vista-saliendo'); });
})();
