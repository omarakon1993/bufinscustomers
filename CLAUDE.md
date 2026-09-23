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
7. **Permisos** (`Permisos/`) - `ValidarSesionAttribute`, `RequierePermisoAttribute`, `SoloSuperAdminAttribute`

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

**Super Admin ↔ empresa principal:** aunque un Super Admin ignora el aislamiento por empresa, se le asocia por defecto a la **empresa principal (Bufins)** para tener un `IdEmpresa` coherente (lo usa, p. ej., la auditoría de seguridad del login). `UsuarioController.Registrar`/`EditarUsuario` rellenan `IdEmpresa` cuando `Admin == 2` y viene vacío, vía `EmpresaService.ObtenerIdEmpresaPrincipal()` (clave de sistema `EmpresaPrincipalId` → empresa llamada "Bufins" → `null`). Para los existentes: `UPDATE Usuarios SET IdEmpresa = (SELECT TOP 1 EmpId FROM Empresas WHERE EmpNombre LIKE 'Bufins%' ORDER BY CASE WHEN EmpNombre='Bufins' THEN 0 ELSE 1 END, EmpId) WHERE Admin = 2 AND (IdEmpresa IS NULL OR IdEmpresa = 0);`

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
- **`RequierePermisoAttribute`** (`Permisos/RequierePermisoAttribute.cs`) - Action filter for permission enforcement: `[RequierePermiso("CODE")]` or `[RequierePermiso("CODE1,CODE2")]` (any-of). Super Admin always passes (via `TienePermiso`). On failure: AJAX → JSON `{ success=false, forbidden=true }` + HTTP 403; normal request → redirect to `~/Error/Forbidden`. **Preferred over inline `if (!EsSuperAdmin() && !TienePermiso("CODE"))` checks** — the framework enforces it even when a new action forgets the guard. Used by `UsuarioController` (all `ADMIN_USUARIOS_GESTOR` actions), `HistorialVersionesCarguesController`, `AuditoriaController`. Keep `ViewBag.Puede*` flags (they drive UI visibility, not access).
- **`SoloSuperAdminAttribute`** (`Permisos/SoloSuperAdminAttribute.cs`) - Same shape as `RequierePermiso` but gates on `EsSuperAdmin()`. Applied **at class level** (next to `[ValidarSesion]`) on the controllers that are 100% Super Admin: `MenuOpcionesController`, `ModelosEjecucionController`, `GestorEscenariosController`, `GestorPromptsController`, `GestorCategoriasController`, `GestorGruposController`, `WidgetsController`, `ConfiguracionGlobalIAController` — their inline `if (!EsSuperAdmin()) return RedirectToAction("Index","Home")` guards were removed. Controllers with mixed gates (Admin de Empresa allowed, or empresa/grupo access — `EmpresaController`, `PermisosController`, `DatosController`, `ConfiguracionEmpresaController`, the Informe* controllers) keep inline checks **by design**, not as debt.
- **`UsuarioSesionHelper.TienePermiso("CODE")`** - Checks permissions using a cached HashSet in session (one DB query per session, not per page load). Super Admin (Admin=2) always returns true.
- **`UsuarioSesionHelper.ObtenerMenuSidebar()`** - Returns cached `List<SidebarCategoriaViewModel>` for dynamic sidebar rendering via `_SidebarMenu.cshtml` partial.
- **`UsuarioSesionHelper.InvalidarCachePermisos()`** - Clears permission/menu cache. Called after saving permissions or on login.

Known permission codes (BD codes, used in sidebar and controllers):
- `DATOS_PLANTILLA_CARGUE`, `DATOS_MODELO_EJECUCION` (Datos)
- `INFORMES_REPORTES_PBI`, `INFORMES_AUDITORIA_CARGUES`, `INFORMES_TABLAS_DATOS`, `INFORMES_RELACIONAMIENTOS`, `INFORMES_AUDITORIA_GENERAL`, `INFORMES_PYG_GERENCIAL` (Informes — `INFORMES_AUDITORIA_GENERAL` is SoloSuperAdmin, visor de la tabla `Auditoria`; `INFORMES_PYG_GERENCIAL` → `InformePYGController`, no SoloSuperAdmin/SoloAdminEmpresa, aún pendiente de crear como fila en `MenuOpciones` vía `/MenuOpciones`)
- `ADMIN_USUARIOS_GESTOR`, `ADMIN_EMPRESAS_GESTOR`, `ADMIN_REPORTES_GESTOR` (Administración)
- `ADMIN_CONFIG_EMPRESAS`, `ADMIN_CONFIG_RELACIONAMIENTOS`, `ADMIN_CONFIG_MENU`, `ADMIN_CONFIG_PROMPTS`, `ADMIN_CONFIG_GRUPOS_EMPRESARIALES`, `ADMIN_CONFIG_ESCENARIOS` (Configuración - `ADMIN_CONFIG_MENU`, `ADMIN_CONFIG_PROMPTS`, `ADMIN_CONFIG_GRUPOS_EMPRESARIALES` and `ADMIN_CONFIG_ESCENARIOS` are SoloSuperAdmin)

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
| GestorEscenariosController | `~/Views/Configuracion/GestorEscenarios.cshtml` |
| GestorPromptsController | `~/Views/Configuracion/GestorPrompts.cshtml` |
| GruposEmpresarialesController | `~/Views/Configuracion/GruposEmpresariales.cshtml`, `~/Views/Configuracion/GestionarGrupoEmpresas.cshtml` |
| ReportesController (CRUD) | `~/Views/Configuracion/MaestroReportes.cshtml` |
| ReportesController (embed) | `~/Views/Reportes/Reportes.cshtml` |
| DatosController | `~/Views/Datos/CargueExcel.cshtml`, `~/Views/Datos/RevisarCargue.cshtml`, `~/Views/Datos/Modelo.cshtml` |
| InformeTablasDatosController | `~/Views/Informes/InformeTablasDatos.cshtml` |
| AnalisisIAController | `~/Views/Informes/AnalisisIA.cshtml` |
| InformeRelacionamientosController | `~/Views/Informes/InformeRelacionamientos.cshtml` |
| InformePYGController | `~/Views/Informes/InformePYG.cshtml` |
| AuditoriaCarguesController | `~/Views/Informes/AuditoriaCargues.cshtml` |
| AuditoriaConsultasIAController | `~/Views/Informes/AuditoriaConsultasIA.cshtml` |
| AuditoriaController | `~/Views/Informes/Auditoria.cshtml` |
| AuditoriaHubController | `~/Views/Informes/AuditoriaHub.cshtml` |
| InformeNavegacionController | `~/Views/Informes/InformeNavegacion.cshtml` |
| HistorialVersionesCarguesController | `~/Views/Informes/HistorialVersionesCargues.cshtml` |
| TablaPUCController | `~/Views/Informes/TablaPUC.cshtml` |
| VariablesPBIController | `~/Views/Informes/VariablesPBI.cshtml` |
| GestorCategoriasController | `~/Views/Configuracion/GestorCategorias.cshtml` |
| GestorGruposController | `~/Views/Configuracion/GestorGrupos.cshtml` |
| WidgetsController | `~/Views/Configuracion/Widgets.cshtml` |
| ConfiguracionVariablesPBIController | `~/Views/Configuracion/ConfiguracionVariablesPBI.cshtml` |
| PermisosController | `~/Views/Permisos/Gestionar.cshtml` |
| ErrorController | `~/Views/Error/*` (convention — see "Error Pages" below) |

