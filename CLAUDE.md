# CLAUDE.md

Guía para Claude Code en este repositorio. **Este archivo guarda reglas y resúmenes; el detalle completo de cada módulo vive en `.claude/notas/`** (texto íntegro, no se perdió nada al condensar). **Antes de modificar un módulo, lee su nota** (tabla abajo) y, si cambias el módulo, actualiza la nota en la misma tarea. Las notas NO se importan con `@` a propósito (se cargarían siempre); se leen bajo demanda.

| Antes de tocar… | Lee |
|---|---|
| `Program.cs`, `Compat/`, `Infra/`, CSP, configuración, despliegue | `.claude/notas/plataforma-net10.md` |
| Capas, BD, sesión, login, cambio de clave, 2FA/passkeys, solicitudes de reinicio, Configuraciones generales, Supuestos balance ppto, Pulso, roles, grupos empresariales, permisos y menús (incl. personalizados por empresa) | `.claude/notas/arquitectura-acceso.md` |
| Rutas de vistas por controlador, lista de controladores clave, Home | `.claude/notas/controladores-vistas.md` |
| Cualquier informe; documentación de informes y biblioteca; logo de empresa; tema de colores; comentarios de gestión; atajos de rango | `.claude/notas/informes.md` |
| Cargue de Excel, staging/validación, gestor de hojas, historial/rollback/respaldos, protección de cargues, escenarios | `.claude/notas/cargues-escenarios.md` |
| Excel (ClosedXML/gráficos), configuración financiera, ModelosEjecucion, IA, filtros globales, AppLogger, auditoría, páginas de error, frontend | `.claude/notas/excel-modelos-ia-auditoria.md` |
| Texto íntegro de convenciones, deuda técnica e i18n | `.claude/notas/convenciones.md` |
| Notificaciones internas | `.claude/notas/notificaciones.md` |
| Estilos, modales, botón Exportar, loader, formularios | `.claude/notas/ui-estilos.md` |

## Proyecto

Bufins Customers: ASP.NET Core MVC en **.NET 10** (migrado desde MVC 5/.NET Framework 4.8 el 2026-11-08; producción en customers.bufins.com). Gestión y reportes financieros multiempresa (empresas, usuarios, configuración financiera, cargue/exportación de Excel). Cultura `es-CO`.

**Documentación**: única carpeta oficial `App_Data/Documentacion/` (subcarpetas `Seguridad/`, `Informes/`, `Cargues/`, `Tecnica/`; la lee «Documentación BUFINS» y se publica). Ya NO existen `docs/` ni `docs-claude/`: las notas antiguas se consultan en la etiqueta git `docs-claude-archivo` (`git show docs-claude-archivo:docs-claude/informes/PYG.md`; listado: `git ls-tree -r docs-claude-archivo --name-only docs-claude`). Cuando algo diga «ver `docs-claude/…`», léelo así; si cambias ese módulo, deja el detalle nuevo en la nota de `.claude/notas/` correspondiente o en la documentación oficial. `.claude/notas/` es guía interna para Claude (no documentación de producto): no crear otras carpetas de documentación.

## Comandos

```bash
dotnet build bufinscustomers.csproj
dotnet run   # https://localhost:44339 / http://localhost:5039
# Production local (desde la carpeta del proyecto, para leer secretsSettings.json):
# ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:5090 dotnet bin/Debug/net10.0/bufinscustomers.dll
# Paquete (autocontenido: el hosting no tiene .NET 10)
dotnet publish bufinscustomers.csproj -c Release -r win-x64 --self-contained true -o C:\Users\omara_\bufins-despliegue\customers\paquete-net10
```

## Plataforma .NET 10 (OBLIGATORIO; detalle en `plataforma-net10.md`)

