# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Bufins Customers is an ASP.NET MVC 5 web application built on .NET Framework 4.8 for financial data management and reporting. The application manages multiple companies (empresas), users, and their financial configurations with Excel-based data import/export capabilities. Configured for Colombian Spanish (`es-CO`).

## Build and Development Commands

```bash
# Build
msbuild bufinscustomers.sln /p:Configuration=Debug

# Restore NuGet packages
nuget restore bufinscustomers.sln

# Run: IIS Express on port 44339 (HTTPS), launched via Visual Studio
```

## Architecture

### Layered MVC with Service Layer

1. **Controllers** (`Controllers/`) - Inherit from `BaseController`
2. **Services** (`Services/`) - Inherit from `BaseService`
3. **Models** (`Models/`) - Data models and ViewModels
4. **Views** (`Views/`) - Razor views organized by functional area (Configuracion, Datos, Informes, etc.)
5. **Helpers** (`Helpers/`) - `UsuarioSesionHelper` (session/auth management)
6. **Filters** (`Filters/`) - `EmpresasViewBagFilter` (registered globally in `FilterConfig`)
7. **Permisos** (`Permisos/`) - `ValidarSesionAttribute`, `RequierePermisoAttribute`

### User Profile Images

`ImagenUsuario` (`Models/ImagenUsuario.cs`) stores user profile images in the `UsuarioImagenes` DB table:
- Fields: `Id`, `UsuarioId`, `ImagenBase64` (Base64-encoded image data), `TipoImagen` (MIME type, e.g. `"image/png"`)
- The `Usuarios` model has an `ImagenUsuario Imagen` navigation property
- Loaded eagerly alongside the user via `LEFT JOIN UsuarioImagenes` in `UsuarioSesionHelper.ObtenerUsuarioPorId()` — available in session without extra DB queries
- Upload handled by `UsuarioController`; upload modal lives in `_Layout.cshtml`

### Base Classes

**`BaseController`** (`Controllers/BaseController.cs`):
- `CadenaConexion` - Centralized connection string from Web.config
- `ConvertirSha256(texto)` - SHA256 hashing (lowercase hex)
- `SetErrorMessage()`, `SetSuccessMessage()`, `SetInfoMessage()` - TempData-based messaging

**`BaseService`** (`Services/BaseService.cs`):
- `CadenaConexion` - Same connection string for service layer

### Database Access Pattern

ADO.NET with stored procedures against SQL Server (`bufinscustomers` database). Standard pattern:
```csharp
using (SqlConnection cn = new SqlConnection(CadenaConexion))
{
    SqlCommand cmd = new SqlCommand("sp_StoredProcName", cn);
    cmd.Parameters.AddWithValue("@ParamName", value);
    cmd.CommandType = CommandType.StoredProcedure;
    cn.Open();
    // Execute command
}
```

**Stored Procedure Naming:** `sp_` prefix + PascalCase verb + entity name. Verbs used: `Obtener` (read), `Registrar` (create), `Editar` (update), `Eliminar` (delete), `Guardar` (save/upsert), `Agregar` (add item), `Actualizar` (update/reorder), `Validar` (validate). Examples: `sp_ObtenerUsuarios`, `sp_RegistrarEmpresa`, `sp_GuardarConfiguracionBasica`. Many procedures use `OUTPUT` parameters for success/error messages.

**Backward-compatible column reading:** When adding a new column incrementally, wrap the reader access in a try-catch on `IndexOutOfRangeException`. This lets code run against DB schemas that may not yet have the column:
```csharp
try { opcion.SoloAdminEmpresa = reader["SoloAdminEmpresa"] != DBNull.Value && Convert.ToBoolean(reader["SoloAdminEmpresa"]); }
catch (IndexOutOfRangeException) { }
```
This pattern is used in `PermisosService` for optional columns.

### Authentication & Session Management

Custom session-based auth via `UsuarioSesionHelper` (`Helpers/UsuarioSesionHelper.cs`):
- Session key: `"UsuarioCompleto"`, timeout: 20 minutes
- `[ValidarSesion]` attribute on controllers/actions requiring auth (handles AJAX vs regular requests)
- Client-side session management with SweetAlert2 warnings at 5 minutes remaining (in `_Layout.cshtml`)
- Key methods: `UsuarioActual`, `EstablecerUsuarioEnSesion()`, `LimpiarSesion()`, `ObtenerInfoSesion()`, `ExtenderSesion()`

**Session keys** (constants in `UsuarioSesionHelper`):
| Key | Type | Purpose |
|-----|------|---------|
| `"UsuarioCompleto"` | `Usuarios` | Full authenticated user object |
| `"LastActivity"` | `DateTime` | Timestamp for inactivity timeout tracking |
| `"LoginTime"` | `DateTime` | Login timestamp |
| `"UsuarioPermisosCodigos"` | `HashSet<string>` | Cached permission codes (lazy-loaded on first `TienePermiso()` call) |
| `"UsuarioMenuSidebar"` | `List<SidebarCategoriaViewModel>` | Cached sidebar menu (lazy-loaded on first `ObtenerMenuSidebar()` call) |
| `"IdUsuario"`, `"usuario"` | backward-compat aliases | Set alongside `"UsuarioCompleto"` for legacy code |
| `"LogImportacion"` | `string` | Excel import log text (set by `DatosController`) |

### Role Hierarchy (3 levels)

The `Usuarios.Admin` field (byte?) defines access levels:

| Value | Role | Description |
|-------|------|-------------|
| 0 | Usuario Normal | Access only to assigned menu options, data isolated to own company (`IdEmpresa`) |
| 1 | Admin de Empresa | Company-level admin, access to assigned menu options, data isolated to own company (`IdEmpresa`) |
| 2 | Super Admin | Full access to everything, bypasses all permission checks, sees all companies |

