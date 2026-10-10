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

Check with: `EsUsuarioNormal()`, `EsAdminEmpresa()`, `EsSuperAdmin()`

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
- **`SoloSuperAdminAttribute`** (`Permisos/SoloSuperAdminAttribute.cs`) - Same shape as `RequierePermiso` but gates on `EsSuperAdmin()`. Applied **at class level** (next to `[ValidarSesion]`) on the controllers that are 100% Super Admin: `MenuOpcionesController`, `ModelosEjecucionController`, `GestorEscenariosController`, `GestorPromptsController`, `GestorCategoriasController`, `GestorGruposController`, `ConfiguracionGlobalIAController` — their inline `if (!EsSuperAdmin()) return RedirectToAction("Index","Home")` guards were removed. Controllers with mixed gates (Admin de Empresa allowed, or empresa/grupo access — `EmpresaController`, `PermisosController`, `DatosController`, `ConfiguracionEmpresaController`, the Informe* controllers) keep inline checks **by design**, not as debt.
- **`UsuarioSesionHelper.TienePermiso("CODE")`** - Checks permissions using a cached HashSet in session (one DB query per session, not per page load). Super Admin (Admin=2) always returns true.
- **`UsuarioSesionHelper.ObtenerMenuSidebar()`** - Returns cached `List<SidebarCategoriaViewModel>` for dynamic sidebar rendering via `_SidebarMenu.cshtml` partial.
- **`UsuarioSesionHelper.InvalidarCachePermisos()`** - Clears permission/menu cache. Called after saving permissions or on login.

Known permission codes (BD codes, used in sidebar and controllers):
- `DATOS_PLANTILLA_CARGUE`, `DATOS_MODELO_EJECUCION` (Datos)
- `INFORMES_REPORTES_PBI`, `INFORMES_AUDITORIA_CARGUES`, `INFORMES_TABLAS_DATOS`, `INFORMES_RELACIONAMIENTOS`, `INFORMES_AUDITORIA_GENERAL`, `INFORMES_PYG_GERENCIAL`, `INFORMES_BALANCE_GERENCIAL` (Informes — `INFORMES_AUDITORIA_GENERAL` is SoloSuperAdmin, visor de la tabla `Auditoria`; `INFORMES_PYG_GERENCIAL` → `InformePYGController` and `INFORMES_BALANCE_GERENCIAL` → `InformeBalanceController`, neither SoloSuperAdmin/SoloAdminEmpresa, both still pending creation as a row in `MenuOpciones` vía `/MenuOpciones`)
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
| InformeBalanceController | `~/Views/Informes/InformeBalance.cshtml` |
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
| ConfiguracionVariablesPBIController | `~/Views/Configuracion/ConfiguracionVariablesPBI.cshtml` |
| PermisosController | `~/Views/Permisos/Gestionar.cshtml` |
| ErrorController | `~/Views/Error/*` (convention — see "Error Pages" below) |

When creating new controllers, use explicit view paths with `~/Views/{area}/{view}.cshtml`. (`ErrorController` is the deliberate exception: it uses convention-based `Views/Error/` so the pages stay minimal and independent.)

### Key Controllers

- **AccesoController** - Login, registration, session management (no `[ValidarSesion]`)
- **HomeController** - Página principal (`[ValidarSesion]` at class level). Ver "Página principal (Home)" más abajo. Endpoints: `Index`, `ObtenerNoticias`, `ObtenerAlertas`, `ObtenerAccesosRapidos`
- **UsuarioController** - User CRUD, profile image upload (no `[ValidarSesion]`, manual checks)
- **EmpresaController** - Company management (no `[ValidarSesion]`, manual checks). Campo opcional **Página web** (`Empresas.PaginaWeb` ↔ columna `Empresas.EmpPaginaWeb`, `Sql/019_Empresas_PaginaWeb.sql`): se valida y normaliza en `Helpers/UrlWebHelper.TryNormalizar` (solo http/https, dominio con punto y TLD ≥2, sin espacios/comillas/`<>`/`\`/usuario:clave; sin esquema → `https://`), con la misma regla en el navegador (`data-url-web` en `Empresas.cshtml`). Como `sp_ObtenerEmpresas`/`sp_RegistrarEmpresa`/`sp_EditarEmpresa` no conocen la columna, `EmpresaService` la lee con `AsignarPaginaWeb` y la escribe con un `UPDATE` aparte (`GuardarPaginaWeb`); si la columna no existe el gestor avisa (`Emp_PaginaWebSinColumna`) en vez de dar el dato por guardado. `CrearEmpresa` ahora también rellena `empresa.Id` (lo busca por NIT+nombre) para que la auditoría registre el Id
- **PermisosController** - Menu option assignment UI for users, accessible by Admin 1 (own company only) and Admin 2 (`[ValidarSesion]`)
- **MenuOpcionesController** - CRUD for menu options, Super Admin only (`[ValidarSesion]`)
- **ConfiguracionEmpresaController** - Financial configuration per company (`[ValidarSesion]`)
- **ConfiguracionRelacionamientoController** - Relationship configuration with Excel upload (`[ValidarSesion]`)
- **ReportesController** - Power BI report embedding and report CRUD (no `[ValidarSesion]`, manual checks)
- **DatosController** - Excel data import/export (`[ValidarSesion]`). `CargarExcel` only stages+validates (see "Validación en dos pasos del cargue de Excel"); `ConfirmarCargue`/`DescartarCargue`/`RevisarCargue` complete the flow
- **InformeTablasDatosController** - Data tables report with AI analysis via OpenAI (`[ValidarSesion]`). Endpoints: `InformeTablasDatos` (view), `ObtenerAnios`, `ObtenerVariables`, `ConsultarDatos`, `ConsultarConIA` (async), `ExportarExcel`
- **InformeRelacionamientosController** - Relationships report (`[ValidarSesion]`)
- **InformePYGController** - Estado de Resultados (PYG) gerencial: Real vs Presupuesto, mes + acumulado del año, KPIs, tendencia e insights de IA (`[ValidarSesion]`). Lee de `dbo.ModeloPYG` (+ `JOIN dbo.Rel_PYG` para el flag de subtotal) vía `InformePYGService` — no ejecuta `sp_ModeloPYG` en vivo. Endpoints: `Index` (view), `ObtenerMeses`, `ConsultarReporte`, `GenerarInsightsIA` (async, reutiliza el patrón de cupo/prompts/auditoría de `IAGateway` con el código `RESUMEN_PYG_GERENCIAL` en `GestorPrompts`), `ExportarExcel` — los 3 últimos reciben el mismo `FiltrosPYG`. Ver "Estado de Resultados (PYG) Gerencial" más abajo
- **InformeBalanceController** - Balance General gerencial: saldo al mes de corte (foto, no acumula meses), comparativo (cierre año anterior/mes anterior/mismo mes año anterior), chequeo de cuadre, KPIs de liquidez/solvencia, estructura y tendencia, insights de IA (`[ValidarSesion]`). Lee de `dbo.ModeloBalance` (+ `JOIN dbo.REL_Balance` para el flag de subtotal) vía `InformeBalanceService` — no ejecuta `sp_ModeloBalance` en vivo. Mismo esqueleto de endpoints que `InformePYGController` (`Index`, `ObtenerMeses`, `ConsultarReporte`, `GenerarInsightsIA` con código `RESUMEN_BALANCE_GERENCIAL`, `ExportarExcel`), recibiendo `FiltrosBalance`. Ver "Balance General Gerencial" más abajo
- **ModelosEjecucionController** - Model execution management/configuration (`[ValidarSesion]`)
- **GestorEscenariosController** - CRUD for the `Escenarios` data-scenario catalog, Super Admin only (`[ValidarSesion][SoloSuperAdmin]`). `Eliminar`-equivalent (`Desactivar`) is always a soft-delete. See "Escenarios de datos"
- **GestorPromptsController** - IA prompt CRUD, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete)
- **GruposEmpresarialesController** - Business group (holding) CRUD and company assignment, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete), `Gestionar` (assign-companies screen), `GuardarEmpresas` (AJAX). See "Multi-Company Group Access" above.
- **AnalisisIAController** - Standalone AI analysis page; reuses `InformeTablasDatosService` for table/empresa lists; company-scoped for non-Super Admin (`[ValidarSesion]`)
- **AuditoriaCarguesController** - Upload audit trail (no `[ValidarSesion]`, manual checks)
- **InformeNavegacionController** - Página-visitas por usuario (`[ValidarSesion]`, gate `EsSuperAdmin() || EsAdminEmpresa()`). Endpoints: `Index`, `Detalle`, `ResumenUsuarios`, `ResumenPaginas`, `ExportarExcel`. Ver "Informe de navegación (páginas visitadas)"
- **AuditoriaHubController** - Módulo unificado de auditoría, `Index` renderiza `~/Views/Informes/AuditoriaHub.cshtml`: una sola pantalla con pestañas **Cambios / Navegación / Cargues / IA**, cada una cargada en línea con `$.load()` (`?embed=1`, layout `_LayoutFragment`). `[ValidarSesion]` + gate `EsSuperAdmin() || EsAdminEmpresa()`. Ver "Módulo unificado de auditoría (hub)"