- Arranque en `Program.cs`; `web.config` solo aloja en IIS. Capa `Compat/` + `Infra/` emula System.Web: código nuevo usa APIs de ASP.NET Core; no ampliar la capa salvo necesidad.
- Configuración: `appsettings.json` + `secretsSettings.json` (gitignored; cadena `ConnectionStrings:DefaultConnection` y `MachineKey:*`). **Nunca imprimir ni versionar la clave de SQL ni las MachineKey. No cambiar las MachineKey sin recifrar** `SecretosProtegidos` (`enc:v1:`) y secretos TOTP.
- GC de estación no concurrente (no cambiar sin medir memoria). Data Protection y sesión en memoria en Conexcol: un reinicio cierra sesiones e invalida formularios abiertos.
- **CSP estricta de scripts (OBLIGATORIO en vistas y JS)**: `script-src 'self' 'nonce-…'` sin `'unsafe-inline'`.
  1. Prohibidos `onclick`/`onchange`/`oninput`/`onsubmit`… en HTML y en HTML generado por JS. En Razor, `Infra/CspTagHelpers.cs` traduce solo llamadas simples (`fn(3,'x')`, `this`, `event`, `this.value`, `this.getAttribute('x')`, `return false`, `return fn()`) a `data-h-click` (`Assets/js/inline-handlers.js`); lo demás queda roto y se registra «Evento en línea no traducible». En HTML generado: `data-*` + listener delegado, o `data-h-click='{"s":[{"f":"fn","a":[1]}]}'`; navegación `data-h-href`; filtros `data-filtro="letras|numeros|alfanumerico"`; hover por CSS.
  2. `<script>` (en línea y `src`) recibe el nonce solo vía `NonceScriptTagHelper`; un script creado por JS o `Content(…)` sin él no corre. Fragmentos HTML pedidos por AJAX e inyectados con jQuery (`?embed=1`, `CgTabs`): `inline-handlers.js` envía el nonce de la página en `X-Csp-Nonce` y `CabecerasSeguridad` lo reutiliza (solo fetch/XHR del mismo sitio); cargarlos siempre con jQuery (no `fetch` + `innerHTML`).
  3. Las funciones llamadas desde `data-h-*` deben ser globales. 4. `style="…"` sigue permitido. 5. Respuestas con CSP propia se respetan.
  - Interruptor de emergencia: `ConfiguracionSistema.CspScriptEstricto = false` (≤ 60 s). Revisar `CSP-REPORT` en el log tras cada publicación.
- **Publicación** (Conexcol/Plesk, Web Deploy 8172): perfil VS `customers.bufins.com - OAG` o `Tools/Despliegue/Desplegar-Customers.ps1` (`Respaldar`, `SubirSecretos`, `Publicar` [sin `-Aplicar` simula], `Restaurar`, `BajarLogs`, `Reiniciar`). **Lo ejecuta el usuario en su terminal; la contraseña nunca pasa por el chat.** Orden: paquete → Respaldar → SubirSecretos -Aplicar (si cambian) → Publicar simulado → Publicar -Aplicar → humo con sesión real.
- **Local usa la BD de PRODUCCIÓN**: todo POST es real. Pruebas automáticas solo GET; nada de cargues, modelos, correos, IA ni reinicios de 2FA sin acuerdo explícito.

## Arquitectura (detalle en `arquitectura-acceso.md`)

- Capas: `Controllers/` (heredan `BaseController`), `Services/` (heredan `BaseService`), `Models/`, `Views/` por área funcional, `Helpers/`, `Filters/`, `Permisos/`. **Controladores nuevos heredan `BaseController`; servicios nuevos `BaseService`.**
- `BaseController`: `CadenaConexion`, `R("Clave")`, `SetErrorMessage/SetSuccessMessage/SetInfoMessage` (TempData `ErrorMessage/SuccessMessage/InfoMessage`), `MensajeError(ex)`, `ValidarAccesoIA(...)`. `ConvertirSha256` solo para migración de login.
- **BD**: ADO.NET + SP en SQL Server (`bufinscustomers`): `using SqlConnection` → `SqlCommand("sp_X", cn)` + `CommandType.StoredProcedure` + `AddWithValue`. Nombres `sp_` + verbo (`Obtener`, `Registrar`, `Editar`, `Eliminar`, `Guardar`, `Agregar`, `Actualizar`, `Validar`) + entidad; muchos usan `OUTPUT` para mensajes.
- **Columnas nuevas incrementales**: leer con `try { … reader["Col"] … } catch (IndexOutOfRangeException) { }`; escrituras: intentar con la columna y caer al INSERT sin ella. Los servicios toleran tablas/scripts ausentes.
- **Scripts `Sql/*.sql` son manuales**: no asumir que corrieron en cada entorno.