When creating new controllers, use explicit view paths with `~/Views/{area}/{view}.cshtml`. (`ErrorController` is the deliberate exception: it uses convention-based `Views/Error/` so the pages stay minimal and independent.)

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
- **DatosController** - Excel data import/export (`[ValidarSesion]`). `CargarExcel` only stages+validates (see "Validación en dos pasos del cargue de Excel"); `ConfirmarCargue`/`DescartarCargue`/`RevisarCargue` complete the flow
- **InformeTablasDatosController** - Data tables report with AI analysis via OpenAI (`[ValidarSesion]`). Endpoints: `InformeTablasDatos` (view), `ObtenerAnios`, `ObtenerVariables`, `ConsultarDatos`, `ConsultarConIA` (async), `ExportarExcel`
- **InformeRelacionamientosController** - Relationships report (`[ValidarSesion]`)
- **InformePYGController** - Estado de Resultados (PYG) gerencial: Real vs Presupuesto, mes + acumulado del año, KPIs, tendencia e insights de IA (`[ValidarSesion]`). Lee de `dbo.ModeloPYG` (+ `JOIN dbo.Rel_PYG` para el flag de subtotal) vía `InformePYGService` — no ejecuta `sp_ModeloPYG` en vivo. Endpoints: `Index` (view), `ObtenerAnios`, `ConsultarReporte`, `GenerarInsightsIA` (async, reutiliza el patrón de cupo/prompts/auditoría de `HomeController.ObtenerResumenIA` con el código `RESUMEN_PYG_GERENCIAL` en `GestorPrompts`), `ExportarExcel`. Ver "Estado de Resultados (PYG) Gerencial" más abajo
- **ModeloController** - Financial model execution (Datos area, `[ValidarSesion]`)
- **ModelosEjecucionController** - Model execution management/configuration (`[ValidarSesion]`)
- **GestorEscenariosController** - CRUD for the `Escenarios` data-scenario catalog, Super Admin only (`[ValidarSesion][SoloSuperAdmin]`). `Eliminar`-equivalent (`Desactivar`) is always a soft-delete. See "Escenarios de datos"
- **GestorPromptsController** - IA prompt CRUD, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete)
- **GruposEmpresarialesController** - Business group (holding) CRUD and company assignment, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete), `Gestionar` (assign-companies screen), `GuardarEmpresas` (AJAX). See "Multi-Company Group Access" above.
- **AnalisisIAController** - Standalone AI analysis page; reuses `InformeTablasDatosService` for table/empresa lists; company-scoped for non-Super Admin (`[ValidarSesion]`)
- **AuditoriaCarguesController** - Upload audit trail (no `[ValidarSesion]`, manual checks)
- **InformeNavegacionController** - Página-visitas por usuario (`[ValidarSesion]`, gate `EsSuperAdmin() || EsAdminEmpresa()`). Endpoints: `Index`, `Detalle`, `ResumenUsuarios`, `ResumenPaginas`, `ExportarExcel`. Ver "Informe de navegación (páginas visitadas)"
- **AuditoriaHubController** - Módulo unificado de auditoría, `Index` renderiza `~/Views/Informes/AuditoriaHub.cshtml`: una sola pantalla con pestañas **Cambios / Navegación / Cargues / IA**, cada una cargada en línea con `$.load()` (`?embed=1`, layout `_LayoutFragment`). `[ValidarSesion]` + gate `EsSuperAdmin() || EsAdminEmpresa()`. Ver "Módulo unificado de auditoría (hub)"

### Estado de Resultados (PYG) Gerencial

Primer informe de una serie de "informes gerenciales" listos para presentar a dirección (basado
visualmente en `InformeLineaTiempo`: mismos `.lt-*` classes de `Assets/css/informe-linea-tiempo.css`
para filtros/tarjetas, mismo patrón de PDF con jsPDF puro sin html2canvas). Muestra la estructura de
P&G (Ingresos → Costo de Ventas → Utilidad Bruta → Gastos → EBITDA → Utilidad Neta) con **Real,
Presupuesto, Variación $, Variación % y Margen %**, en dos bloques lado a lado — **mes seleccionado** y
**acumulado del año** — más 3 tarjetas KPI (Margen Bruto/EBITDA/Margen Neto), 3 mini-gráficas de
tendencia (Chart.js) y una sección de insights generados por IA.

- **Fuente de datos**: `dbo.ModeloPYG` (tabla ya materializada, la misma que hoy navega
  `InformeModelosController` de forma genérica) — **no** ejecuta `sp_ModeloPYG` en vivo.
  `Services/InformePYGService.ObtenerFilas` hace `SELECT ... FROM dbo.ModeloPYG m LEFT JOIN
  dbo.Rel_PYG r ON r.Id = m.Ord WHERE m.IdEmpresa=@e AND m.IdEscenario=@esc AND m.Año=@a` (sin filtro de
  mes — trae el año completo de una vez y filtra/agrega en C#).
- **`Mes` se guarda como abreviatura de 3 letras en español** (`'Ene'..'Dic'`), igual que las demás
  tablas de Ejecución de Modelos (ver `InformeTablasDatosService.ConvertirNumeroAMes`) — el servicio
  convierte a entero 1-12 al leer el `SqlDataReader` y **ordena en C#**, no en SQL (un `ORDER BY Mes`
  alfabético sobre esas abreviaturas quedaría en orden incorrecto).
- **Filas subtotal** (Utilidad Bruta, EBITDA, Utilidad Neta, etc.) se detectan vía
  `dbo.Rel_PYG.Tipo = 'CALCULO'` (traído por el `LEFT JOIN`), no por si `CuentaPUC` viene vacío.
- Las tarjetas KPI y las 3 series de tendencia buscan filas por **`Descripcion` literal**
  (`"Ingresos"`, `"Utilidad Bruta"`, `"EBITDA"`, `"Utilidad Neta"` — constantes en
  `InformePYGService`). ⚠️ Estos literales no están 100% confirmados contra `dbo.Rel_PYG` en vivo; si no
  calzan exacto, la tabla completa se sigue mostrando bien (viene de `Ord`/`Descripcion` reales) — solo
  las tarjetas KPI y las mini-gráficas de tendencia quedarían en cero hasta ajustar esas constantes.
- Usa `ValorPresupuesto`/`ValorPresupuestoAcumulado` (no `ValorPresupuestoConAjuste`, que queda
  disponible en `PygFilaModelo` para un futuro toggle "con ajuste").
- **Insights IA**: nuevo código `RESUMEN_PYG_GERENCIAL` en `GestorPrompts` (crear vía `/GestorPrompts`,
  aún no tiene una fila por defecto) — pide 2-3 bullets fijos "Logro del periodo:"/"Alerta de
  costos:"/"Eficiencia de gastos:". Reutiliza el cupo diario (`Usuarios.LimiteConsultasIA`) y el
  registro en `AuditoriaAnalisisIA` (`NombreTabla = "PYG Gerencial"`) del mismo patrón que
  `HomeController.ObtenerResumenIA`.
- **Fuera de alcance (a propósito) en esta primera versión**: desglose por Líneas de Negocio (quedaría
  alimentado por `dbo.ModeloLineasNegocio`/`sp_ModeloLineasNegocio` — ver punto de extensión comentado
  en `PygReporteViewModel`).
- **Pendiente manual**: crear la opción de menú `INFORMES_PYG_GERENCIAL` vía `/MenuOpciones` (Super
  Admin) para que aparezca en el sidebar — no requiere script SQL.
