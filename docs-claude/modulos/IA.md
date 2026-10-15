<!-- Detalle técnico extraído de CLAUDE.md. Si cambias este módulo, actualiza este archivo. -->

# IA (integración, control de acceso y presupuesto)

### IA (AI) Integration

> **REGLA DE DATOS (MANDATORY):** la IA **solo puede tomar datos de las tablas `dbo.Modelo*`** (`ModeloBalance`, `ModeloBalanceDiff`, `ModeloBalancePpto`, `ModeloFlujoCaja`, `ModeloFlujoEfectivo`, `ModeloLineasNegocio`, `ModeloPYG`, `ModeloTesoreriaPpto`), nunca de `Ini_*` ni de ninguna otra tabla. Se aplica con `InformeTablasDatosService.ObtenerTablasModelos()` (solo modelos activos en `ModelosEjecucion`): `InformeTablasDatosController.ConsultarConIA` rechaza cualquier otra tabla (`EsTablaDeModelo`), `AnalisisIAController` solo lista esas tablas. PYG/Balance gerencial leen `ModeloPYG`/`ModeloBalance`. Cualquier punto de IA nuevo debe cumplir esta regla.

`IAService` (`Services/IAService.cs`) calls the **OpenAI API** (default model `gpt-4o`, overridable via `ConfiguracionSistema` key `OpenAIModel`).

- **Config keys (DB, `ConfiguracionSistema`):** `OpenAIApiKey`, `OpenAIModel`, `OpenAIMaxTokens` (default 8000), `OpenAITemperature` (empty = not sent), `IA_CacheHoras`, `IA_CostoPor1kTokensPrompt/Respuesta`, `IA_TokensMensualesPorEmpresa`, `IA_MaxCaracteresPregunta`
- **Endpoint:** `https://api.openai.com/v1/chat/completions`
- **Timeout:** 60 seconds; retries on 429/503 with backoff; one automatic retry (up to 16 000 tokens) if a reasoning model returns empty content; circuit breaker (3 consecutive failures → open 5 min); response cache in memory + `dbo.CacheRespuestasIA`
- **Data sent:** Análisis IA sends up to 200 000 chars of CSV (first turn only); PYG/Balance send their already-computed report (`filas` + `kpis`)
- **Models:** `IAConsultaRequest` / `IAConsultaResponse` (incl. `TokensPrompt/TokensRespuesta/TokensTotal`, `Modelo`, `DesdeCache`) in `Models/IAModels.cs`

**IAGateway — punto único de ejecución (`Services/IAGateway.cs`, `Models/IAGatewayModels.cs`):** todo punto de IA hace SOLO esto: (1) `ValidarAccesoIA(usuario, esAdmin, idEmpresa)` (helper de `BaseController`: acceso por usuario + presupuesto mensual de tokens vía `IAUsoService`, avisos 80 %/100 % a Super Admin, devuelve el JSON de error ya traducido o `null`), (2) arma los datos (`IAConsultaRequest`), (3) `await new IAGateway().EjecutarAsync(new IASolicitud { Funcion = IAFuncion.X, ... })`. El gateway lee la configuración (`OpenAIApiKey/Model/MaxTokens/Temperature`), resuelve prompt (`CodigoPrompt` → GestorPrompts, `PromptPorDefecto` si falta), guardrail y contexto de negocio, llama a `IAService` en el idioma pedido (`Idioma` "es"/"en"; `BaseController.IdiomaIA`), calcula el costo estimado y **mide siempre**: `AuditoriaAnalisisIA` (con `TokensTotal`, base del presupuesto — `SumarTokensMes`), `dbo.IAUsoLog` (una fila por llamada: función, modelo, tokens prompt/respuesta, costo, latencia, caché, error) y `dbo.IAUsoMensual` (acumulado por empresa/mes). Los registros nunca lanzan (fallan a `AppLogger`). Funciones: `Chat` (Análisis IA), `ResumenHome` (sin uso desde que se retiró el resumen IA del Home; la constante se conserva por las filas históricas de `IAUsoLog`), `InsightsPYG`, `InsightsBalance`, `InsightsTesoreria`, `InsightsFlujoCaja`, `InsightsFlujoEfectivo`, `InsightsIndicadores`. **Un punto de IA nuevo NO debe llamar a `IAService` directo**: agrega una constante a `IAFuncion` y usa el gateway, para que el consumo no se escape de la medición. PYG/Balance pasan `Idioma = IdiomaIA` + `InstruccionEnIngles`: la vista separa los insights por etiqueta (acepta Logro/Alerta/Eficiencia y Achievement/Alert/Efficiency; Liquidez/Solvencia/Capital de trabajo y Liquidity/Solvency/Working capital) y en inglés el gateway le exige al modelo esas etiquetas exactas. **El presupuesto mensual (`SumarTokensMes`) lee `dbo.IAUsoMensual`** (cae a sumar `AuditoriaAnalisisIA` solo si esa tabla no existe), así que limpiar la auditoría no reinicia el consumo. Scripts en orden: `Sql/012_AuditoriaAnalisisIA_TokensEIndice.sql` → `Sql/013_IAUsoLog_CreateTables.sql` → desplegar → `Sql/014_IAUsoMensual_Backfill.sql` (re-ejecutable). Plan y pendientes: `docs/IA_Propuesta_Estructura.html` (sin planes comerciales por empresa: descartado).