### Sesión, login y seguridad

- `UsuarioSesionHelper`: sesión `"UsuarioCompleto"` (20 min), `UsuarioActual`, `EstablecerUsuarioEnSesion`, `LimpiarSesion`, `ExtenderSesion`, `TienePermiso`, `ObtenerMenuSidebar`, `InvalidarCachePermisos`. `[ValidarSesion]` a nivel de clase en todo controlador autenticado nuevo (algunos antiguos usan chequeos manuales).
- Cambio de clave obligatorio (`Usuarios.DebeCambiarClave`, filtro `CambioClaveObligatorioFilter`), 2FA TOTP para todos (`TwoFactorHabilitado`, apagado por defecto; sin códigos de recuperación a propósito; reinicio solo Super Admin), passkeys WebAuthn propias (sin NuGet; el RP ID depende del dominio: si cambia, las passkeys dejan de servir), solicitudes de reinicio (`/SolicitudesSeguridad`). Ver nota antes de tocar `AccesoController`, `SeguridadController` o el login.
- **Contraseñas**: `.Trim()` antes de hashear; `HashearContrasena()` (BCrypt 12) para nuevas; `VerificarContrasena()` acepta ambos formatos; política única en `Helpers/PoliticaContrasena.Validar(...)` (devuelve clave resx; el POST de login nunca la llama).
- **IP del cliente**: siempre `ClientIpHelper.ObtenerIp()`, nunca `Request.UserHostAddress` (proxies solo vía `TrustedProxies`).
- Login: siempre un BCrypt (contra `HASH_SENUELO` si el usuario no existe); rate-limit por IP en `RateLimitLoginService` (`dbo.IntentosLoginIP`, modo defensivo global); throttling de auditoría de intentos anónimos. No reintroducir caché propia en `AccesoController`.
- **Errores al navegador**: `MensajeError(ex)`, nunca `ex.Message`. Nunca datos de usuario en `Html.Raw`; cadenas a JS con `HttpUtility.JavaScriptStringEncode`. Imágenes validadas por firma de bytes.

### Roles y aislamiento por empresa

`Usuarios.Admin`: 0 = Usuario Normal, 1 = Admin de Empresa, 2 = Super Admin (pasa todo, ve todas las empresas; asociado por defecto a la empresa principal Bufins vía `EmpresaService.ObtenerIdEmpresaPrincipal()`). Admin 0/1 solo ven/modifican su empresa (o su grupo) y no crean/eliminan empresas. `EsUsuarioNormal()`, `EsAdminEmpresa()`, `EsSuperAdmin()`.

**Grupos empresariales**: un usuario cuya empresa pertenece a un grupo tiene acceso total (lectura Y escritura) a las demás empresas del grupo. `GrupoEmpresarialService.AsignarEmpresas` es la ÚNICA vía que escribe `IdGrupoEmpresarial`. Caché de empresas app-wide `EmpresaCacheHelper` (5 min, no de sesión).

> **OBLIGATORIO:** toda acción nueva acotada por empresa (leer o escribir) usa `EmpresaAccesoHelper.TieneAcceso()` / `ObtenerIdsEmpresasPermitidas()` (null = sin filtro), nunca `usuario.IdEmpresa == idEmpresa`. Excepción: acciones reservadas a Super Admin (crear/eliminar empresas, gestionar grupos), que conservan `EsSuperAdmin()`.

### Permisos de menú