- **FASE 2 (mejoras gerenciales, generic para cualquier empresa)**:
  - **Comparación interanual (YoY)**: `ConstruirReporte` trae también `ObtenerFilas(..., año - 1)` con el
    mismo rango de meses y agrega `PeriodoVariacionYoYPorcentual`/`AcumVariacionYoYPorcentual` a cada
    `PygFilaReporte` (columna extra "Var. A/A" en tabla/Excel/PDF) y `PeriodoRealPctAnioAnterior`/
    `AcumRealPctAnioAnterior` a cada `PygKpiMargen` (segundo chip "vs. año ant." en las tarjetas KPI,
    junto al ya existente "vs. Ppto."). Si el año-1 no tiene datos cargados (empresa nueva), todos los
    campos *AnioAnterior/YoY quedan `null` (se muestran como "—") y `PygReporteViewModel.HayAnioAnterior`
    queda en `false` — la vista pinta una nota (`PYG_SinAnioAnteriorNota`) en vez de romper el reporte.
  - **Cascada (waterfall) Ingresos → Utilidad Neta**: `InformePYGService.ArmarCascada` arma 5 barras
    (`PygCascadaBarra`, expuestas en `PygReporteViewModel.CascadaPeriodo`) usando los 4 subtotales que
    YA calcula `sp_ModeloPYG` (Ingresos/Utilidad bruta/Utilidad operacional/Utilidad neta) como puntos de
    apoyo — cada tramo intermedio es la diferencia exacta entre dos subtotales consecutivos, así que la
    suma de los tramos SIEMPRE cuadra con Ingresos-Utilidad Neta reales sin depender de reconstruir el
    detalle línea a línea (ni de asumir qué cuentas puntuales componen cada bloque — por eso es genérico
    para cualquier empresa). `Etiqueta` es un código fijo (`Ingresos`/`CostoVentas`/
    `GastosOperacionales`/`OtrosEImpuestos`/`UtilidadNeta`, mismo patrón que `PygKpiMargen.Codigo` — el
    servicio no traduce, la vista sí vía `_pyg.cascadaNombres`). Se dibuja con Chart.js v4 nativo (barras
    flotantes `data:[desde,hasta]` + línea punteada de referencia con la Utilidad Neta presupuestada),
    sin librería adicional. Incluida también como imagen en el PDF (ancho completo, antes de las 3
    mini-tendencias).
  - **Destacados del período** (`pintarDestacados` en la vista, sin cambio de backend): top 4 líneas NO
    subtotal rankeadas por **impacto absoluto en pesos** de `PeriodoVariacionAbsoluta` (no por
    variación %, a propósito — una línea pequeña con un swing % enorme no debe dominar el resumen
    ejecutivo; regla estándar de materialidad).
  - **Resaltado de variaciones críticas**: `claseVariacion()`/`celdaVariacion()` en la vista marcan en
    negrita + ícono cualquier celda de variación **%** (Var. % o Var. A/A, periodo o acumulado) cuyo
    valor absoluto supere `PYG_UMBRAL_CRITICO = 0.15` (15%) — constante de solo-JS, sin exponerse en el
    backend. No aplica a columnas en pesos (Var. $), solo a porcentuales.
  - Encabezado renombrado de "Estado de Resultados — PYG Gerencial" a **"Informe de estado de resultados
    (PYG)"** (`PYG_PageTitle`) — mismo contenido, título más corto y genérico.

### Excel Import Logging

`DatosController` accumulates timestamped import log messages during an Excel upload using a private `StringBuilder _logBuilder` field. Log entries are formatted as `[yyyy-MM-dd HH:mm:ss.fff] message` via private `LogToFile(mensaje)`. The completed log is stored in `Session["LogImportacion"]` via `GuardarLogEnSession()`. Users can download it as `LogImportacion_{yyyyMMdd_HHmmss}.txt` via `GET /Datos/DescargarLog`.

Per-sheet results use `DetalleCargaHojaExcel` (`Models/CargueExcelModels.cs`):
- `Estado` values: `"Exitoso"`, `"Error"`, `"Ignorada"` (empty/no headers/no data rows)
- `ResultadoCargaExcel.MostrarDescargaLog` (bool) — set to `true` on error to show the download button

Import rules: the workbook must have exactly as many sheets as `TablasCargueHelper.MapeoZaIni` has entries (currently 9 — see `Helpers/TablasCargueHelper.cs`, the single source of truth for the sheet-`Z_` → table-`Ini_` set, shared by `DatosController`, `HistorialVersionesCarguesService` and `ConfiguracionEmpresaController`); 5 consecutive empty rows terminate data reading.

### Validación en dos pasos del cargue de Excel (staging)

`CargarExcel` **ya no escribe directo en `Ini_*`**. El flujo es: subir → validar en staging → revisar → confirmar (o descartar). Nada real se toca hasta que el usuario confirma explícitamente.

- **Tablas de staging** (`Sql/007_CarguesStaging_CreateTables.sql`): `dbo.CarguesLotes` (cabecera: empresa/año/modo/escenario/usuario/archivo/`Estado`) y `dbo.CarguesLotesErrores` (un renglón por hallazgo: hoja, fila Excel, columna, `Severidad` Error/Advertencia, `CodigoRegla`, mensaje es/en), más un espejo `dbo.Staging_Ini_*` por cada una de las 9 `Ini_*` — clonado dinámicamente del esquema real (`SELECT TOP(0) INTO`) para no tener que mantener tipos a mano, con `IdLote`/`NumeroFilaExcel` agregados y las 3 columnas de ejecución (`IdUsuarioEjecucion_Log`/`FechaEjecucion_Log`/`Observacion_Log`) quitadas (no aplican antes de confirmar).
- **`Estado` de un lote**: `EnValidacion → ValidadoOk | ConAdvertencias | ConErrores → Confirmado | Descartado`. Solo `ValidadoOk`/`ConAdvertencias` se pueden confirmar (`CargueLote.PuedeConfirmar`).
- **Aislamiento entre empresas y candado por llave de cargue**: `IdLote` es la partición universal (BIGINT IDENTITY único global) — todo lo que toca staging o valida filtra por `IdLote`, así que dos empresas nunca comparten filas ni pueden cruzarse, y `RevisarCargue`/`ConfirmarCargue`/`DescartarCargue` exigen `EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)` antes de tocar un lote ajeno. Además, `UX_CarguesLotes_ActivoPorLlave` (`Sql/008_CarguesLotes_UnicoActivoPorLlave.sql`, índice único **filtrado** `WHERE Estado <> 'Confirmado' AND Estado <> 'Descartado'` sobre `(IdEmpresa, Anio, Modo, IdEscenario)`) impide que existan dos lotes **activos** a la vez para la misma llave de cargue — cierra la carrera de dos cargues casi simultáneos para la misma empresa/año/modo/escenario, donde el segundo en confirmarse pisaría lo que dejó el primero. `CargueStagingService.CrearLote` hace primero un pre-check amable (evita el error crudo del índice en el caso normal) y, si de todos modos hay una carrera real, atrapa la violación del índice (`SqlException.Number` 2601/2627) y responde igual de amable — ambos casos devuelven `(exito:false, idLote del lote activo existente, mensaje)`, y `DatosController.CargarExcel` redirige directo a `RevisarCargue` de ese lote en vez de dejar crear uno nuevo encima.
- **`Services/CargueStagingService.cs`** orquesta todo: `CrearLote`, `ValidarLote(idLote)` (llama al SP — ya no arma ningún TVP), `ConfirmarLote` (recibe la `SqlConnection`/`SqlTransaction` del llamador para participar en la misma transacción), `DescartarLote`/`LimpiarStagingDeLote`, `PurgarLotesVencidosSiToca` (purga oportunista de lotes nunca confirmados con más de 48 h, mismo patrón que `AuditoriaNavegacionService.PurgarSiToca`, sin job de SQL Agent).
- **`dbo.sp_ValidarCargueStaging`** (`Sql/StoredProcedures/`) — **único SP con todas las reglas de validación**, corriendo contra `Staging_Ini_*` (nunca contra `Ini_*` real, salvo para comparar "lo que entra" contra "lo que ya está guardado"). Toda la validación (DELETE idempotente + bloques + UPDATE de cierre) corre dentro de una transacción explícita (`BEGIN/COMMIT/ROLLBACK TRANSACTION` + `SET XACT_ABORT ON`) para que un fallo a mitad de un bloque no deje el lote "a medio validar". Agregar una regla nueva = un bloque más ahí (etiquetado `BLOQUE N`), sin tocar C#. **A propósito, por ahora solo trae 3 bloques** (el resto de reglas de la primera versión — catálogos genéricos vía TVP, campos obligatorios, duplicados, cuadre débito/crédito, ecuación de cuenta, continuidad mes a mes — se retiraron a pedido del usuario para partir de un SP mínimo e ir agregando de a una): **Bloque 1 `PAIS_INCONSISTENTE`** (país de alguna fila del lote que no existe en el catálogo real `dbo.Paises`), **Bloque 2 `PAIS_VACIO`** (alguna fila trae el país vacío/nulo — una sola advertencia por lote), y **Bloque 3 `DIFERENCIA_UNIDAD_MEDIDA`** (ingresos del año que se carga vs. el año YA GUARDADO más cercano en `Ini_PYG` — no solo ±1 año — usando `dbo.Rel_PYG` con `EXISTS`, no `JOIN`, para no inflar el `SUM` si una cuenta tiene más de un mapeo) — migradas/adaptadas de los widgets "Pais inconsistente por empresa" y "Diferencias unidades de medida" (`DashboardTarjetas`, `Tipo=3/Subtipo='plantilla'`, retirados del Gestor de Widgets). **No recibe `@Catalogos`** — ningún bloque actual valida contra listas configurables por empresa; el tipo `dbo.TVP_CatalogoItem` sigue existiendo en `007_CarguesStaging_CreateTables.sql` por si se vuelve a necesitar. No usa `STRING_AGG` (no disponible en esta instancia).
- **`dbo.sp_ConfirmarCargueStaging`** — mueve un lote validado de `Staging_Ini_*` a `Ini_*` con un `INSERT...SELECT` por tabla (columnas resueltas por intersección de `INFORMATION_SCHEMA.COLUMNS`, concatenadas con `FOR XML PATH`+`STUFF` — no `STRING_AGG`, no disponible en esta instancia). **No abre su propia transacción** — corre dentro de la del llamador (`DatosController.ConfirmarCargue`), igual que `CrearSnapshotEnTransaccion`.
- **`DatosController.ConfirmarCargue(idLote)`** hace, en este orden y dentro de una sola transacción: `CrearSnapshotEnTransaccion` (sin cambios) → `EliminarEjecucionDeIni`/`EliminarAnosHistoricosDeIni` (sin cambios) → `sp_ConfirmarCargueStaging` → `RegistrarAuditoria` (sin cambios, tabla `AuditoriaCargues`) → commit. Después del commit sigue corriendo `SP_ValidarPlantillaInicial` como red de seguridad adicional (solo modo ejecución, igual que antes).
- **`GuardarEnIni`** es la misma pieza de siempre (resuelve el esquema destino vía `INFORMATION_SCHEMA.COLUMNS`, así que sirve tanto para `Ini_*` como para `Staging_Ini_*`) con 2 parámetros opcionales nuevos (`idLote`, usado para completar las columnas `IdLote`/`NumeroFilaExcel` cuando la tabla destino las tiene). `CargarExcel` la llama apuntando a `Staging_Ini_*` (vía `TablasCargueHelper.NombreStaging(nombreTablaIni)`); `Z_TablaPUC` (sin tabla `Ini_`/`Staging_Ini_` asociada) sigue guardándose igual que siempre, fuera de este flujo.
- **Vista**: `~/Views/Datos/RevisarCargue.cshtml` — el informe de validación (tabla de hallazgos, badge de errores/advertencias, botón Confirmar deshabilitado mientras `TotalErrores > 0`, modal `.modal-confirm` para Descartar).
- **Resultado del cargue confirmado, en modal**: `Views/Datos/CargueExcel.cshtml` muestra el resultado (`TempData["ResultadoCarga"]`) apenas carga la página, en un SweetAlert2 (`.swal-res-compact`) en vez de solo en el banner/tabla de abajo. Éxito: resumen compacto — chips de empresa/año/modo/escenario, stats grandes de total registros/hojas, y el detalle por hoja (de `ObtenerConteoPorHoja`) en una lista corta; al cerrarlo recarga `CargueExcel` en limpio. Error: mensaje simple, sin recargar (para no perder el detalle por hoja ni el botón "Descargar log"). Los campos estructurados (`NombreEmpresa`/`Anio`/`ModoTexto`/`IdEscenario`/`NotaExtra`) se agregaron a `ResultadoCargaExcel` solo para esto — `DatosController.ConfirmarCargue` los llena; `Mensaje` sigue llevando la frase completa para el banner y `NotificacionesService`.
- **Los widgets "Advertencia tipo plantilla"** (`DashboardTarjetas`, `Tipo=3`/`Subtipo='plantilla'`) que antes se evaluaban post-commit en `CargarExcel` se retiraron de ahí — sus reglas viven ahora dentro de `sp_ValidarCargueStaging`. El Gestor de Widgets sigue existiendo solo para tarjetas de dashboard generales.

