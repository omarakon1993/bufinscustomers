# ClosedXML, configuración, modelos, IA, filtros, logging, auditoría, errores y frontend

> Detalle completo movido desde CLAUDE.md (2026-11-11). CLAUDE.md conserva las reglas obligatorias en resumen; este archivo es la referencia completa. Si cambias el módulo, actualiza este archivo.

### Excel Reading/Writing (ClosedXML)

All Excel import/export uses `ClosedXML.Excel` (`XLWorkbook`, `IXLWorksheet`, `IXLCell`) — never EPPlus/`OfficeOpenXml`, which requires a paid commercial license for for-profit use from v5 onward. Notes when writing new Excel code:
- Save to bytes with `using (var ms = new MemoryStream()) { workbook.SaveAs(ms); return ms.ToArray(); }` — there is no `GetAsByteArray()` equivalent.
- `IXLCell.Value` is an `XLCellValue` struct, not `object`. To read a cell whose type isn't known ahead of time, branch on `.IsNumber`/`.IsBlank` and use `.GetNumber()`; use `.GetFormattedString()` where EPPlus code used to read `.Text`.
- To write a value coming from a loosely-typed source (`DataTable`, `Dictionary<string, object>`, etc.), use `Helpers/ExcelCellHelper.SetValue(cell, value)` instead of assigning `cell.Value = value` directly — direct assignment only compiles when the source expression's compile-time type is a concrete type ClosedXML has an implicit conversion for (string, double, bool, DateTime), not `object`.
- Freeze panes: `ws.SheetView.Freeze(rows, columns)` takes counts, not the EPPlus "top-left cell position" convention (`FreezePanes(2,1)` → `Freeze(1, 0)`).
- **Native (editable) charts**: ClosedXML can't create charts. Use `Helpers/ExcelChartHelper.AgregarGrafico(bytes, hoja, titulo, barras, ...)` on the bytes ClosedXML already saved — it reopens the file with `DocumentFormat.OpenXml` (already a ClosedXML dependency, no new package) and injects a line/column chart linked to the sheet's cells (edit a value → the chart updates), with caches filled so previews draw it too. Wrap the call in try/catch and fall back to the chart-less file. First user: `InformeLineaTiempoController.ExportarExcel`, which also receives `OpcionesExportLineaTiempo` (chart type, unit, hidden series, titles) so the Excel matches what's on screen; units are applied via number format (`#,##0.0,,` = millions) so the full value is kept.

### Configuration System

`ConfiguracionEmpresa` model with sub-configurations: `EmpresasConsolidar`, `Paises`, `Categorias`, `Tipos`, `LineasNegocio`, `Ajuste1`, `Ajuste2`. Managed by `ConfiguracionEmpresaService`.

### ModelosEjecucion Framework

A configurable financial model execution system. Models are records in the `ModelosEjecucion` DB table; each points to a stored procedure that performs the actual computation.

**`ModeloEjecucion`** (`Models/ModeloEjecucion.cs`): `Id`, `Nombre`, `NombreSP` (stored procedure name), `Descripcion`, `Icono`, `Orden`, `Activo` (bool, soft-delete flag). `ModeloPageViewModel` combines `List<Empresas>` and `List<ModeloEjecucion>` for the execution view.

**`ModeloService`** (`Services/ModeloService.cs`):
- `ObtenerModelosActivos()` — calls `sp_ObtenerModelosEjecucion`, returns only active models (for user-facing dropdown)
- `ObtenerModeloPorId(id)` — inline SELECT with `Activo = 1` filter
- `ObtenerTodos()` — includes inactive models (for admin CRUD)
- `Crear()`, `Editar()`, `Eliminar()` — `Eliminar` is a soft-delete (`UPDATE ModelosEjecucion SET Activo = 0`)

**`ModelosEjecucionController`** — CRUD UI for model management. Super Admin only (every action guards with `EsSuperAdmin()`). Uses `[ValidarSesion]` at class level. View: `~/Views/Configuracion/ModelosEjecucion.cshtml`.

**`DatosController.EjecutarModeloAjax`** (la antigua `EjecutarModelo` no AJAX se eliminó por no tener llamadores) — fetches `NombreSP` from DB (never from user input), then calls the SP with `@IdEmpresa`, `@IdUsuario`, `@Año`. The SP may return multiple intermediate result sets; the **final** result set must have `CodMessage` (1 = success) and `ErrorMessage` columns. `CommandTimeout` is 300 seconds.


### IA (AI) — resumen (detalle en `docs-claude/modulos/IA.md`)

