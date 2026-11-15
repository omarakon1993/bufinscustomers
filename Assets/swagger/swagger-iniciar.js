// Arranque del probador interactivo de la API (Swagger UI 5.17.14, Apache-2.0; ver LICENSE y NOTICE en esta carpeta).
// Archivo propio de BUFINS, externo a la página para cumplir la CSP estricta de scripts (sin bloques en línea).
window.addEventListener('load', function () {
    window.ui = SwaggerUIBundle({
        url: '/api/v1/openapi.json',      // el contrato de la API (público, sin clave)
        dom_id: '#swagger-ui',
        deepLinking: true,
        docExpansion: 'list',
        defaultModelsExpandDepth: -1,
        displayRequestDuration: true,
        persistAuthorization: false,      // la clave solo vive en la memoria de la pestaña; no se guarda en el navegador
        tryItOutEnabled: true,
        presets: [SwaggerUIBundle.presets.apis],
        layout: 'BaseLayout'
    });
});