### Historial de Versiones de Cargues (rollback)

Every successful Excel upload (ejecución **and** histórico) writes a version row to `HistorialVersionesCargues` plus one `SnapshotsCargues` row per `Ini_` table, inside the same DB transaction as the upload. `HistorialVersionesCarguesService`:
- **Snapshot is atomic**: wrapped in a SQL savepoint (`CrearSnapshotEnTransaccion` → `CrearSnapshotInterno`). If it fails mid-way it rolls back only its own work and the upload continues **without** a history row — never a partial version. `SerializarTabla` no longer swallows errors (a real read failure aborts the whole snapshot; a genuinely empty table serializes to `"[]"`).
- **Snapshot payload is GZip-compressed**: `Comprimir`/`Descomprimir` in the service GZip **UTF-8** bytes; stored in `SnapshotsCargues.DatosGzip` (`VARBINARY(MAX)`), `DatosJson` left `''`. Rollback reads `DatosGzip`, falling back to `DatosJson` for any legacy plain-text row. Requires the column: `ALTER TABLE dbo.SnapshotsCargues ADD DatosGzip VARBINARY(MAX) NULL;`. Note: T-SQL `COMPRESS(DatosJson)` would GZip the UTF-16 bytes of an `nvarchar` — not readable by `Descomprimir` — so never populate `DatosGzip` from SQL.
- **Retention**: `MaxVersionesPorLlaveCargue = 2` (const; renamed from `MaxVersionesPorEscenario` once "Escenario" became a real business concept — see "Escenarios de datos" below — to avoid the name collision). Per **llave de cargue** = (IdEmpresa, Anio, Modo, IdEscenario), only the 2 most recent versions are kept — ranked `EsVersionActual DESC, FechaCargue DESC` so the active version is never purged even after a rollback. `PurgarVersionesAntiguas` runs on every upload, deletes the surplus versions **and their `SnapshotsCargues` children explicitly** (no reliance on `ON DELETE CASCADE`) and sweeps any orphan snapshot rows. There is no time-based purge and no UI maintenance action — changing the const takes effect for each llave de cargue on its next upload.
- **Rollback** (`EjecutarRollback`) aborts (returns false, no changes) if the version is missing any expected `Ini_` snapshot, instead of partially restoring.

### Escenarios de datos

Each empresa can have **more than one parallel copy** of its loaded data — "Escenario 1" (always the
principal) and "Escenario 2" today, parametrizable to N without schema changes. An escenario is a full
parallel copy of the *data* only: the empresa's configuration (años, Ajuste1/Ajuste2, líneas de negocio) is
shared across all its escenarios.

- **Catalog**: `dbo.Escenarios` (`Id TINYINT PK, Nombre, Orden, Activo`) — see `Sql/001_Escenarios_CreateTable.sql`.
  `Models/Escenario.cs`, `Services/EscenarioService.cs` (CRUD; `Desactivar` is always a soft-delete — the Id
  is referenced by FK from the `Ini_*` tables, `HistorialVersionesCargues` and `AuditoriaCargues`, so a row is
  never physically deleted). `Helpers/EscenarioCacheHelper.cs` (5 min `MemoryCache`, same pattern as
  `EmpresaCacheHelper`) + `Filters/EscenariosViewBagFilter.cs` (global filter, registered in `FilterConfig`)
  populate `ViewBag.Escenarios` on every request — no per-controller wiring needed to list them in a
  `<select>`. Super Admin CRUD: `GestorEscenariosController` → `~/Views/Configuracion/GestorEscenarios.cshtml`.
- **Column**: `IdEscenario TINYINT NOT NULL DEFAULT (1)` added to the 9 `Ini_*` tables,
  `HistorialVersionesCargues` and `AuditoriaCargues` (`Sql/002_Ini_AddEscenario.sql`,
  `Sql/003_HistorialVersionesCargues_AddEscenario.sql`, `Sql/004_AuditoriaCargues_AddEscenario.sql`) — all
  additive with a default, so every row loaded before this feature is automatically "Escenario 1". Reads
  filter with `AND ISNULL(IdEscenario,1) = @IdEscenario` (tolerates the column even where a backfill hasn't
  run). `DatosController.RegistrarAuditoria` and `HistorialVersionesCarguesService.CrearSnapshotInterno` try
  the INSERT with the column first and fall back to the column-less INSERT if it doesn't exist yet on that
  DB (same backward-compatible spirit as the "Backward-compatible column reading" pattern above, applied to
  a write).