- `MenuOpciones` (+ `SoloSuperAdmin`, `SoloAdminEmpresa`, `NombreGrupo`, iconos, orden) y `UsuarioMenuPermisos`; servicio `MenuOpcionesService` (`Services/PermisosService.cs`). Opciones nuevas en la tabla aparecen solas en el sidebar y el gestor de permisos.
- **Preferir `[RequierePermiso("COD")]` / `[RequierePermiso("A,B")]`** sobre chequeos inline; `[SoloSuperAdmin]` a nivel de clase en controladores 100 % Super Admin. Los controladores con gates mixtos (`EmpresaController`, `PermisosController`, `DatosController`, `ConfiguracionEmpresaController`, Informe*) mantienen chequeos inline por diseño. Conservar los `ViewBag.Puede*` (solo visibilidad).
- **Menús personalizados por empresa** (`MenuOpcionesEmpresas`): sin filas = global; con filas = solo usuarios cuya `Usuarios.IdEmpresa` (no el grupo) esté en la lista; Super Admin siempre. El filtro vive en `MenuOpcionesService` (`ObtenerIdsOpcionesNoAplicables`) y el controlador del informe revalida empresa en cada endpoint. Llevan la cápsula `@Html.Partial("_InformePersonalizadoTag")`.
- Sidebar: un grupo con nombre siempre es desplegable, aunque tenga una sola opción.
- Códigos conocidos y lista completa: ver nota.

### Vistas

Vistas por **área funcional**, no por controlador: los controladores usan rutas explícitas `View("~/Views/{Area}/{Vista}.cshtml")` (única excepción: `ErrorController`, por convención en `Views/Error/`). Mapa completo controlador → vista en `controladores-vistas.md`.

## Informes (detalle en `informes.md`; además `git show docs-claude-archivo:docs-claude/informes/<X>.md`)

Leen las tablas materializadas `dbo.Modelo*` (nunca ejecutan los SP del modelo en vivo). Reglas que no se pueden olvidar:
- PYG: `Mes` es abreviatura española y se ordena en C#, nunca en SQL. Etiquetas IA: Logro / Alerta / Eficiencia.
- Balance: es una foto (nunca sumar meses); Activo = Pasivo + Patrimonio; «Comparar contra» `ppto`/`proy` cruza por `Descripcion` (el `Ord` de `ModeloBalancePpto` no es estable). Etiquetas Liquidez / Solvencia / Capital de trabajo.
- Tesorería: las 3 líneas de saldo se derivan en el servicio; el puente de caja siempre cuadra. Etiquetas Posición de caja / Financiación / Acción recomendada.
- Flujos: 19 + 25 + 29 (Caja) = línea 30 (Efectivo); «proyectado» = real hasta el último mes ejecutado y `ValorFuturo` después. Etiquetas Generación de caja / Uso del efectivo / Acción recomendada.
- Indicadores: LTM 12 meses, nunca se anualiza; agregar uno = línea en `IndicadoresCatalogo.Todos` + 6 claves `Ind_{Codigo}_*` en ambos `.resx` + regenerar su documentación. Umbrales propios en `dbo.IndicadoresUmbrales` (solo editables los que cumplen `IndicadoresUmbralesService.EsEditable`). No hay comparación contra presupuesto ni benchmark externo (a propósito). Etiquetas Fortaleza / Riesgo / Prioridad.
- Histórico de precios: `Valor` 0/vacío = sin precio; variación contra los mismos semestres del año previo; no usa IA.
- Líneas de negocio: la línea es el SUFIJO de `Descripcion`; costos y gastos positivos; debe cuadrar con `ModeloPYG` y lo muestra.
- Gerencia mensual: no calcula nada propio; cada sección es independiente.
- Las etiquetas de IA de cada informe son fijas (las usa `separarInsights()`); un prompt sobrescrito en `GestorPrompts` debe conservarlas.