### Página principal (Home)

`Views/Home/Index.cshtml` — sin modelo; cada bloque se llena por AJAX para que el Home cargue rápido. **Diseño "2a" (handoff de diseño, pensado para verse completo sin scroll en 14″ ≈ 1280×800)**: `.dash-wrapper` es una columna flex de alto `calc(100vh - 70px - 2.5rem)` (el `<hr>` del layout se oculta) con (1) cabecera compacta (saludo + fecha a la izquierda, chips de empresas/grupo a la derecha, logo 24 px) y (2) `.dash-main`, un grid `minmax(0,1fr) 300px`: **columna izquierda** = tarjeta Indicadores y noticias (franja TRM USD/EUR/fecha/fuente + fila "Noticias financieras" con chips de filtro por portal + lista con scroll interno); **columna derecha** = Alertas Bufins → Accesos rápidos → Mi consumo de IA (solo Super Admin / Admin de Empresa). Bajo 992 px pasa a una columna con scroll de página. Cada tarjeta se colapsa por separado (`.dash-section-toggle`). Los widgets configurables (KPI/gráficos/advertencias de `DashboardTarjetas`) y su gestor **ya no existen**.

- **Filtro por portal de noticias** — solo en cliente: `_feeds` guarda `feeds` de `ObtenerNoticias`; chip "Todas" + un chip por feed (etiqueta sin `es.`/`.co`/`.com`, contador, color del feed); clic filtra, clic en el activo o en "Todas" vuelve a todas; se recuerda en `sessionStorage["bufins_noticias_filtro"]` (por nombre de feed) y sobrevive al botón de refrescar.
- **Alertas Bufins** — `HomeController.ObtenerAlertas` → `AlertasHomeService.Obtener(idsEmpresas)` → `dbo.sp_ObtenerAlertasHome` (`Sql/StoredProcedures/sp_ObtenerAlertasHome.sql`; vive solo en la BD). Mismo esquema de hallazgos que `sp_ValidarCargueStaging` (`Severidad` Error/Advertencia/Info, `CodigoRegla`, `Titulo`/`TituloEn`, `Mensaje`/`MensajeEn`) pero acumulado en `#Alertas` y por empresa en vez de por lote; **solo lectura**. Recibe `@IdsEmpresas` (CSV, `NULL` = todas) que C# arma con el alcance del usuario (Super Admin = todas; el resto `EmpresaAccesoHelper.ObtenerIdsEmpresasPermitidas`, grupo-aware). Devuelve 2 result sets: cierre (`CodMessage`/`ErrorMessage`/totales) y las alertas. **Agregar una alerta = un BLOQUE más en el SP (plantilla comentada dentro), sin tocar C#.** La vista las pinta con las tarjetas `.dash-adv-*` (verde si no hay ninguna → sección colapsada; badge con el conteo si hay). Textos del título/mensaje salen del SP (es/en); el resto de textos de la sección en `Home_Alertas_*`.
- **Accesos rápidos** — `HomeController.ObtenerAccesosRapidos`: top de páginas del usuario (últimos 60 días, `AuditoriaNavegacionService.ResumenPorPagina` filtrado por `IdUsuario`) **cruzado con su propio sidebar** (`UsuarioSesionHelper.ObtenerMenuSidebar()`), así nunca muestra algo sin permiso. Máx. 8; con menos de 4 se completa con las opciones `EsDestacado` de su menú (nota `Home_Accesos_Sugeridos`). Sin historial ni destacados → `Home_Accesos_Vacio`. `Home/Index` no se registra en la navegación, por lo que no se auto-refuerza.
- **Mi consumo de IA** — tarjeta compacta que reutiliza `AnalisisIAController.MiConsumo` (mismo control de acceso y datos que la tarjeta de Análisis IA) y las claves `IAConsumo_*`; selector de empresa si el usuario ve más de una; barra ámbar ≥80 %, roja al agotar; visible solo con `IAModuloHelper.Visible()`.
- **Retirado**: el resumen IA del Home (`ObtenerResumenIA`/`ObtenerTablasConfiguradasIA`) ya no tenía UI y se eliminó junto con `Home/CargueExcel` y `Home/CerrarSesion` (muertos). También se retiró la configuración "tablas para resumen IA" por empresa (`EmpresaTablasResumenIAService`, la pestaña en Configuración de empresa y `GuardarTablasResumenIA`); la tabla `EmpresaTablasResumenIA` se elimina de la BD con `Sql/Limpieza_CodigoMuerto_2026-10-03.sql`.

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
  dbo.Rel_PYG r ON r.Id = m.Ord WHERE m.IdEmpresa=@e AND m.IdEscenario=@esc AND m.Año BETWEEN @d AND @h`
  (sin filtro de mes — trae los años completos de una vez y filtra/agrega en C#).
- **Filtros (`FiltrosPYG`, v3)** — el panel replica el de `InformeLineaTiempo`: **rango continuo** de meses
  con datos (`ObtenerMeses` → slider con los años debajo, puede cruzar años; atajos COMPARTIDOS
  con Línea de Tiempo — ver "Atajos del rango de fechas (compartido)") + **Agrupar por** Mes/Trimestre/Año (solo afecta las tendencias). El botón
  del panel dice "Generar informe" (`LineaTiempo_BtnGenerar`), igual que en Línea de Tiempo. Semántica con rango multi-año: **Período** = suma de los meses del rango;
  **Acumulado** = acumulado del año final hasta el mes final; **YoY** = mismo rango desplazado 12 meses
  (el servicio lee también `AnioDesde-1`). Filtros nuevos:
  - **Comparar contra** (`PygComparar`: `ppto` = `ValorPresupuesto`/`ValorPresupuestoAcumulado`;
    `pptoAjuste` = `ValorPresupuestoConAjuste`; `forecast` = `ValorForecast`). Para los dos últimos el
    acumulado se calcula sumando sus meses ene..mes final (no tienen columna acumulada) — ⚠️ se asume que
    ambas columnas son mensuales, como `ValorPresupuesto`; si alguna resultara ser ya acumulada, ajustar
    `InformePYGService.ValoresLinea`. Todo lo que dice "Presupuesto" en la vista/Excel/PDF toma el nombre del
    comparativo elegido; las propiedades `*Presupuesto*` del modelo guardan el comparativo.
  - **Comparar con escenario** (default "Ninguno"; la lista la arma JS sin el escenario principal, y los
    escenarios sin filas en `ModeloPYG` para la empresa — `ObtenerMeses` devuelve `escenariosConDatos` —
    quedan deshabilitados como "Escenario X (sin datos)" + tooltip — texto plano en la opción, sin plantilla ni CSS de Select2): agrega `*RealEscenario`/`*VariacionEscenarioPorcentual` (filas),
    `*RealPctEscenario` (KPIs) y `RealEscenario` (tendencias → 3ª línea cian). Si ese escenario no tiene
    datos en el rango, `HayEscenarioComparar=false` y la vista muestra `PYG_EscenarioSinDatosNota`.
  - **Detalle de la tabla** (Todas las cuentas / Solo subtotales): solo cliente (no reconsulta); se respeta
    en PDF y Excel (`ExportarExcel(filtros, soloSubtotales)`).
  - `PygReporteViewModel` expone `EbitdaFila`/`UtilidadNetaFila` (además de `IngresosFila`) para los
    encabezados de tendencia, y `PygPuntoTendencia.EnRango` (calculado en el servidor) para el sombreado.
  - El Excel ahora formatea las columnas % como porcentaje (antes todas iban con `#,##0.0` y los % salían 0,0).
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
  registro en `AuditoriaAnalisisIA` (`NombreTabla = "PYG Gerencial"`) vía `IAGateway`.
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
- **Rediseño v2 del contenido de resultados** (handoff de diseño "opción 1b"; el panel de filtros no cambió).
  Clases `.pyg2-*` en `Assets/css/informe-pyg.css` (que ahora también define `.lt-card`/`.lt-empty`, antes
  solo existentes en el `<style>` de `InformeLineaTiempo.cshtml`), textos en claves `PYG_V2_*`. Estructura:
  encabezado con chips (período · escenario · acumulado · comparado) + menú de Unidad + botones Excel/PDF
  (el `<form id="frmExportarExcelPYG">` quedó oculto y lo dispara el botón) → 4 KPIs (Ingresos + 3 márgenes,
  conmutador Período/Acumulado) → cascada con valores/conectores dibujados por plugin inline + "Lo más
  relevante" → 3 tendencias con el rango consultado sombreado → tabla `.pyg2-table` con vistas
  Período/Acumulado/Ambos y columna "vs {año ant.}" opcional → insights en 3 tarjetas.
  - `PygReporteViewModel.IngresosFila` (nuevo, armado en `InformePYGService.ConstruirReporte`) alimenta la
    tarjeta KPI de Ingresos — la vista no busca la fila de ingresos por texto dentro de `Filas`.
  - Preferencias de vista (`kpiVista`, `vistaTabla`, `mostrarYoY`, `unidad`) se recuerdan por navegador en
    `localStorage['pyg.prefs']`. Todo el pintado usa `reporte.MesDesde/MesHasta` (no el slider, que el
    usuario pudo mover después de consultar).
  - **PDF gerencial** (`generarPdf` → `construirPdfPYG`, jsPDF puro, A4 horizontal, textos en `_pyg.textos.pdf`
    / claves `PYG_Pdf_*`): **pág. 1 resumen ejecutivo** (banda con degradado de marca, empresa, chips de
    contexto, 4 KPIs con deltas, insights en 3 tarjetas —o nota si no se generaron— y "Lo más relevante" en 4
    tarjetas); **pág. 2 análisis** (cascada a ancho completo + 3 tendencias con leyenda de líneas);
    **pág. 3+ tabla** (misma vista Período/Acumulado/Ambos, columna YoY/escenario y nivel de detalle que en
    pantalla; encabezado repetido; subtotales/total resaltados; variaciones en verde/rojo y punto en las
    críticas). Pie en todas: empresa · período · confidencial · "Página X de Y". Las KPIs y los destacados
    salen de `datosKpisPYG()`/`datosDestacadosPYG()`, compartidos con la pantalla. Las gráficas se
    re-renderizan a 3x y a la proporción exacta de su caja (`capturarChartPdf(chart, anchoPx, altoPx)`) y se
    exportan como **JPEG sobre blanco** — jsPDF incrusta PNG sin comprimir (el PDF pasaba de 10 MB; así ~0,4 MB).
  - **Prompt de insights**: si no existe (o está inactivo) `RESUMEN_PYG_GERENCIAL` en `GestorPrompts` —
    caso actual en la BD — se usa `InformePYGController.PromptResumenPygPorDefecto` (pide exactamente 3 líneas
    "Logro:", "Alerta:", "Eficiencia:" con la cifra clave en **negrita**), NO el genérico de
    `IAService.ConstruirPrompt` ("3 a 5 párrafos"), que era por lo que antes salía texto corrido sin tarjetas.
  - **Insights sin tocar el prompt**: `separarInsights()` corta la respuesta por las palabras clave
    Logro/Alerta/Eficiencia al inicio de línea (tolera viñetas, negritas, `#`); si falta alguna de las 3,
    se muestra el markdown completo como antes. Si un Super Admin cambia el prompt `RESUMEN_PYG_GERENCIAL`,
    debe mantener esas 3 etiquetas para conservar las tarjetas.

