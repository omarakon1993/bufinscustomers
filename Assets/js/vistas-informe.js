/*
 * Selector de vistas de una familia de informes (parcial Views/Shared/_VistasInforme.cshtml).
 * Al pulsar otra vista se navega a ella conservando la empresa y el escenario elegidos en esta página (?idEmpresa=…&idEscenario=…);
 * la vista de destino los preselecciona. Sin dependencias salvo el DOM.
 */
(function () {
    'use strict';
    document.addEventListener('click', function (e) {
        var a = e.target.closest ? e.target.closest('.vistas-informe-btn') : null;
        if (!a || a.classList.contains('on')) { if (a) e.preventDefault(); return; }
        var cont = a.closest('.vistas-informe');
        var q = [];
        function valor(sel) { var el = sel ? document.querySelector(sel) : null; return el && el.value ? el.value : ''; }
        var emp = valor(cont.getAttribute('data-emp')), esc = valor(cont.getAttribute('data-esc'));
        if (emp) q.push('idEmpresa=' + encodeURIComponent(emp));
        if (esc) q.push('idEscenario=' + encodeURIComponent(esc));
        if (q.length) { e.preventDefault(); window.location.href = a.getAttribute('href') + (a.getAttribute('href').indexOf('?') >= 0 ? '&' : '?') + q.join('&'); }
    });
})();