**Todo informe nuevo o modificado con resultados por empresa DEBE llevar** (recetas exactas en `informes.md` y `ui-estilos.md`):
1. **Cápsula «Información modelo Bufins»** si lee `Modelo*`: `@Html.Partial("_ModeloBufinsTag")` tras el `<h1 class="powerbi-header-title">` (no en Ejecución de Modelos, que las genera). Agregarlo a la lista de la nota.
2. **Botón Exportar estándar** `_ExportMenu` (Excel · PDF · imagen · correo) + `_ExportCorreoModal` + `export-menu.js/.css`. Nunca botones sueltos ni menú propio; salidas que no aplican se ocultan con `Opciones`. El correo pasa por `InformeCorreoHelper` + `EmailService.EnviarInforme`, reconstruye el informe en el servidor (nunca confía en cifras del navegador) y se audita (`AuditoriaTipo.Informes` + `AuditoriaAccion.Enviar`). El constructor del PDF devuelve el documento (con `__nombre`).
3. **Logo de la empresa** junto al nombre (placa blanca `.emp-logo-plate` + `EmpresaLogo.mostrar`) y en PDF (`EmpresaLogo.pdf`), imagen, Excel (`ExcelLogoHelper`) y correo (`logoEmpresa`), sin quitar nunca el logo de Bufins y sin cambios si la empresa no tiene logo. Validación del archivo solo en `EmpresaLogoHelper.Procesar` (contenido real, PNG/JPG).
4. **Tema de colores** «Estilos del informe» (`_TemaInformeSelector` bajo rango/agrupación, `informe-tema.js`, `tema-informe.css` con `AssetVersion.Url`): Bufins por defecto e idéntico al de siempre, no se recuerda la elección, se aplica al Generar; CSS de marca siempre como `var(--tm-…, <literal Bufins>)`; nunca tematizar marca Bufins, logo ni colores semánticos; Excel y correo re-resuelven el tema en el servidor (`ExcelTemaHelper`/`TemaInformeService.Resolver`, inválido → Bufins); el navegador no reimplementa el algoritmo de `TemaInforme`.
5. **Comentarios de gestión** si tiene cierre mensual: clave en `ComentariosGestionService.InformesValidos`, `Com_Inf_<Clave>` en ambos `.resx`, `_ComentariosGestion` + `ComentariosGestion.montar/cargar`.
6. **Documentación** (preguntar siempre; ver abajo), textos en ambos `.resx` y su nota `informes.md` actualizada.

Atajos del rango (Línea de tiempo + PYG): una sola implementación `Assets/js/rango-atajos.js`, relativos a HOY del servidor; por defecto «Año en curso» (o «Todo»); no disponibles con `.no-disponible` + `aria-disabled` (no `disabled`).

## Documentación de informes — ESTÁNDAR (detalle en `informes.md`)

- HTML de solo lectura en `App_Data/Documentacion/<Categoría>/` (informes: `Informes/Informe_<Nombre>.html`), **única copia, nunca en `Assets`**, registrado como `<Content>` en el `.csproj`. Variante inglesa opcional `.en.html`.
- Agregar = guardar el HTML + una línea en `Helpers/DocumentacionCatalogo.cs`. Lo sirve `DocumentacionController.Ver(clave)` vía `DocumentacionProtegidaHelper` (permiso, marca de agua, sin caché, auditoría). Visor: `doc-visor.css/js` + `_DocumentacionVisor` + `DocVisor.init`; cápsula `header-tag-doc` (o `_DocumentacionCapsula`); textos `{Prefijo}_Doc_ModalTitulo` / `{Prefijo}_DocumentacionTooltip`. **Nada de acciones, modales ni CSS propios por informe.**
- **Estilo visual único** de toda documentación HTML = el de `Informe_Indicadores.html` (tokens `:root` y modo oscuro, header con degradado, nav sticky, secciones numeradas, componentes `.card/.g2/.g3/.tw/.note/.ficha/details/ol.steps/.tag/.flow`), autocontenido (capturas en base64), sin emojis.
- Contenido mínimo: para qué/para quién, capturas, origen de cifras, cálculos y datos faltantes, filtros, IA, exportaciones, preguntas frecuentes, límites.
- Generadores: `Tools/Documentacion/gen_doc_informes.py` (PYG, Balance, Tesorería, Flujos, Línea de tiempo, Informe de modelos; ES/EN); para Indicadores, Histórico de precios, Líneas de negocio y Gerencia mensual se edita el español a mano y el inglés se regenera (`agregar_map.py <X>` → traducir → `gen_doc_en.py`); documentación técnica: `gen_doc_tecnica.py` (actualizar `doc_tecnica.py` y `FECHA` al cambiar versiones, módulos o controles de seguridad).
- **Biblioteca «Documentación BUFINS»** (`DocumentacionBufinsController`, `Helpers/DocumentacionBibliotecaCatalogo`): solo lo oficial y **solo lo que el usuario pida expresamente**. Agregar = HTML en su subcarpeta + `<Content>` + una línea del catálogo (categoría nueva: `Categorias` + `DocBuf_Cat_<id>` en ambos `.resx`). Visibilidad «Solo Super Admin» en `dbo.DocumentacionAcceso`.
- **Mantenimiento (OBLIGATORIO)**: tras cualquier cambio en una opción con documentación (ver `DocumentacionCatalogo`), revisar secciones y capturas, regenerar, guardar en su subcarpeta, verificar, y **decir en la respuesta final** «Documentación actualizada: sí/no y qué» o «Documentación revisada: sin cambios». Lo mismo para `Cargues/Auditoria_Cargues_Blindaje.html` si cambia la protección de cargues, y `Seguridad/Ciberseguridad_BUFINS.html` tras cada tanda de seguridad.