**Optimización de costo (2026-10)** — scripts en `Sql/018_IA_OptimizacionCostos.sql` (opcional; el código funciona sin él):
- **Historial**: `InformeTablasDatosController.SanearHistorial` conserva contexto + últimos 3 pares Q&A (`MaxMensajesHistorial = 7`) y abrevia a 3.000 caracteres las respuestas previas del asistente salvo la última (recorte determinista: no rompe el prefijo cacheable).
- **Datos**: `ConstruirCsv` omite columnas vacías o constantes en todas las filas (sin pérdida: se declaran una vez en `IAConsultaRequest.NotaDatos`, que `ConstruirPrompt` imprime antes de los datos) y normaliza decimales. PYG/Balance serializan su reporte con `BaseController.JsonIASinNulos` (sin campos nulos).
- **Caché de prompts de OpenAI**: `ConstruirPrompt` ordena lo estable primero (rol + contexto de negocio) y lo variable después (tabla/filtros, datos, pregunta). `IAService` envía `prompt_cache_key = "bufins:{tabla}"` en modelos `gpt-*` (si el proveedor responde 400 mencionándolo, se reintenta sin él y se deja de enviar). `IAConsultaResponse.TokensCacheados` lee `usage.prompt_tokens_details.cached_tokens`; se guarda en `IAUsoLog.TokensCacheados` (Sql/018; `IAGateway.InsertarLog` baja de nivel solo si la columna no existe — tras crearla hay que reciclar el app pool) y se ve como KPI "Entrada en caché OpenAI" en Uso y costos.
- **Caché de respuestas atada a los datos (mejora 5)**: la clave (`GenerarCacheKey`) ya incluye el hash de `DatosJson` (o del historial, que contiene los datos, en seguimientos). Los resúmenes/insights (sin pregunta ni historial) viven `IA_CacheHorasModelo` horas (def. 720 = 30 días; `IA_CacheHoras` sigue mandando en preguntas libres y 0 apaga toda la caché). **No hay invalidación manual**: al ejecutar el modelo —completo o solo algunas tablas— los datos de las tablas afectadas cambian → cambia el hash → la clave deja de coincidir y se regenera; las tablas/empresas cuyos datos no cambiaron siguen sirviendo desde caché. Un acierto de caché devuelve una copia sin tokens (antes la caché en memoria arrastraba los tokens de la llamada original y se descontaban de nuevo del presupuesto).
- **Enrutamiento por tarea** (`IAGateway.EsTareaSimple`): insights PYG/Balance y, en Análisis IA, resumen gerencial/"solo cifras" sin pregunta usan el modelo de `IA_ModeloSimple` (**vacío = sin enrutamiento**). Preguntas libres, análisis detallado y riesgo siguen con `OpenAIModel`. El modelo fijo de la empresa tiene prioridad; `Degradar` sigue pisando ambos.
- **Costo**: `IAGateway.CalcularCosto` usa tarifa por modelo si existe (`IA_CostoPor1kTokensPrompt:{modelo}`, `IA_CostoPor1kTokensRespuesta:{modelo}`, `IA_FactorCostoCache:{modelo}`) y si no la global; los tokens cacheados se cobran al `IA_FactorCostoCache` (def. 0.5; gpt-5.x ≈ 0.1).

Integrated in `InformeTablasDatosController.ConsultarConIA()` (Análisis IA), `InformePYGController.GenerarInsightsIA` and `InformeBalanceController.GenerarInsightsIA`. The prompt instructs the model to act as a Colombian corporate finance analyst and respond in Spanish with markdown.