- **Selector**: independent per screen (Cargue de Excel, Historial de Versiones, Ejecución de Modelos each
  have their own `<select>`, always visible, defaulting to Escenario 1) — it does **not** persist across
  screens or in session.
- **Cargue de Excel** (`DatosController.CargarExcel`): `idEscenarioSeleccionado` (default 1) flows into
  `EliminarEjecucionDeIni`/`EliminarAnosHistoricosDeIni` (so loading one escenario never deletes the other's
  data for the same año), `GuardarEnIni` (writes the `IdEscenario` column, same pattern as `IdEmpresa_Log`),
  the snapshot (`CrearSnapshotEnTransaccion`) and the audit INSERT.
- **Cierre de año** (`ConfiguracionEmpresaController.CerrarAnioEjecucion`) is **intentionally unchanged** —
  its `UPDATE Ini_x SET Historico_Log=1 WHERE IdEmpresa_Log=@e AND Historico_Log=0` doesn't filter by
  escenario, so closing the año correctly flips **all** escenarios of that empresa together (the fiscal
  year is shared; only the data differs).
- **Historial de Versiones / Rollback** (`HistorialVersionesCarguesService`): the retention/version key is
  now `(IdEmpresa, Año, Modo, IdEscenario)`. The 4 duplicated `WHERE` strings were unified into
  `ConstruirWhere(byte modo)`. `HistorialVersiones.IdEscenario` reads back with the same
  try/catch-`IndexOutOfRangeException` pattern used elsewhere for incremental columns.
- **`PlantillaConDatosService`**: `ObtenerAniosConDatos`/`GenerarExcel` take `idEscenario` and filter by it;
  the exported filename includes `_Esc{n}`.
- **Ejecución de Modelos** (`DatosController` + `Views/Datos/Modelo.cshtml`): `EjecutarModelo`,
  `EjecutarModeloAjax`, `EjecutarModeloYEscribirHoja` (shared by `ExportarTodosModelos`/
  `ExportarModeloIndividual`/`ExportarModelosSeleccionados`/`EnviarModelosPorCorreo`) all take `idEscenario`
  (default 1) and pass `@IdEscenario` to the stored procedure. The 8 model SPs
  (`sp_ModeloBalance`, `sp_ModeloPYG`, `sp_ModeloBalancePpto`, `sp_ModeloLineasNegocio`,
  `sp_ModeloTesoreriaPpto`, `sp_ModeloBalanceDiff`, `sp_ModeloFlujoCaja`, `sp_ModeloFlujoEfectivo`) each
  take `@IdEscenario TINYINT = 1`; the ones that read `Ini_*` directly filter by it, and the ones that
  compose other model SPs (`sp_ModeloBalanceDiff`, `sp_ModeloFlujoCaja`, `sp_ModeloFlujoEfectivo`, and
  `sp_ModeloBalancePpto`/`sp_ModeloTesoreriaPpto` for their nested calls) propagate it through every nested
  `EXEC` — see `Sql/README.md` for the full composition graph and the exact mechanical recipe used to edit
  them. Every SP's final `##Final*` table and its terminal `SELECT` return `IdEscenario` as the FIRST
  column (added as a literal `@IdEscenario AS IdEscenario` at the publish point, not threaded through the
  internal calc — low risk); local temp tables that receive another SP's final output via `INSERT ... EXEC`
  or `SELECT * FROM ##Final...` (positional match required) gained `IdEscenario` as their first column too.
  `DatosController` needed no change for this — it reads result-set columns dynamically. **These SPs live
  only in the DB** (not deployable by this app) — see `Sql/StoredProcedures/`.
- **Deliberately out of scope** (see the "Escenarios de datos" plan for the full rationale): the 10
  `*_VT` reporting views / `InformeTablasDatosController` (pending redesign by the user),
  `InformeRelacionamientos`/`TablaPUC`/`VariablesPBI` (global catalogs, don't vary by escenario), and
  `sp_ObtenerAuditoriaCarguesPorEmpresa` (not shared, so `AuditoriaCargues.IdEscenario` is written but not
  yet shown in that grid).

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

**Business knowledge base (Fase A — "entrenar" el agente sin fine-tuning)**: `Codigo = 'CONTEXTO_NEGOCIO_BUFINS'` in `GestorPrompts` holds free-form, Super-Admin-curated context (glossary, business rules, recurring clarifications) that gets prepended to **every** IA prompt — both the auto-summary and question-answering paths, regardless of `modo` — via the `contextoNegocio` parameter now on `IAService.ConsultarAsync()`/`ConstruirPrompt()`. Wired at both call sites that build a fresh conversation: `InformeTablasDatosController.ConsultarConIA()` and `HomeController`'s dashboard AI-insight action (`AnalisisIAController` reuses `ConsultarConIA` from the frontend, so it's covered too). Sent only on the first turn of a conversation (same convention as `[DATOS_FINANCIEROS]`); folded into the cache key so edits invalidate cached answers. No row exists until a Super Admin creates one via the existing `GestorPromptsController` UI — `ObtenerPorCodigo` returns `null` and behavior is unchanged (opt-in, zero risk). This is intentionally **not** real model fine-tuning: OpenAI fine-tuning would need a curated training dataset and a batch retrain/redeploy cycle, wouldn't reflect same-day edits, and risks baking in wrong answers learned from unreviewed traffic — a curated prompt block reviewed by a human stays safer for a multi-company financial system. `AuditoriaAnalisisIA` already logs every question+answer (per user/empresa/tabla) and is the natural source to mine for what to add here (Fase B, human-reviewed, not yet built).

**Available financial tables** (static dictionary in `InformeTablasDatosService`):
`TableBalance_Datos_VT`, `TablePYG_Datos_VT`, `TableEbitda_Datos_VT`, `TableFlujoCaja_Datos_VT`, `TableFlujoTesoreria_Datos_VT`, `TableGasFijosYVar_Datos_VT`, `TableTakeRate_Datos_VT`, `TableIngCosGas_Datos_VT`, `TableIngLineasVenta_Datos_VT`, `TablePYGAjustado_Datos_VT`. Table names are whitelist-validated before use in SQL to prevent injection.

> To switch AI provider, only `IAService.cs` needs to change — update `OpenAIEndpoint`, `OpenAIModel`, and the Authorization header format. The config key is `OpenAIApiKey` in Web.config.

**`ConfiguracionSistemaService`** (`Services/ConfiguracionSistemaService.cs`) — reads key/value pairs from the `ConfiguracionSistema` DB table (`SELECT Valor FROM ConfiguracionSistema WHERE Clave = @Clave`). Used by `InformeTablasDatosController.ConsultarConIA()` to fetch `OpenAIApiKey` at runtime (DB value takes precedence over Web.config). Use this service for any secret or runtime-configurable setting that should be stored in the DB rather than deployed config.

### Global Filter

`EmpresasViewBagFilter` (`Filters/EmpresasViewBagFilter.cs`) is registered globally in `FilterConfig` and loads all companies into `ViewBag.Empresas` for every request.

`LoggingHandleErrorAttribute` (`Filters/LoggingHandleErrorAttribute.cs`) is the other global filter — a `HandleErrorAttribute` subclass that logs the exception to `AppLogger` before letting `customErrors` render the error page. Registered in `FilterConfig` in place of the stock `HandleErrorAttribute`.

### Logging (`AppLogger`)

`Helpers/AppLogger.cs` — dependency-free app logger. Writes one line per event to `App_Data/logs/app-yyyyMMdd.log` (daily rotation, 30-day retention), never throws (falls back to `Trace`). API: `AppLogger.Info/Warn/Error(mensaje, ...)` and `AppLogger.Error(Exception, contexto)`. Each line carries timestamp, level, request ip/user/url, optional `ctx=`, message, and the full exception chain. Wired into `Global.asax.Application_Error` (safety net) and `LoggingHandleErrorAttribute` (MVC pipeline). Use it in any `catch` where today the code only does `SetErrorMessage(ex.Message)` or swallows silently.