### Balance General Gerencial

Segundo informe de la serie de "informes gerenciales" (mismo estilo visual y clases `.pyg2-*`/`.lt-*`
que el PYG — `Assets/css/informe-pyg.css` se incluye tal cual en la vista; `Assets/css/informe-balance.css`
solo agrega el badge de cuadre). Muestra la estructura del balance (Activo Corriente/No Corriente,
Pasivo Corriente/No Corriente, Patrimonio) con **Saldo al corte, % vertical (sobre activo total),
comparativo elegido, Variación $ y Variación %**, más 4 KPIs (Activo total, Capital de trabajo, Razón
corriente, Endeudamiento total), un chequeo de cuadre, 2 barras apiladas de estructura, 3 mini-tendencias
y una sección de insights de IA.

- **Diferencia de fondo con el PYG: el balance es una FOTO, no un flujo.** No existe "Período" ni
  "Acumulado" — el valor de cada cuenta es su saldo en el **mes de corte**, que es siempre el extremo
  derecho (`AnioHasta`/`MesHasta`) del mismo slider de rango continuo que usan PYG/Línea de Tiempo. El
  rango completo (incluido su extremo izquierdo) solo alimenta las 3 tendencias mensuales; no se suman
  meses en ningún punto del informe.
- **Fuente de datos**: `dbo.ModeloBalance` (tabla ya materializada por `sp_ModeloBalance`) — **no**
  ejecuta el SP en vivo. `Services/InformeBalanceService.ObtenerFilas` hace `SELECT ... FROM
  dbo.ModeloBalance m LEFT JOIN dbo.REL_Balance r ON r.Id = m.Ord WHERE m.IdEmpresa=@e
  AND m.IdEscenario=@esc AND m.Año BETWEEN @d AND @h`. A diferencia de `dbo.ModeloPYG`, `ModeloBalance`
  **no tiene columnas de presupuesto/forecast** — solo el saldo real por Año/Mes/Cuenta — por lo que el
  v1 de este informe **no compara contra presupuesto** (`dbo.ModeloBalancePpto` existe y es correcto, pero
  es un SP grande y compuesto; queda para una fase futura si se necesita esa comparación).
- **Comparativo por defecto: cierre del año anterior** (31-dic), el estándar de NIC1/NIIF Pymes para
  balance. `FiltrosBalance.Comparar` (`BalanceComparar`: `cierreAnterior` (default) | `mesAnterior` |
  `mismoMesAnioAnterior`). El servicio arma una "foto" (`Foto`/`FotoDe`, saldo de todas las cuentas en un
  mes exacto) tanto para el corte como para el comparativo elegido; si el comparativo no tiene datos
  cargados (`HayComparativo = false`), la tabla/KPIs muestran el saldo al corte sin variación ("—") en
  vez de comparar contra cero.
- **Literales de línea CONFIRMADOS contra la BD real** (`SELECT Id, CuentaPUC, Descripcion, Tipo,
  Corriente, Formula FROM dbo.REL_Balance WHERE Tipo = 'CALCULO' ORDER BY Id`): `DescActivoTotal =
  "Total Activo"`, `DescActivoCorriente = "Total activo corriente"`, `DescActivoNoCorriente = "Total
  activo no corriente"`, `DescPasivoTotal = "Total pasivo"`, `DescPasivoCorriente = "Total pasivo
  corriente"`, `DescPasivoNoCorriente = "Total pasivo no corriente"`, `DescPatrimonioTotal = "Total
  patrimonio"` en `InformeBalanceService` — nótese el prefijo `"Total "` en corriente/no corriente, que
  la primera versión no tenía. `DescResultadoEjercicio = "Resultados del ejercicio"` (plural, cruce
  opcional con el PYG) se confirmó como componente de la fórmula de `Total patrimonio` (Id 361), pero es
  una línea de detalle (no `Tipo='CALCULO'`) — si su nombre exacto varía entre empresas,
  `HayComparacionPYG` queda en `false` sin romper el resto del informe. `REL_Balance` también trae ya
  calculada `"Total pasivo + patrimonio"` (Id 362, suma de `Total pasivo` + `Total patrimonio`) — el
  servicio no la usa (calcula la suma él mismo a partir de las dos partes ya confirmadas), pero es una
  vía alterna si se quisiera simplificar el chequeo de cuadre en el futuro.
- **Chequeo de cuadre** (nuevo respecto al PYG, no tiene equivalente allí): badge "Activo = Pasivo +
  Patrimonio" o "Descuadre de $X" (`BalanceReporteViewModel.CuadraBalance`/`DiferenciaCuadre`, tolerancia
  1 peso). Segundo badge opcional cruzando con `dbo.ModeloPYG`: utilidad neta acumulada del PYG a la misma
  fecha de corte vs. una línea candidata `DescResultadoEjercicio = "Resultado del Ejercicio"` del balance
  — solo se muestra si esa línea existe en el balance de la empresa (`HayComparacionPYG`); si no, se omite
  sin romper el resto del informe.
- **Clasificación Pasivo vs. Activo/Patrimonio para el color de las variaciones**
  (`BalanceFilaReporte.EsPasivo`, análogo a `PygFilaReporte.EsGastoOCosto`): por el primer dígito del PUC
  (`CuentaPUC` empieza en `"2"`) cuando la línea tiene cuenta propia; para subtotales sin cuenta propia
  (`Total Pasivo`, `Pasivo Corriente`…) cae a buscar "pasivo" en la descripción sin que contenga
  "patrimonio". Aumentar un pasivo es desfavorable (más deuda); aumentar activo o patrimonio es favorable
  — controla el rojo/verde de "Var. %"/"Var. $" y el criterio de "Lo más relevante" (top 4 por impacto
  absoluto en pesos, igual que PYG).