**Data isolation rule:** Admin 0 and Admin 1 can only see/modify data belonging to their own company (`usuario.IdEmpresa`). This is enforced at the controller level and in `EmpresasViewBagFilter`. Only Super Admin (Admin=2) has cross-company access. Admin 0/1 cannot create or delete companies.

Check with: `EsUsuarioNormal()`, `EsAdminEmpresa()`, `EsSuperAdmin()`, `EsAdministrador()` (returns true for Admin=1)

### Multi-Company Group Access (Grupos Empresariales)

Companies can optionally belong to a **Grupo Empresarial** (`GruposEmpresariales` table). A company belongs to at most one group (`Empresas.IdGrupoEmpresarial`, nullable FK). Only Super Admin manages groups and which companies belong to them, via `GruposEmpresarialesController` (`~/Views/Configuracion/GruposEmpresariales.cshtml` for CRUD of groups, `~/Views/Configuracion/GestionarGrupoEmpresas.cshtml` for assigning companies to a group). `GrupoEmpresarialService` (`Services/GrupoEmpresarialService.cs`) does the CRUD + `AsignarEmpresas(idGrupo, idsEmpresas)` — a full-replace assignment that is the only code path allowed to write `IdGrupoEmpresarial`, guaranteeing a company never ends up in two groups. The Empresas gestor (`Views/Configuracion/Empresas.cshtml`) shows the group as a read-only badge — it is never editable from there.

**What group membership unlocks:** a user whose own company belongs to a group has **full access — read AND write** — to every other company in the same group, exactly as if it were their own: viewing reports/dashboards/audits, creating/editing/deleting users, editing financial configuration, uploading Excel and executing models, editing the company record, and managing permissions. The only thing that stays special about `Usuarios.IdEmpresa` (the "empresa principal") is that it's the default/preselected company in dropdowns and the fallback when a submitted company isn't valid — it carries no extra privilege over a sibling company in the same group. Creating/deleting companies and creating/managing `GruposEmpresariales` themselves remain Super Admin-only, unrelated to this.

**How it's implemented:**
- `Helpers/EmpresaCacheHelper.cs` — the single app-wide (not session) cache of all companies (5 min TTL, `MemoryCache`), including `IdGrupoEmpresarial`. Deliberately not session-cached: a Super Admin can reassign a company's group and every already-logged-in user picks up the change within the same TTL window, instead of only after their next login.
- `Helpers/EmpresaAccesoHelper.cs` — `TieneAcceso(usuario, idEmpresa)` (bool) and `ObtenerIdsEmpresasPermitidas(usuario)` (`List<int>`, `null` = Super Admin/no filter). Both are group-aware and read from `EmpresaCacheHelper`. Used uniformly for both read and write checks.
- `Filters/EmpresasViewBagFilter.cs` uses `EmpresaAccesoHelper` to populate `ViewBag.Empresas`, so every empresa dropdown across the app is group-aware by default.

> **MANDATORY:** any new controller action scoped by empresa — whether it queries/displays data or creates/edits/deletes it — must use `EmpresaAccesoHelper.TieneAcceso()` / `ObtenerIdsEmpresasPermitidas()` instead of comparing `usuario.IdEmpresa == idEmpresa` directly, so access correctly expands to the user's group. The only exceptions are actions already reserved for Super Admin regardless of company (creating/deleting companies, managing `GruposEmpresariales`), which keep their existing `EsSuperAdmin()` checks untouched.

### Menu-Based Permissions System

Permissions are managed through the `MenuOpciones` table and `MenuOpcionesService` (`Services/PermisosService.cs`):

- **`MenuOpciones`** model (`Models/PermisosModulos.cs`) - Menu items with hierarchical structure (parent/child), codes, categories. Includes `SoloSuperAdmin` (bool/bit): when true, only visible to Admin=2 and excluded from assignment for Admin 0/1. Also `SoloAdminEmpresa` (bool/bit): when true, not assignable to Admin=0 users (only Admin 1 and 2 can use it).
- **`UsuarioMenuPermisos`** model (`Models/UsuarioPermisos.cs`) - Junction table linking users to menu options
- **`MenuOpcionesService`** - CRUD for menu options and permission assignments. Key methods: `ObtenerTodas()`, `CrearMenuOpcion()`, `EditarMenuOpcion()`, `EliminarMenuOpcion()`, `ObtenerMenuParaUsuario()`, `ObtenerCodigosPermisos()`, `GuardarOpcionesUsuario()`
- **`RequierePermisoAttribute`** (`Permisos/RequierePermisoAttribute.cs`) - Available for controller-level permission enforcement via `[RequierePermiso("CODE")]`, but currently not used by any controller. Permissions are checked manually inline with `UsuarioSesionHelper.TienePermiso("CODE")` or `EsSuperAdmin()`.
- **`UsuarioSesionHelper.TienePermiso("CODE")`** - Checks permissions using a cached HashSet in session (one DB query per session, not per page load). Super Admin (Admin=2) always returns true.
- **`UsuarioSesionHelper.ObtenerMenuSidebar()`** - Returns cached `List<SidebarCategoriaViewModel>` for dynamic sidebar rendering via `_SidebarMenu.cshtml` partial.
- **`UsuarioSesionHelper.InvalidarCachePermisos()`** - Clears permission/menu cache. Called after saving permissions or on login.

Known permission codes (BD codes, used in sidebar and controllers):
- `DATOS_PLANTILLA_CARGUE`, `DATOS_MODELO_EJECUCION` (Datos)
- `INFORMES_REPORTES_PBI`, `INFORMES_AUDITORIA_CARGUES`, `INFORMES_TABLAS_DATOS`, `INFORMES_RELACIONAMIENTOS` (Informes)
- `ADMIN_USUARIOS_GESTOR`, `ADMIN_EMPRESAS_GESTOR`, `ADMIN_REPORTES_GESTOR` (Administración)
- `ADMIN_CONFIG_EMPRESAS`, `ADMIN_CONFIG_RELACIONAMIENTOS`, `ADMIN_CONFIG_MENU`, `ADMIN_CONFIG_PROMPTS`, `ADMIN_CONFIG_GRUPOS_EMPRESARIALES` (Configuración - `ADMIN_CONFIG_MENU`, `ADMIN_CONFIG_PROMPTS` and `ADMIN_CONFIG_GRUPOS_EMPRESARIALES` are SoloSuperAdmin)