## Cargues (detalle en `cargues-escenarios.md` — LEERLA antes de tocar `DatosController`, `CargueStagingService`, `CargueHojasService` o `HistorialVersionesCarguesService`)

- Flujo en dos pasos: `CargarExcel` solo escribe en `Staging_Ini_*` y valida (`sp_ValidarCargueStaging`, único SP de reglas; regla nueva = bloque nuevo en el SP); nada real cambia hasta `ConfirmarCargue` (una transacción: snapshot → borrados → `sp_ConfirmarCargueStaging` → auditoría). `IdLote` aísla todo; acceso por `EmpresaAccesoHelper`; un lote activo por llave `(IdEmpresa, Anio, Modo, IdEscenario)`; ningún lote debe quedar pegado (abandono, recarga, fin de sesión, purga).
- Hojas Z_ → Ini_ en el catálogo `dbo.CargueHojas` (gestor Super Admin con DDL; reglas de nombres/tipos, `Año` obligatoria, cambios destructivos solo con tablas vacías y sin módulos SQL que las mencionen). API de `TablasCargueHelper` conservada.
- Historial: snapshot GZip UTF-8 en `DatosGzip` (**nunca llenarlo desde SQL con `COMPRESS`**), atómico, 2 versiones por llave; rollback aborta si falta un snapshot; respaldo automático antes de restaurar.
- **Regla de oro: nunca dejar un año vacío** (misma regla `ImpactoCargue` en cargue, deshacer y recuperar; bloqueo para todos bajo el umbral).
- Escenarios: copia paralela de datos por empresa (`IdEscenario`, default 1); selectores independientes por pantalla; lecturas `ISNULL(IdEscenario,1) = @IdEscenario`; el cierre de año NO filtra por escenario a propósito; los 8 SP de modelo reciben y propagan `@IdEscenario` (viven solo en la BD; receta en `Sql/README.md`).
- Excepción de tema: Cargue de Excel, Revisar cargue, Historial de versiones y Auditoría de cargues usan modales CLAROS (`cargues-claro.css` + `body.pg-claro`; clases `cg-conf-*`, `cargues-modulo.css`); el gestor de hojas también (`hc-swal`, `hc-overlay-claro`).

## Otros módulos (detalle en `excel-modelos-ia-auditoria.md`)

- **Excel: solo ClosedXML** (nunca EPPlus). `SaveAs(ms)` → bytes; `XLCellValue` (`IsNumber`/`GetNumber`/`GetFormattedString`); valores `object` con `ExcelCellHelper.SetValue`; `Freeze(filas, columnas)`; gráficos nativos con `ExcelChartHelper.AgregarGrafico` en try/catch.
- **Modelos**: `ModelosEjecucion` → SP cuyo nombre se lee de BD (nunca del usuario); último result set con `CodMessage`/`ErrorMessage`; timeout 300 s; eliminar = soft-delete.
- **IA — REGLA DE DATOS (OBLIGATORIO)**: solo datos de `dbo.Modelo*` (`InformeTablasDatosService.ObtenerTablasModelos()`), nunca `Ini_*` ni otras tablas. **Todo punto de IA pasa por `IAGateway`**: `ValidarAccesoIA(...)` → `IAConsultaRequest` → `new IAGateway().EjecutarAsync(new IASolicitud { Funcion = IAFuncion.X, Traducir = R, Idioma = IdiomaIA, ... })`, con constante nueva en `IAFuncion`. Nunca llamar `IAService` directo.
- **Logging**: `AppLogger.Info/Warn/Error` en todo `catch` que hoy solo muestra o traga el error.
- **Auditoría central**: `new AuditoriaService().RegistrarCambio(AuditoriaTipo.X, AuditoriaAccion.Y, entidad, entidadId, descripcion, valorAnterior, valorNuevo, idEmpresa)` o `RegistrarSeguridad(...)` tras el éxito (nunca lanza: sin try/catch). Auditorías nuevas con el esqueleto `ax-` (`_AudFiltros`, `AudKit`, `auditoria-shared.css`), sin cargar datos hasta «Consultar».
- Errores: `ErrorController` (500/404/403) con layout propio; `Program.cs` re-ejecuta `/Error/Status`.
- Frontend: Bootstrap 4.6 + SB Admin 2, jQuery 3.6, Chart.js 4.4.1, jsPDF 2.5.1, html2canvas 1.4.1, SweetAlert2 11.19.1, Select2 4.1, Font Awesome (solo `all.min.css` + webfonts: no volver a agregar SVG/js/scss). Sin DataTables.