- **KPIs** (`BalanceKpi`, sin bloque Período/Acumulado): Activo total (moneda), Capital de trabajo
  (Activo corriente − Pasivo corriente, moneda), Razón corriente (Activo corriente / Pasivo corriente,
  formato `"1,8x"`, `NoAplica` si el pasivo corriente es 0) y Endeudamiento total (Pasivo total / Activo
  total, %, `NoAplica` si el activo total es 0). El sentido de "favorable" es específico por KPI
  (`_bal.kpiFavorableSiSube` en la vista): sube es favorable para los 3 primeros, desfavorable para
  Endeudamiento.
- **Estructura** (reemplazo de la cascada del PYG — el balance no tiene una secuencia causal
  Ingresos→Utilidad Neta): 2 barras apiladas Chart.js, Activo (corriente + no corriente) junto a
  Pasivo + Patrimonio (pasivo corriente + pasivo no corriente + patrimonio).
- **Tendencias**: 3 series mensuales (Capital de trabajo, Razón corriente, Endeudamiento) a través de
  todo el rango del slider. A diferencia de PYG (que SUMA los meses del grupo al agrupar por
  Trimestre/Año), cada punto de Balance es la **foto del último mes con datos dentro del grupo**
  (`InformeBalanceService.ArmarTendencia`) — un balance no se puede sumar mes a mes.
- **Insights IA**: código `RESUMEN_BALANCE_GERENCIAL` en `GestorPrompts` (aún no tiene una fila por
  defecto, igual que pasó con `RESUMEN_PYG_GERENCIAL`) — pide 3 líneas fijas "Liquidez:"/"Solvencia:"/
  "Capital de trabajo:". Mismo cupo diario y registro en `AuditoriaAnalisisIA` (`NombreTabla =
  "Balance Gerencial"`) que el PYG.
- **Pendiente manual**: crear la opción de menú `INFORMES_BALANCE_GERENCIAL` vía `/MenuOpciones` (Super
  Admin) para que aparezca en el sidebar.
- **Fuera de alcance a propósito en v1**: comparación contra presupuesto (`dbo.ModeloBalancePpto`),
  indicadores de actividad/rentabilidad cruzados 12 meses móviles con el PYG (días de cartera/inventario/
  proveedores, ROE/ROA), semáforos configurables por sector y vista consolidada de grupo sin
  eliminaciones — quedarían para una fase 2, siguiendo el mismo patrón incremental que tuvo el PYG.

### Atajos del rango de fechas (compartido: Línea de Tiempo + PYG)

Los dos informes con slider de rango continuo (`InformeLineaTiempo`, `InformePYG`) usan EXACTAMENTE los
mismos atajos, con una sola implementación:
- **Lógica**: `Assets/js/rango-atajos.js` (`window.RangoAtajos`: `indices`, `etiquetaVentana`,
  `pintarBotones`, `porDefecto`), sin dependencias; cada vista solo delega en él.
- **Markup/estilo**: `.rango-atajos` > 2 `.rango-atajos-grupo` (en `informe-linea-tiempo.css`), barra
  segmentada compacta en su propia fila bajo "Rango de fechas" — cabe en una línea en pantallas de 14"
  (~1100 px útiles). Columna "Agrupar por" de `.lt-grid-rango` = 170 px.
- **Grupos**: `[Todo]` (todos los meses con datos) | `[Año en curso ·
  Últimos 6 meses · Último trimestre · Mes anterior · Mes actual]`, todos **relativos a la fecha de HOY del servidor** (`hoy` =
  `DateTime.Now` al renderizar, equivalente a GETDATE(); NO el reloj del navegador ni el último mes
  cargado). Ventanas terminando en el mes de hoy (Último trimestre = últimos 3 meses; Mes anterior en
  enero = diciembre del año anterior), intersectadas con los meses que tienen datos. Si ningún mes de la
  ventana tiene datos, el atajo queda `.no-disponible` (clase + `aria-disabled`, NO el atributo `disabled`,
  que en Chrome impide ver el tooltip); el tooltip muestra la ventana exacta o el motivo.
- **Default** al cargar meses: "Año en curso"; si el año de hoy no tiene datos, "Todo".
- Textos en claves genéricas `Rango_*` (ambos `.resx`). Un solo mes seleccionado se muestra como "Ago 2026"
  (sin "Ago 2026 → Ago 2026").

### Excel Import Logging

`DatosController` accumulates timestamped import log messages during an Excel upload using a private `StringBuilder _logBuilder` field. Log entries are formatted as `[yyyy-MM-dd HH:mm:ss.fff] message` via private `LogToFile(mensaje)`. The completed log is stored in `Session["LogImportacion"]` via `GuardarLogEnSession()`. Users can download it as `LogImportacion_{yyyyMMdd_HHmmss}.txt` via `GET /Datos/DescargarLog`.

Per-sheet results use `DetalleCargaHojaExcel` (`Models/CargueExcelModels.cs`):
- `Estado` values: `"Exitoso"`, `"Error"`, `"Ignorada"` (empty/no headers/no data rows)
- `ResultadoCargaExcel.MostrarDescargaLog` (bool) — set to `true` on error to show the download button