**Dynamic Sidebar**: The sidebar in `_Layout.cshtml` uses `Html.RenderPartial("_SidebarMenu", UsuarioSesionHelper.ObtenerMenuSidebar())`. Menu options are read from `MenuOpciones` table with columns `NombreGrupo`, `IconoGrupo`, `IconoCategoria`, `OrdenCategoria` for hierarchical rendering. New menu items added to the table auto-appear in the sidebar and permission manager.

**Sidebar view model hierarchy** (built by `MenuOpcionesService.ConstruirMenuJerarquico()`):
- `SidebarCategoriaViewModel` → has `List<SidebarGrupoViewModel>` (grouped by `NombreGrupo`)
- `SidebarGrupoViewModel` → has `List<SidebarItemViewModel>` (individual menu items)
- `SidebarItemViewModel` → `Id`, `Codigo`, `Nombre`, `Controller`, `Action`, `Icono`

**Note:** `PermisosService`, `PermisosModulos`, `UsuarioPermisos`, `PermisoUsuarioViewModel` are deprecated aliases kept for backward compatibility. Use `MenuOpcionesService`, `MenuOpciones`, `UsuarioMenuPermisos`, `OpcionMenuUsuarioViewModel` instead.

### View Routing Convention

Views are organized by **functional area**, not by controller name. Controllers use explicit paths like `View("~/Views/Configuracion/Empresas.cshtml")`. The mapping:

| Controller | View Path |
|---|---|
| EmpresaController | `~/Views/Configuracion/Empresas.cshtml` |
| UsuarioController | `~/Views/Configuracion/Usuarios.cshtml` |
| ConfiguracionEmpresaController | `~/Views/Configuracion/ConfiguracionesEmpresas.cshtml` |
| ConfiguracionRelacionamientoController | `~/Views/Configuracion/ConfiguracionRelacionamiento.cshtml` |
| MenuOpcionesController | `~/Views/Configuracion/MenuOpciones.cshtml` |
| ModelosEjecucionController | `~/Views/Configuracion/ModelosEjecucion.cshtml` |
| GestorPromptsController | `~/Views/Configuracion/GestorPrompts.cshtml` |
| GruposEmpresarialesController | `~/Views/Configuracion/GruposEmpresariales.cshtml`, `~/Views/Configuracion/GestionarGrupoEmpresas.cshtml` |
| ReportesController (CRUD) | `~/Views/Configuracion/MaestroReportes.cshtml` |
| ReportesController (embed) | `~/Views/Reportes/Reportes.cshtml` |
| DatosController | `~/Views/Datos/CargueExcel.cshtml`, `~/Views/Datos/Modelo.cshtml` |
| InformeTablasDatosController | `~/Views/Informes/InformeTablasDatos.cshtml` |
| AnalisisIAController | `~/Views/Informes/AnalisisIA.cshtml` |
| InformeRelacionamientosController | `~/Views/Informes/InformeRelacionamientos.cshtml` |
| AuditoriaCarguesController | `~/Views/Informes/AuditoriaCargues.cshtml` |
| AuditoriaConsultasIAController | `~/Views/Informes/AuditoriaConsultasIA.cshtml` |
| HistorialVersionesCarguesController | `~/Views/Informes/HistorialVersionesCargues.cshtml` |
| TablaPUCController | `~/Views/Informes/TablaPUC.cshtml` |
| VariablesPBIController | `~/Views/Informes/VariablesPBI.cshtml` |
| GestorCategoriasController | `~/Views/Configuracion/GestorCategorias.cshtml` |
| GestorGruposController | `~/Views/Configuracion/GestorGrupos.cshtml` |
| WidgetsController | `~/Views/Configuracion/Widgets.cshtml` |
| ConfiguracionVariablesPBIController | `~/Views/Configuracion/ConfiguracionVariablesPBI.cshtml` |
| PermisosController | `~/Views/Permisos/Gestionar.cshtml` |

When creating new controllers, use explicit view paths with `~/Views/{area}/{view}.cshtml`.

### Key Controllers

- **AccesoController** - Login, registration, session management (no `[ValidarSesion]`)
- **HomeController** - Dashboard (`[ValidarSesion]` at class level)
- **UsuarioController** - User CRUD, profile image upload (no `[ValidarSesion]`, manual checks)
- **EmpresaController** - Company management (no `[ValidarSesion]`, manual checks)
- **PermisosController** - Menu option assignment UI for users, accessible by Admin 1 (own company only) and Admin 2 (`[ValidarSesion]`)
- **MenuOpcionesController** - CRUD for menu options, Super Admin only (`[ValidarSesion]`)
- **ConfiguracionEmpresaController** - Financial configuration per company (`[ValidarSesion]`)
- **ConfiguracionRelacionamientoController** - Relationship configuration with Excel upload (`[ValidarSesion]`)
- **ReportesController** - Power BI report embedding and report CRUD (no `[ValidarSesion]`, manual checks)
- **DatosController** - Excel data import/export (`[ValidarSesion]`)
- **InformeTablasDatosController** - Data tables report with AI analysis via OpenAI (`[ValidarSesion]`). Endpoints: `InformeTablasDatos` (view), `ObtenerAnios`, `ObtenerVariables`, `ConsultarDatos`, `ConsultarConIA` (async), `ExportarExcel`
- **InformeRelacionamientosController** - Relationships report (`[ValidarSesion]`)
- **ModeloController** - Financial model execution (Datos area, `[ValidarSesion]`)
- **ModelosEjecucionController** - Model execution management/configuration (`[ValidarSesion]`)
- **GestorPromptsController** - IA prompt CRUD, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete)
- **GruposEmpresarialesController** - Business group (holding) CRUD and company assignment, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete), `Gestionar` (assign-companies screen), `GuardarEmpresas` (AJAX). See "Multi-Company Group Access" above.
- **AnalisisIAController** - Standalone AI analysis page; reuses `InformeTablasDatosService` for table/empresa lists; company-scoped for non-Super Admin (`[ValidarSesion]`)
- **AuditoriaCarguesController** - Upload audit trail (no `[ValidarSesion]`, manual checks)