**Prompt customization**: The instruction block for the automatic summary (when no question is asked) is stored in the `GestorPrompts` DB table with `Codigo = 'RESUMEN_GERENCIAL'`. `GestorPromptsService.ObtenerPorCodigo("RESUMEN_GERENCIAL")` fetches it; if inactive/missing, `IAService` falls back to the hardcoded text. Managed via `GestorPromptsController` (Super Admin only). `IAService.ConsultarAsync()` accepts an optional `instruccionesPersonalizadas` parameter.

**Business knowledge base (Fase A — "entrenar" el agente sin fine-tuning)**: `Codigo = 'CONTEXTO_NEGOCIO_BUFINS'` in `GestorPrompts` holds free-form, Super-Admin-curated context (glossary, business rules, recurring clarifications) that gets prepended to **every** IA prompt — both the auto-summary and question-answering paths, regardless of `modo` — via the `contextoNegocio` parameter now on `IAService.ConsultarAsync()`/`ConstruirPrompt()`. Wired at both call sites that build a fresh conversation: `InformeTablasDatosController.ConsultarConIA()` (`AnalisisIAController` reuses `ConsultarConIA` from the frontend, so it's covered too). Sent only on the first turn of a conversation (same convention as `[DATOS_FINANCIEROS]`); folded into the cache key so edits invalidate cached answers. No row exists until a Super Admin creates one via the existing `GestorPromptsController` UI — `ObtenerPorCodigo` returns `null` and behavior is unchanged (opt-in, zero risk). This is intentionally **not** real model fine-tuning: OpenAI fine-tuning would need a curated training dataset and a batch retrain/redeploy cycle, wouldn't reflect same-day edits, and risks baking in wrong answers learned from unreviewed traffic — a curated prompt block reviewed by a human stays safer for a multi-company financial system. `AuditoriaAnalisisIA` already logs every question+answer (per user/empresa/tabla) and is the natural source to mine for what to add here (Fase B, human-reviewed, not yet built).

**Available tables for IA:** the 8 `Modelo*` tables listed in the data rule above (`InformeTablasDatosService.ObtenerTablasModelos()`). `InformeTablasDatosService` only knows these 8 tables (the old `*_VT` ones were removed). Table names are whitelist-validated before use in SQL to prevent injection.

> To switch AI provider, only `IAService.cs` needs to change — update `OpenAIEndpoint`, `OpenAIModelDefault`, and the Authorization header format. The config key is `OpenAIApiKey` in Web.config.

**`ConfiguracionSistemaService`** (`Services/ConfiguracionSistemaService.cs`) — reads key/value pairs from the `ConfiguracionSistema` DB table (`SELECT Valor FROM ConfiguracionSistema WHERE Clave = @Clave`). Used by `InformeTablasDatosController.ConsultarConIA()` to fetch `OpenAIApiKey` at runtime (DB value takes precedence over Web.config). Use this service for any secret or runtime-configurable setting that should be stored in the DB rather than deployed config.

### Control de acceso y presupuesto de IA por empresa

Punto único de control para **todas** las consultas de IA del sistema (Análisis IA, 
insights de PYG/Balance). **No hay planes comerciales por empresa** (decisión tomada): el control se
configura directamente sobre cada empresa.

- **`IAUsoService`** (`Services/IAUsoService.cs`) — `EvaluarAcceso(usuario, esSuperAdmin, idEmpresa, funcion, tokensEstimados, tokensReservados)`
  devuelve un `ResultadoAcceso`. Super Admin exento de todo. Reglas, en este orden: (1) interruptor
  maestro `IaHabilitada` de la empresa → `EMPRESA_SIN_IA`; (2) `Usuarios.AccesoConsultasIA` (NULL/true =
  permitido) → `SIN_ACCESO`; (3) función habilitada para la empresa (`FuncionesPermitidas`, NULL = todas) →
  `FUNCION_NO_PERMITIDA`; (4) tope diario de tokens por usuario (`TopeDiarioUsuario`) → `TOPE_USUARIO`;
  (5) presupuesto de tokens del periodo (override de la empresa o global `IA_TokensMensualesPorEmpresa`; 0 =
  ilimitado). Al agotarse **o si la consulta estimada lo excedería**, se aplica `PoliticaAgotado`:
  `Bloquear` (→ `PRESUPUESTO_AGOTADO` / `PRESUPUESTO_INSUFICIENTE`), `Degradar` (modelo económico
  `IA_ModeloEconomico`, por defecto `gpt-4o-mini`, y máx. 1.200 tokens) o `Sobreconsumo` (se atiende normal y
  `IAUsoLog.Sobreconsumo = 1`). **Pool por Grupo Empresarial**: con `PoolGrupo`, el consumo es la suma de las
  empresas del grupo que también tengan pool y el presupuesto la suma de los suyos (si alguno es ilimitado, el
  pool lo es). **Día de corte** (`DiaCorte` 1-28): el periodo reinicia ese día; sin él es mes calendario y el
  consumo sale del acumulado `IAUsoMensual` (camino rápido); con corte o pool se suma `AuditoriaAnalisisIA` por
  fechas. `ModeloPermitido` fija el modelo de la empresa. El servicio **no lee recursos de idioma**:
  `CodigoError` se traduce con `IAUsoService.ClaveMensaje(...)` → `R(...)`. El throttle de avisos al 80 %/100 %
  (≤ 1 por empresa y tipo cada 24 h, vía `HttpRuntime.Cache`) vive en el servicio; el aviso a Super Admin lo
  dispara `BaseController.ValidarAccesoIA`.
- **Doble verificación**: `BaseController.ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.X)` es el filtro
  temprano (antes de armar datos pesados); `IAGateway.EjecutarAsync` lo repite con la **estimación de tokens**
  (≈ caracteres/4 + salida típica) y la **reserva** de tokens de consultas en curso de la empresa (evita que
  peticiones simultáneas pasen todas el control). Los controladores pasan `Traducir = R` en `IASolicitud`.
- **`ConfiguracionIAEmpresaService`** — `ObtenerConfig`/`ObtenerConfigs`/`GuardarConfig` sobre
  `dbo.ConfiguracionIAEmpresa` (modelo `ConfigIAEmpresa`; sin fila = valores por defecto; lectura compatible
  con esquemas sin las columnas de `Sql/015`) y el acceso por usuario (`ObtenerUsuariosDeEmpresa`/`GuardarAccesoUsuario`).
- **UI centralizada en Configuración IA** (`ConfiguracionGlobalIAController` +
  `~/Views/Configuracion/ConfiguracionGlobalIA.cshtml`, Super Admin only), pestaña "Por Empresa": consumo del
  periodo (barra, desde cuándo, pool), tarjeta "Control de uso de IA" (habilitada, presupuesto, política al agotarse,
  día de corte, tope por usuario, modelo fijo, pool, funciones) y lista de usuarios con su acceso. Endpoints AJAX:
  `ObtenerResumenEmpresaIA`, `GuardarConfigEmpresaIA` (registra en `AuditoriaService`, `AuditoriaTipo.Configuracion`),
  `GuardarAccesoUsuarioIA` (`AuditoriaTipo.Usuarios`).
- **Visibilidad (Fase 3)**: `Helpers/IAModuloHelper.Visible()` (Super Admin siempre; el resto según
  `IaHabilitada` de su empresa y su `AccesoConsultasIA`, cacheado 60 s; `Invalidar(idEmpresa)` al guardar la
  configuración) oculta la opción "Análisis IA" del sidebar (`_SidebarMenu.cshtml`), las tarjetas de insights de
  PYG/Balance y redirige `AnalisisIAController.Index`; el servidor sigue rechazando consultas igual. **"Mi consumo
  de IA"** (`Views/Shared/_IAConsumoCard.cshtml`, dentro de Análisis IA, solo Super Admin y Admin de Empresa con
  acceso a la empresa): `AnalisisIAController.MiConsumo` (consumo, proyección al cierre del periodo, uso por
  usuario y por función) y `ExportarUso` (Excel de 3 hojas: resumen por función, por usuario y detalle por consulta
  desde `IAUsoLog`, vía `Services/IAUsoReporteService`). Los avisos de presupuesto 80 %/100 % llegan también a los
  Admin de Empresa (`IAUsoService.EnviarAvisoPresupuestoATodosSuperAdmin(..., idEmpresa)` → `/AnalisisIA`). El visor
  de Auditoría IA muestra tokens y valoración 👍/👎. **Contexto de negocio por empresa**
  (`ConfigIAEmpresa.ContextoNegocio`, editable en Configuración IA → Por Empresa, máx. 4.000 caracteres): el gateway
  lo suma al `CONTEXTO_NEGOCIO_BUFINS` global en todos los prompts de esa empresa.