> **REGLA DE DATOS (MANDATORY):** la IA **solo puede tomar datos de las tablas `dbo.Modelo*`** (`ModeloBalance`, `ModeloBalanceDiff`, `ModeloBalancePpto`, `ModeloFlujoCaja`, `ModeloFlujoEfectivo`, `ModeloLineasNegocio`, `ModeloPYG`, `ModeloTesoreriaPpto`), nunca de `Ini_*` ni de ninguna otra tabla. Se aplica con `InformeTablasDatosService.ObtenerTablasModelos()` (solo modelos activos); `ConsultarConIA` rechaza otras tablas y `AnalisisIAController` solo lista esas. Cualquier punto de IA nuevo debe cumplirla.

> **IAGateway es el punto único de ejecución (MANDATORY):** un punto de IA nuevo NO llama a `IAService` directo. Hace: (1) `ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.X)` (helper de `BaseController`; devuelve el JSON de error traducido o `null`), (2) arma el `IAConsultaRequest`, (3) `await new IAGateway().EjecutarAsync(new IASolicitud { Funcion = IAFuncion.X, Traducir = R, Idioma = IdiomaIA, ... })`. Agrega una constante a `IAFuncion` para que el consumo quede medido (`AuditoriaAnalisisIA`, `dbo.IAUsoLog`, `dbo.IAUsoMensual`).

- **Proveedor**: OpenAI (`IAService`; modelo `OpenAIModel`, default `gpt-4o`; clave `OpenAIApiKey` en `ConfiguracionSistema`, que manda sobre `appsettings.json`). Cambiar de proveedor solo toca `IAService.cs`. Parámetros leídos con `ConfiguracionSistemaService`.
- **Control por empresa** (`IAUsoService`, UI en Configuraciones generales BUFINS → IA por empresa, Super Admin): interruptor `IaHabilitada`, acceso por usuario, funciones habilitadas, tope diario por usuario, presupuesto de tokens del periodo con política al agotarse (`Bloquear` / `Degradar` / `Sobreconsumo`), día de corte, pool por Grupo Empresarial y modelo fijo. Super Admin exento. No hay planes comerciales por empresa.
- **Prompts**: `GestorPrompts` (códigos `RESUMEN_GERENCIAL`, `CONTEXTO_NEGOCIO_BUFINS` global y contexto por empresa en `ConfigIAEmpresa.ContextoNegocio`, más uno por informe gerencial). No es fine-tuning, a propósito.
- **Scripts SQL (manuales, en orden)**: `Sql/012` → `013` → `015` → `016` → desplegar → `014`; `018` y `017` opcionales. Los servicios leen con compatibilidad hacia atrás.
- Optimización de costo, caché de respuestas, enrutamiento por tarea, observabilidad ("Uso y costos"), "Pregúntale a este informe" y visibilidad (`IAModuloHelper`) están en el detalle.

### Global Filter

`EmpresasViewBagFilter` (`Filters/EmpresasViewBagFilter.cs`) is registered globally in `FilterConfig` and loads all companies into `ViewBag.Empresas` for every request.

`LoggingHandleErrorAttribute` (`Filters/LoggingHandleErrorAttribute.cs`) is the other global filter — a `HandleErrorAttribute` subclass that logs the exception to `AppLogger` before letting `customErrors` render the error page. Registered in `FilterConfig` in place of the stock `HandleErrorAttribute`.

### Logging (`AppLogger`)

`Helpers/AppLogger.cs` — dependency-free app logger. Writes one line per event to `App_Data/logs/app-yyyyMMdd.log` (daily rotation, 30-day retention), never throws (falls back to `Trace`). API: `AppLogger.Info/Warn/Error(mensaje, ...)` and `AppLogger.Error(Exception, contexto)`. Each line carries timestamp, level, request ip/user/url, optional `ctx=`, message, and the full exception chain. Wired into `Global.asax.Application_Error` (safety net) and `LoggingHandleErrorAttribute` (MVC pipeline). Use it in any `catch` where today the code only does `SetErrorMessage(ex.Message)` or swallows silently.

### Auditoría (central, navegación y hub) — resumen (detalle y lista de instrumentación en `docs-claude/modulos/Auditoria.md`)