## Convenciones importantes (texto íntegro en `convenciones.md`)

- **SQL compatible con SQL Server 2012 (OBLIGATORIO)** en C#, SP, scripts y comentarios. Prohibido: `STRING_AGG` (usar `FOR XML PATH`+`STUFF`), `STRING_SPLIT`, `CONCAT_WS`, `TRIM` (usar `LTRIM(RTRIM())`), `TRANSLATE`, `CREATE OR ALTER`, `DROP … IF EXISTS`, funciones JSON (`OPENJSON`, `JSON_VALUE`, `FOR JSON`, `ISJSON`), `AT TIME ZONE`, `DATEDIFF_BIG`, `COMPRESS`/`DECOMPRESS`, `SESSION_CONTEXT`, `GREATEST`/`LEAST`, `GENERATE_SERIES`, `DATETRUNC`, tablas temporales de sistema e in-memory. Permitido: `OFFSET/FETCH`, `THROW`, `TRY_CONVERT/CAST/PARSE`, `IIF`, `CHOOSE`, `CONCAT`, `FORMAT`, `EOMONTH`, `DATEFROMPARTS`, `LAG/LEAD/FIRST_VALUE`, `SEQUENCE`. «Corre en el servidor (2016)» no es prueba.
- Plantillas Excel en `Assets/Plantillas/`. Límite de subida 100 MB (`Program.cs` + `web.config`).
- Enlaces a CSS/JS propios con `AssetVersion.Url(...)`.
- **Preguntar SIEMPRE antes de cerrar una tarea**:
  - **Notificaciones internas** para toda funcionalidad nueva con resultado observable (si sí: `NotificacionesService.Crear(...)`, claves en ambos `.resx`, actualizar la lista).
  - **Auditoría** para toda acción que cambie estado persistente (si sí: línea `RegistrarCambio`/`RegistrarSeguridad`, código en `AuditoriaTipo` si hace falta, actualizar «Ya instrumentado»).
  - **Documentación** para toda pantalla nueva (informe, análisis, gestor, módulo) con el estándar; si se modifica un informe documentado, si se regenera.
  - **¿Agregarla a la biblioteca «Documentación BUFINS»?** cada vez que se cree documentación nueva (por defecto NO).

### i18n (OBLIGATORIO)

Todo texto visible va en AMBOS `App_GlobalResources/Strings.resx` (es-CO) y `Strings.en-US.resx` (en-US), sincronizados, antes de implementar la UI. Vistas: `@Resources.Strings.Clave`; JS: inyectar desde Razor (`var x = '@Resources.Strings.Clave';`), nunca texto fijo; controladores: `R("Clave")`; servicios/helpers no leen recursos (reciben textos traducidos o usan códigos). Claves `{Area}_{Elemento}`, agrupadas con comentario XML en ambos archivos.

### Notificaciones (detalle en `notificaciones.md`)

Tabla `Notificaciones` (con `Url` de enlace). Servidor: `new NotificacionesService().Crear(usuario.Id, R("Notif_X"), mensaje, tipo, url)` (nunca lanza) tras operaciones largas o con resultado relevante (cargues, modelos, exportaciones largas, lotes > 2 s, creación/eliminación de entidades importantes); **no** para ediciones simples, lecturas o donde el mensaje flash basta. Cliente (sin BD): `notifPushLocal(tipo, _notifStr.x, _notifStr.y)`. Tipos `success`/`info`/`warning`/`error`. Títulos bilingües. Lista vigente: `grep -rn "NotificacionesService().Crear(" Controllers`.