### Auditoría centralizada (`AuditoriaService`)

Tabla **única** `Auditoria` para TODA auditoría del sistema, diferenciada por columna `Tipo`. Ver el CREATE + índices en el encabezado de `Services/AuditoriaService.cs`. Modelos y constantes en `Models/AuditoriaModels.cs` (`RegistroAuditoria`, `AuditoriaTipo`, `AuditoriaAccion`, `AuditoriaFiltro`, `AuditoriaResultado`).

- **Escribir**: `new AuditoriaService().RegistrarCambio(AuditoriaTipo.X, AuditoriaAccion.Y, entidad, entidadId, descripcion, valorAnterior, valorNuevo, idEmpresa)` — serializa `valor*` a JSON. Para seguridad: `RegistrarSeguridad(accion, descripcion, idUsuario, nombreUsuario, idEmpresa)` — en el login se pasa la empresa del usuario explícitamente (aún no hay sesión), y `AccesoController.Login` la lee con la columna `IdEmpresa` del `SELECT` de credenciales; esto es lo que permite el alcance por empresa del visor. `Registrar(RegistroAuditoria)` autocompleta fecha/usuario/IP/user-agent desde `HttpContext`. **Nunca lanza** (cae a `Trace`), así que se llama después de la operación exitosa sin envolver en try/catch.
- **Ya instrumentado**: `EmpresaController` (crear/editar/eliminar), `GruposEmpresarialesController` (crear/editar/eliminar/asignar-empresas), `MenuOpcionesController` (crear/editar/eliminar), `PermisosController.Guardar`, `ModelosEjecucionController` (crear/editar/eliminar), `UsuarioController` (crear/editar/eliminar), `ConfiguracionEmpresaController` (guardar config básica / cierre de año / tablas resumen IA), `DatosController.EnviarModelosPorCorreo` (`AuditoriaTipo.Modelos` + `AuditoriaAccion.Enviar`, severidad Advertencia — datos que salen del sistema), `DatosController.EjecutarModeloAjax` (`AuditoriaTipo.Modelos` + `AuditoriaAccion.Ejecutar`, solo en éxito), `DatosController.ExportarTodosModelos`/`ExportarModelosSeleccionados` (`AuditoriaTipo.Modelos` + `AuditoriaAccion.Exportar`, severidad Advertencia — datos que salen del sistema), `DatosController.DescargarPlantillaConDatos` (`AuditoriaTipo.Cargues` + `AuditoriaAccion.Exportar`, severidad Advertencia — descarga de la plantilla BUFINS rellena con los datos actuales), `DatosController.ConfirmarCargue`/`DescartarCargue` (`AuditoriaTipo.Cargues` + `AuditoriaAccion.Confirmar`/`Descartar` — ver "Validación en dos pasos del cargue de Excel"; `Descartar` con severidad Advertencia), `GestorEscenariosController` (crear/editar/desactivar, `AuditoriaTipo.Escenarios`), `AccesoController.Login` (éxito, fallido, bloqueo — esto es B3). Para auditar algo nuevo: elegir/crear código en `AuditoriaTipo` y añadir una línea `RegistrarCambio(...)` tras el éxito.
- **Legibilidad (fase 2/3)** — columnas añadidas a `Auditoria`: `EntidadNombre NVARCHAR(200)`, `Severidad NVARCHAR(20)`, `OperacionId UNIQUEIDENTIFIER` (`ALTER TABLE` + índices `IX_Auditoria_Entidad (Entidad, EntidadId, Fecha DESC)` e `IX_Auditoria_Operacion (OperacionId)`). Se **autocompletan** en `AuditoriaService.Registrar` sin tocar los call-sites: `Severidad` = `AuditoriaSeveridad.Derivar(tipo, accion)` (Eliminar/Bloqueo → Critico; Crear/Asignar/LoginFallido y cualquier cambio de PERMISOS → Advertencia; resto → Info); `EntidadNombre` = primer campo legible del JSON (`Nombre`/`EmpNombre`/`Titulo`/…); `OperacionId` = un GUID por petición (`HttpContext.Items["_auditoria_op"]`) → agrupa los N cambios de una misma acción. `RegistrarCambio` acepta además `entidadNombre` y `severidad` opcionales para forzarlos. `AuditoriaFiltro` soporta `EntidadId`, `Severidad`, `OperacionId`. **Compatibilidad**: `AuditoriaService.TieneColumnasFase23()` (cacheado) detecta si la migración se ejecutó; si no, escribe/lee sin esas columnas — nunca se pierde una fila.
- **Visor**: `AuditoriaController` + `~/Views/Informes/Auditoria.cshtml`. `[ValidarSesion]`, sin gate por rol — `PuedeAcceder()` solo exige sesión válida; quién ve la opción en el menú/sidebar se controla por permiso/menú, no en este controlador. **UX para usuario final** (rediseño): el panel abre con un conmutador **«Cambios del sistema / Inicios de sesión»** — «cambios» manda `excluirTipo=SEGURIDAD`, «login» manda `tipo=SEGURIDAD` (nuevo `AuditoriaFiltro.ExcluirTipo` → `AND a.Tipo <> @ExcluirTipo`). El filtro **«Tipo» pasó a «Área»** con etiquetas de negocio (mapa `Audit_AreasMap` en resx, `value` = código crudo); **«Acción» → «Qué ocurrió»** (opciones que cambian según el modo, `value` crudo); **nuevo filtro «Usuario»** (`#fUsuario` → `idUsuario` → `AuditoriaFiltro.IdUsuario`; opciones desde `AuditoriaService.ObtenerUsuariosParaFiltro(idsEmpresaPermitidas)` — usuarios que aparecen en `Auditoria`, acotados a la empresa/grupo para Admin de Empresa, `ViewBag.Usuarios`; no se renderiza para Usuario Normal, que solo puede ver su propia auditoría); **fechas → cápsula de período** `.aud-periodo-cap` (`#fPeriodo`): etiqueta + chips `.aud-date-chip` (`Hoy / 7 d / 30 d / Mes`, activo = clase `.on`) + botón `custom` (revela `#wrapFechas` con `#fDesde`/`#fHasta` dentro de la cápsula) + segmento **rango calculado** a la derecha (`#audRango` → `pintarRango()` muestra «10 ago – 8 sep» + badge de días con `Audit_Dia`/`Audit_Dias`; `aplicarPeriodoUI()` lo refresca en cada cambio, init y Limpiar). `rangoPeriodo()` calcula `desde/hasta` según `data-range`, default 30 días. Layout de posición fija: la grilla de selectores, el buscador (`.af-buscar-row` → `.af-buscar`, ancho hasta 620 px) y la cápsula (`.af-periodo-row`) van **cada uno en su propia fila** (nada de flex-wrap que salte según el ancho); todos comparten altura `--f-h` con los `.form-control`; solo <768 px la cápsula pasa a ancho completo y apila sus segmentos; **«Severidad» → «Relevancia»** (Alta/Media/Normal = Critico/Advertencia/Info) plegada en «Filtros avanzados»; se **eliminó el campo libre «Entidad»** (el «historial de un registro» sigue por el botón 🕘 de cada fila). Todos los controles del panel viven en una sola grilla responsiva (`.af-grid`, clase `.aud-filtros` reduce su tamaño) + el conmutador de modo en la barra del título (`.af-titlebar`). Tabla de **5 columnas**: `Cuándo` (tiempo relativo + fecha exacta), `Actividad` (punto de relevancia + verbo `Audit_AccFraseMap` + chip de área + descripción del sistema), `Usuario`, `Empresa`, ojo. Modal de detalle: encabezado como frase, bloque **«Qué cambió»** con nombres de campo traducidos (`Audit_CamposMap`); para filas `UsuarioMenuPermisos`, `AuditoriaController.ResolverPermisos()` resuelve `idUsuario`→nombre y `permisos[]`→nombres de `MenuOpciones`; JSON crudo detrás de «Ver datos técnicos». Diff campo a campo (`campo: antes → después`) para ediciones; botón «Ver todo lo que cambió en esta acción» (filtra por `OperacionId`); conmutador **Tabla / Línea de tiempo**. **Alcance por rol**: Super Admin ve todo, todas las empresas; **Admin de Empresa** ve la misma experiencia completa (todos los `Tipo`, incluye ejecuciones de modelos/cargues/cambios/login) pero acotada a su empresa o su mismo grupo empresarial; **Usuario Normal** también tiene la experiencia completa (todos los `Tipo`) pero acotada a **sus propias filas** — `AplicarAlcance()` fuerza `AuditoriaFiltro.IdUsuario = usuario.Id` (ignora cualquier `idUsuario` del cliente) y no filtra por empresa. Para Admin de Empresa, `AplicarAlcance()` solo setea `AuditoriaFiltro.IdsEmpresaPermitidas`, sin forzar `Tipo`; `Detalle` revalida por fila contra el alcance de cada rol (empresa permitida, o `IdUsuario` propio). El conmutador de modo, «Área» y «Relevancia» se renderizan igual para los tres roles; el filtro «Usuario» no se renderiza para Usuario Normal; lo único exclusivo de Super Admin es el botón `Limpiar`. **No auto-carga**: la grilla solo se llena al pulsar «Consultar». **Paginación server-side** (`Consultar` → `{ items, total, pagina, totalPaginas }` con `OFFSET/FETCH`; la vista muestra «Mostrando X–Y de N»). Detalle en modal `Swal` con JSON coloreado + botón Copiar; export Excel + Imprimir/PDF. **`Limpiar` (Super Admin)**: botón + modal `Swal` idéntico al de Auditoría IA (todo / conservar N meses) más un selector de alcance **todas las empresas / una sola**; `AuditoriaService.Limpiar(mesesConservar, idEmpresa)` hace el `DELETE` acotado; notificación `Notif_AuditoriaLimpiada`.
- Las tablas `AuditoriaCargues` y `AuditoriaAnalisisIA` (con sus servicios/vistas propias) **siguen separadas por ahora**; la idea es migrarlas a `Auditoria` como `Tipo` más adelante.

