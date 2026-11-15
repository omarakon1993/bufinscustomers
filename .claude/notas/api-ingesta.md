# API de ingesta de BUFINS (`/api/v1`)

> Fase 1 en curso (2026-11-14). Plan y estudios: carpeta «Investigación API» de Documentación BUFINS (fuera del repo). Si cambias la API, actualiza esta nota.

## Principios
- **Una puerta nueva al cargue de siempre**, no un sistema paralelo: la API usa `Services/CargueOrquestadorService` (la misma lógica de la pantalla de Excel: staging, `sp_ValidarCargueStaging`, regla de oro, snapshot, auditoría). Nunca duplicar reglas en la API.
- **Endpoints mínimos aparte de MVC** (`Api/ApiV1.cs`, `app.MapGroup("/api/v1")`): no les aplican los filtros globales (sesión, 2FA, antiforgery, navegación). Responden siempre JSON (`{ "error": { "codigo", "mensaje" } }`); `Program.cs` excluye `/api` de la re-ejecución de páginas de error.
- **Autenticación propia** (`ApiV1Middleware` + `IntegracionesService`): `Authorization: Bearer bfk_{prefijo}_{secreto}`. La clave NO se guarda: solo prefijo + SHA-256 (secreto de 256 bits; comparación en tiempo constante, con hash señuelo si el prefijo no existe). Se ve una sola vez al crear/rotar. Revocar es definitivo.
- **Aislamiento**: una integración = una empresa + un **usuario técnico** (usuario existente de esa empresa; las acciones se auditan a su nombre vía `HttpContext.Items["ApiUsuarioTecnico"]`, que `AuditoriaService.CompletarDesdeContexto` lee antes que la sesión). La empresa sale de la integración, nunca del cuerpo de la petición. La API solo ve/toca los lotes que **ella misma** creó (`CarguesLotes.IdIntegracion`); si no, 404.
- **Confirmación humana por defecto**: `Integraciones.PermiteConfirmar = 0` → `/confirmar` responde 403 y subir con `confirmar=true` se ignora. Aun con permiso, la API **nunca** confirma una reducción fuerte (`requiere_revision_humana`) ni salta la regla de oro; solo auto-confirma lotes `ValidadoOk` (sin advertencias).
- **Límites** (en memoria): 20 claves incorrectas por IP / 10 min → 429; 120 llamadas por integración / min → 429. Lista de IP permitidas opcional por integración.
- **Idempotencia**: cabecera `Idempotency-Key` en `POST /cargues/archivo` (tabla `IntegracionesIdempotencia`, 7 días): un reenvío devuelve el lote ya creado (`repetido: true`).
- **Vencimiento**: los lotes con `Origen='API'` esperan revisión 48 h (los de Excel, 2 h) — `CargueStagingService.CondicionVencido`.
- Sin el script `Sql/046_Integraciones_API.sql` la API responde 503 `api_no_configurada` (el resto del sitio no cambia).

## Endpoints (v1)
Contrato OpenAPI 3 (público, sin clave): `GET /api/v1/openapi.json` ← `Api/openapi.v1.json` (**actualizarlo con cada cambio de endpoint**; se copia al publicar vía `<Content Update>` del `.csproj`).
`GET /ping` · `GET /esquemas` · **(A) plantilla completa**: `POST /cargues/archivo` (multipart: `archivo`, `anio`, `modo`=ejecucion|historico, `idEscenario`?, `confirmar`?) · **(B) por hojas en JSON**: `POST /cargues` (`{anio, modo, idEscenario?, referencia?}`) → `POST /cargues/{id}/hojas/{hoja}` (`{filas:[{Col:valor}], reemplazar?}`, todo o nada, máx. 50 000 filas/llamada) → `POST /cargues/{id}/validar` (`{confirmar?}`) · `GET /cargues/{id}` (estado + impacto) · `GET /cargues/{id}/hallazgos` · `POST /cargues/{id}/confirmar` · `DELETE /cargues/{id}`. Idioma de los mensajes: `?idioma=en` o `Accept-Language: en`.

### Envío por hojas (B) — reglas
- Lógica en `CargueOrquestadorService` (`IniciarLoteSinArchivo`, `AgregarFilasHoja`, `ValidarLoteApi`); escribe en `Staging_Ini_*` con `GuardarEnIni` (misma conversión que el Excel; `numeroFilaBase` continúa la numeración entre tandas).
- **Toda hoja de la empresa debe declararse** (aunque vaya con `filas: []`) antes de `validar` — tabla `dbo.CarguesLotesHojas` (script 046). Motivo: confirmar borra los `Ini_*` del año; una hoja olvidada borraría sus datos. Sin filas del año en total → 422.
- Cada llamada de hoja devuelve el lote a `EnValidacion` (`CargueStagingService.ReabrirParaEdicion`): hay que volver a validar antes de confirmar. Los lotes `Origen='API'` en `EnValidacion` vencen a las 6 h y en revisión a las 48 h.
- Columnas: se resuelven contra el esquema real de `Staging_Ini_X` (alias `Anio`→`Año`); columna desconocida → 422. Filas de otro año se omiten e informan (como el Excel). `Z_TablaPUC` (sin tabla Ini_) no se admite por JSON.
- **Aviso a revisores**: `NotificarRevisores` crea una notificación (campana, en español) para los Admin de Empresa (`Admin=1`) de la empresa cuando un cargue de la API queda esperando revisión o con errores (`Notif_CargueApi*`).

## Pantalla «Integraciones» (hecha)
`IntegracionesController` → `Views/Configuracion/Integraciones.cshtml` (textos `Integ_*` en ambos `.resx`). Permiso `ADMIN_CONFIG_INTEGRACIONES` (**crear la opción en `/MenuOpciones`**: Controller `Integraciones`, Action `Index`, `SoloAdminEmpresa`, no `SoloSuperAdmin`). Super Admin y Admin de Empresa (este, solo empresa/grupo vía `EmpresaAccesoHelper`). Todo por AJAX con antiforgery; Crear/Rotar devuelven la clave una sola vez (modal sin cierre accidental; no se guarda en `TempData`, log ni auditoría). Usuario técnico: cualquier usuario de la empresa que no sea Super Admin (el servicio devuelve códigos de error, el controlador los traduce). Auditoría `AuditoriaTipo.Integraciones` (Crear / Editar [incluye rotar] / Eliminar [revocar]).

## Manual de integración (hecho)
`App_Data/Documentacion/Tecnica/Manual_Integracion_API.html` (estilo oficial de la documentación, autocontenido, sin emojis). **Se sirve público en `GET /api/v1/manual`** (como `openapi.json`, sin clave; sin secretos) y la pantalla Integraciones lo enlaza junto al contrato. **Excepción deliberada al visor protegido** (`DocumentacionController`/`DocumentacionCatalogo`): ese visor bloquea copiar/imprimir/guardar y el manual existe para que el equipo técnico del cliente (sin cuenta en BUFINS) copie los ejemplos. NO está en la biblioteca «Documentación BUFINS» (no se pidió). **Al cambiar un endpoint, un código de error, un límite o la pantalla Integraciones, actualizar el manual y `Api/openapi.v1.json` en la misma tarea.** Variante en inglés escrita a mano (`Manual_Integracion_API.en.html`, la sirve `?idioma=en`; la pantalla enlaza el idioma de la interfaz): **al cambiar el español, ajustar también el inglés** (mismas secciones y ejemplos; no usa `gen_doc_en.py`).

## Pendiente
Sandbox (entorno de pruebas). Fase 2: ejecución de modelos asíncrona, agente de carpeta, webhook saliente.
