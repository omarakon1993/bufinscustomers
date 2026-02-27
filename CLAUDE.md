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

### Authentication & Session Management

Custom session-based auth via `UsuarioSesionHelper` (`Helpers/UsuarioSesionHelper.cs`):
- Session key: `"UsuarioCompleto"`, timeout: 20 minutes
- `[ValidarSesion]` attribute on controllers/actions requiring auth (handles AJAX vs regular requests)
- Client-side session management with SweetAlert2 warnings at 5 minutes remaining (in `_Layout.cshtml`)
- Key methods: `UsuarioActual`, `EstablecerUsuarioEnSesion()`, `LimpiarSesion()`, `ObtenerInfoSesion()`, `ExtenderSesion()`

### Role Hierarchy (3 levels)

The `Usuarios.Admin` field (byte?) defines access levels:

| Value | Role | Description |
|-------|------|-------------|
| 0 | Usuario Normal | Access only to assigned menu options, data isolated to own company (`IdEmpresa`) |
| 1 | Admin de Empresa | Company-level admin, access to assigned menu options, data isolated to own company (`IdEmpresa`) |
| 2 | Super Admin | Full access to everything, bypasses all permission checks, sees all companies |

**Data isolation rule:** Admin 0 and Admin 1 can only see/modify data belonging to their own company (`usuario.IdEmpresa`). This is enforced at the controller level and in `EmpresasViewBagFilter`. Only Super Admin (Admin=2) has cross-company access. Admin 0/1 cannot create or delete companies.

Check with: `EsUsuarioNormal()`, `EsAdminEmpresa()`, `EsSuperAdmin()`, `EsAdministrador()` (returns true for Admin=1)

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
- `ADMIN_CONFIG_EMPRESAS`, `ADMIN_CONFIG_RELACIONAMIENTOS`, `ADMIN_CONFIG_MENU` (Configuración - `ADMIN_CONFIG_MENU` is SoloSuperAdmin)

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
| ReportesController (CRUD) | `~/Views/Configuracion/MaestroReportes.cshtml` |
| ReportesController (embed) | `~/Views/Reportes/Reportes.cshtml` |
| DatosController | `~/Views/Datos/CargueExcel.cshtml`, `~/Views/Datos/Modelo.cshtml` |
| InformeTablasDatosController | `~/Views/Informes/InformeTablasDatos.cshtml` |
| InformeRelacionamientosController | `~/Views/Informes/InformeRelacionamientos.cshtml` |
| AuditoriaCarguesController | `~/Views/Informes/AuditoriaCargues.cshtml` |
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
- **InformeTablasDatosController** - Data tables report (`[ValidarSesion]`)
- **InformeRelacionamientosController** - Relationships report (`[ValidarSesion]`)
- **ModeloController** - Financial model execution (Datos area, `[ValidarSesion]`)
- **AuditoriaCarguesController** - Upload audit trail (no `[ValidarSesion]`, manual checks)

### Configuration System

`ConfiguracionEmpresa` model with sub-configurations: `EmpresasConsolidar`, `Paises`, `Categorias`, `Tipos`, `LineasNegocio`, `Ajuste1`, `Ajuste2`. Managed by `ConfiguracionEmpresaService`.

### Global Filter

`EmpresasViewBagFilter` (`Filters/EmpresasViewBagFilter.cs`) is registered globally in `FilterConfig` and loads all companies into `ViewBag.Empresas` for every request.

### Frontend Stack

- Bootstrap 5.3.7 + SB Admin 2 theme with custom modern sidebar (`Assets/css/modern-sidebar.css`, `Assets/js/modern-sidebar.js`)
- jQuery 3.7.1
- SweetAlert2 for session notifications and confirmations
- FontAwesome icons
- EPPlus 8.0.7 for Excel operations (license set in `Global.asax.cs`)

### Layout Structure

`Views/Shared/_Layout.cshtml` contains the full sidebar navigation with permission-based visibility using `@if (UsuarioSesionHelper.TienePermiso("CODE"))` checks, user profile modal, image upload modal, and client-side session timeout management.

## Important Conventions

- Passwords: Always `.Trim()` before hashing with `ConvertirSha256()`
- Messages between redirects: Use `SetErrorMessage/SetSuccessMessage/SetInfoMessage` (TempData keys: `"ErrorMessage"`, `"SuccessMessage"`, `"InfoMessage"`)
- New controllers must inherit from `BaseController`; new services from `BaseService`
- Use `[ValidarSesion]` at class level on all new authenticated controllers (note: some existing controllers like EmpresaController, UsuarioController, ReportesController, AuditoriaCarguesController lack it and use manual session checks instead)
- For permission checks, use `UsuarioSesionHelper.TienePermiso("CODE")` inline (the `[RequierePermiso]` attribute exists but is not currently used by any controller)
- Excel templates stored in `Assets/Plantillas/`
- Connection string key is `"DefaultConnection"` in Web.config
- File upload limit: `maxRequestLength="1048576"` (1 GB) and `executionTimeout="3600"` (1 hour) — configured for large Excel imports
- EPPlus 8 requires license call at startup: `ExcelPackage.License.SetNonCommercialOrganization("bufinscustomers")` in `Global.asax.cs`