### Excel Import Logging

`DatosController` accumulates timestamped import log messages during an Excel upload using a private `StringBuilder _logBuilder` field. Log entries are formatted as `[yyyy-MM-dd HH:mm:ss.fff] message` via private `LogToFile(mensaje)`. The completed log is stored in `Session["LogImportacion"]` via `GuardarLogEnSession()`. Users can download it as `LogImportacion_{yyyyMMdd_HHmmss}.txt` via `GET /Datos/DescargarLog`.

Per-sheet results use `DetalleCargaHojaExcel` (`Models/CargueExcelModels.cs`):
- `Estado` values: `"Exitoso"`, `"Error"`, `"Ignorada"` (empty/no headers/no data rows)
- `ResultadoCargaExcel.MostrarDescargaLog` (bool) — set to `true` on error to show the download button

Import rules: the workbook must have exactly 10 sheets; 5 consecutive empty rows terminate data reading.

### Excel Reading/Writing (ClosedXML)

All Excel import/export uses `ClosedXML.Excel` (`XLWorkbook`, `IXLWorksheet`, `IXLCell`) — never EPPlus/`OfficeOpenXml`, which requires a paid commercial license for for-profit use from v5 onward. Notes when writing new Excel code:
- Save to bytes with `using (var ms = new MemoryStream()) { workbook.SaveAs(ms); return ms.ToArray(); }` — there is no `GetAsByteArray()` equivalent.
- `IXLCell.Value` is an `XLCellValue` struct, not `object`. To read a cell whose type isn't known ahead of time, branch on `.IsNumber`/`.IsBlank` and use `.GetNumber()`; use `.GetFormattedString()` where EPPlus code used to read `.Text`.
- To write a value coming from a loosely-typed source (`DataTable`, `Dictionary<string, object>`, etc.), use `Helpers/ExcelCellHelper.SetValue(cell, value)` instead of assigning `cell.Value = value` directly — direct assignment only compiles when the source expression's compile-time type is a concrete type ClosedXML has an implicit conversion for (string, double, bool, DateTime), not `object`.
- Freeze panes: `ws.SheetView.Freeze(rows, columns)` takes counts, not the EPPlus "top-left cell position" convention (`FreezePanes(2,1)` → `Freeze(1, 0)`).

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

**`DatosController.EjecutarModelo(idEmpresa, anio, idModelo)`** — fetches `NombreSP` from DB (never from user input), then calls the SP with `@IdEmpresa`, `@IdUsuario`, `@Año`. The SP may return multiple intermediate result sets; the **final** result set must have `CodMessage` (1 = success) and `ErrorMessage` columns. `CommandTimeout` is 300 seconds.

> **Note:** `Controllers/ModeloController.cs` is an **empty placeholder** — do not confuse it with `ModelosEjecucionController`.

### IA (AI) Integration

`IAService` (`Services/IAService.cs`) calls the **OpenAI API** using model `gpt-4o-mini`.

- **Config key:** `appSettings["OpenAIApiKey"]` in Web.config
- **Endpoint:** `https://api.openai.com/v1/chat/completions`
- **Timeout:** 60 seconds, max 1024 output tokens, temperature 0.4
- **Max rows sent to AI:** 50 (hardcoded `MaxFilas = 50`)
- **Models:** `IAConsultaRequest` (pregunta, datosJson, nombreTabla, filtrosDescripcion) / `IAConsultaResponse` (exitoso, respuesta, error) in `Models/IAModels.cs`

Currently integrated in `InformeTablasDatosController.ConsultarConIA()` — re-queries the DB, serializes up to 50 rows as JSON, and sends them with the user's optional question to OpenAI. The prompt instructs the model to act as a Colombian corporate finance analyst and respond in Spanish with markdown.

**Prompt customization**: The instruction block for the automatic summary (when no question is asked) is stored in the `GestorPrompts` DB table with `Codigo = 'RESUMEN_GERENCIAL'`. `GestorPromptsService.ObtenerPorCodigo("RESUMEN_GERENCIAL")` fetches it; if inactive/missing, `IAService` falls back to the hardcoded text. Managed via `GestorPromptsController` (Super Admin only). `IAService.ConsultarAsync()` accepts an optional `instruccionesPersonalizadas` parameter.

**Available financial tables** (static dictionary in `InformeTablasDatosService`):
`TableBalance_Datos_VT`, `TablePYG_Datos_VT`, `TableEbitda_Datos_VT`, `TableFlujoCaja_Datos_VT`, `TableFlujoTesoreria_Datos_VT`, `TableGasFijosYVar_Datos_VT`, `TableTakeRate_Datos_VT`, `TableIngCosGas_Datos_VT`, `TableIngLineasVenta_Datos_VT`, `TablePYGAjustado_Datos_VT`. Table names are whitelist-validated before use in SQL to prevent injection.

> To switch AI provider, only `IAService.cs` needs to change — update `OpenAIEndpoint`, `OpenAIModel`, and the Authorization header format. The config key is `OpenAIApiKey` in Web.config.

**`ConfiguracionSistemaService`** (`Services/ConfiguracionSistemaService.cs`) — reads key/value pairs from the `ConfiguracionSistema` DB table (`SELECT Valor FROM ConfiguracionSistema WHERE Clave = @Clave`). Used by `InformeTablasDatosController.ConsultarConIA()` to fetch `OpenAIApiKey` at runtime (DB value takes precedence over Web.config). Use this service for any secret or runtime-configurable setting that should be stored in the DB rather than deployed config.