### Informe de navegación (páginas visitadas)

Telemetría de "qué páginas abre cada usuario". **Tabla física separada de `Auditoria` por diseño** (volumen ~10-100× mayor, forma distinta, retención corta): unificar la *experiencia* en el módulo de auditoría, no la tabla.

- **Tabla `dbo.AuditoriaNavegacion`** (CREATE + índices + INSERT de la opción de menú en la cabecera de `Services/AuditoriaNavegacionService.cs`). Columnas mínimas: fecha, usuario (id/nombre instantáneo/empresa/rol), controller/action, `CodigoMenu`+`TituloPagina` (resueltos contra `MenuOpciones`), IP, user-agent. **No** guarda URL ni query string.
- **`Filters/RegistroNavegacionFilter.cs`** — filtro global (registrado en `FilterConfig`). Solo registra un **GET no-AJAX que devuelve `ViewResult`** (página real; descarta JSON/parciales/redirects/descargas sin listar nada) de un usuario autenticado. Excluye `Error/*` y **`Home/Index`** (el dashboard es la página de aterrizaje → puro ruido). Dedup en memoria por `(usuario, controller/action)` durante 15 s (evita refrescos/dobles clic). La escritura va en `HostingEnvironment.QueueBackgroundWorkItem` → **latencia cero** para la petición; el nombre del menú se resuelve ahí vía `Helpers/MenuRutaCacheHelper` (mapa `controller/action → (codigo, titulo)`, `MemoryCache` 10 min).
- **`AuditoriaNavegacionService`** — `Registrar` (nunca lanza) + **purga "por debajo"**: `PurgarSiToca()` corre 1 vez cada 24 h dentro de la misma llamada en segundo plano, en lotes `DELETE TOP (5000)` acotados (`NavegacionRetencionDias`, def. 90). **No hay job de SQL Agent.** Lectura: `Consultar` (detalle paginado), `ResumenPorUsuario`, `ResumenPorPagina`, `ConsultarParaExport`.
- **`InformeNavegacionController`** (`[ValidarSesion]`, gate inline `EsSuperAdmin() || EsAdminEmpresa()` igual que `AuditoriaController`; no-Super recortado a su empresa/grupo vía `EmpresaAccesoHelper`). Vista `~/Views/Informes/InformeNavegacion.cshtml` con pestañas Detalle / Resumen por usuario / Resumen por página + export Excel. Permiso/menú: `INFORMES_NAVEGACION`.
- **Restricciones a respetar en todas las fases**: información básica, tabla que no crezca mucho, cero impacto de rendimiento (todo lo pesado en segundo plano), purga oportunista (no job).

### Módulo unificado de auditoría (hub)

`AuditoriaHubController` + `~/Views/Informes/AuditoriaHub.cshtml`: **una sola opción de menú** (`INFORMES_AUDITORIA`) con pestañas **Cambios / Navegación / Cargues / IA**. Cada pestaña se carga **en línea (AJAX, `$.load()`)** dentro del mismo DOM del hub — **no hay iframe**. La vista de cada pestaña se pide con `?embed=1` y se renderiza con un layout de fragmento.

- **`~/Views/Shared/_LayoutFragment.cshtml`** — dos líneas: `@RenderBody()` + `@RenderSection("scripts", required:false)`. Sin `<html>`/`<head>`/bundles. `$.load(url)` (sin selector de fragmento) inyecta el HTML y **ejecuta los `<script>`**; jQuery / SweetAlert / el helper CSRF ya están en la página anfitriona (`_Layout`).
- Cada una de las 4 vistas: `@{ if ((bool)(ViewBag.Embed ?? false)) { Layout = "~/Views/Shared/_LayoutFragment.cshtml"; } }`; su controlador hace `ViewBag.Embed = string.Equals(Request.QueryString["embed"], "1")`. Sin `embed=1` funcionan igual que siempre (layout completo).
- Solo **una** pestaña vive en el DOM a la vez (`$.load` reemplaza `#audhubPanel`), así que **no hay colisión de estilos/JS** entre vistas aunque compartan nombres de clase.
- El hub oculta `.powerbi-header` del panel (muestra el título arriba, dinámico con el `data-title` de la pestaña) y neutraliza el doble `padding` del `.container-fluid.px-4` anidado. Ventajas vs. iframe: una sola URL y un solo scroll, modales centrados en la ventana real, exports/print correctos, y desaparece el problema de altura.
- `AuditoriaConsultasIA` es la única con un botón propio en su cabecera (Limpiar): cuando `embed`, lo renderiza en una barra compacta aparte (la cabecera va envuelta en `@if (!embed)`).
- Cada pestaña aplica su **propio alcance por rol/empresa** (el hub no filtra nada, solo hospeda). Alcance para **Admin de Empresa** (no Super): datos siempre acotados a `EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas()` (grupo-aware) en las 4 pestañas — los 4 servicios tratan lista vacía como "no ve nada" (`AND 1=0`), no como "ve todo". El **filtro de empresa** (dropdown) se muestra a Admin de Empresa cuyo grupo tenga >1 empresa, acotado a ese grupo, en las 4 pestañas (Cambios y Cargues ya lo hacían; Navegación e IA se ajustaron para lo mismo). En **Cambios** el Admin de Empresa ve todos los `Tipo` (igual que Super Admin), solo acotado por empresa/grupo — la única excepción que sigue siendo exclusiva de Super Admin ahí es el botón `Limpiar`.
- **Gate del hub**: `AuditoriaHubController` solo exige sesión válida (`UsuarioActual != null`) — no filtra por rol; quién ve la opción `INFORMES_AUDITORIA` en el sidebar se controla por permiso/menú, no aquí. **Usuario Normal**: `ViewBag.EsUsuarioNormal` oculta las pestañas Navegación/Cargues/IA (`.audhub-seg` pasa a una sola columna, clase `.solo1`) porque esos 3 controladores siguen exigiendo `EsSuperAdmin() || EsAdminEmpresa()` sin cambios; solo la pestaña **Cambios** (`AuditoriaController`) queda visible y acota los datos a la propia auditoría del usuario (ver más arriba).
- **Migración de menú**: las 4 opciones de menú antiguas se desactivan (`Activo = 0`) y se sustituyen por `INFORMES_AUDITORIA` → `AuditoriaHub/Index`. `InformeNavegacionController`, `AuditoriaCarguesController` y `AuditoriaConsultasIAController` siguen gateando por **rol** (`EsSuperAdmin() || EsAdminEmpresa()`), no por código de permiso, así que desactivar sus `MenuOpciones` no afecta el acceso — solo la visibilidad en el sidebar (deseado).

