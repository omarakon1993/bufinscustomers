/* JS de UI de las pantallas de acceso (sin dependencias). Los textos llegan por data-* desde las vistas. */
window.Acceso = (function () {
    'use strict';

    /* OTP: un input real invisible sobre 6 casillas. opciones.onCompleto(valor) y opciones.onCambio(valor) son opcionales. */
    function otp(opciones) {
        opciones = opciones || {};
        var cont = document.getElementById('accOtp');
        if (!cont) return null;
        var input = cont.querySelector('input');
        var cajas = cont.querySelectorAll('.acc-otp-box');

        function pintar() {
            var v = input.value;
            for (var i = 0; i < cajas.length; i++) {
                cajas[i].textContent = v.charAt(i) || '';
                cajas[i].classList.toggle('filled', i < v.length);
                cajas[i].classList.toggle('current', i === Math.min(v.length, cajas.length - 1));
            }
            if (opciones.onCambio) opciones.onCambio(v);
        }
        input.addEventListener('input', function () {
            input.value = input.value.replace(/\D/g, '').slice(0, 6);
            pintar();
            if (input.value.length === 6 && opciones.onCompleto) opciones.onCompleto(input.value);
        });
        input.addEventListener('focus', function () { cont.classList.add('focused'); pintar(); });
        input.addEventListener('blur', function () { cont.classList.remove('focused'); });
        cont.addEventListener('click', function () { input.focus(); });
        pintar();
        if (document.activeElement === input) cont.classList.add('focused');
        return { input: input, valor: function () { return input.value; } };
    }

    function togglePwd(inputId, btn) {
        var inp = document.getElementById(inputId), ic = btn.querySelector('i');
        if (inp.type === 'password') { inp.type = 'text'; ic.classList.replace('fa-eye', 'fa-eye-slash'); }
        else { inp.type = 'password'; ic.classList.replace('fa-eye-slash', 'fa-eye'); }
    }

    /* Medidor de fuerza: textos en data-weak/fair/good/strong del elemento #strengthLabel */
    function fuerza(pwd) {
        var fill = document.getElementById('strengthFill'), label = document.getElementById('strengthLabel');
        if (!fill || !label) return;
        if (!pwd) { fill.style.width = '0%'; label.textContent = ''; return; }
        var score = 0;
        if (pwd.length >= 12) score++;
        if (pwd.length >= 16) score++;
        if (/[A-Z]/.test(pwd) && /[a-z]/.test(pwd)) score++;
        if (/\d/.test(pwd)) score++;
        if (/[^A-Za-z0-9]/.test(pwd)) score++;
        var d = label.dataset;
        var map = [
            { pct: '20%', color: '#ef4444', text: d.weak }, { pct: '40%', color: '#f97316', text: d.weak },
            { pct: '60%', color: '#eab308', text: d.fair }, { pct: '80%', color: '#22c55e', text: d.good },
            { pct: '100%', color: '#059669', text: d.strong }
        ];
        var s = map[Math.min(score, map.length - 1)];
        fill.style.width = s.pct; fill.style.background = s.color;
        label.textContent = s.text; label.style.color = s.color;
    }

    /* Temporizador TOTP informativo. Plantilla en data-tpl del contenedor: "El código cambia en {0} s" */
    function temporizador() {
        var cont = document.getElementById('accTimer');
        if (!cont) return;
        var fg = cont.querySelector('.ring-fg'), txt = cont.querySelector('.acc-timer-txt');
        var tpl = cont.getAttribute('data-tpl') || '{0}';
        var C = 56.55;
        function tick() {
            var s = 30 - (Math.floor(Date.now() / 1000) % 30);
            fg.style.strokeDashoffset = (C * (1 - s / 30)).toFixed(2);
            cont.classList.toggle('low', s <= 5);
            var partes = tpl.split('{0}');
            txt.textContent = '';
            txt.appendChild(document.createTextNode(partes[0]));
            var b = document.createElement('b'); b.textContent = s; txt.appendChild(b);
            txt.appendChild(document.createTextNode(partes.slice(1).join(String(s))));
        }
        tick(); setInterval(tick, 1000);
    }

    return { otp: otp, togglePwd: togglePwd, fuerza: fuerza, temporizador: temporizador };
})();