### Global Filter

`EmpresasViewBagFilter` (`Filters/EmpresasViewBagFilter.cs`) is registered globally in `FilterConfig` and loads all companies into `ViewBag.Empresas` for every request.

### Frontend Stack

- Bootstrap 5.3.7 + SB Admin 2 theme with custom modern sidebar (`Assets/css/modern-sidebar.css`, `Assets/js/modern-sidebar.js`)
- jQuery 3.7.1
- SweetAlert2 for session notifications and confirmations
- FontAwesome icons
- Select2 (`Assets/js/select2/`, `Assets/css/select2/`) for enhanced dropdowns
- ClosedXML 0.105.0 for Excel operations (MIT license, no license call needed — chosen over EPPlus 5+/Polyform Noncommercial specifically to keep the project free of any commercial-license obligation)

### Layout Structure

`Views/Shared/_Layout.cshtml` contains the full sidebar navigation with permission-based visibility using `@if (UsuarioSesionHelper.TienePermiso("CODE"))` checks, user profile modal, image upload modal, and client-side session timeout management.

## Important Conventions

- Passwords: Always `.Trim()` before hashing. Use `HashearContrasena()` (BCrypt, work factor 12) for new passwords. `ConvertirSha256()` is kept only for the SHA256→BCrypt migration path in login — do NOT use it for new code. `VerificarContrasena()` handles both formats transparently.
- Messages between redirects: Use `SetErrorMessage/SetSuccessMessage/SetInfoMessage` (TempData keys: `"ErrorMessage"`, `"SuccessMessage"`, `"InfoMessage"`)
- New controllers must inherit from `BaseController`; new services from `BaseService`
- Use `[ValidarSesion]` at class level on all new authenticated controllers (note: some existing controllers like EmpresaController, UsuarioController, ReportesController, AuditoriaCarguesController lack it and use manual session checks instead)
- For permission checks, use `UsuarioSesionHelper.TienePermiso("CODE")` inline (the `[RequierePermiso]` attribute exists but is not currently used by any controller)
- Excel templates stored in `Assets/Plantillas/`
- Connection string key is `"DefaultConnection"` in Web.config
- File upload limit: `maxRequestLength="102400"` (100 MB) and `executionTimeout="3600"` (1 hour). IIS-level limit `maxAllowedContentLength="104857600"` (100 MB) in `system.webServer`.
- **i18n OBLIGATORIO — SIEMPRE en ambos idiomas:** Cada string visible para el usuario (vistas, JS, controladores) debe agregarse a AMBOS archivos de recursos antes de implementar la UI: `App_GlobalResources/Strings.resx` (es-CO) y `App_GlobalResources/Strings.en-US.resx` (en-US). Nunca hardcodear texto en vistas ni JS. Ver sección "Internationalization (i18n)" para detalles completos.
- **Estilos — SIEMPRE usar el sistema de diseño del sitio:** Toda vista nueva o modificada DEBE seguir los mismos estilos visuales del sitio. Ver sección "UI Style System — MANDATORY" para la referencia completa.
- **Notificaciones internas — PREGUNTAR SIEMPRE:** Al implementar cualquier feature nuevo que tenga un resultado observable (cargue, exportación, ejecución, creación/eliminación de entidades, rollback, etc.), preguntar explícitamente al usuario si desea agregar notificaciones internas para esa acción antes de cerrar el task. Si el usuario dice sí, agregar las llamadas a `NotificacionesService.Crear(...)` en el controller correspondiente, las claves a ambos `.resx`, y actualizar la tabla "Active notifications" en esta sección de CLAUDE.md.
- **Modales de confirmación/alerta y overlays de carga — SIEMPRE con el tema oscuro:** Todo modal nuevo de confirmación, eliminación, alerta o confirmación de guardado DEBE llevar la clase `modal-confirm` en su `.modal` exterior (nunca en modales de crear/editar/gestionar, que se quedan con el look claro de Bootstrap de siempre). Todo overlay nuevo de "cargando/procesando" de pantalla completa DEBE usar las clases `.overlay-cargando`/`.overlay-cargando-card`/`.overlay-cargando-icon`/`.overlay-cargando-title`/`.overlay-cargando-text`/`.overlay-cargando-timer` en vez de estilos inline propios. Todo `Swal.fire(...)` hereda el tema oscuro automáticamente, sin nada que hacer. Ver sección "Confirmation/alert modals — unified dark theme" para la referencia completa y ejemplos de markup.

## Internationalization (i18n) — MANDATORY

The app supports two languages: **es-CO** (default) and **en-US**. Every user-visible string added anywhere in the project MUST be added to both resource files.

### Resource files
- `App_GlobalResources/Strings.resx` — Spanish (es-CO, default)
- `App_GlobalResources/Strings.en-US.resx` — English

Both files must always stay in sync: every key present in one must exist in the other.

### Where each layer reads resources

| Layer | How to read |
|-------|-------------|
| Razor views (`.cshtml`) | `@Resources.Strings.KeyName` |
| JS inside Razor views | Inject as a Razor variable: `var x = '@Resources.Strings.KeyName';` — never hardcode UI strings in JS |
| C# controllers | `R("KeyName")` — protected helper in `BaseController` that calls `System.Web.HttpContext.GetGlobalResourceObject("Strings", key)` and respects the active thread culture |
| C# services / helpers | Do NOT read resources from services. Pass already-translated strings from the controller layer, or keep service-level messages generic (error codes, not sentences). |

