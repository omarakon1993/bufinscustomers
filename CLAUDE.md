# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Bufins Customers is an ASP.NET MVC 5 web application built on .NET Framework 4.8 for financial data management and reporting. The application manages multiple companies (empresas), users, and their financial configurations with Excel-based data import/export capabilities.

## Build and Development Commands

### Build
```bash
# Build the solution (requires Visual Studio or MSBuild)
msbuild bufinscustomers.sln /p:Configuration=Debug

# Build for release
msbuild bufinscustomers.sln /p:Configuration=Release
```

### Run
```bash
# Run with IIS Express (default port 44339 for HTTPS)
# The application can be launched through Visual Studio or IIS Express directly
```

### Restore Packages
```bash
# Restore NuGet packages
nuget restore bufinscustomers.sln
```

## Architecture

### Core Pattern: MVC with Service Layer

The application follows a layered architecture:

1. **Controllers** (`Controllers/`) - Handle HTTP requests, inherit from `BaseController`
2. **Services** (`Services/`) - Business logic layer, inherit from `BaseService`
3. **Models** (`Models/`) - Data models and POCOs
4. **Views** (`Views/`) - Razor views organized by controller
5. **Helpers** (`Helpers/`) - Utility classes
6. **Filters** (`Filters/`) - Custom action filters
7. **Permisos** (`Permisos/`) - Authorization attributes

### Authentication & Session Management

The application uses a custom session-based authentication system centered around `UsuarioSesionHelper` (Helpers/UsuarioSesionHelper.cs):

- **Session Storage**: User data stored in `HttpContext.Session` with key `"UsuarioCompleto"`
- **Session Timeout**: 20 minutes of inactivity (configurable in Web.config)
- **Validation**: Use `[ValidarSesion]` attribute from `Permisos/ValidarSesionAttribute.cs` on controllers/actions requiring authentication
- **Session Helper Methods**:
  - `UsuarioSesionHelper.UsuarioActual` - Gets current logged-in user
  - `UsuarioSesionHelper.EstablecerUsuarioEnSesion(usuario)` - Sets user in session
  - `UsuarioSesionHelper.LimpiarSesion()` - Clears session
  - `UsuarioSesionHelper.EsAdministrador()` - Checks if current user is admin
  - `UsuarioSesionHelper.ObtenerInfoSesion()` - Gets session activity info

### Base Classes

**BaseController** (`Controllers/BaseController.cs`):
- Provides centralized connection string: `CadenaConexion`
- SHA256 hashing utility: `ConvertirSha256(texto)`
- Message helpers: `SetErrorMessage()`, `SetSuccessMessage()`, `SetInfoMessage()`
- All controllers should inherit from this

**BaseService** (`Services/BaseService.cs`):
- Provides centralized connection string: `CadenaConexion`
- All service classes inherit from this

### Database Access Pattern

The application uses **ADO.NET with stored procedures**:
- Connection string stored in Web.config under `DefaultConnection`
- Database: SQL Server at 190.90.160.168,1433 (bufinscustomers database)
- All database operations use stored procedures (e.g., `sp_RegistrarUsuario`, `sp_ObtenerEmpresas`)
- Standard pattern:
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

### Configuration System

The application has a complex configuration system for financial data (`ConfiguracionEmpresa`):

- **Main Configuration** (`ConfiguracionEmpresa` model):
  - Year of execution, currency signs, monetary units
  - Contains multiple sub-configurations:
    - `EmpresasConsolidar` - Companies to consolidate
    - `Paises` - Countries
    - `Categorias` - Categories
    - `Tipos` - Types
    - `LineasNegocio` - Business lines
    - `Ajuste1`, `Ajuste2` - Adjustments

- **Configuration Service**: `ConfiguracionEmpresaService` handles all configuration CRUD operations

### Key Controllers

- **AccesoController**: Login, registration, authentication (no `[ValidarSesion]`)
- **HomeController**: Dashboard and main views (requires `[ValidarSesion]`)
- **UsuarioController**: User management
- **EmpresaController**: Company (empresa) management
- **ConfiguracionEmpresaController**: Financial configuration management
- **ReportesController**: Report generation and Excel exports
- **DatosController**: Data import/export operations
- **ModeloController**: Model/template management
- **AuditoriaCarguesController**: Upload audit trail

### Global Filters

**EmpresasViewBagFilter** (`Filters/EmpresasViewBagFilter.cs`):
- Automatically loads all companies into `ViewBag.Empresas`
- Used across views for company selection dropdowns
- Applied globally or per-controller

### Excel Integration

The application uses **EPPlus 8.0.7** for Excel operations:
- License configured in `Global.asax.cs`: `ExcelPackage.License.SetNonCommercialOrganization("bufinscustomers")`
- Used for importing/exporting financial data
- Excel templates stored in `Assets/Plantillas/`

### Culture & Localization

- Application is configured for Colombian Spanish: `culture="es-CO"` and `uiCulture="es-CO"`
- Currency default: Colombian Peso (CO$)
- Date/time formats follow Colombian conventions

## Important Patterns & Conventions

### Password Handling
- Passwords are hashed using SHA256 via `BaseController.ConvertirSha256()`
- Hash format: lowercase hexadecimal
- Always trim passwords before hashing

### User Authentication Flow
1. User submits credentials to `AccesoController.Login()`
2. Password is hashed with SHA256
3. Stored procedure validates credentials
4. On success, user object is stored in session via `UsuarioSesionHelper.EstablecerUsuarioEnSesion()`
5. Subsequent requests check session with `[ValidarSesion]` attribute

### Message Passing
- Use `TempData` for messages between redirects
- Keys: `"ErrorMessage"`, `"SuccessMessage"`, `"InfoMessage"`
- Set via `BaseController` helper methods

### Admin vs Regular Users
- Admin flag stored in `Usuarios.Admin` (byte?, 1 = admin)
- Check with `UsuarioSesionHelper.EsAdministrador()`
- Admin users can manage other users and system configurations

## File Structure Notes

- `Assets/` - Frontend assets (CSS, JS, images, Excel templates)
- `SQL/` - Empty directory (SQL scripts may be stored here if needed)
- `Content/` - Legacy Bootstrap/CSS files
- `Scripts/` - Legacy JavaScript libraries
- `Views/Shared/` - Shared layouts and partial views
- `App_Start/` - MVC configuration (routes, bundles, filters)

## Dependencies

Key NuGet packages:
- ASP.NET MVC 5.3.0
- EPPlus 8.0.7 (Excel manipulation)
- Newtonsoft.Json 13.0.3 (JSON serialization)
- Bootstrap 5.3.7
- jQuery 3.7.1
- Microsoft.Bcl.Cryptography 9.0.7

## Connection String Location

The database connection string is in `Web.config`:
```xml
<connectionStrings>
  <add name="DefaultConnection"
       connectionString="Data Source=190.90.160.168,1433;Initial Catalog=bufinscustomers;..."
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

## Session Configuration

In `Web.config`:
- Mode: InProc
- Timeout: 20 minutes
- Cookie name: BUFINS_SessionId
- httpOnlyCookies: true
- Session expiration is tracked in `UsuarioSesionHelper` with warning at 5 minutes remaining

## Security Notes

- Custom session validation via `ValidarSesionAttribute`
- Supports both regular and AJAX requests (returns JSON for AJAX)
- Session expiration warnings sent via response headers (`X-Session-Warning`, `X-Minutes-Remaining`)
- Passwords hashed with SHA256 (consider migration to more secure hashing like bcrypt/PBKDF2)
- SQL injection protected via parameterized stored procedures