### Error Pages

`ErrorController` (`Controllers/ErrorController.cs`, no `[ValidarSesion]`) renders branded, session-independent pages: `Index` (500), `NotFound` (404), `Forbidden` (403) — each sets `Response.StatusCode` + `TrySkipIisCustomErrors`. Views in `Views/Error/` use `Views/Error/_ErrorLayout.cshtml` (`Layout = null`, self-contained dark-purple + brand-gradient card, strings from `Err_*` resx keys). `Views/Shared/Error.cshtml` (used by `LoggingHandleErrorAttribute`) shares the same layout. Routing in: `Web.config` `<customErrors defaultRedirect="~/Error" redirectMode="ResponseRewrite">` with `<error>` for 403/404, plus `<httpErrors existingResponse="Auto">` in `system.webServer` for errors that never reach MVC.

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

- Passwords: Always `.Trim()` before hashing. Use `HashearContrasena()` (BCrypt, work factor 12) for new passwords. `ConvertirSha256()` is kept only for the SHA256→BCrypt migration path in login — do NOT use it for new code. `VerificarContrasena()` handles both formats transparently. **Strength policy is centralized in `Helpers/PoliticaContrasena.cs`** — `Validar(clave, usuario, out errorKey, comprobarFiltracion)` returns a resx *key* (caller does `R(errorKey)`): min 12 chars, complexity, no 3× repeats, not a common password, must not contain the username, and (unless `HibpCheckEnabled=false` in Web.config) not present in Have I Been Pwned's Pwned Passwords (k-anonymity range API, fail-open on any network error). Used by `AccesoController` (Registrar / RestablecerClave) and `UsuarioController.EsClaveSegura` (thin wrapper). The login POST never calls it.
- Client IP: use `Helpers/ClientIpHelper.ObtenerIp()` (or the `HttpRequestBase` overload) — not `Request.UserHostAddress` directly. It honours `X-Forwarded-For` / `CF-Connecting-IP` **only** when the direct connection comes from an IP/CIDR listed in `appSettings["TrustedProxies"]` (empty by default ⇒ identical to `UserHostAddress`); `TrustCloudflareHeader` gates the CF header. Wired into `AccesoController` rate-limiting/audit, `AuditoriaService`, `AppLogger`.
- Login timing: the login POST always runs one BCrypt verify — against `HASH_SENUELO` when the identifier doesn't exist — so "unknown user" and "wrong password" take the same time (no user enumeration by timing).
- Login per-IP rate limit: **`Services/RateLimitLoginService.cs`** (`Comprobar` / `RegistrarFallo` / `Limpiar`), state in `dbo.IntentosLoginIP` (create-table script in the file header) so it survives app-pool recycles and works multi-node, with an in-memory `HttpRuntime.Cache` fallback if the table is missing / DB errors. Threshold `LoginRateLimit_Umbral` (default 8) fails → IP blocked 15 min. A system-wide spike (`SUM(Intentos)` in the last 10 min ≥ `LoginRateLimit_UmbralGlobal`, default 100) flips **defensive mode**: per-IP threshold drops to 3 and Super Admins get a notification (≤1×/30 min). `AccesoController` no longer keeps its own `_rl_` cache helpers.
- Security-audit noise: failed logins against a *nonexistent* identifier are logged to `AppLogger` on every hit but written to the `Auditoria` table at most once per IP per `AUDIT_THROTTLE_MIN` (10) min; `AuditoriaService.PurgarSeguridadAnonimaAntigua(dias)` (called opportunistically from the login path, ≤1×/day) deletes anonymous `SEGURIDAD` rows older than 90 days. A *real* account getting locked still writes every event **and** fires internal notifications (see notifications table).
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
- **Auditoría — PREGUNTAR/SUGERIR SIEMPRE:** Al crear o modificar cualquier acción que cambie estado persistente (crear/editar/eliminar entidades, guardar configuración, asignar permisos/empresas, cierres, rollbacks, ejecuciones, eventos de seguridad, etc.), sugerir explícitamente al usuario registrarla en la auditoría central antes de cerrar el task. Si acepta: añadir una línea `new AuditoriaService().RegistrarCambio(AuditoriaTipo.X, AuditoriaAccion.Y, entidad, entidadId, descripcion, valorAnterior, valorNuevo, idEmpresa)` tras la operación exitosa (o `RegistrarSeguridad(...)` para login/bloqueos), crear el código en `AuditoriaTipo` si hace falta, y actualizar la lista "Ya instrumentado" en la sección "Auditoría centralizada". `AuditoriaService` nunca lanza, así que no se envuelve en try/catch.
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
- **DB table:** `Notificaciones` (`Id`, `IdUsuario`, `Titulo`, `Mensaje`, `Tipo`, `Leida`, `FechaCreacion`, `Url`). Auto-cleanup: rows older than 30 days are deleted on insert. `Url` (nullable, D4) is the deep-link opened when the notification is clicked — `NotificacionesService.Crear(idUsuario, titulo, mensaje, tipo, url)`. Pass a relative path like `"/Datos/CargueExcel"`. Requires `ALTER TABLE Notificaciones ADD Url NVARCHAR(300) NULL;` (the reader is backward-compatible if the column is missing, but `ObtenerRecientes` selects it explicitly).
- **Service:** `NotificacionesService` (`Services/NotificacionesService.cs`) — `Crear(idUsuario, titulo, mensaje, tipo)`. Swallows all exceptions so it never breaks the main flow.
- **Controller:** `NotificacionesController` (`Controllers/NotificacionesController.cs`) — endpoints `GET /Notificaciones/Recientes`, `POST /Notificaciones/MarcarLeida`, `POST /Notificaciones/MarcarTodasLeidas`, `POST /Notificaciones/Eliminar`.
- **Frontend:** Bell icon with badge in `_Layout.cshtml`. Polls `GET /Notificaciones/Recientes` every 30 s, **paused while the tab is hidden** (`document.hidden`), with an immediate refresh on `visibilitychange` back to visible (D4). Clicking a notification marks it read and, if it has a `Url`, navigates there. JS functions: `notifPushLocal(tipo, titulo, mensaje)` for transient client-side alerts (no DB). Also in `_Layout`: a **silent session keepalive** (D5) — user activity fires `POST /Acceso/ExtenderSesion` at most every 5 min with no UI, so an active user never hits the server-side timeout.

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
| Models sent by email (success) | `success` | `DatosController` | `Notif_ModelosEnviadosCorreo` |
| Models sent by email (error) | `error` | `DatosController` | `Notif_ErrorEnvioCorreoModelos` |
| Template with data downloaded (BUFINS template pre-filled with current DB data) | `success` | `DatosController` | `Notif_PlantillaConDatosDescargada` |
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
| Real account locked after failed logins (notifies the locked user) | `warning` | `AccesoController` | `Notif_CuentaBloqueadaTitulo` |
| Real account locked after failed logins (notifies every Super Admin) | `error` | `AccesoController` | `Notif_CuentaBloqueadaAdminTitulo` |
| Login rate-limit entered defensive mode — global spike (notifies every Super Admin in-app, ≤1×/30 min; also emails `appSettings["SeguridadAlertasDestino"]` via `EmailService.EnviarAlertaSeguridad`, queued background) | `error` | `AccesoController` | `Notif_ModoDefensivoTitulo` |
| Version rollback success | `success` | `HistorialVersionesCarguesController` | `Notif_RollbackEjecutado` |
| Version rollback error | `error` | `HistorialVersionesCarguesController` | `Notif_ErrorRollback` |
| AI query audit log cleared (full or keep-last-N-months) | `warning` | `AuditoriaConsultasIAController` | `Notif_AuditoriaLimpiada` |
| General audit log cleared (full / keep-N-months, all or one company) | `warning` | `AuditoriaController` | `Notif_AuditoriaLimpiada` |

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