## Estilos UI (OBLIGATORIO; referencia completa en `ui-estilos.md`)

- Toda vista nueva o modificada sigue el sistema del sitio: `bufins-components.css` + grid de Bootstrap. Antes de estilizar, mirar cómo se hace ese mismo elemento en otra pantalla e igualarlo. Sin colores, degradados ni reglas CSS nuevas si ya existe una clase; sin CSS por página salvo necesidad real; sin `style` para layout/color; sin DataTables; tablas siempre `.table-wrapper` + `.data-table`.
- Esqueleto: `.powerbi-header` (icono + título) → alertas → mensajes TempData → `.filter-card` (`.filter-card-title`, `.filter-section`, `.btn-group-actions`) → `.table-container` (`.table-header` + `.info-badge`, `.table-wrapper`, `.data-table`) → `.no-data-message`; `.loading-spinner`, `.pagination-controls`.
- Botones: principal `.btn-modern-gradient`; secundarios/cancelar/descargas `.btn-modern-secondary` (degradado oscuro de chrome); `.btn-modern-success` solo crear/activar/confirmar; `.btn-modern-excel` solo exportaciones reales de datos. En tablas: `btn-sm` `btn-warning` (restaurar), `btn-danger` (eliminar), `btn-primary` (ver/editar), `btn-success` (activar). Degradados: `--content-gradient` para contenido, `--app-glow-bg` para chrome y botones neutros.
- Iconos Font Awesome `fas fa-*` según significado. Formularios: `.form-group` + `<label>` + `.form-control`; `select2` donde aplique; obligatorio `<span class="required">*</span>`.
- **Confirmaciones/alertas** (eliminar, destructivas, confirmar guardado): clase `modal-confirm` en el `.modal` exterior (tema oscuro); nunca en modales de crear/editar. `Swal.fire` ya es oscuro. **Overlays de carga**: `.overlay-cargando` (+ `-card/-icon/-title/-text/-timer`) con `@Html.Partial("_LoaderDots", "aqua"|"dark")`. Modales de elección: botones `.opt primary/secondary` en el cuerpo. (Excepciones claras: módulos de cargues y gestor de hojas, ver arriba.)
- **Modales de formulario** (crear/editar): estilo «Limpio con acento» de `bufins-modal.css`: `modal-dialog-centered` (+ `bf-modal-md`/`bf-modal-wide`), título con `<i>`, secciones `.bf-section`, campos `<label>` sin icono + `.bf-input` con icono (nunca `form-control-sm`; select2/textarea sin icono), pie con `.bf-footer-note` (`Common_CamposObligatorios`) + Cancelar `btn-secondary` + acción `btn-primary`. Modelo: `Views/Configuracion/Empresas.cshtml`.

## Deuda técnica y pendientes

- Tablas sin uso: `dbo.DashboardTarjetas`, `dbo.EmpresaTablasResumenIA` (`Sql/Limpieza_CodigoMuerto_2026-10-03.sql`). `dbo.EmpresaPersonalizaciones` aún sirve de respaldo de `CargueHojasService`.
- Opciones por crear en `/MenuOpciones`: `INFORMES_PYG_GERENCIAL`, `INFORMES_BALANCE_GERENCIAL` y las que marque cada informe.
- `AuditoriaCargues` y `AuditoriaAnalisisIA` sin migrar a `Auditoria`.
- Exportaciones por migrar a `_ExportMenu`: Revisar cargue (copia inline `.rc-exp*`), Cargue de Excel, Auditoría, Relacionamientos, PUC, Variables PBI, Navegación, Análisis IA.
- Scripts pendientes de la tanda de informes (2026-10): `040_ComentariosGestion.sql`, `041_Menu_LineasNegocio_GerenciaMensual.sql`, `042_IndicadoresUmbrales.sql`. Pendiente antes de publicar 2FA: scripts 026/029, `TwoFactorHabilitado=true`, `WebAuthnRpId`, instructivo.
