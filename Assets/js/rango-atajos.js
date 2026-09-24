/*
 * Atajos del slider de rango de fechas — compartidos por el Informe PYG y el Informe Dinámico de
 * Líneas de Tiempo (misma lógica y mismo markup `.rango-atajos` en ambos).
 *
 *   todo        → todos los meses con datos (no depende de la fecha).
 *   ytd         → año en curso: enero del año de HOY hasta el mes de hoy.
 *   6m / 3m     → últimos 6 / 3 meses terminando en el mes de hoy ("Último trimestre" = 3m).
 *   mesAnterior → el mes anterior al de hoy (en enero, diciembre del año anterior).
 *   mesActual   → el mes de hoy.
 *
 * "Hoy" es la fecha del SERVIDOR (la vista la inyecta al renderizar, equivalente a GETDATE()), no la
 * del navegador. Cada ventana de calendario se intersecta con los meses que tienen datos (el slider
 * solo contiene meses con datos): se seleccionan los meses cargados dentro de la ventana; si ninguno
 * tiene datos, el atajo no está disponible (indices() devuelve null).
 *
 * rango: [{anio, mes}] en orden cronológico · hoy: {anio, mes} · abrevs: ['Ene', …, 'Dic'].
 */
window.RangoAtajos = (function () {
    function clave(anio, mes) { return anio * 12 + (mes - 1); }

    function ventana(preset, hoy) {
        var kHoy = clave(hoy.anio, hoy.mes);
        switch (preset) {
            case 'ytd': return [clave(hoy.anio, 1), kHoy];
            case '6m': return [kHoy - 5, kHoy];
            case '3m': return [kHoy - 2, kHoy];
            case 'mesAnterior': return [kHoy - 1, kHoy - 1];
            case 'mesActual': return [kHoy, kHoy];
            default: return null;
        }
    }

    /** Índices {d, h} dentro de `rango` que selecciona el atajo, o null si no aplica / sin datos. */
    function indices(preset, rango, hoy) {
        var n = rango.length;
        if (!n) return null;
        if (preset === 'todo') return { d: 0, h: n - 1 };
        var v = ventana(preset, hoy);
        if (!v) return null;
        var d = -1, h = -1;
        for (var i = 0; i < n; i++) {
            var k = clave(rango[i].anio, rango[i].mes);
            if (k >= v[0] && k <= v[1]) { if (d < 0) d = i; h = i; }
        }
        return d < 0 ? null : { d: d, h: h };
    }

    /** Texto de la ventana de calendario del atajo: "Sep 2026" o "Abr 2026 – Sep 2026" ('' para "todo"). */
    function etiquetaVentana(preset, hoy, abrevs) {
        var v = ventana(preset, hoy);
        if (!v) return '';
        var f = function (k) { return abrevs[k % 12] + ' ' + Math.floor(k / 12); };
        return v[0] === v[1] ? f(v[0]) : (f(v[0]) + ' ' + String.fromCharCode(0x2013) + ' ' + f(v[1]));
    }

    /**
     * Pinta el estado de los botones `$botones` (con data-preset): activo si coincide con [iDesde, iHasta];
     * `.no-disponible` + aria-disabled si el atajo no tiene datos (NO el atributo disabled, que en Chrome
     * impide ver el tooltip); tooltip con la ventana de calendario o el motivo.
     * textos: { tooltipFmt: 'Según la fecha de hoy: {0}', sinDatosFmt: '{0} no tiene datos cargados' }.
     */
    function pintarBotones($botones, rango, hoy, iDesde, iHasta, abrevs, textos) {
        $botones.each(function () {
            var preset = String($(this).data('preset'));
            var p = indices(preset, rango, hoy);
            var ventanaTxt = etiquetaVentana(preset, hoy, abrevs);
            var titulo = !ventanaTxt ? '' : (p ? textos.tooltipFmt : textos.sinDatosFmt).replace('{0}', ventanaTxt);
            $(this).prop('disabled', !rango.length)
                .toggleClass('no-disponible', rango.length > 0 && !p)
                .attr('aria-disabled', p ? 'false' : 'true')
                .attr('title', titulo)
                .toggleClass('active', !!p && p.d === iDesde && p.h === iHasta);
        });
    }

    /** Atajo inicial: el preferido si tiene datos; si no, "todo". */
    function porDefecto(preferido, rango, hoy) {
        return indices(preferido, rango, hoy) || indices('todo', rango, hoy);
    }

    return { indices: indices, etiquetaVentana: etiquetaVentana, pintarBotones: pintarBotones, porDefecto: porDefecto };
})();
