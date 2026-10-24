/* Logo de la empresa en los informes — UNA sola implementación para todos (ver CLAUDE.md, "Logo de la empresa").
   Sin dependencias (usa fetch). Uso típico en un informe:

     // (la dirección del servicio se deduce sola; EmpresaLogo.config(url) solo si hiciera falta cambiarla)
     EmpresaLogo.mostrar(idEmpresa, document.getElementById('pygLogo'));   // al pintar el resultado
     ...
     // dentro del constructor del PDF (síncrono): usa el logo ya cargado de la empresa consultada
     var ancho = EmpresaLogo.pdf(pdf, { xDerecha: pageW - M, y: 4, alto: 14 });   // 0 si no hay logo

   El logo viaja como data URL (misma respuesta JSON), así que html2canvas y jsPDF lo usan sin problemas de CORS. */
(function () {
    // La dirección se deduce de donde se cargó este script (respeta un directorio virtual): <raíz>/Assets/js/empresa-logo.js
    var urlDatos = (function () {
        try {
            var src = document.currentScript && document.currentScript.src;
            var i = src ? src.indexOf('/Assets/js/') : -1;
            if (i > 0) return src.substring(0, i) + '/EmpresaLogo/Datos';
        } catch (e) { }
        return '/EmpresaLogo/Datos';
    })();
    var cache = {};          // idEmpresa -> Promise<logo|null>
    var actual = null;       // logo de la empresa del informe que se ve ahora (lo lee el constructor del PDF)
    var turno = 0;           // evita que una respuesta tardía pise a una más reciente

    function cargar(id) {
        id = parseInt(id, 10);
        if (!id) return Promise.resolve(null);
        if (!cache[id]) {
            cache[id] = fetch(urlDatos + (urlDatos.indexOf('?') >= 0 ? '&' : '?') + 'id=' + id, { credentials: 'same-origin', cache: 'no-store' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (j) { return j && j.tiene ? { dataUrl: j.dataUrl, ancho: j.ancho, alto: j.alto, version: j.version } : null; })
                .catch(function () { delete cache[id]; return null; });
        }
        return cache[id];
    }

    function pintar(plate, logo) {
        if (!plate) return;
        var img = plate.querySelector('img');
        var cont = plate.parentNode;
        if (logo && img) {
            img.src = logo.dataUrl;
            plate.hidden = false;
            if (cont && cont.classList) cont.classList.add('con-logo');
        } else {
            if (img) img.removeAttribute('src');
            plate.hidden = true;
            if (cont && cont.classList) cont.classList.remove('con-logo');
        }
    }

    window.EmpresaLogo = {
        /** Dirección del endpoint (usar @Url.Action para respetar el directorio virtual). */
        config: function (url) { if (url) urlDatos = url; },

        /** Promise con { dataUrl, ancho, alto, version } o null si la empresa no tiene logo. Cacheado por empresa. */
        cargar: cargar,

        /** Olvida lo cargado (p. ej. tras subir un logo nuevo en la misma sesión). */
        olvidar: function () { cache = {}; },

        /** Carga el logo de la empresa, lo pinta en la placa y lo deja como "actual" para el PDF. Devuelve la Promise. */
        mostrar: function (idEmpresa, plate) {
            var mio = ++turno;
            actual = null;
            pintar(plate, null);
            return cargar(idEmpresa).then(function (logo) {
                if (mio !== turno) return logo;
                actual = logo;
                pintar(plate, logo);
                return logo;
            });
        },

        /** Quita el logo de pantalla (al limpiar el informe). */
        ocultar: function (plate) { turno++; actual = null; pintar(plate, null); },

        /** Logo de la empresa del informe que se está viendo (o null). */
        get actual() { return actual; },

        /**
         * Dibuja el logo en un PDF de jsPDF, sobre una placa blanca (el encabezado del PDF es oscuro).
         * opts: xDerecha (borde derecho de la placa, mm), y (borde superior, mm), alto (alto total de la placa, mm),
         *       anchoMax (opcional, mm; por defecto 4× el alto), logo (opcional; por defecto el actual).
         * Devuelve el ancho ocupado en mm (0 si no hay logo o no se pudo dibujar) para que el llamador evite pisarlo.
         */
        pdf: function (pdf, opts) {
            var logo = (opts && opts.logo) || actual;
            if (!pdf || !logo || !logo.dataUrl) return 0;
            try {
                var pad = 1.6;
                var altoPlaca = opts.alto || 14;
                var anchoMax = opts.anchoMax || altoPlaca * 4;
                var hImg = altoPlaca - pad * 2;
                var wImg = hImg * (logo.ancho / logo.alto);
                if (wImg > anchoMax - pad * 2) { wImg = anchoMax - pad * 2; hImg = wImg * (logo.alto / logo.ancho); }
                var wPlaca = wImg + pad * 2;
                var x = opts.xDerecha - wPlaca;
                pdf.saveGraphicsState();            // no deja el color de relleno cambiado para quien sigue dibujando
                try {
                    pdf.setFillColor(255, 255, 255);
                    pdf.roundedRect(x, opts.y, wPlaca, altoPlaca, 1.6, 1.6, 'F');
                    pdf.addImage(logo.dataUrl, 'PNG', x + pad, opts.y + (altoPlaca - hImg) / 2, wImg, hImg);
                } finally { pdf.restoreGraphicsState(); }
                return wPlaca;
            } catch (e) {
                if (window.console) console.warn('Logo de la empresa en PDF:', e);
                return 0;
            }
        }
    };
})();
