/* WebAuthn (passkeys) — utilidades del navegador para el inicio de sesión y la pantalla Seguridad.
   Sin dependencias. Expone window.WebAuthnBufins. */
(function () {
    function b64uABuf(s) {
        s = String(s || '').replace(/-/g, '+').replace(/_/g, '/');
        while (s.length % 4) s += '=';
        var bin = atob(s), buf = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
        return buf.buffer;
    }

    function bufAB64u(buf) {
        var b = new Uint8Array(buf), s = '';
        for (var i = 0; i < b.length; i++) s += String.fromCharCode(b[i]);
        return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    function soportado() {
        return !!(window.PublicKeyCredential && navigator.credentials && navigator.credentials.get);
    }

    // POST de formulario con el token anti-falsificación; devuelve la respuesta JSON.
    function post(url, token, datos) {
        var cuerpo = new URLSearchParams();
        cuerpo.append('__RequestVerificationToken', token);
        Object.keys(datos || {}).forEach(function (k) { cuerpo.append(k, datos[k]); });
        return fetch(url, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
            body: cuerpo.toString()
        }).then(function (r) { return r.json(); });
    }

    // Inicio de sesión: pide el reto, firma con la passkey y envía la aserción.
    // Devuelve una promesa con el JSON final del servidor ({ok, redirect, mensaje}) o {cancelado:true}.
    function iniciarSesion(urlOpciones, urlVerificar, token) {
        return post(urlOpciones, token, {}).then(function (o) {
            if (!o.ok) return o;
            var pk = {
                challenge: b64uABuf(o.challenge), rpId: o.rpId, timeout: o.timeout,
                userVerification: o.userVerification,
                allowCredentials: o.allowCredentials.map(function (c) { return { type: c.type, id: b64uABuf(c.id) }; })
            };
            return navigator.credentials.get({ publicKey: pk }).then(function (cred) {
                var r = cred.response;
                return post(urlVerificar, token, {
                    credentialId: bufAB64u(cred.rawId),
                    clientDataJSON: bufAB64u(r.clientDataJSON),
                    authenticatorData: bufAB64u(r.authenticatorData),
                    signature: bufAB64u(r.signature)
                });
            }, function () { return { cancelado: true }; });
        });
    }

    // Registro (pantalla Seguridad): el servidor da las opciones y recibe la credencial creada.
    function registrar(urlOpciones, urlCompletar, token, nombre, extra) {
        return post(urlOpciones, token, extra || {}).then(function (o) {
            if (!o.ok) return o;
            var pk = {
                challenge: b64uABuf(o.challenge),
                rp: { name: o.rpName, id: o.rpId },
                user: { id: b64uABuf(o.userId), name: o.userName, displayName: o.userDisplayName },
                pubKeyCredParams: [{ type: 'public-key', alg: -7 }, { type: 'public-key', alg: -257 }],
                timeout: o.timeout, attestation: 'none',
                authenticatorSelection: { residentKey: 'discouraged', userVerification: 'required' },
                excludeCredentials: (o.excludeCredentials || []).map(function (c) { return { type: c.type, id: b64uABuf(c.id) }; })
            };
            return navigator.credentials.create({ publicKey: pk }).then(function (cred) {
                var r = cred.response;
                return post(urlCompletar, token, {
                    clientDataJSON: bufAB64u(r.clientDataJSON),
                    attestationObject: bufAB64u(r.attestationObject),
                    nombre: nombre
                });
            }, function () { return { cancelado: true }; });
        });
    }

    window.WebAuthnBufins = { soportado: soportado, iniciarSesion: iniciarSesion, registrar: registrar };
})();