- **Observabilidad (Fase 4)**: pestaña "Uso y costos" en Configuración IA (Super Admin):
  `ConfiguracionGlobalIAController.ObtenerObservabilidadIA(desde, hasta)` → `IAUsoReporteService.Observabilidad`
  sobre `IAUsoLog` (consultas, % atendidas, tokens, costo estimado, % caché, latencia p50/p95 de llamadas reales,
  consumo por día/función/modelo/empresa y errores/rechazos recientes con el código traducido).
  La pestaña tiene el botón **Limpiar estadísticas** (`LimpiarUsoIA`, Super Admin; borra solo `IAUsoLog` — NO toca
  `IAUsoMensual` ni `AuditoriaAnalisisIA`, así que el presupuesto mensual no se reinicia; queda registrado en la
  auditoría con severidad crítica). La pestaña **General** agrupa los parámetros de `ConfiguracionSistema` por tema
  (Proveedor y modelo / Presupuesto y costos / Caché y límites / Otros) en filas compactas con búsqueda, chips de
  estado y edición por delegación (`data-clave`/`data-valor`/`data-desc`); las claves no listadas caen en "Otros".
  **Selector de modelos reutilizable** (`ModeloPicker` en la vista, alimentado por `ObtenerModelosOpenAI`): desplegable con los
  modelos de OpenAI (lista cargada una vez y compartida, botón de recarga, filtro al escribir, teclado ↑↓ Enter Esc, permite
  escribir un modelo que no aparezca). Se usa en el modal de edición para `OpenAIModel` e `IA_ModeloEconomico` (función
  `esClaveModelo`) y en "Modelo fijo de la empresa" de Por Empresa (con la opción "Usar el modelo global").
  Las pestañas **Por Empresa** y **Uso y costos** siguen el mismo lenguaje visual (`cfge-*`/`cfgu-*` en la vista): Por Empresa
  = barra de consumo del periodo + formulario "Control de uso" en secciones (presupuesto y periodo / límites y modelo /
  funciones como píldoras / contexto) con botón Guardar que solo se habilita con cambios, y lista lateral de usuarios con
  interruptor de acceso; Uso y costos = rangos rápidos (7/30/90 días, este mes), KPIs, gráfico de barras por día (días
  sin consumo incluidos) y rankings por función/modelo/empresa con barra relativa.