### Rules
1. **Never hardcode a user-visible string** in views, JS, or controllers. Always use a resource key.
2. Key naming convention: `{Area}_{Element}` — e.g. `Notif_Titulo`, `Datos_JS_Procesando`, `Layout_Guest`.
3. Group keys with an XML comment in both resx files: `<!-- NOTIFICACIONES -->` / `<!-- NOTIFICATIONS -->`.
4. When adding a new feature, add all its strings to both files before implementing the UI.

## Internal Notification System — MANDATORY

The app has a persistent bell-icon notification center visible in the top navbar for every authenticated user.

### Architecture
- **DB table:** `Notificaciones` (`Id`, `IdUsuario`, `Titulo`, `Mensaje`, `Tipo`, `Leida`, `FechaCreacion`). Auto-cleanup: rows older than 30 days are deleted on insert.
- **Service:** `NotificacionesService` (`Services/NotificacionesService.cs`) — `Crear(idUsuario, titulo, mensaje, tipo)`. Swallows all exceptions so it never breaks the main flow.
- **Controller:** `NotificacionesController` (`Controllers/NotificacionesController.cs`) — endpoints `GET /Notificaciones/Recientes`, `POST /Notificaciones/MarcarLeida`, `POST /Notificaciones/MarcarTodasLeidas`, `POST /Notificaciones/Eliminar`.
- **Frontend:** Bell icon with badge in `_Layout.cshtml`. Polls `GET /Notificaciones/Recientes` every 30 s. JS functions: `notifPushLocal(tipo, titulo, mensaje)` for transient client-side alerts (no DB).

### Notification types
| `Tipo` | Color | Icon | Use for |
|--------|-------|------|---------|
| `"success"` | green | `fa-check-circle` | Completed operations |
| `"info"` | purple | `fa-info-circle` | Informational events |
| `"warning"` | amber | `fa-exclamation-triangle` | Alerts requiring attention |
| `"error"` | red | `fa-times-circle` | Failed operations |

### Active notifications (complete list — keep updated)