Import rules: the workbook must contain every sheet of `TablasCargueHelper.MapeoParaEmpresa(idEmpresa)` (the 9 standard ones of `MapeoZaIni` plus the company's custom sheets, see "Personalizaciones por empresa"); unrecognized sheets are ignored; 5 consecutive empty rows terminate data reading.

### Validación en dos pasos del cargue de Excel (staging)

`CargarExcel` **ya no escribe directo en `Ini_*`**. El flujo es: subir → validar en staging → revisar → confirmar (o descartar). Nada real se toca hasta que el usuario confirma explícitamente.

- **Tablas de staging** (`Sql/007_CarguesStaging_CreateTables.sql`): `dbo.CarguesLotes` (cabecera: empresa/año/modo/escenario/usuario/archivo/`Estado`) y `dbo.CarguesLotesErrores` (un renglón por hallazgo: hoja, fila Excel, columna, `Severidad` Error/Advertencia, `CodigoRegla`, mensaje es/en), más un espejo `dbo.Staging_Ini_*` por cada una de las 9 `Ini_*` — clonado dinámicamente del esquema real (`SELECT TOP(0) INTO`) para no tener que mantener tipos a mano, con `IdLote`/`NumeroFilaExcel` agregados y las 3 columnas de ejecución (`IdUsuarioEjecucion_Log`/`FechaEjecucion_Log`/`Observacion_Log`) quitadas (no aplican antes de confirmar).
- **`Estado` de un lote**: `EnValidacion → ValidadoOk | ConAdvertencias | ConErrores → Confirmado | Descartado`. Solo `ValidadoOk`/`ConAdvertencias` se pueden confirmar (`CargueLote.PuedeConfirmar`).
- **Aislamiento entre empresas y candado por llave de cargue**: `IdLote` es la partición universal (BIGINT IDENTITY único global) — todo lo que toca staging o valida filtra por `IdLote`, así que dos empresas nunca comparten filas ni pueden cruzarse, y `RevisarCargue`/`ConfirmarCargue`/`DescartarCargue` exigen `EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)` antes de tocar un lote ajeno. Además, `UX_CarguesLotes_ActivoPorLlave` (`Sql/008_CarguesLotes_UnicoActivoPorLlave.sql`, índice único **filtrado** `WHERE Estado <> 'Confirmado' AND Estado <> 'Descartado'` sobre `(IdEmpresa, Anio, Modo, IdEscenario)`) impide que existan dos lotes **activos** a la vez para la misma llave de cargue — cierra la carrera de dos cargues casi simultáneos para la misma empresa/año/modo/escenario, donde el segundo en confirmarse pisaría lo que dejó el primero. `CargueStagingService.CrearLote` hace primero un pre-check amable (evita el error crudo del índice en el caso normal) y, si de todos modos hay una carrera real, atrapa la violación del índice (`SqlException.Number` 2601/2627) y responde igual de amable — ambos casos devuelven `(exito:false, idLote del lote activo existente, mensaje)`, y `DatosController.CargarExcel` redirige directo a `RevisarCargue` de ese lote en vez de dejar crear uno nuevo encima.
- **`Services/CargueStagingService.cs`** orquesta todo: `CrearLote`, `ValidarLote(idLote)` (llama al SP — ya no arma ningún TVP), `ConfirmarLote` (recibe la `SqlConnection`/`SqlTransaction` del llamador para participar en la misma transacción), `DescartarLote`/`DescartarLotes`/`DescartarLotesPendientesDeUsuario` (todos pasan por `EjecutarBorradoDeLotes`: staging + hallazgos + cabecera en UNA transacción con `XACT_ABORT`, nunca a medias), `PurgarLotesVencidosSiToca` (≤1 vez cada 15 min, en segundo plano, llamada desde `CargueExcel` GET, `RevisarCargue` GET y `CargarExcel`; borra los lotes que cumplan `CondicionVencido` — `EnValidacion` > 70 min (cargue caído), cualquier no confirmado > 2 h, o `Confirmado` sin limpiar — más filas de staging/hallazgos huérfanas; sin job de SQL Agent). **Identidad única**: `IdLote` (BIGINT IDENTITY) nunca se repite — las FK de staging → `CarguesLotes` impiden un `TRUNCATE` que reinicie el contador, y el bulk copy a staging usa `SqlBulkCopyOptions.CheckConstraints` (ninguna fila puede apuntar a un lote inexistente). **Ningún lote debe quedar pegado**: (1) salir de `RevisarCargue` sin Confirmar/Descartar (otra opción, "Subir archivo corregido", cerrar pestaña) dispara `navigator.sendBeacon` → `DatosController.AbandonarCargue` (solo lotes del propio usuario); **recargar la página de cualquier forma también descarta**: la vista detecta `performance` navigation type `reload` y envía `#frmAbandonarCargue` (`porRecarga=true`) → `AbandonarCargue` redirige a Cargue de Excel con `RevisarCargue_DescartadoPorRecarga`; (2) `CargarExcel` descarta antes los lotes pendientes del mismo usuario **para la misma empresa** (cualquier año/modo/escenario; nunca de otra empresa) y `CrearLote` libera los lotes vencidos de su misma llave; (3) fin de sesión — logout (`UsuarioSesionHelper.LimpiarSesion`) o inactividad (`Global.asax Session_End`, sessionState InProc) — descarta los lotes que ESA sesión abrió (`Helpers/CargueLoteSesionHelper`, lista en `Session["CarguesLotesAbiertos"]`); (4) al confirmar, `ObtenerConteoPorHoja` + `LimpiarLoteEnTransaccion` corren dentro de la transacción de confirmación, así nunca queda un lote `Confirmado` con su staging colgado. **Sin cruces por tiempo**: `ConfirmarCargue` llama primero a `CargueStagingService.BloquearLoteParaConfirmar` (UPDLOCK sobre la cabecera del lote dentro de su transacción, verifica empresa/estado y que el staging no esté vacío) antes de borrar nada real; los borrados (`EjecutarBorradoDeLotes`) toman el mismo UPDLOCK al seleccionar los lotes, así un descarte/purga/abandono concurrente espera a que termine la confirmación y omite el lote ya `Confirmado`, o — si ganó el descarte — la confirmación aborta sin tocar las `Ini_*`.
- **`dbo.sp_ValidarCargueStaging`** (`Sql/StoredProcedures/`) — **único SP con todas las reglas de validación**, corriendo contra `Staging_Ini_*` (nunca contra `Ini_*` real, salvo para comparar "lo que entra" contra "lo que ya está guardado"). Toda la validación (DELETE idempotente + bloques + UPDATE de cierre) corre dentro de una transacción explícita (`BEGIN/COMMIT/ROLLBACK TRANSACTION` + `SET XACT_ABORT ON`) para que un fallo a mitad de un bloque no deje el lote "a medio validar". Agregar una regla nueva = un bloque más ahí (etiquetado `BLOQUE N`), sin tocar C#. **A propósito, por ahora solo trae 3 bloques** (el resto de reglas de la primera versión — catálogos genéricos vía TVP, campos obligatorios, duplicados, cuadre débito/crédito, ecuación de cuenta, continuidad mes a mes — se retiraron a pedido del usuario para partir de un SP mínimo e ir agregando de a una): **Bloque 1 `PAIS_INCONSISTENTE`** (país de alguna fila del lote que no existe en el catálogo real `dbo.Paises`), **Bloque 2 `PAIS_VACIO`** (alguna fila trae el país vacío/nulo — una sola advertencia por lote), y **Bloque 3 `DIFERENCIA_UNIDAD_MEDIDA`** (ingresos del año que se carga vs. el año YA GUARDADO más cercano en `Ini_PYG` — no solo ±1 año — usando `dbo.Rel_PYG` con `EXISTS`, no `JOIN`, para no inflar el `SUM` si una cuenta tiene más de un mapeo) — migradas/adaptadas de los widgets "Pais inconsistente por empresa" y "Diferencias unidades de medida" (`DashboardTarjetas`, `Tipo=3/Subtipo='plantilla'`; el Gestor de Widgets ya fue eliminado). **No recibe `@Catalogos`** — ningún bloque actual valida contra listas configurables por empresa; el tipo `dbo.TVP_CatalogoItem` sigue existiendo en `007_CarguesStaging_CreateTables.sql` por si se vuelve a necesitar. No usa `STRING_AGG` (no disponible en esta instancia).
- **`dbo.sp_ConfirmarCargueStaging`** — mueve un lote validado de `Staging_Ini_*` a `Ini_*` con un `INSERT...SELECT` por tabla (columnas resueltas por intersección de `INFORMATION_SCHEMA.COLUMNS`, concatenadas con `FOR XML PATH`+`STUFF` — no `STRING_AGG`, no disponible en esta instancia). **No abre su propia transacción** — corre dentro de la del llamador (`DatosController.ConfirmarCargue`), igual que `CrearSnapshotEnTransaccion`.
- **`DatosController.ConfirmarCargue(idLote)`** hace, en este orden y dentro de una sola transacción: `CrearSnapshotEnTransaccion` (sin cambios) → `EliminarEjecucionDeIni`/`EliminarAnosHistoricosDeIni` (sin cambios) → `sp_ConfirmarCargueStaging` → `RegistrarAuditoria` (sin cambios, tabla `AuditoriaCargues`) → commit. Después del commit sigue corriendo `SP_ValidarPlantillaInicial` como red de seguridad adicional (solo modo ejecución, igual que antes).
- **`GuardarEnIni`** es la misma pieza de siempre (resuelve el esquema destino vía `INFORMATION_SCHEMA.COLUMNS`, así que sirve tanto para `Ini_*` como para `Staging_Ini_*`) con 2 parámetros opcionales nuevos (`idLote`, usado para completar las columnas `IdLote`/`NumeroFilaExcel` cuando la tabla destino las tiene). `CargarExcel` la llama apuntando a `Staging_Ini_*` (vía `TablasCargueHelper.NombreStaging(nombreTablaIni)`); `Z_TablaPUC` (sin tabla `Ini_`/`Staging_Ini_` asociada) sigue guardándose igual que siempre, fuera de este flujo.
- **Vista**: `~/Views/Datos/RevisarCargue.cshtml` — el informe de validación (tabla de hallazgos, badge de errores/advertencias, botón Confirmar deshabilitado mientras `TotalErrores > 0`, modal `.modal-confirm` para Descartar).
- **Resultado del cargue confirmado, en modal**: `Views/Datos/CargueExcel.cshtml` muestra el resultado (`TempData["ResultadoCarga"]`) apenas carga la página, en un SweetAlert2 (`.swal-res-compact`) en vez de solo en el banner/tabla de abajo. Éxito: resumen compacto — chips de empresa/año/modo/escenario, stats grandes de total registros/hojas, y el detalle por hoja (de `ObtenerConteoPorHoja`) en una lista corta; al cerrarlo recarga `CargueExcel` en limpio. Error: mensaje simple, sin recargar (para no perder el detalle por hoja ni el botón "Descargar log"). Los campos estructurados (`NombreEmpresa`/`Anio`/`ModoTexto`/`IdEscenario`/`NotaExtra`) se agregaron a `ResultadoCargaExcel` solo para esto — `DatosController.ConfirmarCargue` los llena; `Mensaje` sigue llevando la frase completa para el banner y `NotificacionesService`.
- **Los widgets "Advertencia tipo plantilla"** (`DashboardTarjetas`, `Tipo=3`/`Subtipo='plantilla'`) que antes se evaluaban post-commit en `CargarExcel` se retiraron de ahí — sus reglas viven ahora dentro de `sp_ValidarCargueStaging`. El Gestor de Widgets (y los widgets del Home) fueron **eliminados por completo**; la tabla `DashboardTarjetas` queda huérfana en la BD (se puede borrar con `DROP TABLE dbo.DashboardTarjetas;` y quitar la opción de menú de widgets en `/MenuOpciones`).

### Personalizaciones por empresa (plantilla y hojas propias)

Algunas empresas tienen hojas de cargue que nadie más tiene. **Primer caso: Churido (EmpId 6) y Ucrania (EmpId 8)** — plantilla `PlantillaBUFINS_Churido.xlsx` con la hoja extra `Z_HistPrecios_Churido` (`Empresa | TipoFruta | Año | Mes | Valor`) → `dbo.Ini_HistPrecios_Churido` (+ `Staging_Ini_HistPrecios_Churido`).

- **Qué hace un paquete** = código: `Helpers/PersonalizacionesEmpresaHelper.Catalogo` (`PersonalizacionCargue`: `Codigo`, `Plantilla`, `HojasExtra` hoja Z_ → tabla Ini_). **A quién aplica** = BD: `dbo.EmpresaPersonalizaciones (IdEmpresa, Codigo, Activo)`, leída por `PersonalizacionEmpresaService` y cacheada 5 min (sin la tabla ⇒ nadie personalizado). Script: `Sql/020_PersonalizacionesEmpresa_HistPreciosChurido.sql` (tabla de asignación + seed 6/8 + tablas Ini/Staging + `sp_ConfirmarCargueStaging` con lista de tablas dinámica = todas las `Staging_Ini_X` con su `Ini_X`).
- **Todo lo que es "por empresa" usa `TablasCargueHelper.*ParaEmpresa`** (`MapeoParaEmpresa`, `TablasIniParaEmpresa`, `HojasPersonalizadasParaEmpresa`) para operaciones de datos: hojas exigidas/reconocidas en `CargarExcel` (una hoja personalizada de otra empresa se ignora), borrado previo `Eliminar*DeIni`, snapshot/conteo del historial, cierre de año. `MapeoZaIni`/`TablasIni` siguen siendo solo el cargue estándar. `TablasCargueHelper.MapeoCompleto` (todas las hojas del catálogo) solo para operaciones filtradas por `IdLote` (limpieza/conteo de staging en `CargueStagingService`, protegidas con `IF OBJECT_ID(...)`).
- **Rollback**: exige snapshot de las tablas estándar; las personalizadas se restauran solo si la versión trae su snapshot (versiones anteriores a la personalización las dejan como están).
- **Visibilidad (más estricta que el acceso por grupo, a propósito)**: `PersonalizacionesEmpresaHelper.VisiblesPara(usuario, idEmpresa)` — Super Admin ve todos los paquetes de la empresa; el resto solo los que **su propia empresa** (`Usuarios.IdEmpresa`) también tiene. Todo lo que se muestra/descarga (aviso, "Plantilla", "Plantilla con datos") usa esa lista vía `TablasCargueHelper.MapeoParaPaquetes`/`PlantillaParaPaquetes`. `PuedeOperarCargue` bloquea `CargarExcel`/`RevisarCargue`/`ConfirmarCargue` (mensaje `Datos_PlantillaPersonalizadaSinPermiso`, sin nombrar hojas) a quien tenga acceso a la empresa pero no vea todos sus paquetes.
- **Elección de plantilla (solo Super Admin), en modales, no como filtro de la pantalla**: el botón "Plantilla" abre `#modalPlantilla` (selector `#mpPlantilla`) y el modal existente de "Plantilla con datos" suma el selector `#pcdPlantilla` (al cambiarlo recarga el conteo de años). Opciones: "Estándar" + cada paquete del catálogo (`@helper OpcionesPlantilla`, nombre en la clave `ClaveNombre`), preseleccionadas con `plantillaCodigo` de `ObtenerAnosDisponibles`. Para el resto de usuarios el botón "Plantilla" sigue siendo un enlace directo. El valor se envía como `plantilla` a `DescargarPlantilla`, `ObtenerAniosConDatos` y `DescargarPlantillaConDatos`. `DatosController.PaquetesParaDescarga` solo respeta ese parámetro si `EsSuperAdmin()`; para cualquier otro usuario lo ignora y usa `VisiblesPara`.
- **Plantillas personalizadas en `App_Data/PlantillasEmpresa/`** (no se sirven por URL): solo se bajan por `DatosController.DescargarPlantilla`.
- **Validación**: `sp_ValidarCargueStaging` BLOQUE 10 (Año/Mes vacío) ya recorre todas las `Staging_Ini_*`, incluida la personalizada; reglas propias de la hoja = un bloque nuevo en el SP.
- **Agregar una personalización**: (1) crear `Ini_X` + `Staging_Ini_X` (misma convención de columnas `_Log`/`IdEscenario`/`IdLote`/`NumeroFilaExcel`, FK `ON DELETE CASCADE` a `CarguesLotes`), (2) entrada en el `Catalogo` (+ plantilla en `App_Data/PlantillasEmpresa`, registrada como `Content` en el `.csproj`), (3) `INSERT` en `dbo.EmpresaPersonalizaciones`. Habilitar un paquete existente para otra empresa = solo el paso 3.

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
- **Ejecución de Modelos** (`DatosController` + `Views/Datos/Modelo.cshtml`): `EjecutarModeloAjax`,
  `EjecutarModeloYEscribirHoja` (shared by `ExportarTodosModelos`/
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
- **Deliberately out of scope** (see the "Escenarios de datos" plan for the full rationale): `InformeTablasDatosController` is now just the AJAX backend of Análisis IA (the "Tablas de datos" report and the 10 `*_VT` tables were removed),
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

**`DatosController.EjecutarModeloAjax`** (la antigua `EjecutarModelo` no AJAX se eliminó por no tener llamadores) — fetches `NombreSP` from DB (never from user input), then calls the SP with `@IdEmpresa`, `@IdUsuario`, `@Año`. The SP may return multiple intermediate result sets; the **final** result set must have `CodMessage` (1 = success) and `ErrorMessage` columns. `CommandTimeout` is 300 seconds.


### IA (AI) Integration

> **REGLA DE DATOS (MANDATORY):** la IA **solo puede tomar datos de las tablas `dbo.Modelo*`** (`ModeloBalance`, `ModeloBalanceDiff`, `ModeloBalancePpto`, `ModeloFlujoCaja`, `ModeloFlujoEfectivo`, `ModeloLineasNegocio`, `ModeloPYG`, `ModeloTesoreriaPpto`), nunca de `Ini_*` ni de ninguna otra tabla. Se aplica con `InformeTablasDatosService.ObtenerTablasModelos()` (solo modelos activos en `ModelosEjecucion`): `InformeTablasDatosController.ConsultarConIA` rechaza cualquier otra tabla (`EsTablaDeModelo`), `AnalisisIAController` solo lista esas tablas. PYG/Balance gerencial leen `ModeloPYG`/`ModeloBalance`. Cualquier punto de IA nuevo debe cumplir esta regla.

`IAService` (`Services/IAService.cs`) calls the **OpenAI API** (default model `gpt-4o`, overridable via `ConfiguracionSistema` key `OpenAIModel`).

- **Config keys (DB, `ConfiguracionSistema`):** `OpenAIApiKey`, `OpenAIModel`, `OpenAIMaxTokens` (default 8000), `OpenAITemperature` (empty = not sent), `IA_CacheHoras`, `IA_CostoPor1kTokensPrompt/Respuesta`, `IA_TokensMensualesPorEmpresa`, `IA_MaxCaracteresPregunta`
- **Endpoint:** `https://api.openai.com/v1/chat/completions`
- **Timeout:** 60 seconds; retries on 429/503 with backoff; one automatic retry (up to 16 000 tokens) if a reasoning model returns empty content; circuit breaker (3 consecutive failures → open 5 min); response cache in memory + `dbo.CacheRespuestasIA`
- **Data sent:** Análisis IA sends up to 200 000 chars of CSV (first turn only); PYG/Balance send their already-computed report (`filas` + `kpis`)
- **Models:** `IAConsultaRequest` / `IAConsultaResponse` (incl. `TokensPrompt/TokensRespuesta/TokensTotal`, `Modelo`, `DesdeCache`) in `Models/IAModels.cs`

**IAGateway — punto único de ejecución (`Services/IAGateway.cs`, `Models/IAGatewayModels.cs`):** todo punto de IA hace SOLO esto: (1) `ValidarAccesoIA(usuario, esAdmin, idEmpresa)` (helper de `BaseController`: acceso por usuario + presupuesto mensual de tokens vía `IAUsoService`, avisos 80 %/100 % a Super Admin, devuelve el JSON de error ya traducido o `null`), (2) arma los datos (`IAConsultaRequest`), (3) `await new IAGateway().EjecutarAsync(new IASolicitud { Funcion = IAFuncion.X, ... })`. El gateway lee la configuración (`OpenAIApiKey/Model/MaxTokens/Temperature`), resuelve prompt (`CodigoPrompt` → GestorPrompts, `PromptPorDefecto` si falta), guardrail y contexto de negocio, llama a `IAService` en el idioma pedido (`Idioma` "es"/"en"; `BaseController.IdiomaIA`), calcula el costo estimado y **mide siempre**: `AuditoriaAnalisisIA` (con `TokensTotal`, base del presupuesto — `SumarTokensMes`), `dbo.IAUsoLog` (una fila por llamada: función, modelo, tokens prompt/respuesta, costo, latencia, caché, error) y `dbo.IAUsoMensual` (acumulado por empresa/mes). Los registros nunca lanzan (fallan a `AppLogger`). Funciones: `Chat` (Análisis IA), `ResumenHome` (sin uso desde que se retiró el resumen IA del Home; la constante se conserva por las filas históricas de `IAUsoLog`), `InsightsPYG`, `InsightsBalance`. **Un punto de IA nuevo NO debe llamar a `IAService` directo**: agrega una constante a `IAFuncion` y usa el gateway, para que el consumo no se escape de la medición. PYG/Balance pasan `Idioma = IdiomaIA` + `InstruccionEnIngles`: la vista separa los insights por etiqueta (acepta Logro/Alerta/Eficiencia y Achievement/Alert/Efficiency; Liquidez/Solvencia/Capital de trabajo y Liquidity/Solvency/Working capital) y en inglés el gateway le exige al modelo esas etiquetas exactas. **El presupuesto mensual (`SumarTokensMes`) lee `dbo.IAUsoMensual`** (cae a sumar `AuditoriaAnalisisIA` solo si esa tabla no existe), así que limpiar la auditoría no reinicia el consumo. Scripts en orden: `Sql/012_AuditoriaAnalisisIA_TokensEIndice.sql` → `Sql/013_IAUsoLog_CreateTables.sql` → desplegar → `Sql/014_IAUsoMensual_Backfill.sql` (re-ejecutable). Plan y pendientes: `docs/IA_Propuesta_Estructura.html` (sin planes comerciales por empresa: descartado).

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

Punto único de control para **todas** las consultas de IA del sistema (Análisis IA, resumen de Home,
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
- Reemplaza el viejo tope numérico diario por usuario (`Usuarios.LimiteConsultasIA`), ya eliminado del código.

### Global Filter

`EmpresasViewBagFilter` (`Filters/EmpresasViewBagFilter.cs`) is registered globally in `FilterConfig` and loads all companies into `ViewBag.Empresas` for every request.

`LoggingHandleErrorAttribute` (`Filters/LoggingHandleErrorAttribute.cs`) is the other global filter — a `HandleErrorAttribute` subclass that logs the exception to `AppLogger` before letting `customErrors` render the error page. Registered in `FilterConfig` in place of the stock `HandleErrorAttribute`.

### Logging (`AppLogger`)

`Helpers/AppLogger.cs` — dependency-free app logger. Writes one line per event to `App_Data/logs/app-yyyyMMdd.log` (daily rotation, 30-day retention), never throws (falls back to `Trace`). API: `AppLogger.Info/Warn/Error(mensaje, ...)` and `AppLogger.Error(Exception, contexto)`. Each line carries timestamp, level, request ip/user/url, optional `ctx=`, message, and the full exception chain. Wired into `Global.asax.Application_Error` (safety net) and `LoggingHandleErrorAttribute` (MVC pipeline). Use it in any `catch` where today the code only does `SetErrorMessage(ex.Message)` or swallows silently.

### Auditoría centralizada (`AuditoriaService`)

Tabla **única** `Auditoria` para TODA auditoría del sistema, diferenciada por columna `Tipo`. Ver el CREATE + índices en el encabezado de `Services/AuditoriaService.cs`. Modelos y constantes en `Models/AuditoriaModels.cs` (`RegistroAuditoria`, `AuditoriaTipo`, `AuditoriaAccion`, `AuditoriaFiltro`, `AuditoriaResultado`).

- **Escribir**: `new AuditoriaService().RegistrarCambio(AuditoriaTipo.X, AuditoriaAccion.Y, entidad, entidadId, descripcion, valorAnterior, valorNuevo, idEmpresa)` — serializa `valor*` a JSON. Para seguridad: `RegistrarSeguridad(accion, descripcion, idUsuario, nombreUsuario, idEmpresa)` — en el login se pasa la empresa del usuario explícitamente (aún no hay sesión), y `AccesoController.Login` la lee con la columna `IdEmpresa` del `SELECT` de credenciales; esto es lo que permite el alcance por empresa del visor. `Registrar(RegistroAuditoria)` autocompleta fecha/usuario/IP/user-agent desde `HttpContext`. **Nunca lanza** (cae a `Trace`), así que se llama después de la operación exitosa sin envolver en try/catch.
- **Ya instrumentado**: `EmpresaController` (crear/editar/eliminar), `GruposEmpresarialesController` (crear/editar/eliminar/asignar-empresas), `MenuOpcionesController` (crear/editar/eliminar), `PermisosController.Guardar`, `ModelosEjecucionController` (crear/editar/eliminar), `UsuarioController` (crear/editar/eliminar), `ConfiguracionEmpresaController` (guardar config básica / cierre de año), `DatosController.EnviarModelosPorCorreo` (`AuditoriaTipo.Modelos` + `AuditoriaAccion.Enviar`, severidad Advertencia — datos que salen del sistema), `DatosController.EjecutarModeloAjax` (`AuditoriaTipo.Modelos` + `AuditoriaAccion.Ejecutar`, solo en éxito), `DatosController.ExportarTodosModelos`/`ExportarModelosSeleccionados` (`AuditoriaTipo.Modelos` + `AuditoriaAccion.Exportar`, severidad Advertencia — datos que salen del sistema), `DatosController.DescargarPlantillaConDatos` (`AuditoriaTipo.Cargues` + `AuditoriaAccion.Exportar`, severidad Advertencia — descarga de la plantilla BUFINS rellena con los datos actuales), `DatosController.ConfirmarCargue`/`DescartarCargue` (`AuditoriaTipo.Cargues` + `AuditoriaAccion.Confirmar`/`Descartar` — ver "Validación en dos pasos del cargue de Excel"; `Descartar` con severidad Advertencia), `GestorEscenariosController` (crear/editar/desactivar, `AuditoriaTipo.Escenarios`), `AccesoController.Login` (éxito, fallido, bloqueo — esto es B3), `ConfiguracionGlobalIAController` (`GuardarPresupuestoEmpresaIA` → `AuditoriaTipo.Configuracion`, `GuardarAccesoUsuarioIA` → `AuditoriaTipo.Usuarios` — ver "Control de acceso y presupuesto de IA por empresa"). Para auditar algo nuevo: elegir/crear código en `AuditoriaTipo` y añadir una línea `RegistrarCambio(...)` tras el éxito.
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

### Auditoría — esqueleto y filtros uniformes (prefijo `ax-`)

Las 4 pestañas del hub (Cambios `Auditoria.cshtml`, Navegación `InformeNavegacion.cshtml`, Cargues `AuditoriaCargues.cshtml`, Consultas IA `AuditoriaConsultasIA.cshtml`) comparten **un solo esqueleto** y **un solo panel de filtros**. **Cualquier pestaña/auditoría nueva debe usarlos, no inventar el suyo.**

- **Orden de la pantalla** (idéntico en todas): [conmutador de modo/vista si lo hay, `.ax-vista.ax-modo`] → tarjetas de resumen `#axKpis` → panel de filtros → estado inicial/vacío → resultados (`.table-container` con `.ax-toolbar` a la derecha: vistas, Excel, Imprimir, y Limpiar solo Super Admin) → paginación.
- **Panel de filtros = parcial `Views/Shared/_AudFiltros.cshtml`** + modelo `AudFiltrosVM`/`AudCampo` (`Models/AuditoriaUIModels.cs`). Orden fijo: **Período (cápsula Hoy · 7 d · 30 d · Mes · personalizado con rango calculado) · Búsqueda de texto (`#fTexto`) · Empresa (`#fEmpresa`) · Usuario (`#fUsuario`) · campos PROPIOS de la pestaña** (resaltados con `Propio = true`) · "Más filtros" (`Avanzados`, p. ej. Relevancia en Cambios, Valoración en IA) · chips de filtros activos `#axChips` · Consultar / Limpiar filtros. Cada vista arma su `AudFiltrosVM` arriba; los ids fijos son `fPeriodo`, `fDesde`, `fHasta`, `fTexto`, `axMas`, `axAvanzados`, `axChips`, `btnConsultar`, `btnLimpiar`. Campos propios hoy: Cambios = Área, Qué ocurrió (+Relevancia); Navegación = Página/opción, Rol; Cargues = Escenario; IA = Tabla analizada, Tipo (+Valoración). Las opciones de un `AudCampo` pueden venir fijas o llenarse por JS (Qué ocurrió, Usuario de IA, Tabla de IA).
- **JS compartido `Assets/js/auditoria-shared.js` (`window.AudKit`)**: `periodo.init/rango/reset` (siempre devuelve `yyyy-MM-dd`; personalizado vacío = sin límite), `masFiltros`, `enter(fn)`, `filtros.limpiar/chips(onChange, extras)` (los chips salen solos de los `<select>` y del buscador y se quitan con un clic), `kpis.render`, `pager.render/info`, `esqueleto`, `imprimir`. El **detalle de cada registro se sigue abriendo en modal SweetAlert** (decisión de producto: se probó un panel lateral y se descartó) — Cambios usa `.aud-dt-popup`/`.aud-dt-*`, IA su modal de respuesta. Los textos NO están en el JS: salen de atributos `data-t-*` del panel (claves `Ax_*`). Las pestañas se inyectan con `$.load()`, así que el JS está protegido contra doble evaluación (`__v`); el hub cierra el panel lateral al cambiar de pestaña. **CSS compartido: `Assets/css/auditoria-shared.css`** (se apoya en `bufins-components.css`).
- **Resumen (KPIs)**: Cambios → `AuditoriaService.Resumen(filtro)` viaja en `Consultar` (`resumen`: usuarios, relevanciaAlta, ultimo); Navegación → endpoint `ResumenGeneral` (`AuditoriaNavegacionService.ResumenGeneral`, usa los mismos filtros); Cargues e IA los calculan en el navegador sobre el conjunto filtrado.
- **Filtros nuevos agregados para uniformar**: `texto` en Navegación (`NavegacionFiltro.Texto`, en todos sus endpoints y el Excel); en Cargues, Usuario como selector (id), búsqueda de texto, período y **Escenario** (`AuditoriaCargues.IdEscenario`: el SP no lo trae, `AuditoriaCarguesService.CompletarEscenario` lo lee aparte de `dbo.AuditoriaCargues`, tolerante a que la columna no exista) — la exportación de Cargues ahora recibe `idEmpresa/idUsuario/escenario/texto/fechaDesde/fechaHasta`; en Consultas IA, Tabla analizada y Valoración, y **Excel** nuevo (`AuditoriaConsultasIAController.ExportarExcel`, aplica en servidor los mismos filtros locales de la pantalla).
- **Comportamiento — NINGUNA pestaña carga ni muestra datos hasta pulsar «Consultar»** (o Enter en el buscador): al entrar y tras «Limpiar filtros» se ve `#axInicial` ("Define los filtros y pulsa Consultar") sin KPIs ni chips. Cargues pide sus filas al endpoint `AuditoriaCarguesController.ObtenerCargues` (mismos filtros que el Excel, en `Filtrar`); la vista solo usa el conjunto del SP para armar los selectores. IA conserva una única excepción: el enlace permanente `?ia=<id>` (p. ej. desde una notificación) consulta solo y abre el detalle buscando sin límite de fechas. En Navegación el conmutador de vista (Detalle / Por usuario / Por página / Usuario × página) va fuera de los resultados y, tras la primera consulta, cambiar de vista vuelve a consultar solo.

### Error Pages

`ErrorController` (`Controllers/ErrorController.cs`, no `[ValidarSesion]`) renders branded, session-independent pages: `Index` (500), `NotFound` (404), `Forbidden` (403) — each sets `Response.StatusCode` + `TrySkipIisCustomErrors`. Views in `Views/Error/` use `Views/Error/_ErrorLayout.cshtml` (`Layout = null`, self-contained dark-purple + brand-gradient card, strings from `Err_*` resx keys). `Views/Shared/Error.cshtml` (used by `LoggingHandleErrorAttribute`) shares the same layout. Routing in: `Web.config` `<customErrors defaultRedirect="~/Error" redirectMode="ResponseRewrite">` with `<error>` for 403/404, plus `<httpErrors existingResponse="Auto">` in `system.webServer` for errors that never reach MVC.

### Frontend Stack

- Bootstrap 5.3.7 + SB Admin 2 theme with custom modern sidebar (`Assets/css/modern-sidebar.css`, `Assets/js/modern-sidebar.js`)
- jQuery 3.7.1
- SweetAlert2 for session notifications and confirmations
- FontAwesome icons
- Select2 (`Assets/js/select2/`, `Assets/css/select2/`) for enhanced dropdowns
- Font Awesome 7 Free: only `Assets/Bootstrap/fontawesome-free/css/all.min.css` + `webfonts/` + `LICENSE.txt` are kept (the SVGs/sprites/js/scss were removed to keep deploys small - don't add them back)
- ClosedXML 0.105.0 for Excel operations (MIT license, no license call needed — chosen over EPPlus 5+/Polyform Noncommercial specifically to keep the project free of any commercial-license obligation)

### Layout Structure

`Views/Shared/_Layout.cshtml` contains the full sidebar navigation with permission-based visibility using `@if (UsuarioSesionHelper.TienePermiso("CODE"))` checks, user profile modal, image upload modal, and client-side session timeout management.

## Important Conventions

- **SQL compatible con SQL Server 2012 o superior (OBLIGATORIO):** todo T-SQL — en strings de C#, stored procedures, scripts de migración y scripts en comentarios — debe correr en SQL Server 2012 (nivel de compatibilidad 110). La instancia actual es 2016 (compat 130), así que algo de 2016+ funcionaría hoy y fallaría en una instancia 2012: no usar la prueba "corre en el servidor". **Prohibido**: `STRING_AGG` (usar `FOR XML PATH` + `STUFF`), `STRING_SPLIT`, `CONCAT_WS`, `TRIM` (usar `LTRIM(RTRIM(...))`), `TRANSLATE`, `CREATE OR ALTER` (usar `IF OBJECT_ID(...) IS NOT NULL DROP ...` + `CREATE`, o `CREATE` + `ALTER`), `DROP ... IF EXISTS` (usar `IF OBJECT_ID('dbo.X','U') IS NOT NULL DROP TABLE dbo.X`), funciones JSON (`OPENJSON`, `JSON_VALUE`, `FOR JSON`, `ISJSON` — serializar en C#), `AT TIME ZONE`, `DATEDIFF_BIG`, `COMPRESS`/`DECOMPRESS`, `SESSION_CONTEXT`, `GREATEST`/`LEAST`, `GENERATE_SERIES`, `DATETRUNC`, tablas temporales de sistema (`FOR SYSTEM_TIME`) e in-memory. **Sí permitido** (2012): `OFFSET/FETCH`, `THROW`, `TRY_CONVERT`/`TRY_CAST`/`TRY_PARSE`, `IIF`, `CHOOSE`, `CONCAT`, `FORMAT`, `EOMONTH`, `DATEFROMPARTS`, `LAG`/`LEAD`/`FIRST_VALUE`, `SEQUENCE`, `DECLARE @x INT = 0`, `+=`. Revisión hecha el 2026-10-09: el código C# y los 36 módulos de la BD (SP/funciones/vistas) ya cumplen.
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

### Opciones que consumen de las tablas `Modelo*` — cápsula "Información modelo Bufins" (OBLIGATORIO)

> **MANDATORY:** toda opción (informe, análisis, consulta, exportación) que **lea datos de las tablas `dbo.Modelo*`** (`ModeloBalance`, `ModeloPYG`, `ModeloBalancePpto`, `ModeloBalanceDiff`, `ModeloFlujoCaja`, `ModeloFlujoEfectivo`, `ModeloLineasNegocio`, `ModeloTesoreriaPpto`) debe llevar en su título la cápsula **"Información modelo Bufins"**, igual que Línea de Tiempo. Avisa al usuario de que esos datos solo existen después de ejecutar el modelo.

Cómo ponerla: dentro de `.powerbi-header-content`, justo después del `<h1 class="powerbi-header-title">`, una sola línea:
```html
<h1 class="powerbi-header-title">@Resources.Strings.X_PageTitle</h1>
@Html.Partial("_ModeloBufinsTag")
```
El partial `Views/Shared/_ModeloBufinsTag.cshtml` usa la clase compartida `.header-tag-modelo` (en `bufins-components.css`) y los textos `Common_ModeloBufinsTag` / `Common_ModeloBufinsTagTooltip` (ya en ambos `.resx`) — no se duplica markup, CSS ni textos por vista. Hoy la llevan: Análisis IA, Informe de Modelos, Línea de Tiempo, Estado de Resultados (PYG) y Balance General. **Ejecución de Modelos (`Datos/Modelo.cshtml`) NO la lleva**: esa pantalla *genera* las tablas, no las consume. Al crear una opción nueva que lea de `Modelo*`, añadir la cápsula y agregarla a esta lista.

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

### Brand loader (wordmark + gradient) - reusable partial

Animated loader built from the Bufins wordmark images (`Assets/img/Bufins_Wordmark_Aqua.png` for dark backgrounds, `Assets/img/Bufins_Wordmark_Dark.png` - purple `#160933` - for light backgrounds) plus the brand gradient. Pure CSS + image, no JS/library. CSS lives in `Assets/css/bufins-components.css` (classes `.l3-*`); it is wrapped as the Razor partial `Views/Shared/_LoaderDots.cshtml` (static logo + 4 bouncing gradient dots), the one wired into the `.overlay-cargando` loading overlays above. It takes a `string` model - `"aqua"` (default, dark/purple backgrounds) or `"dark"` (white/light backgrounds):
```csharp
@Html.Partial("_LoaderDots", "aqua")   @* dark modal/card background *@
```
Use it inside any new `.overlay-cargando-card` or `.modal-confirm .modal-body` that needs a lightweight loading state. (The ring and sweep variants, `_LoaderRing`/`_LoaderSweep`, were removed in the 2026-10 dead-code cleanup because nothing used them.)

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