- **"Pregúntale a este informe" (Fase 4)**: `InformePYGController.PreguntarInforme` e
  `InformeBalanceController.PreguntarInforme` (caja de pregunta dentro de la tarjeta de insights de cada informe).
  Reconstruyen el reporte en el servidor con los mismos filtros (nunca confían en datos del cliente), envían solo el
  reporte ya calculado (más barato y preciso que CSV crudo) y pasan por `IAGateway` con la función
  `InsightsPYG`/`InsightsBalance` (misma habilitación y presupuesto que los insights). Cada pregunta es independiente
  (sin historial); máx. 500 caracteres. El informe del mes bajo demanda ya existe: PDF y Excel en PYG y Balance.
- **Límite de pregunta único**: `IAModuloHelper.MaxCharsPregunta()` (parámetro `IA_MaxCaracteresPregunta`, 50–4000,
  por defecto 500) gobierna Análisis IA y "Pregúntale a este informe" (maxlength de la caja y validación del servidor).
- **Contexto "a qué se dedica la empresa"**: `ConfigIAEmpresa.ContextoNegocio` (Configuración IA → Por Empresa, con
  plantilla guiada). `IAGateway` lo rotula como "Contexto de la empresa consultada…" y lo suma al global en TODAS las
  funciones de IA (incluido Super Admin consultando esa empresa). Ya no existe `IA_LimiteConsultasDefault` (obsoleto);
  `Sql/017` (opcional) lo elimina y crea `IA_ModeloEconomico`.
- **Scripts (ejecutar manualmente, en orden)**: `Sql/012` → `013` → `015` → `016` (contexto por empresa) → desplegar → `014`.
  Los servicios leen con compatibilidad hacia atrás: sin 015/016 todo funciona con valores por defecto.
- **Pendiente**: consentimiento/política de datos por empresa y llave propia (BYOK); edición del contexto por el
  Admin de Empresa con aprobación (hoy solo Super Admin).