| Event | Type | Controller | Resource key |
|-------|------|-----------|-------------|
| Excel upload success | `success` | `DatosController` | `Notif_CargueCompletado` |
| Excel upload error | `error` | `DatosController` | `Notif_ErrorCargue` |
| Model executed (success) | `success` | `DatosController` | `Notif_ModeloEjecutado` |
| Model error | `error` | `DatosController` | `Notif_ErrorModelo` |
| Company created | `success` | `EmpresaController` | `Notif_EmpresaCreada` |
| Company deleted | `warning` | `EmpresaController` | `Notif_EmpresaEliminada` |
| User created | `success` | `UsuarioController` | `Notif_UsuarioCreado` |
| User deleted | `warning` | `UsuarioController` | `Notif_UsuarioEliminado` |
| User role changed (permissions reset to new role's defaults) | `warning` | `UsuarioController` | `Notif_PermisosReiniciados` |
| Report (PBI) created | `success` | `ReportesController` | `Notif_ReporteCreado` |
| Report (PBI) deleted | `warning` | `ReportesController` | `Notif_ReporteEliminado` |
| Business group created | `success` | `GruposEmpresarialesController` | `Notif_GrupoCreado` |
| Business group deleted | `warning` | `GruposEmpresarialesController` | `Notif_GrupoEliminado` |
| Business group's companies updated | `success` | `GruposEmpresarialesController` | `Notif_GrupoEmpresasActualizadas` |
| Session expiring (client-side) | `warning` | `_Layout.cshtml` (JS only) | `Notif_SesionExpiraTitulo` |
| Version rollback success | `success` | `HistorialVersionesCarguesController` | `Notif_RollbackEjecutado` |
| Version rollback error | `error` | `HistorialVersionesCarguesController` | `Notif_ErrorRollback` |

### When to create a notification (server-side)
> **MANDATORY:** When finishing any new feature, explicitly ask the user whether to add internal notifications before closing the task. If yes: add `NotificacionesService.Crear(...)` calls, add keys to both `.resx` files, and update the Active notifications table above.

Call `new NotificacionesService().Crear(usuario.Id, R("KeyName"), mensaje, tipo)` from a controller after **any operation that takes noticeable time or has a meaningful outcome**:
- Successful or failed Excel uploads
- Model execution (success or error)
- Long-running exports or imports
- Any batch operation that completes asynchronously or takes > 2 s
- Creation or deletion of significant entities (companies, users, reports)

**Do NOT add notifications for:** edits to existing records (name changes, config updates), read-only operations, or any action where the flash message on the same page is already sufficient feedback.

### When to push a client-side notification (no DB)
Use `notifPushLocal('tipo', _notifStr.keyTitulo, _notifStr.keyMsg)` in JS for transient events that don't need persistence:
- Session about to expire
- Network errors on AJAX calls
- Real-time warnings triggered from the browser

### Titles and messages must be bilingual
- Notification titles from controllers: use `R("Notif_KeyName")` so they respect the user's active language.
- Notification titles from JS: use `_notifStr.keyName` variables injected via Razor.
- Add both `es-CO` and `en-US` entries to the resx files for every new notification title.

## UI Style System — MANDATORY

Every new view or UI modification MUST follow the site's existing visual design system. Do not use plain Bootstrap classes, custom inline styles, or new CSS files — always use the components defined in `Assets/css/bufins-components.css` plus Bootstrap's grid system.

> **MANDATORY:** For any new page, option, or UI change, always base the design on the standards already established across the app — reuse existing classes/components/colors exactly as documented in this section instead of inventing new ones. Before styling anything new, check how the same kind of element (header, button, badge, table, modal) is already done elsewhere in the app and match it. Never introduce a new color, gradient, or one-off CSS rule when an existing shared class already covers the case.

### Brand gradients — which one to use where

There are two distinct gradient variables (both defined in `Assets/css/modern-sidebar.css`). Do not confuse them or use one in place of the other:

| Variable | Value | Used for |
|----------|-------|----------|
| `--content-gradient` | `linear-gradient(90deg,#6366F1 0%,#3B82F6 35%,#06B6D4 70%,#01E1D7 100%)` | Content elements: `.powerbi-header`, `.btn-modern-gradient` (primary action), `.data-table thead th`, `.modal-header` |
| `--app-glow-bg` (+ `--app-glow-bg-hover`) | Dark radial glow over `#160933` | Chrome (sidebar, topbar) **and** neutral/secondary buttons: `.btn-modern-secondary`, `.btn-outline-secondary`, modal Cancelar buttons |

`.btn-modern-success` (green) stays reserved for create/activate/confirm actions only — never for downloads or neutral actions (those use `.btn-modern-secondary`, which is intentionally styled with the dark chrome gradient, not gray or white).

### Required stylesheet

Every view that renders a content page must include:
```html
<link href="~/Assets/css/bufins-components.css" rel="stylesheet" />
```

### Standard page structure

All content pages follow this exact layout skeleton:
```html
<link href="~/Assets/css/bufins-components.css" rel="stylesheet" />

<div class="container-fluid px-4">

    <!-- 1. Page header (gradient banner) -->
    <div class="powerbi-header">
        <div class="powerbi-header-content">
            <i class="fas fa-{icon} powerbi-header-icon"></i>
            <h1 class="powerbi-header-title">@Resources.Strings.Page_Title</h1>
        </div>
    </div>

    <!-- 2. Optional alert/warning banners (Bootstrap alert + bufins border) -->

    <!-- 3. TempData success/error flash messages (standard Bootstrap dismissible alerts) -->

    <!-- 4. Filter panel -->
    <div class="filter-card">
        <h3 class="filter-card-title"><i class="fas fa-filter"></i> @Resources.Strings.Common_FiltrosConsulta</h3>
        <div class="filter-section">
            <div class="row"> ... form-group cols ... </div>
            <div class="btn-group-actions">
                <button class="btn btn-modern-gradient"><i class="fas fa-search mr-1"></i>@Resources.Strings.Common_Consultar</button>
                <button class="btn btn-modern-secondary"><i class="fas fa-eraser mr-2"></i>@Resources.Strings.Common_Limpiar</button>
            </div>
        </div>
    </div>

    <!-- 5. Results table -->
    <div class="table-container">
        <div class="table-header">
            <h5><i class="fas fa-list mr-2"></i>@Resources.Strings.Common_Resultados</h5>
            <span class="info-badge">N @Resources.Strings.Page_TotalItems</span>
        </div>
        <div class="table-wrapper">
            <table class="data-table"> ... </table>
        </div>
    </div>

    <!-- 6. Empty state (shown when no results) -->
    <div class="no-data-message">
        <i class="fas fa-inbox"></i>
        <h4>@Resources.Strings.Page_NoData</h4>
        <p>@Resources.Strings.Page_NoDataDesc</p>
    </div>

</div>
```

### Component reference

| Component | Class(es) | Purpose |
|-----------|-----------|---------|
| Page header | `.powerbi-header` → `.powerbi-header-content` → `.powerbi-header-icon` + `.powerbi-header-title` | Gradient top banner, every page |
| Filter panel | `.filter-card` → `.filter-card-title` → `.filter-section` | White card wrapping filter controls |
| Button group | `.btn-group-actions` | Row of action buttons at the bottom of a filter panel |
| Primary button | `.btn-modern-gradient` | Main action (Consultar, Guardar, etc.) |
| Success button | `.btn-modern-success` | Positive secondary action |
| Secondary button | `.btn-modern-secondary` | Cancel, Limpiar, Instructivo, descargar plantilla/log — dark chrome gradient (`--app-glow-bg`), not gray/white |
| Excel export button | `.btn-modern-excel` | Any button that exports/downloads data as an Excel file (`fa-file-excel` icon) — Excel brand green (`#217346`, hover `#185c37`). Do not use for templates/instructivos, only for actual data exports |
| Table wrapper | `.table-container` → `.table-header` → `.table-wrapper` → `.data-table` | Full table with sticky gradient header |
| Count badge | `.info-badge` | Green pill showing record count in table header |
| Empty state | `.no-data-message` | Centered icon + text when no results |
| Loading spinner | `.loading-spinner` | Hidden by default; show/hide via JS during async calls |
| Pagination | `.pagination-controls` → `.btn-pagination` / `.btn-pagination.active` | Page navigation below table |

### Buttons inside tables

Use standard Bootstrap button sizes with semantic colors — do not create new button styles:
- `btn btn-sm btn-warning` — restore / revert actions
- `btn btn-sm btn-danger` — delete actions
- `btn btn-sm btn-primary` — view / edit actions
- `btn btn-sm btn-success` — activate / confirm actions

### Icons

Always use FontAwesome 5 (`fas fa-*`). Match icons to the semantic meaning of the action. Common patterns already in use: `fa-filter` (filters), `fa-search` (search), `fa-eraser` (clear), `fa-list` (results), `fa-inbox` (empty state), `fa-history` (history), `fa-undo` (restore), `fa-building` (company), `fa-calendar` (year/date), `fa-user` (user), `fa-file-excel` (Excel file).

### Confirmation/alert modals — unified dark theme (`.modal-confirm`)

This dark, Bufins-branded skin applies ONLY to confirmation, delete, and destructive-action modals — NOT to the regular create/edit/manage modals used by every gestor (those keep their original light Bootstrap look untouched). Every SweetAlert2 dialog (`Swal.fire(...)`) is also always dark, since in this codebase `Swal.fire` is only ever used for quick confirmations/alerts, never full edit forms.

**To make a Bootstrap modal dark, add the `modal-confirm` class to its outer `.modal` wrapper** — nothing else changes:
```html
<div class="modal fade modal-confirm" id="eliminarModal" tabindex="-1" ...>
  <div class="modal-dialog modal-dialog-centered">
    <div class="modal-content">
      <div class="modal-header bg-danger text-white">
        <h5 class="modal-title"><i class="fas fa-exclamation-triangle mr-2"></i>@Resources.Strings.Key</h5>
        <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
      </div>
      <div class="modal-body">...</div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" data-dismiss="modal">Cancelar</button>
        <button type="submit" class="btn btn-danger">Eliminar</button>
      </div>
    </div>
  </div>
</div>
```
Do NOT add `modal-confirm` to create/edit/manage modals (e.g. `crearUsuarioModal`, `editEmpresaModal`, `editarModal` in any gestor) — those must stay as plain `.modal` with the default light Bootstrap look, per explicit product decision.

Existing modals already marked `.modal-confirm`: `confirmDeleteModal` (Usuarios, Empresas, MaestroReportes), `eliminarModal` (GestorPrompts, GruposEmpresariales, ModelosEjecucion, ConfiguracionGlobalIA), `modalCierreAnio` (ConfiguracionesEmpresas).

The CSS (scoped under `.modal-confirm`, in `Assets/css/bufins-components.css`) restyles `.modal-content` (dark card `#1a1240`, rounded, top gradient accent bar using `--content-gradient`), `.modal-header`/`.modal-body`/`.modal-footer` (dark background, light text — including neutralizing old one-off header classes like `bg-danger`/`.modal-header-rojo`), `.close` (circular button), and footer buttons by their existing Bootstrap class — `.btn-primary`/`.btn-success` get the brand gradient, `.btn-secondary` gets the neutral dark-glass look, `.btn-danger` gets the red gradient. SweetAlert2's `.swal2-popup`/`.swal2-confirm`/`.swal2-cancel`/`.swal2-deny`/`.swal2-loader` are styled globally (unscoped) to match, with zero JS changes required.

**Blocking "cargando/procesando" overlays** (full-screen wait dialogs shown during long operations — Excel upload, relationship config upload, PBI variable processing, model export) use the same dark theme via reusable classes `.overlay-cargando` (outer fixed backdrop) / `.overlay-cargando-card` (inner card) / `.overlay-cargando-title` / `.overlay-cargando-text` / `.overlay-cargando-timer`, instead of the old per-view inline `style="background:white;..."`. The element keeps its own inline `style="display:none;"` untouched — JS toggles it directly (`overlay.style.display = 'flex'/'none'`) and that keeps working unmodified; only the color/shape properties moved into the shared classes. Existing usages: `#overlayProcesando` (CargueExcel, ConfiguracionRelacionamiento, ConfiguracionVariablesPBI), `#overlayExportacion` (Datos/Modelo). The purely in-page (non-overlay) `#loadingEjecucion` spinner in Datos/Modelo is NOT part of this system — it's an inline page state, not a blocking modal, and keeps its original light look.

### Brand loaders (wordmark + gradient) — reusable partials

Three animated loader components, built from the Bufins wordmark images (`Assets/img/Bufins_Wordmark_Aqua.png` for dark backgrounds, `Assets/img/Bufins_Wordmark_Dark.png` — purple `#160933` — for light backgrounds) plus the brand gradient. Pure CSS + image, no JS/library. CSS lives in `Assets/css/bufins-components.css` (classes `.l1-*`/`.l2-*`/`.l3-*`); each is also wrapped as a Razor partial in `Views/Shared/` so it can be dropped into any view:

| Partial | Classes | Use case |
|---|---|---|
| `_LoaderRing.cshtml` | `.l1-wrap` / `.l1-ring` / `.l1-ring2` / `.l1-logo` | Full-page loading screens — orbiting gradient ring + pulsing logo |
| `_LoaderSweep.cshtml` | `.l2-wrap` / `.l2-base` / `.l2-mask` | Buttons or indeterminate progress bars — light sweep across the wordmark |
| `_LoaderDots.cshtml` | `.l3-wrap` / `.l3-logo` / `.l3-dots` | Small modals or inline states — static logo + 4 bouncing gradient dots (this is the one wired into the `.overlay-cargando` loading overlays above) |

Each partial takes a `string` model — `"aqua"` (default, for dark/purple backgrounds) or `"dark"` (for white/light backgrounds) — to pick the right wordmark automatically:
```csharp
@Html.Partial("_LoaderDots", "aqua")   @* dark modal/card background *@
@Html.Partial("_LoaderRing", "dark")   @* white/light background *@
```
Use `_LoaderDots` inside any new `.overlay-cargando-card` or `.modal-confirm .modal-body` that needs a lightweight inline loading state; use `_LoaderRing` for full-page/full-screen loading transitions; use `_LoaderSweep` for buttons or progress-bar-style indeterminate loading.

For "choice" modals (multiple actions to pick from, e.g. "¿Qué te gustaría hacer?"), use this pattern inside `.modal-body` instead of `.modal-footer` buttons:
```html
<button type="button" class="opt primary" ...>
  <div class="ic"><i class="fas fa-{icon}"></i></div>
  <div class="txt"><span class="t">Texto principal</span><span class="s">Descripción corta</span></div>
  <i class="fas fa-arrow-right go"></i>
</button>
```
Use `opt primary` for the recommended/highlighted action and `opt secondary` for the rest.

### Forms inside views

- Use `.form-group` + `<label>` (with icon) + `.form-control` for every input/select
- Labels: `<label><i class="fas fa-{icon} mr-1"></i>@Resources.Strings.Key</label>`
- Selects with enhanced UX: add `class="form-control select2"` and initialize Select2 in `@section scripts`
- Required field marker: add `<span class="required">*</span>` inside `<label>`

### What NOT to do

- Do not use `style="..."` inline for layout or color — use the component classes above
- Do not create new `.css` files for individual pages unless a feature genuinely requires isolated styles (e.g. `analisis-ia.css`)
- Do not use DataTables — it is not in the project's frontend stack
- Do not use raw `<table>` without the `.data-table` class and `.table-wrapper` container
- Do not use plain `<button class="btn btn-primary">` for main page actions — use `.btn-modern-gradient`