- **Auditoría central**: tabla única `Auditoria` (columna `Tipo`). Escribir con `new AuditoriaService().RegistrarCambio(AuditoriaTipo.X, AuditoriaAccion.Y, entidad, entidadId, descripcion, valorAnterior, valorNuevo, idEmpresa)` o `RegistrarSeguridad(...)` para login/bloqueos; **nunca lanza**, así que se llama tras el éxito sin try/catch. Severidad, nombre de entidad y `OperacionId` se autocompletan. Para auditar algo nuevo: crear el código en `AuditoriaTipo` si hace falta y añadir la línea. La lista de lo ya instrumentado vive en `Auditoria.md` (o `grep "RegistrarCambio("`).
- **Visor** (`AuditoriaController` → `Auditoria.cshtml`): no auto-carga (solo con «Consultar»), paginación server-side. Alcance: Super Admin todo; Admin de Empresa su empresa/grupo (`EmpresaAccesoHelper`); Usuario Normal solo sus propias filas. «Limpiar» es exclusivo de Super Admin.
- **Navegación**: tabla separada `dbo.AuditoriaNavegacion` por diseño (volumen y retención distintos). `RegistroNavegacionFilter` (global) registra solo GET no-AJAX con `ViewResult`, excluye `Error/*` y `Home/Index`, dedup 15 s y escritura en segundo plano; purga oportunista a 90 días (sin job). `InformeNavegacionController` exige `EsSuperAdmin() || EsAdminEmpresa()`.
- **Hub** (`AuditoriaHubController`, menú `INFORMES_AUDITORIA`): pestañas Cambios / Navegación / IA / **Portal público** (esta última solo Super Admin, con nombre configurable `PortalPublico_Nombre`; analiza las visitas anónimas al sitio público desde los archivos de `uso\\eventos` del portal: `AuditoriaPortalController` + `PortalUsoService`, detalle en `docs-claude/modulos/PulsoDatos.md`) (mismo componente `.cg-tab` que «Historial de cargues»; Cargues salió del hub) cargadas por AJAX con `?embed=1` (layout `_LayoutFragment`, sin iframe). Usuario Normal solo ve Cambios. Cada pestaña aplica su propio alcance.
- **Esqueleto `ax-` (MANDATORY para cualquier auditoría nueva)**: panel de filtros `Views/Shared/_AudFiltros.cshtml` + `AudFiltrosVM`/`AudCampo`, `Assets/js/auditoria-shared.js` (`AudKit`) y `auditoria-shared.css`. Ninguna pestaña carga datos hasta pulsar «Consultar»; el detalle se abre en modal SweetAlert.
- `AuditoriaCargues` y `AuditoriaAnalisisIA` siguen como tablas separadas (ver "Deuda técnica y pendientes").

### Error Pages

`ErrorController` (`Controllers/ErrorController.cs`, no `[ValidarSesion]`) renders branded, session-independent pages: `Index` (500), `NotFound` (404), `Forbidden` (403) — each sets `Response.StatusCode`. Views in `Views/Error/` use `Views/Error/_ErrorLayout.cshtml` (`Layout = null`, self-contained dark-purple + brand-gradient card, strings from `Err_*` resx keys). `Views/Shared/Error.cshtml` (used by `LoggingHandleErrorAttribute`) shares the same layout. Routing in: `Program.cs` (`UseExceptionHandler("/Error")` + `UseStatusCodePagesWithReExecute("/Error/Status", "?code={0}")`; `ErrorController.Status(int code)` keeps the real status code).

### Frontend Stack

- Bootstrap 4.6.0 + SB Admin 2 (4.1.3) theme with custom modern sidebar (real versions verified 2026-11-08; Bootstrap 4 is end-of-life — see the technical documentation) (`Assets/css/modern-sidebar.css`, `Assets/js/modern-sidebar.js`)
- jQuery 3.6.0, Chart.js 4.4.1, jsPDF 2.5.1, html2canvas 1.4.1, SweetAlert2 11.19.1, Select2 4.1.0
- SweetAlert2 for session notifications and confirmations
- FontAwesome icons
- Select2 (`Assets/js/select2/`, `Assets/css/select2/`) for enhanced dropdowns
- Font Awesome 7 Free: only `Assets/Bootstrap/fontawesome-free/css/all.min.css` + `webfonts/` + `LICENSE.txt` are kept (the SVGs/sprites/js/scss were removed to keep deploys small - don't add them back)
- ClosedXML 0.105.0 for Excel operations (MIT license, no license call needed — chosen over EPPlus 5+/Polyform Noncommercial specifically to keep the project free of any commercial-license obligation)

### Layout Structure

`Views/Shared/_Layout.cshtml` contains the full sidebar navigation with permission-based visibility using `@if (UsuarioSesionHelper.TienePermiso("CODE"))` checks, user profile modal, image upload modal, and client-side session timeout management.
