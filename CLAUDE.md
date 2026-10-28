# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Bufins Customers is an ASP.NET MVC 5 web application built on .NET Framework 4.8 for financial data management and reporting. The application manages multiple companies (empresas), users, and their financial configurations with Excel-based data import/export capabilities. Configured for Colombian Spanish (`es-CO`).

## Detalle técnico fuera de este archivo (`docs-claude/`)

Este archivo guarda reglas, convenciones y resúmenes. El detalle fino vive en `docs-claude/` (los HTML de documentación al usuario siguen en `docs/`): **léelo antes de modificar el módulo y actualízalo en la misma tarea**.
- `docs-claude/informes/` — PYG, Balance, Tesorería, Flujos, Indicadores, Histórico de precios.
- `docs-claude/modulos/` — `Home.md`, `IA.md` (integración, costos, presupuesto por empresa), `Auditoria.md` (incluye la lista "ya instrumentado"), `Notificaciones_activas.md`.
- `docs-claude/Guia_CLAUDE_md.html` — guía visual de qué contiene este archivo.

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

**Cambio de contraseña obligatorio (primer inicio de sesión)**: columna `Usuarios.DebeCambiarClave BIT` (`Sql/025_Usuarios_DebeCambiarClave.sql`, manual). Se activa con el checkbox "Exigir cambio de contraseña al primer inicio de sesión" al crear un usuario (`UsuarioController.Registrar(..., ExigirCambioClave)`, marcado por defecto). `Services/ClaveInicialService` lee/escribe la columna sin romper si aún no existe (SQL error 207); `Usuarios.DebeCambiarClave` se carga en `ObtenerUsuarioPorId` (helper) y `ObtenerUsuarioCompletoPorId` (login). `FinalizarLogin` redirige a `AccesoController.CambiarClaveInicial` (`Views/Acceso/CambiarClaveInicial.cshtml`), y el filtro global `Filters/CambioClaveObligatorioFilter` bloquea todo lo demás (AJAX → 403 JSON con `redirectUrl`) salvo esa pantalla, `CerrarSesion`, `ExtenderSesion` y `SetLanguage`. La nueva clave pasa por `PoliticaContrasena` y debe diferir de la actual; al guardar se apaga la marca y se audita (`RegistrarSeguridad`). Pendiente opcional: exigirlo también al restablecer la clave de otro usuario (`UsuarioController.CambiarClave`).

**Doble factor (2FA / TOTP) para TODOS los usuarios** (rama `feature/2fa`, en pruebas locales; sin SMS): interruptor `appSettings["TwoFactorHabilitado"]` (**`false` por defecto** ⇒ login idéntico al de siempre) + script manual `Sql/026_Usuarios_2FA.sql` (`Usuarios.TotpActivo/TotpSecretoCifrado/TotpActivadoEn/TotpUltimoPaso`; **sin códigos de recuperación a propósito**: si el usuario pierde sus métodos, solo un Super Admin puede reiniciarlos, tras verificar su identidad por otro canal; sin el script `DobleFactorService.Obtener` devuelve `Disponible=false` y nadie queda obligado). Flujo: `AccesoController.Login` valida usuario+clave → si 2FA está habilitado **no abre sesión**: guarda un pendiente (`_2fa_<token>` en `HttpRuntime.Cache`, 5 min, cookie `_2fa` HttpOnly) y redirige a `VerificarDobleFactor` (ya tiene autenticador) o `ConfigurarDobleFactor` (alta obligatoria: QR dibujado en el navegador con `Assets/js/qrcode-generator.js`, confirma con un primer código) → `CompletarInicioDeSesion` (audita el login y entrega el token `_lt` de siempre). El código se pide en **cada** login (sin "recordar equipo"). TOTP propio sin NuGet (`Helpers/TotpHelper`, RFC 6238, ±1 intervalo, anti-reutilización con `TotpUltimoPaso`); secreto protegido con `MachineKey.Protect` (aislado en `DobleFactorService.Proteger`). 5 códigos errados descartan el pendiente y cada fallo suma al rate-limit por IP. Reinicio por pérdida del celular: **solo Super Admin** (`UsuarioController.ReiniciarDobleFactor`, botón en el gestor de usuarios, auditado + notifica al usuario); emergencia por SQL documentada en el script 026. Vistas: `Views/Acceso/{_Layout2FA,VerificarDobleFactor,ConfigurarDobleFactor,CodigosRecuperacion}.cshtml`, textos `Login2FA_*`/`Notif_2FA_*`/`Usr_*2FA*`. **Passkeys (WebAuthn) como método adicional** (`Sql/029_UsuarioPasskeys.sql`, claves `PasskeysHabilitado` y `WebAuthnRpId` en `ConfiguracionSistema`, apagado por defecto; requiere el 2FA habilitado): coexisten con la app autenticadora — en `VerificarDobleFactor` el código de la app es lo principal y el botón «Usar passkey» es secundario (`AccesoController.PasskeyOpciones/PasskeyVerificar`, JS `Assets/js/webauthn-bufins.js`). La app autenticadora sigue siendo el método base obligatorio (se fuerza en el primer ingreso y no se puede quitar); las passkeys se agregan/eliminan en **Seguridad** (exige reconfirmar con un código vigente de la app, para que una sesión robada no pueda registrar una passkey propia; avisa por notificación y por correo al usuario) (`SeguridadController`, `Views/Seguridad/Index.cshtml`, enlace en el menú de usuario). Verificación propia sin NuGet: `Helpers/CborLector` + `Helpers/WebAuthnHelper` (attestation `none`, ES256 y RS256, exige UP+UV, valida rpIdHash/reto/origen/firma; el contador solo avisa si no crece) y `Services/PasskeyService` (tabla `UsuarioPasskeys`; máximo por usuario configurable con la clave `PasskeysMaximoPorUsuario` de `ConfiguracionSistema`, 5 por defecto, tope 20). El RP ID es el host de la petición o `WebAuthnRpId`: **si el dominio de producción cambia, las passkeys registradas dejan de servir**. Los reintentos fallidos suman al rate-limit por IP y al tope de 5 intentos del pendiente 2FA. «Reiniciar métodos de autenticación» (Super Admin, gestor de usuarios; avisa por correo al usuario) borra app y passkeys. **Solicitudes de reinicio** (`Sql/030_SolicitudesReinicioAuth.sql`: tabla `SolicitudesReinicioAuth` + opción de menú `ADMIN_SOLICITUDES_SEGURIDAD`, solo Super Admin): el usuario que perdió todos sus métodos usa el enlace «No puedo acceder a mis métodos de autenticación» de `VerificarDobleFactor` (`AccesoController.SolicitarReinicio`; solo con la contraseña ya validada — así no enumera usuarios ni permite spam; una pendiente por usuario) → se avisa a TODOS los Super Admin con notificación interna y correo a su correo registrado (el correo no lleva enlace de aprobación, solo avisa) → `SolicitudesSeguridadController` (`/SolicitudesSeguridad`) las lista con evidencia (IP, navegador, empresa, teléfono) y permite **Aprobar y reiniciar** (exige marcar que se verificó la identidad por otro canal; nadie resuelve su propia solicitud) o **Rechazar**; todo auditado y con aviso por correo al usuario. Nota de librerías: Fido2 ≥3.0 ya no soporta .NET Framework (solo net6+), por eso la verificación es propia; al migrar a .NET 8+ se puede cambiar por la librería oficial aislando `WebAuthnHelper`. **Descubribilidad de las passkeys** (`Helpers/PasskeyAvisoHelper`, aplica solo si el 2FA y las passkeys están habilitados, el 2FA le aplica al usuario —`TwoFactorAlcance`—, ya tiene la app activa y aún no tiene passkey): (1) tras activar la app en el primer ingreso se muestra `OfrecerPasskey` («Agregar passkey ahora» → entra y abre `Seguridad?agregar=1`; «Ahora no» pospone 7 días); (2) franja en el Home (`HomeController.ObtenerAvisoPasskey`, «Configurar ahora / Más tarde (7 días) / No volver a mostrar» vía `SeguridadController.PosponerAvisoPasskey`; la decisión se guarda en `Usuarios.PasskeyAvisoHasta`, `Sql/031_Usuarios_PasskeyAviso.sql` — sin el script la franja no aparece); (3) etiqueta «Nuevo» junto a «Seguridad» en el menú de usuario mientras no tenga passkeys. Pendiente antes de publicar: ejecutar 026 (y 029 para passkeys) en producción, poner `TwoFactorHabilitado=true`, definir el dominio estable (`WebAuthnRpId`), instructivo para usuarios.

**Configuraciones generales BUFINS (`ConfiguracionGlobalController`, Super Admin)**: pestañas General / IA por empresa / Uso y costos IA sobre `dbo.ConfiguracionSistema` (clave/valor). Los parámetros de la pestaña General se agrupan por **tipo** (categoría) administrado en BD: `dbo.ConfiguracionSistemaTipos` (`Nombre`, `NombreEN`, `Icono`, `Orden`, `Activo`) + `ConfiguracionSistema.IdTipo` (`Sql/028_ConfiguracionSistema_Tipos.sql`, manual; siembra IA/Seguridad y clasifica las claves actuales). Botón «Tipos» en la barra de la pestaña (crear/editar/eliminar tipo; eliminar deja sus parámetros en «Otros parámetros»; audita como `AuditoriaTipo.Configuracion`) y selector «Tipo» al crear/editar cada parámetro. Sin el script 028 la pantalla usa las agrupaciones fijas de antes (`ConfiguracionSistemaService.ObtenerTipos(out disponible)`). Un parámetro nuevo sin tipo cae en «Otros parámetros». Claves 2FA: `TwoFactorHabilitado`, `TwoFactorAlcance` (ver Doble factor). El menú se identifica por `MenuOpciones.Controller = 'ConfiguracionGlobal'` (`Sql/027`).

**Supuestos del balance del ppto (`SupuestosBalancePptoController`, `ADMIN_CONFIG_BALANCE_PPTO`, Super Admin + Admin de Empresa)**: `dbo.Rel_BalancePpto` ya NO es global ni se administra en Relacionamientos (esas pantallas la excluyen): vive por empresa (`IdEmpresa` + trazabilidad), solo `Valor` es editable (las filas `CALCULO` son de solo lectura), con Excel, edición en grilla, autocompletado por mes e insights de completitud. Scripts manuales `Sql/038` (datos actuales → ULTRA, resto con `Valor` NULL) y `Sql/039` (`sp_ModeloBalancePpto` filtra por `IdEmpresa`). Toda empresa nueva nace con sus supuestos vacíos (modal de confirmación en Empresas) y Ejecución de Modelos avisa si faltan, enlazando al nombre ACTUAL del menú (leído de `MenuOpciones`, no hardcodeado). Ver `docs-claude/modulos/SupuestosBalancePpto.md`.

**Datos de Pulso Bufins (`PulsoDatosController`, Super Admin)**: pantalla enlazada desde la barra de Configuraciones generales; muestra los datos publicados en bufins.com/pulso-bufins, compara con la fuente (datos.gov.co) y lanza `PulsoEtl.exe` como proceso aparte. Rutas autodetectadas o guardadas en `ConfiguracionSistema` (claves `PulsoEtlExe` / `PulsoDatosDir`), nunca en codigo. Sin SQL. Ver `docs-claude/modulos/PulsoDatos.md`.

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
- **`RequierePermisoAttribute`** (`Permisos/RequierePermisoAttribute.cs`) - Action filter for permission enforcement: `[RequierePermiso("CODE")]` or `[RequierePermiso("CODE1,CODE2")]` (any-of). Super Admin always passes (via `TienePermiso`). On failure: AJAX → JSON `{ success=false, forbidden=true }` + HTTP 403; normal request → redirect to `~/Error/Forbidden`. **Preferred over inline `if (!EsSuperAdmin() && !TienePermiso("CODE"))` checks** — the framework enforces it even when a new action forgets the guard. Used by `UsuarioController` (all `ADMIN_USUARIOS_GESTOR` actions), `HistorialVersionesCarguesController`, `InformeHistPreciosController`. Keep `ViewBag.Puede*` flags (they drive UI visibility, not access).
- **`SoloSuperAdminAttribute`** (`Permisos/SoloSuperAdminAttribute.cs`) - Same shape as `RequierePermiso` but gates on `EsSuperAdmin()`. Applied **at class level** (next to `[ValidarSesion]`) on the controllers that are 100% Super Admin: `MenuOpcionesController`, `ModelosEjecucionController`, `GestorEscenariosController`, `GestorPromptsController`, `GestorCategoriasController`, `GestorGruposController`, `ConfiguracionGlobalController` — their inline `if (!EsSuperAdmin()) return RedirectToAction("Index","Home")` guards were removed. Controllers with mixed gates (Admin de Empresa allowed, or empresa/grupo access — `EmpresaController`, `PermisosController`, `DatosController`, `ConfiguracionEmpresaController`, the Informe* controllers) keep inline checks **by design**, not as debt.
- **`UsuarioSesionHelper.TienePermiso("CODE")`** - Checks permissions using a cached HashSet in session (one DB query per session, not per page load). Super Admin (Admin=2) always returns true.
- **`UsuarioSesionHelper.ObtenerMenuSidebar()`** - Returns cached `List<SidebarCategoriaViewModel>` for dynamic sidebar rendering via `_SidebarMenu.cshtml` partial.
- **`UsuarioSesionHelper.InvalidarCachePermisos()`** - Clears permission/menu cache. Called after saving permissions or on login.

Known permission codes (BD codes, used in sidebar and controllers):
- `DATOS_PLANTILLA_CARGUE`, `DATOS_MODELO_EJECUCION` (Datos)
- `INFORMES_REPORTES_PBI`, `INFORMES_AUDITORIA_CARGUES`, `INFORMES_TABLAS_DATOS`, `INFORMES_RELACIONAMIENTOS`, `INFORMES_AUDITORIA_GENERAL`, `INFORMES_PYG_GERENCIAL`, `INFORMES_BALANCE_GERENCIAL`, `INFORMES_TESORERIA_PPTO`, `INFORMES_FLUJO_CAJA`, `INFORMES_FLUJO_EFECTIVO`, `INFORMES_INDICADORES`, `INFORMES_HIST_PRECIOS`, `INFORMES_LINEAS_NEGOCIO`, `INFORMES_GERENCIA_MENSUAL` (Informes — `INFORMES_INDICADORES` → `InformeIndicadoresController`, global (ver `docs-claude/informes/Indicadores.md`); `INFORMES_TESORERIA_PPTO` → `InformeTesoreriaPptoController`, `INFORMES_FLUJO_CAJA` → `InformeFlujoCajaController` e `INFORMES_FLUJO_EFECTIVO` → `InformeFlujoEfectivoController` (ver `docs-claude/informes/Flujos.md`), todos globales; `INFORMES_HIST_PRECIOS` es un menú **personalizado por empresa** (Churido y Ucrania), ver "Menús personalizados por empresa"; `INFORMES_AUDITORIA_GENERAL` is SoloSuperAdmin, visor de la tabla `Auditoria`; `INFORMES_PYG_GERENCIAL` → `InformePYGController` and `INFORMES_BALANCE_GERENCIAL` → `InformeBalanceController`, neither SoloSuperAdmin/SoloAdminEmpresa, both still pending creation as a row in `MenuOpciones` vía `/MenuOpciones`)
- `ADMIN_USUARIOS_GESTOR`, `ADMIN_EMPRESAS_GESTOR`, `ADMIN_REPORTES_GESTOR` (Administración)
- `ADMIN_CONFIG_EMPRESAS`, `ADMIN_CONFIG_RELACIONAMIENTOS`, `ADMIN_CONFIG_MENU`, `ADMIN_CONFIG_PROMPTS`, `ADMIN_CONFIG_GRUPOS_EMPRESARIALES`, `ADMIN_CONFIG_ESCENARIOS`, `ADMIN_CONFIG_HOJAS_CARGUE`, `ADMIN_CONFIG_BALANCE_PPTO` (Configuración, `SoloAdminEmpresa`; ver «Supuestos del balance del ppto») (Configuración - `ADMIN_CONFIG_MENU`, `ADMIN_CONFIG_PROMPTS`, `ADMIN_CONFIG_GRUPOS_EMPRESARIALES`, `ADMIN_CONFIG_ESCENARIOS` and `ADMIN_CONFIG_HOJAS_CARGUE` are SoloSuperAdmin)

**Habilitar para todos (Gestor de menús)**: cada fila tiene el botón "Habilitar para todos" (`HabilitarParaTodos` → `HabilitarOpcionParaTodosLosUsuarios`) y el pie de la tarjeta tiene **"Habilitar todos los menús"** (`HabilitarTodasParaTodos` → `HabilitarTodasLasOpcionesParaTodosLosUsuarios`): recorre las opciones activas no `SoloSuperAdmin` con la misma regla por opción (respeta `SoloAdminEmpresa` y las personalizadas por empresa, no toca otros permisos ni a Super Admin) y audita una sola vez (`AuditoriaTipo.Permisos`).

**Sidebar — grupos**: un grupo con nombre (`NombreGrupo`) siempre se pinta como desplegable, **aunque el usuario solo tenga una opción dentro** (así "Catálogos y referencias" no cambia de forma según los permisos); solo las opciones SIN grupo (`EsGrupoImplicito`) van como enlace directo.

**Dynamic Sidebar**: The sidebar in `_Layout.cshtml` uses `Html.RenderPartial("_SidebarMenu", UsuarioSesionHelper.ObtenerMenuSidebar())`. Menu options are read from `MenuOpciones` table with columns `NombreGrupo`, `IconoGrupo`, `IconoCategoria`, `OrdenCategoria` for hierarchical rendering. New menu items added to the table auto-appear in the sidebar and permission manager.

**Sidebar view model hierarchy** (built by `MenuOpcionesService.ConstruirMenuJerarquico()`):
- `SidebarCategoriaViewModel` → has `List<SidebarGrupoViewModel>` (grouped by `NombreGrupo`)
- `SidebarGrupoViewModel` → has `List<SidebarItemViewModel>` (individual menu items)
- `SidebarItemViewModel` → `Id`, `Codigo`, `Nombre`, `Controller`, `Action`, `Icono`


### Menús personalizados por empresa (`MenuOpcionesEmpresas`)

Una opción de `MenuOpciones` puede restringirse a una o varias empresas (informes hechos a la medida). Tabla `dbo.MenuOpcionesEmpresas (IdMenuOpcion, IdEmpresa, FechaAsignacion)` — `Sql/023_MenuOpcionesEmpresas_HistPrecios.sql` (script manual; ver también la opción `INFORMES_HIST_PRECIOS` que crea). **Sin filas = opción global** (comportamiento de siempre); **con filas** = solo aplica a usuarios cuya `Usuarios.IdEmpresa` esté en la lista (el Super Admin siempre la ve). Se edita desde el Gestor de menús (`/MenuOpciones`): campo "Personalizada para empresas" (lista de checkboxes) en crear/editar y badge "Personalizada" en la grilla; `MenuOpciones.IdsEmpresas` lleva la lista.
- **Dónde se aplica el filtro** (todo en `MenuOpcionesService`, sin tocar los SP de la BD): `ObtenerMenuParaUsuario` (sidebar), `ObtenerCodigosPermisos` (así `TienePermiso`/`[RequierePermiso]` también la rechazan aunque el usuario tenga la fila en `UsuarioMenuPermisos`), `ObtenerOpcionesConEstado` (la pantalla de permisos no la ofrece a usuarios de otra empresa), `HabilitarOpcionParaTodosLosUsuarios` (el botón "Habilitar para todos" solo la da a usuarios de esas empresas) y `UsuarioController.AsignarPermisosPorDefecto` (alta de usuario / cambio de rol). Núcleo: `ObtenerIdsOpcionesNoAplicables(idUsuario)`.
- `CrearMenuOpcion`/`EditarMenuOpcion` guardan la opción **y** sus empresas en UNA transacción (si falla lo uno, no queda nada: una opción pensada como personalizada nunca queda global por error). Sin la tabla (script 023 sin ejecutar) todo sigue funcionando como global y guardar una restricción da un error claro.
- **Alcance deliberadamente simple**: la restricción mira la empresa propia del usuario (`Usuarios.IdEmpresa`), no el grupo empresarial — el informe personalizado es de esas empresas. El controlador del informe vuelve a validar empresa (`MenuOpcionesService.ObtenerIdsEmpresasDeOpcion(codigo)` ∩ `EmpresaAccesoHelper`) en cada endpoint.
- **Cápsula "Informe personalizado"**: todo informe personalizado lleva en su título `@Html.Partial("_InformePersonalizadoTag")` (clase `.header-tag-personalizado` en `bufins-components.css`, textos `Common_InformePersonalizadoTag*`), igual que la cápsula "Información modelo Bufins" de los que leen `Modelo*`.
- Auditoría: el alta/edición de la opción ya se registra (`AuditoriaTipo.Menu`) y la descripción incluye las empresas.

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
| GestorHojasCargueController | `~/Views/Configuracion/GestorHojasCargue.cshtml` |
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
| InformeTesoreriaPptoController | `~/Views/Informes/InformeTesoreriaPpto.cshtml` |
| InformeFlujoCajaController, InformeFlujoEfectivoController (hijos de InformeFlujoBaseController) | `~/Views/Informes/InformeFlujo.cshtml` (una sola vista, `ViewBag.Tipo`) |
| InformeIndicadoresController | `~/Views/Informes/InformeIndicadores.cshtml` |
| InformeHistPreciosController | `~/Views/Informes/InformeHistPrecios.cshtml` |
| InformeLineasNegocioController | `~/Views/Informes/InformeLineasNegocio.cshtml` |
| InformeGerenciaMensualController | `~/Views/Informes/InformeGerenciaMensual.cshtml` |
| ComentariosGestionController | (sin vista propia: lo consume la tarjeta compartida `~/Views/Shared/_ComentariosGestion.cshtml`) |
| DocumentacionController | (sin vista propia: sirve `App_Data/Documentacion/*.html` al visor `_DocumentacionVisor`) |
| DocumentacionBufinsController | `~/Views/Configuracion/DocumentacionBufins.cshtml` (biblioteca; los documentos los sirve su acción `Ver`) |
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
| SupuestosBalancePptoController | `~/Views/Configuracion/SupuestosBalancePpto.cshtml` |
| PermisosController | `~/Views/Permisos/Gestionar.cshtml` |
| SeguridadController | `~/Views/Seguridad/Index.cshtml` |
| SolicitudesSeguridadController | `~/Views/SolicitudesSeguridad/Index.cshtml` |
| ErrorController | `~/Views/Error/*` (convention — see "Error Pages" below) |

When creating new controllers, use explicit view paths with `~/Views/{area}/{view}.cshtml`. (`ErrorController` is the deliberate exception: it uses convention-based `Views/Error/` so the pages stay minimal and independent.)

### Key Controllers

- **AccesoController** - Login, registration, session management (no `[ValidarSesion]`)
- **HomeController** - Página principal (`[ValidarSesion]` at class level). Ver "Página principal (Home)" más abajo y `docs-claude/modulos/Home.md`. Endpoints: `Index`, `ObtenerNoticias`, `ObtenerAlertas`, `ObtenerAccesosRapidos`
- **UsuarioController** - User CRUD, profile image upload (no `[ValidarSesion]`, manual checks)
- **EmpresaController** - Company management (no `[ValidarSesion]`, manual checks). Campo opcional **Página web** (`Empresas.PaginaWeb` ↔ columna `Empresas.EmpPaginaWeb`, `Sql/019_Empresas_PaginaWeb.sql`): se valida y normaliza en `Helpers/UrlWebHelper.TryNormalizar` (solo http/https, dominio con punto y TLD ≥2, sin espacios/comillas/`<>`/`\`/usuario:clave; sin esquema → `https://`), con la misma regla en el navegador (`data-url-web` en `Empresas.cshtml`). Como `sp_ObtenerEmpresas`/`sp_RegistrarEmpresa`/`sp_EditarEmpresa` no conocen la columna, `EmpresaService` la lee con `AsignarPaginaWeb` y la escribe con un `UPDATE` aparte (`GuardarPaginaWeb`); si la columna no existe el gestor avisa (`Emp_PaginaWebSinColumna`) en vez de dar el dato por guardado. `CrearEmpresa` ahora también rellena `empresa.Id` (lo busca por NIT+nombre) para que la auditoría registre el Id
- **EmpresaLogoController** - Logo de la empresa (`Ver`, `Datos`, `Subir`, `Eliminar`); lo sube/quita el gestor de Empresas y lo consumen los informes. Ver "Logo de la empresa en los informes — ESTÁNDAR".
- **EmpresaPaletaController** / **InformeTemaController** - Colores corporativos por empresa (`Obtener/Guardar/Eliminar`) y opciones/tokens de paleta para los informes. Ver "Tema de colores de los informes — ESTÁNDAR".
- **PermisosController** - Menu option assignment UI for users, accessible by Admin 1 (own company only) and Admin 2 (`[ValidarSesion]`)
- **MenuOpcionesController** - CRUD for menu options, Super Admin only (`[ValidarSesion]`)
- **ConfiguracionEmpresaController** - Financial configuration per company (`[ValidarSesion]`)
- **ConfiguracionRelacionamientoController** - Relationship configuration with Excel upload (`[ValidarSesion]`)
- **ReportesController** - Power BI report embedding and report CRUD (no `[ValidarSesion]`, manual checks)
- **DatosController** - Excel data import/export (`[ValidarSesion]`). `CargarExcel` only stages+validates (see "Validación en dos pasos del cargue de Excel"); `ConfirmarCargue`/`DescartarCargue`/`RevisarCargue` complete the flow
- **InformeTablasDatosController** - Data tables report with AI analysis via OpenAI (`[ValidarSesion]`). Endpoints: `InformeTablasDatos` (view), `ObtenerAnios`, `ObtenerVariables`, `ConsultarDatos`, `ConsultarConIA` (async), `ExportarExcel`
- **InformeRelacionamientosController** - Relationships report (`[ValidarSesion]`)
- **InformePYGController** / **InformeBalanceController** / **InformeTesoreriaPptoController** - Informes gerenciales (`[ValidarSesion]`) sobre `dbo.Modelo*`; endpoints típicos `Index`, `ObtenerMeses`, `ConsultarReporte`, `GenerarInsightsIA`, `PreguntarInforme`, `ExportarExcel`, `EnviarPorCorreo`. Ver "Informes gerenciales y personalizados" y `docs-claude/informes/`
- **InformeFlujoCajaController / InformeFlujoEfectivoController** - Heredan todos los endpoints de `InformeFlujoBaseController` (`FiltrosFlujo`); solo fijan tipo, nombre, función de IA y prompt. Detalle en `docs-claude/informes/Flujos.md`
- **InformeIndicadoresController** - 27 indicadores con semáforo (`[ValidarSesion]`, hereda `BaseController`, `FiltrosIndicadores`). Detalle en `docs-claude/informes/Indicadores.md`
- **InformeHistPreciosController** - Informe PERSONALIZADO Churido & Ucrania (`[ValidarSesion][RequierePermiso("INFORMES_HIST_PRECIOS")]`). Detalle en `docs-claude/informes/HistPrecios.md`
- **InformeLineasNegocioController** - Rentabilidad por línea de negocio (`[ValidarSesion][RequierePermiso("INFORMES_LINEAS_NEGOCIO")]`, lee `ModeloLineasNegocio`). Detalle en `docs-claude/informes/LineasNegocio.md`
- **InformeGerenciaMensualController** - Informe mensual de gerencia: el cierre de UN mes en un documento (`[ValidarSesion][RequierePermiso("INFORMES_GERENCIA_MENSUAL")]`). Detalle en `docs-claude/informes/GerenciaMensual.md`
- **ComentariosGestionController** - Comentarios de gestión de los informes (tarjeta compartida; `[ValidarSesion]`, acceso por `EmpresaAccesoHelper`). Ver `docs-claude/modulos/ComentariosGestion.md`
- **DocumentacionBufinsController** - Biblioteca «Documentación BUFINS» (Configuración, opción `ADMIN_DOCUMENTACION_BUFINS`, `[ValidarSesion][SoloSuperAdmin]`). Ver «Biblioteca Documentación BUFINS» más abajo.
- **ModelosEjecucionController** - Model execution management/configuration (`[ValidarSesion]`)
- **GestorEscenariosController** - CRUD for the `Escenarios` data-scenario catalog, Super Admin only (`[ValidarSesion][SoloSuperAdmin]`). `Eliminar`-equivalent (`Desactivar`) is always a soft-delete. See "Escenarios de datos"
- **GestorHojasCargueController** - Catalog of upload sheets (Z_ → Ini_), their scope (standard / custom per company) and table design (creates/alters `Ini_X` + `Staging_Ini_X`), Super Admin only (`[ValidarSesion][SoloSuperAdmin]`). See "Gestor de hojas de cargue"
- **GestorPromptsController** - IA prompt CRUD, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete)
- **GruposEmpresarialesController** - Business group (holding) CRUD and company assignment, Super Admin only (`[ValidarSesion]`). Actions: `Index`, `Crear`, `Editar`, `Eliminar` (soft-delete), `Gestionar` (assign-companies screen), `GuardarEmpresas` (AJAX). See "Multi-Company Group Access" above.
- **AnalisisIAController** - Standalone AI analysis page; reuses `InformeTablasDatosService` for table/empresa lists; company-scoped for non-Super Admin (`[ValidarSesion]`)
- **AuditoriaCarguesController** - Auditoría de cargues (`[ValidarSesion]` + gate Super Admin/Admin de Empresa en `OnActionExecuting`). Vive como pestaña junto al Historial de versiones; incluye `Detalle(id)` y `DescargarArchivo(id)` (solo Super Admin, auditado). Ver `docs-claude/modulos/Auditoria.md` («Auditoría detallada de cargues») y `docs/Informe_Auditoria_Cargues_Blindaje.html`
- **InformeNavegacionController** - Página-visitas por usuario (`[ValidarSesion]`, gate `EsSuperAdmin() || EsAdminEmpresa()`). Endpoints: `Index`, `Detalle`, `ResumenUsuarios`, `ResumenPaginas`, `ExportarExcel`. Ver "Auditoría (central, navegación y hub)"
- **AuditoriaHubController** - Módulo unificado de auditoría, `Index` renderiza `~/Views/Informes/AuditoriaHub.cshtml`: una sola pantalla con pestañas **Cambios / Navegación / IA** (la auditoría de **Cargues** salió del hub: ahora es una pestaña del módulo de cargues, junto al Historial de versiones, dentro de la página anfitriona `Views/Informes/HistorialCargues.cshtml`), cada una cargada en línea por AJAX (`?embed=1`, layout `_LayoutFragment`). `[ValidarSesion]` + gate `EsSuperAdmin() || EsAdminEmpresa()`. Ver "Auditoría (central, navegación y hub)"

### Página principal (Home) — resumen (detalle en `docs-claude/modulos/Home.md`)

`Views/Home/Index.cshtml` sin modelo; cada bloque se llena por AJAX. Columna izquierda: indicadores (TRM) y noticias financieras con filtro por portal (solo cliente). Columna derecha: **Solicitudes de seguridad** (tarjeta solo Super Admin, solo si hay pendientes, enlaza a `/SolicitudesSeguridad`; `HomeController.ObtenerSolicitudesSeguridad`), **Alertas Bufins** (`HomeController.ObtenerAlertas` → `dbo.sp_ObtenerAlertasHome`, solo lectura; **agregar una alerta = un bloque más en el SP, sin tocar C#**), **Accesos rápidos** (top de páginas del usuario cruzado con su propio sidebar, nunca muestra algo sin permiso) y **Mi consumo de IA** (solo con `IAModuloHelper.Visible()`). `Home/Index` no se registra en la navegación.

### Informes gerenciales y personalizados — resumen (detalle en `docs-claude/informes/`)

> **El detalle técnico de cada informe vive FUERA de este archivo**, en `docs-claude/informes/<Informe>.md` (carpeta de referencia solo para Claude/desarrolladores; `docs/` queda para los HTML de documentación). **Léelo antes de modificar el informe** y mantenlo al día en la misma tarea que el cambio. Esta sección solo resume lo transversal.

**Rasgos comunes** (PYG, Balance, Tesorería, Flujos, Indicadores): leen las tablas materializadas `dbo.Modelo*` (nunca ejecutan los SP del modelo en vivo) y por eso llevan la cápsula "Información modelo Bufins"; estilos `.lt-*`/`.pyg2-*` (`informe-linea-tiempo.css`, `informe-pyg.css`); filtro de rango continuo con atajos compartidos; comparativos; exportación con `_ExportMenu` (Excel · PDF jsPDF puro · imagen · correo auditado); insights de IA vía `IAGateway` con 3 etiquetas fijas por informe que `separarInsights()` usa para armar tarjetas (el prompt por defecto vive en el controlador y `GestorPrompts` lo puede sobrescribir conservando las etiquetas); "Pregúntale a este informe" donde aplica. Las opciones de menú globales se crean desde `/MenuOpciones` (script SQL manual cuando se indica).

| Informe | Controlador → vista | Menú / permiso | Fuente | Detalle |
|---|---|---|---|---|
| Estado de resultados (PYG) | `InformePYGController` → `InformePYG.cshtml` | `INFORMES_PYG_GERENCIAL` (crear en `/MenuOpciones`) | `ModeloPYG` + `Rel_PYG` | `PYG.md` |
| Balance general | `InformeBalanceController` → `InformeBalance.cshtml` | `INFORMES_BALANCE_GERENCIAL` (crear en `/MenuOpciones`) | `ModeloBalance` + `REL_Balance` (foto al mes de corte, sin presupuesto) | `Balance.md` |
| Tesorería del presupuesto | `InformeTesoreriaPptoController` → `InformeTesoreriaPpto.cshtml` | `INFORMES_TESORERIA_PPTO` | `ModeloTesoreriaPpto` (solo año de ejecución) | `Tesoreria.md` |
| Flujo de caja / Flujo de efectivo | `InformeFlujoBaseController` + hijos → `InformeFlujo.cshtml` (`ViewBag.Tipo`) | `INFORMES_FLUJO_CAJA` / `INFORMES_FLUJO_EFECTIVO` | `ModeloFlujoCaja` / `ModeloFlujoEfectivo` (varios años; líneas clave por `Ord`) | `Flujos.md` |
| Indicadores con semáforo | `InformeIndicadoresController` → `InformeIndicadores.cshtml` | `INFORMES_INDICADORES` (Orden 12) | `ModeloPYG` + `ModeloBalance` + `ModeloFlujoCaja`; catálogo de 27 en `IndicadoresCatalogo.cs` | `Indicadores.md` |
| Histórico de precios (Churido & Ucrania) | `InformeHistPreciosController` → `InformeHistPrecios.cshtml` | `INFORMES_HIST_PRECIOS`, personalizado empresas 6 y 8; cápsula "Informe personalizado" | `Ini_HistPrecios_Churido` (semestral; **no usa IA**) | `HistPrecios.md` |
| Rentabilidad por línea de negocio | `InformeLineasNegocioController` → `InformeLineasNegocio.cshtml` | `INFORMES_LINEAS_NEGOCIO` (script `Sql/041`) | `ModeloLineasNegocio` (la línea es el SUFIJO de `Descripcion`; dinámica por empresa) | `LineasNegocio.md` |
| Informe mensual de gerencia | `InformeGerenciaMensualController` → `InformeGerenciaMensual.cshtml` | `INFORMES_GERENCIA_MENSUAL` (script `Sql/041`) | Reutiliza los servicios de PYG, Balance, Flujo de caja, Indicadores y Líneas de negocio | `GerenciaMensual.md` |

**Reglas que no se pueden olvidar sin abrir el detalle**
- PYG: `Mes` es abreviatura española y se ordena en C#, nunca en SQL; los literales de `Descripcion` de KPIs/tendencias no están 100 % confirmados. Etiquetas de IA: Logro / Alerta / Eficiencia.
- Balance: es una foto (nunca sumar meses); cuadre Activo = Pasivo + Patrimonio; etiquetas Liquidez / Solvencia / Capital de trabajo.
- Tesorería: las 3 líneas de saldo se derivan en el servicio (no se leen de la tabla); puente de caja que siempre cuadra; etiquetas Posición de caja / Financiación / Acción recomendada.
- Flujos: relación 19 + 25 + 29 (Caja) = línea 30 (Efectivo); "proyectado" = real hasta el último mes ejecutado y `ValorFuturo` después; etiquetas Generación de caja / Uso del efectivo / Acción recomendada.
- Indicadores: LTM de 12 meses, nunca se anualiza; sin 12 meses de PYG los indicadores con `NecesitaLtmCompleto` no se calculan; agregar un indicador = una línea en `IndicadoresCatalogo.Todos` + 6 claves `Ind_{Codigo}_*` en ambos `.resx` + regenerar su documentación HTML. Etiquetas Fortaleza / Riesgo / Prioridad.
- Indicadores, umbrales propios: `dbo.IndicadoresUmbrales` (`Sql/042`, manual) guarda cortes por empresa y por indicador; solo son editables los indicadores con dirección, no monetarios y sin regla propia (`IndicadoresUmbralesService.EsEditable`). Los edita el Super Admin o el Admin de Empresa desde el botón «Umbrales» (reutiliza el panel lateral de la ficha); validan el orden de los cortes según la dirección, se auditan y `IndicadorValor.UmbralPropio`/`Cortes` los llevan a la pantalla. Además: «Comparar con escenario» (`ValorEsc`) y «Frente a su historia» (`MejorQueFraccion`: % de los demás meses del RANGO en que estuvo peor; exige ≥ 5 meses). **No existe comparación contra presupuesto ni benchmark externo** (a propósito: solo `Modelo*`).
- Histórico de precios: un `Valor` 0 o vacío = "sin precio"; la variación compara los mismos semestres del año previo.
- Líneas de negocio: `ModeloLineasNegocio` no tiene columna de línea; cada fila trae un concepto fijo por `CuentaPUC` (16) y la línea sale de quitar ese concepto del `Descripcion` ("Ingresos Exp" → "Exp"). Costos y gastos son positivos. El informe debe cuadrar con `ModeloPYG` (ingresos y utilidad neta) y lo muestra en una franja.
- Informe mensual de gerencia: no calcula nada propio (las cifras son idénticas a las de cada informe) y cada sección es independiente; si falta una, las demás se muestran.
- Balance: "Comparar contra" admite `ppto`/`proy` (`ModeloBalancePpto.Valor`/`ValorFuturo` del MISMO mes de corte, cruzado por `Descripcion` porque su `Ord` NO es estable). `ModeloBalanceDiff` aún no se usa (pendiente confirmar que sus columnas son el movimiento mensual).
- Cualquier informe nuevo: cápsula "Información modelo Bufins" si lee `Modelo*`, `_ExportMenu`, auditoría de correo, textos en ambos `.resx`, y su archivo `docs-claude/informes/<Nombre>.md`.

### Documentación de informes — ESTÁNDAR

Todo informe (o pantalla compleja) nuevo se entrega con su **documentación**: un HTML de solo lectura que se abre desde la cápsula "Documentación" del título, con marca de agua. **Una sola implementación para todos** (nada de acciones, modales ni CSS propios por informe):

- **Dónde viven los archivos**: `App_Data/Documentacion/Informe_<Nombre>.html` (convención de nombre). **Nunca en `Assets`** (es público: se podría bajar sin sesión, sin marca de agua y sin auditoría). Copia del HTML en `docs/` (repositorio) y en la carpeta de documentación técnica (`Common/Documentacion BUFINS/Tecnica`). En el `.csproj` va como `<Content Include="App_Data\Documentacion\...html" />`.
- **Catálogo único**: `Helpers/DocumentacionCatalogo.cs` — una línea por documento: `Clave` (corta, va en la URL), `Archivo`, `Permiso` (código de menú del informe) y `Titulo` (para la auditoría). **Agregar la documentación de un informe nuevo = guardar el HTML + una línea en el catálogo**; el archivo nunca sale de la petición (solo claves del catálogo).
- **Servidor**: `DocumentacionController.Ver(clave)` (`[ValidarSesion]`): clave inexistente → 404; sin el permiso del informe (el Super Admin pasa; aplica también la restricción por empresa de los menús personalizados) → 403. El contenido lo arma `Helpers/DocumentacionProtegidaHelper.Servir`: rechaza (403) la apertura directa en una pestaña (`Sec-Fetch-Dest: document`), sin caché, CSP propia, **marca de agua repetida** ("BUFINS · CONFIDENCIAL · PROPIEDAD DE BUFINS" + nombre, empresa y fecha; sin hora, IP ni códigos), aviso fijo de confidencialidad al pie, bloqueo de imprimir/guardar/copiar/arrastrar/menú contextual, y **audita cada consulta** (`AuditoriaTipo.Informes` + `AuditoriaAccion.Ver`, entidad `Documentacion.<clave>`). Textos genéricos `Doc_MarcaLeyenda` / `Doc_Aviso`. Desalienta la descarga casual; no impide capturas de pantalla ni las herramientas del navegador.
- **Visor en la página** (3 líneas): `<link href="~/Assets/css/doc-visor.css" />`, `<script src="~/Assets/js/doc-visor.js">`, `@Html.Partial("_DocumentacionVisor", <título del modal>)` y `DocVisor.init({ boton: '#miDocBtn', url: '@Url.Action("Ver", "Documentacion", new { clave = "mi-clave" })' })`. La cápsula del título es `<button type="button" class="header-tag-doc" id="miDocBtn" title="..."><i class="fas fa-book"></i>@Resources.Strings.Common_DocumentacionTag</button>` (junto a "Información modelo Bufins" / "Informe personalizado"). Modal casi a pantalla completa con iframe `sandbox="allow-scripts"`, botón de pantalla completa, cierra con X, Esc o clic fuera. Textos del informe: `{Prefijo}_Doc_ModalTitulo` y `{Prefijo}_DocumentacionTooltip` en ambos `.resx`.
- **ESTILO VISUAL ÚNICO de TODA documentación HTML (OBLIGATORIO)**: cualquier documento nuevo o actualizado (de un informe, de seguridad, técnico, etc.) debe verse igual que `Informe_Indicadores.html` — es el estilo general. Reglas: (1) tokens en `:root` `--bg:#f6f5fb; --card:#fff; --ink:#1c1738; --ink2:#5b5678; --line:#e3e0f1; --brand:#583aff; --chip:#efecff; --grad:linear-gradient(90deg,#6366F1 0%,#3B82F6 35%,#06B6D4 70%,#01E1D7 100%)` (más `--ok/--warn/--mid/--bad`) y su variante oscura bajo `@media (prefers-color-scheme:dark)` (`--bg:#120a2b; --card:#1a1240; --ink:#efecff; --ink2:#a9a3cc; --line:#2d2460; --brand:#8f7bff; --chip:#271d5c`); (2) fuente `"Segoe UI",system-ui,sans-serif` 15px/1.6, contenedor `.w` de 1040 px; (3) `<header>` con fondo `--grad`, texto blanco, `h1` de 27 px y una línea de descripción; (4) `<nav>` fija (sticky) con enlaces tipo píldora a cada sección; (5) `<main class="w">` con `<section id>` numeradas (`<h2>1. …</h2>` + `<p class="lead">`), `h3` en color `--brand`; (6) componentes: `.card`/`.g2`/`.g3` (tarjetas), `.tw`+`table` con encabezado `--grad`, `.note` (franja lateral; variantes `ok`/`info`/`bad`), `.ficha`, `<details>` para preguntas frecuentes, `code`, y — como complemento del mismo sistema — pasos numerados (`ol.steps`), etiquetas de estado (`.tag`) y diagrama de flujo (`.flow`/`.node`); (7) HTML autocontenido (sin fuentes, scripts ni imágenes externas; capturas en base64), con estilo de impresión (sin la barra de navegación). Sin emojis ni otro tema visual. Modelos de referencia: `Informe_Indicadores.html` y `Informe_Seguridad_Blindaje_Inicio_Sesion.html` (en `docs/`). Los documentos de Líneas de negocio y Gerencia mensual se generan con `docs/herramientas/gen_doc_nuevos_informes.py` (que también contiene los parches de Indicadores e Histórico de precios); **no llevan capturas todavía** (hay que correr la app y agregarlas).
- **Contenido mínimo del documento** (HTML autocontenido, tema claro/oscuro con tokens `:root`, sin dependencias externas, capturas incrustadas en base64 porque la CSP solo permite `img-src data:`): para qué sirve y para quién; capturas reales de la pantalla; de dónde salen las cifras (tablas y líneas); cómo se calcula cada cosa y qué pasa cuando faltan datos; filtros; IA (qué se envía y qué no); exportaciones; preguntas frecuentes; límites. Modelos de referencia: `Informe_HistPrecios_vs_Excel.html` e `Informe_Indicadores.html`.
- **Mantenimiento — la documentación SIEMPRE debe quedar al día (OBLIGATORIO)**: al terminar CUALQUIER cambio en una opción que tenga documentación (ver `DocumentacionCatalogo`: hoy Histórico de precios, Indicadores, Rentabilidad por línea de negocio e Informe mensual de gerencia) —nueva función, filtro, cálculo, fórmula, rango, texto, columna, exportación, comportamiento con datos faltantes o cambio visual relevante— hay que **validar el documento contra lo que se hizo y ajustarlo en la misma tarea**. Checklist: (1) ¿alguna sección describe algo que cambió, se agregó o se quitó? (cálculos, tablas/líneas de origen, filtros, IA, exportaciones, preguntas frecuentes, límites); (2) ¿las capturas siguen mostrando la pantalla actual? Si la UI cambió, **volver a tomarlas**; (3) regenerar el HTML; (4) copiarlo a `App_Data/Documentacion/`, `docs/` y la carpeta de documentación técnica (`Common/Documentacion BUFINS/Tecnica`) y comprobar que las tres copias son idénticas; (5) verificar el resultado (abrirlo en el navegador; conteos y textos coinciden con el código/resx); (6) **decirlo en la respuesta final**: "Documentación actualizada: sí/no y qué se ajustó". Si el cambio no afecta lo documentado, se indica "Documentación revisada: sin cambios". No se da una tarea por cerrada con la documentación desfasada.
- **Biblioteca «Documentación BUFINS»** (Configuración → Documentación BUFINS, **solo Super Admin**; `DocumentacionBufinsController` + `Views/Configuracion/DocumentacionBufins.cshtml`, opción de menú `ADMIN_DOCUMENTACION_BUFINS` creada con `Sql/032_Menu_DocumentacionBufins.sql`): reúne la documentación OFICIAL del sistema, organizada por **categoría** e indicando si cada documento es **general** o **de una opción del menú** (con cuál). **Solo contiene lo oficial: hoy la documentación del Informe de indicadores, la del Informe histórico de precios, la de Rentabilidad por línea de negocio y la del Informe mensual de gerencia (agregadas a pedido expreso del usuario el 2026-10-27), el documento «Blindaje del inicio de sesión · Doble factor» y la «Documentación oficial de ciberseguridad» (`Biblioteca/Ciberseguridad_BUFINS.html`, se actualiza tras cada tanda de seguridad); NO se agrega nada más por iniciativa propia — solo lo que el usuario pida explícitamente.** Catálogo único: `Helpers/DocumentacionBibliotecaCatalogo` (`Clave`, `Ruta`, `Titulo`, `Descripcion`, `Categoria`, `EsDeMenu`/`Menu`, `Actualizado`). **Agregar un documento = guardar el HTML en `App_Data/Documentacion/Biblioteca/` (o reutilizar uno de `App_Data/Documentacion/`), registrarlo como `<Content>` en el `.csproj` y añadir UNA línea al catálogo** (y, si es de una categoría nueva, la categoría en `Categorias` y el recurso `DocBuf_Cat_<id>` en ambos `.resx`). Se abre en el MISMO visor modal de los informes (`_DocumentacionVisor` + `DocVisor.abrirUrl(url, titulo)`) y lo sirve `DocumentacionProtegidaHelper` (marca de agua, sin caché, rechazo de apertura directa, auditoría). Solo caben documentos HTML autocontenidos (la CSP del visor bloquea recursos externos); PDF/Word no se muestran. Títulos y descripciones del catálogo en español; la interfaz es bilingüe (`DocBuf_*`).
- **Generación**: `Informe_Indicadores.html` se genera con `docs/herramientas/gen_doc_indicadores.py` (fuente única: las fichas salen de `Strings.resx` y los rangos de `IndicadoresCatalogo.cs`, así que solo las capturas se toman a mano; ver su cabecera para el uso). Un documento nuevo debe tener su generador en `docs/herramientas/` (o ser un HTML escrito a mano guardado en `docs/`), nunca depender de archivos temporales.

### Logo de la empresa en los informes — ESTÁNDAR (detalle en `docs-claude/modulos/LogoEmpresa.md`)

Cada empresa puede tener su logo (gestor de **Empresas**, modal de edición: zona de carga con vista previa y requisitos). Si lo tiene, **todo informe lo muestra junto al nombre de la empresa y lo incluye en TODAS sus salidas** (pantalla, PDF, imagen, Excel y correo), **sin quitar nunca el logo de Bufins**. Una sola implementación compartida; nada propio por informe.

- **Datos**: tabla `dbo.EmpresaLogos` (`Sql/036_EmpresaLogos.sql`, **manual**; una fila por empresa, PNG ya normalizado + `Version` hash). No toca `Empresas` ni sus SP. Sin el script todo funciona sin logos y el gestor avisa (`EmpLogo_SinTabla`). `Empresas.LogoVersion` (la llena `EmpresaService.AsignarLogo`) dice si tiene.
- **Reglas del archivo** (`Helpers/EmpresaLogoHelper.Procesar`, única definición; el navegador solo avisa antes con las mismas constantes): se valida el **contenido real** (firma de bytes; nunca extensión/MIME), PNG o JPG (SVG/GIF/WebP **no**: pueden llevar scripts o no se decodifican), ≤ 2 MB, mínimo 200×50 px, máximo 4000 px por lado y 12 MP, proporción ancho/alto entre 0,9 y 6, archivo completo (IEND / FFD9), no en blanco/liso. Se **re-dibuja** y se guarda como PNG ≤ 600×200 px (nunca se amplía; orientación EXIF aplicada; sin metadatos ni cargas ocultas). Textos de error = claves `EmpLogo_Err_*`; aviso no bloqueante si no tiene transparencia.
- **Endpoints** (`EmpresaLogoController`, `[ValidarSesion]`, acceso siempre por `EmpresaAccesoHelper.TieneAcceso`): `Ver(id)` (PNG, gestor), `Datos(id)` (JSON `{tiene,dataUrl,ancho,alto,version}`; sin logo/sin acceso → `tiene:false`, nunca 404), `Subir`/`Eliminar` (POST, antiforgery, `[RequierePermiso("ADMIN_EMPRESAS_GESTOR")]`, auditados como `AuditoriaTipo.Empresas`/`Editar`).
- **En pantalla** — `Assets/css/empresa-logo.css` + `Assets/js/empresa-logo.js` (`EmpresaLogo.mostrar(idEmpresa, placa)` al pintar el resultado; deja el logo como "actual" para el PDF). Markup: dentro de `.pyg2-head-l` (o `.lt-head-l`) una **placa blanca** `<span class="emp-logo-plate" id="xLogo" hidden><img alt="@Resources.Strings.Common_LogoEmpresaAlt"></span>` a la izquierda de `<div class="emp-logo-texto">` (título + chips). La placa blanca es obligatoria: el logo se lee igual con cualquier fondo y en la captura como imagen.
- **PDF** (jsPDF, síncrono): `EmpresaLogo.pdf(pdf, { xDerecha, y, alto })` dentro del constructor, en la franja oscura de la primera página (devuelve el ancho usado; 0 si no hay logo). Informes gerenciales: `{ xDerecha: W - M, y: 12.5, alto: 14 }`.
- **Imagen**: html2canvas ya lo captura (el logo viaja como data URL, sin CORS).
- **Excel**: `Helpers/ExcelLogoHelper` (`Obtener(idEmpresa)` → null si no tiene y el libro queda **idéntico** al de siempre; `Colocar(ws, logo, columna, fila)`). Hojas con título propio (filas 1-2, tabla desde la 4): `ColocarEnTodas(wb, logo)` antes de `SaveAs` (ancla B1). Hojas sin título (PYG, Balance, Informe de modelos): con logo se reservan `FilasBloque` (3) filas arriba (`int o = …`) y todo se desplaza `+ o` (encabezados, autofiltro, `Freeze`). Línea de tiempo: ancla en `Math.Max(totalColumnas + 1, 5)`. `ExcelChartHelper` calcula el id de forma como `max + 1` (no chocar con el logo).
- **Correo**: `EmailService.EnviarInforme(..., esIngles, byte[] logoEmpresa = null)` → placa blanca bajo la marca Bufins con la imagen **incrustada** (`cid:logo-empresa`); cada `EnviarPorCorreo` pasa `ExcelLogoHelper.Png(idEmpresa)`. El PDF adjunto ya lo lleva.
- **Informe nuevo (OBLIGATORIO)**: placa del logo junto al nombre de la empresa + `EmpresaLogo.mostrar` + logo en el PDF + `ExcelLogoHelper` en el Excel + `logoEmpresa` en el correo, y agregarlo a la lista. **Hoy lo llevan**: Informe de modelos (placa en la cabecera de la tabla; sin PDF/imagen), Línea de tiempo, PYG, Balance, Tesorería, Flujo de caja y de efectivo, Indicadores e Histórico de precios.

### Tema de colores de los informes — ESTÁNDAR (detalle en `docs-claude/informes/Tema.md`)

Todo informe con resultados por empresa permite elegir la **paleta de colores** del contenido (gráficos, tablas, KPIs, chips) y de **todas sus exportaciones** (PDF, imagen, Excel, correo), en una sección propia **«Estilos del informe»** (mismo título `.lt-section-title` que «Rango y agrupación», dentro de una tarjeta resaltada) ubicada en los filtros **justo debajo de «Rango y agrupación»** (o del rango/corte de cada informe) y antes de los botones Generar/Limpiar (en Línea de tiempo, entre el rango y la barra «Comparar con otro escenario»). Por defecto es **Bufins** (los colores de siempre, idénticos píxel a píxel). La elección **no se recuerda** (cada informe inicia en Bufins) y **solo se aplica al pulsar Generar/Consultar**. El **logo de la empresa es independiente** del tema, igual que la marca Bufins (letterhead) y los colores **semánticos** (verde/rojo de variaciones, semáforo de Indicadores, cascada positiva/negativa), que nunca cambian.

- **Opciones de la sección**: **Bufins** (la única predeterminada, seleccionada por defecto) · **Corporativa 1 y 2 de la empresa elegida** (se nombran «{Empresa} 1/2», el nombre NO es personalizable; si no existen aparecen deshabilitadas con la etiqueta «Sin registrar», y sin empresa elegida se indica que hay que elegir una) · **Personalizada** (selector de color **y** código HTML). Mínimos: Principal y Secundario obligatorios; Acento opcional (por defecto ámbar `#D97706`).
- **Una sola fuente de verdad (C#)**: `Helpers/TemaInforme.cs` deriva TODO desde los 3 colores y define el preset Bufins (valores exactos actuales). **Los temas distintos de Bufins usan COLORES FIJOS, sin degradados** (el «degradado» es el principal en las 4 paradas, el correo usa un solo tono oscuro del principal y los fondos suaves son tintes del principal); la serie de 8 colores de los gráficos sale SOLO de tonos del principal, secundario y acento (nunca matices ajenos a la paleta). Derivados: color fuerte (contraste ≥ 4,5 sobre blanco), tintes `T1..T4`, `Soft*`, bloque PDF. El navegador **no reimplementa** el algoritmo: `InformeTemaController.Opciones` (Bufins + las 2 corporativas con `registrada` true/false) y `Resolver` devuelven los tokens. Validación: hex `#RRGGBB`; el Principal debe contrastar ≥ 3:1 con blanco (se usa con texto blanco) — bloquea; aviso si < 4,5 o si Principal ≈ Secundario.
- **Papel de cada color** (los 3 deben VERSE en cada informe; solo en temas distintos de Bufins, que mantiene exactamente lo de siempre): **Principal** = cabeceras, totales, botones/segmentados activos, títulos, serie principal y barras de KPI; **Secundario** = iconos de las tarjetas KPI y de las tarjetas de gráficos, línea que separa las secciones, serie de **comparación** (presupuesto/año anterior: línea y leyenda `--tm-comp`; en Excel/PDF la 2.ª serie) y etiquetas de familia en Indicadores; **Acento** = serie del **escenario comparado**. Tokens de apoyo: `--tm-s-text` (secundario oscurecido para texto/iconos sobre blanco), `--tm-icon-bg`/`--tm-icon-on` (fondo y texto de los iconos), `--tm-wash-s`, `--tm-comp`, `--tm-line-h`. Todo uso nuevo del secundario/acento sigue la regla `var(--tm-…, <valor Bufins>)`.
- **Resolución segura**: `TemaInformeService.Resolver(usuario, tema, p, s, a)` con ids `bufins` | `corp:<idEmpresa>:<1|2>` | `custom`; valida acceso con `EmpresaAccesoHelper` y **cualquier valor inválido/ajeno cae a Bufins** (un tema nunca rompe un informe). Excel y correo SIEMPRE re-resuelven el tema en el servidor (`ExcelTemaHelper.Resolver`); nunca confían en colores del navegador.
- **Colores corporativos por empresa** (`Sql/037_EmpresaPaletas.sql`, **manual**; `dbo.EmpresaPaletas(IdEmpresa, Slot 1|2, Nombre (reservado, siempre NULL), Primario, Secundario, Acento?)`, `EmpresaPaletaService` tolera tabla ausente): modal compartido `Views/Shared/_ColoresCorporativosModal.cshtml` + `Assets/js/colores-corporativos.js` (`ColoresCorp.abrir(idEmpresa, nombre)`), con vista previa en vivo (usa `Resolver`), abierto desde **Empresas** (botón `.btn-paleta` con degradado cónico por fila) y **Configuración de empresas** (botón junto a «Cargar»). `EmpresaPaletaController` (`Obtener/Guardar/Eliminar`, `[RequierePermiso("ADMIN_EMPRESAS_GESTOR,ADMIN_CONFIG_EMPRESAS")]` + `EmpresaAccesoHelper`, auditado `Empresas`/`Editar`). Pensado también para usos futuros (documentos, correos).
- **Receta para un informe (nuevo o existente)**:
  1. Vista: `<link tema-informe.css>` + `<script informe-tema.js>` (siempre con `AssetVersion.Url(...)`: sin versión, el navegador puede seguir usando un JS/CSS viejo en caché y romper el modal o el selector); `@Html.Partial("_TemaInformeSelector", "xxTema")` debajo del bloque de rango/agrupación; `InformeTema.montar({ selector: '#xxTema', empresa: '#xxEmpresa' })` en el document-ready.
  2. Generar: `InformeTema.preparar().then(ok => { if (ok) consultar... })` (false = personalizada inválida: no consultar); al pintar el resultado `InformeTema.aplicar(document.getElementById('xxResultado'))` y `$.extend(filtrosConsultados, InformeTema.parametros())` (campos `Tema/TemaP/TemaS/TemaA`); en Limpiar `InformeTema.reset()`.
  3. CSS: los literales de MARCA (`#583AFF`→`--tm-strong`, `#6366F1`→`--tm-p`, cian→`--tm-s`, gradiente→`--tm-grad`, `rgba(99,102,241,x)`→`rgba(var(--tm-p-rgb,…),x)`, tintes claros `#C7D2FE/#A5B4FC/#818CF8`→`--tm-t2/--tm-t3/--tm-t4`, `#3B2FBF`→`--tm-dk`, `--tm-soft*`, y los «lavados» decorativos de dos tonos →`var(--tm-wash|--tm-wash2, <degradado Bufins>)` que en los temas es un tinte sólido) se escriben SIEMPRE como `var(--tm-…, <literal Bufins>)`: las variables solo se definen en el contenedor de resultado, así filtros y cromo no cambian. Nunca tematizar `.pyg2-brand`/`.lt-brand-mark` (marca Bufins) ni los semánticos. Auditoría de colores sueltos: ningún hex/rgba de matiz cian→violeta fuera de variables (los grises azulados `#8B85A0`, `#5B5570`, `#E4E1F0`… son neutros y se quedan). JS de gráficos: para un tono claro de la marca usar `InformeTema.actual.t3`, NO `serie[i]` (en Bufins `serie` trae verde/rojo).
  4. JS de gráficos: nada de hex de marca; usar `InformeTema.actual.p / .a / .tercero / .strong / .serie[i]` y `InformeTema.alfa(hex, a)`. PDF: `C = InformeTema.pdf(PDF_COL)` y `InformeTema.pdfDegradado()` (solo sustituyen las claves de marca).
  5. Servidor: agregar `Tema, TemaP, TemaS, TemaA` al modelo `Filtros*`; en Excel `ExcelTemaHelper.Principal/Suave/Serie(tema, i)` y `TemaInforme.SinAlmohadilla(...)` para gráficos nativos; en el correo pasar el tema a `EmailService.EnviarInforme(..., logoEmpresa, tema)`.
- **Hoy lo llevan**: Línea de tiempo, PYG, Balance, Tesorería, Flujo de caja y de efectivo, Indicadores, Rentabilidad por línea de negocio, Informe mensual de gerencia, Informe de modelos (cabeceras, pestañas y Excel/correo) e Histórico de precios (series, mapa de calor, PDF, Excel y correo).

### Comentarios de gestión — tarjeta compartida (detalle en `docs-claude/modulos/ComentariosGestion.md`)

Notas del equipo sobre el CIERRE (año/mes) de un informe: `dbo.ComentariosGestion` (`Sql/040`, manual; sin la tabla la tarjeta avisa y el informe sigue igual). **Los leen y escriben todos los usuarios con acceso a la empresa** (`EmpresaAccesoHelper`); editar/eliminar solo su autor o un administrador; el borrado es lógico y todo se audita (`AuditoriaTipo.Informes`). Piezas: `Views/Shared/_ComentariosGestion.cshtml` (`@Html.Partial("_ComentariosGestion", "xxComentarios")`), `Assets/js/comentarios-gestion.js` (`ComentariosGestion.montar({raiz, informe[, todos]})`, `.cargar({idEmpresa, anio, mes, claves})`, `.ultimos()` para el PDF), `ComentariosGestionController`/`Service` (la clave de informe viaja de una lista blanca, `ComentariosGestionService.InformesValidos`). **Hoy la llevan**: Líneas de negocio, PYG, Balance, Tesorería, Flujo de caja y de efectivo, Indicadores e Informe mensual de gerencia (este lista los de TODOS los informes del cierre y los incluye en PDF y Excel). Un informe nuevo con cierre mensual debe llevarla: agregar su clave a `InformesValidos`, la clave `Com_Inf_<Clave>` en ambos `.resx` y los tres pasos de arriba.

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

Import rules: the workbook must contain every sheet of `TablasCargueHelper.MapeoParaEmpresa(idEmpresa)` (the active standard sheets plus the company's custom sheets from the catalog, see "Gestor de hojas de cargue"); unrecognized sheets are ignored; 5 consecutive empty rows terminate data reading.

### Validación en dos pasos del cargue de Excel (staging)

`CargarExcel` **ya no escribe directo en `Ini_*`**. El flujo es: subir → validar en staging → revisar → confirmar (o descartar). Nada real se toca hasta que el usuario confirma explícitamente.

- **Tablas de staging** (`Sql/007_CarguesStaging_CreateTables.sql`): `dbo.CarguesLotes` (cabecera: empresa/año/modo/escenario/usuario/archivo/`Estado`) y `dbo.CarguesLotesErrores` (un renglón por hallazgo: hoja, fila Excel, columna, `Severidad` Error/Advertencia, `CodigoRegla`, mensaje es/en), más un espejo `dbo.Staging_Ini_*` por cada una de las 9 `Ini_*` — clonado dinámicamente del esquema real (`SELECT TOP(0) INTO`) para no tener que mantener tipos a mano, con `IdLote`/`NumeroFilaExcel` agregados y las 3 columnas de ejecución (`IdUsuarioEjecucion_Log`/`FechaEjecucion_Log`/`Observacion_Log`) quitadas (no aplican antes de confirmar).
- **`Estado` de un lote**: `EnValidacion → ValidadoOk | ConAdvertencias | ConErrores → Confirmado | Descartado`. Solo `ValidadoOk`/`ConAdvertencias` se pueden confirmar (`CargueLote.PuedeConfirmar`).
- **Aislamiento entre empresas y candado por llave de cargue**: `IdLote` es la partición universal (BIGINT IDENTITY único global) — todo lo que toca staging o valida filtra por `IdLote`, así que dos empresas nunca comparten filas ni pueden cruzarse, y `RevisarCargue`/`ConfirmarCargue`/`DescartarCargue` exigen `EmpresaAccesoHelper.TieneAcceso(usuario, lote.IdEmpresa)` antes de tocar un lote ajeno. Además, `UX_CarguesLotes_ActivoPorLlave` (`Sql/008_CarguesLotes_UnicoActivoPorLlave.sql`, índice único **filtrado** `WHERE Estado <> 'Confirmado' AND Estado <> 'Descartado'` sobre `(IdEmpresa, Anio, Modo, IdEscenario)`) impide que existan dos lotes **activos** a la vez para la misma llave de cargue — cierra la carrera de dos cargues casi simultáneos para la misma empresa/año/modo/escenario, donde el segundo en confirmarse pisaría lo que dejó el primero. `CargueStagingService.CrearLote` hace primero un pre-check amable (evita el error crudo del índice en el caso normal) y, si de todos modos hay una carrera real, atrapa la violación del índice (`SqlException.Number` 2601/2627) y responde igual de amable — ambos casos devuelven `(exito:false, idLote del lote activo existente, mensaje)`, y `DatosController.CargarExcel` redirige directo a `RevisarCargue` de ese lote en vez de dejar crear uno nuevo encima.
- **`Services/CargueStagingService.cs`** orquesta todo: `CrearLote`, `ValidarLote(idLote)` (llama al SP — ya no arma ningún TVP), `ConfirmarLote` (recibe la `SqlConnection`/`SqlTransaction` del llamador para participar en la misma transacción), `DescartarLote`/`DescartarLotes`/`DescartarLotesPendientesDeUsuario` (todos pasan por `EjecutarBorradoDeLotes`: staging + hallazgos + cabecera en UNA transacción con `XACT_ABORT`, nunca a medias), `PurgarLotesVencidosSiToca` (≤1 vez cada 15 min, en segundo plano, llamada desde `CargueExcel` GET, `RevisarCargue` GET y `CargarExcel`; borra los lotes que cumplan `CondicionVencido` — `EnValidacion` > 70 min (cargue caído), cualquier no confirmado > 2 h, o `Confirmado` sin limpiar — más filas de staging/hallazgos huérfanas; sin job de SQL Agent). **Identidad única**: `IdLote` (BIGINT IDENTITY) nunca se repite — las FK de staging → `CarguesLotes` impiden un `TRUNCATE` que reinicie el contador, y el bulk copy a staging usa `SqlBulkCopyOptions.CheckConstraints` (ninguna fila puede apuntar a un lote inexistente). **Ningún lote debe quedar pegado**: (1) salir de `RevisarCargue` sin Confirmar/Descartar (otra opción, "Subir archivo corregido", cerrar pestaña) dispara `navigator.sendBeacon` → `DatosController.AbandonarCargue` (solo lotes del propio usuario); **recargar la página de cualquier forma también descarta**: la vista detecta `performance` navigation type `reload` y envía `#frmAbandonarCargue` (`porRecarga=true`) → `AbandonarCargue` redirige a Cargue de Excel con `RevisarCargue_DescartadoPorRecarga`; (2) `CargarExcel` descarta antes los lotes pendientes del mismo usuario **para la misma empresa** (cualquier año/modo/escenario; nunca de otra empresa) y `CrearLote` libera los lotes vencidos de su misma llave; (3) fin de sesión — logout (`UsuarioSesionHelper.LimpiarSesion`) o inactividad (`Global.asax Session_End`, sessionState InProc) — descarta los lotes que ESA sesión abrió (`Helpers/CargueLoteSesionHelper`, lista en `Session["CarguesLotesAbiertos"]`); (4) al confirmar, `ObtenerConteoPorHoja` + `LimpiarLoteEnTransaccion` corren dentro de la transacción de confirmación, así nunca queda un lote `Confirmado` con su staging colgado. **Sin cruces por tiempo**: `ConfirmarCargue` llama primero a `CargueStagingService.BloquearLoteParaConfirmar` (UPDLOCK sobre la cabecera del lote dentro de su transacción, verifica empresa/estado y que el staging no esté vacío) antes de borrar nada real; los borrados (`EjecutarBorradoDeLotes`) toman el mismo UPDLOCK al seleccionar los lotes, así un descarte/purga/abandono concurrente espera a que termine la confirmación y omite el lote ya `Confirmado`, o — si ganó el descarte — la confirmación aborta sin tocar las `Ini_*`.
- **`dbo.sp_ValidarCargueStaging`** (`Sql/StoredProcedures/`) — **único SP con todas las reglas de validación**, corriendo contra `Staging_Ini_*` (nunca contra `Ini_*` real, salvo para comparar "lo que entra" contra "lo que ya está guardado"). Toda la validación (DELETE idempotente + bloques + UPDATE de cierre) corre dentro de una transacción explícita (`BEGIN/COMMIT/ROLLBACK TRANSACTION` + `SET XACT_ABORT ON`) para que un fallo a mitad de un bloque no deje el lote "a medio validar". Agregar una regla nueva = un bloque más ahí (etiquetado `BLOQUE N`), sin tocar C#. **A propósito, por ahora solo trae 3 bloques** (el resto de reglas de la primera versión — catálogos genéricos vía TVP, campos obligatorios, duplicados, cuadre débito/crédito, ecuación de cuenta, continuidad mes a mes — se retiraron a pedido del usuario para partir de un SP mínimo e ir agregando de a una): **Bloque 1 `PAIS_INCONSISTENTE`** (país de alguna fila del lote que no existe en el catálogo real `dbo.Paises`), **Bloque 2 `PAIS_VACIO`** (alguna fila trae el país vacío/nulo — una sola advertencia por lote), y **Bloque 3 `DIFERENCIA_UNIDAD_MEDIDA`** (ingresos del año que se carga vs. el año YA GUARDADO más cercano en `Ini_PYG` — no solo ±1 año — usando `dbo.Rel_PYG` con `EXISTS`, no `JOIN`, para no inflar el `SUM` si una cuenta tiene más de un mapeo). **No recibe `@Catalogos`** — ningún bloque actual valida contra listas configurables por empresa; el tipo `dbo.TVP_CatalogoItem` sigue existiendo en `007_CarguesStaging_CreateTables.sql` por si se vuelve a necesitar. No usa `STRING_AGG` (no disponible en esta instancia).
- **`dbo.sp_ConfirmarCargueStaging`** — mueve un lote validado de `Staging_Ini_*` a `Ini_*` con un `INSERT...SELECT` por tabla (columnas resueltas por intersección de `INFORMATION_SCHEMA.COLUMNS`, concatenadas con `FOR XML PATH`+`STUFF` — no `STRING_AGG`, no disponible en esta instancia). **No abre su propia transacción** — corre dentro de la del llamador (`DatosController.ConfirmarCargue`), igual que `CrearSnapshotEnTransaccion`.
- **`DatosController.ConfirmarCargue(idLote)`** hace, en este orden y dentro de una sola transacción: `CrearSnapshotEnTransaccion` (sin cambios) → `EliminarEjecucionDeIni`/`EliminarAnosHistoricosDeIni` (sin cambios) → `sp_ConfirmarCargueStaging` → `RegistrarAuditoria` (sin cambios, tabla `AuditoriaCargues`) → commit. Después del commit sigue corriendo `SP_ValidarPlantillaInicial` como red de seguridad adicional (solo modo ejecución, igual que antes).
- **`GuardarEnIni`** es la misma pieza de siempre (resuelve el esquema destino vía `INFORMATION_SCHEMA.COLUMNS`, así que sirve tanto para `Ini_*` como para `Staging_Ini_*`) con 2 parámetros opcionales nuevos (`idLote`, usado para completar las columnas `IdLote`/`NumeroFilaExcel` cuando la tabla destino las tiene). `CargarExcel` la llama apuntando a `Staging_Ini_*` (vía `TablasCargueHelper.NombreStaging(nombreTablaIni)`); `Z_TablaPUC` (sin tabla `Ini_`/`Staging_Ini_` asociada) sigue guardándose igual que siempre, fuera de este flujo.
- **Vista**: `~/Views/Datos/RevisarCargue.cshtml` — el informe de validación (tabla de hallazgos, badge de errores/advertencias, botón Confirmar deshabilitado mientras `TotalErrores > 0`, modal `.modal-confirm` para Descartar). **Exportar hallazgos** (solo si hay ≥1 error o advertencia): botón destacado `.rc-exp` en la cabecera de la tarjeta de hallazgos con menú Excel / PDF / Copiar como imagen / Enviar por correo. Excel = `DatosController.ExportarHallazgosExcel(idLote)` (ClosedXML, se descarga con `fetch`+blob para no navegar — salir de la página dispara el descarte del lote); PDF = jsPDF puro en el navegador (`construirPdf`); imagen = html2canvas de `#rcCaptura` (tarjetas de estado + hallazgos) → `ClipboardItem`, con respaldo a descarga PNG; correo = modal claro → `DatosController.EnviarHallazgosPorCorreo` (correo registrado del usuario y/o otros, máx. 10; adjunta el Excel generado en el servidor y el PDF que manda la vista, validado como `%PDF` ≤ 8 MB; `EmailService.EnviarHallazgosCargue`). Mismo control de acceso que `RevisarCargue`. Textos `RevisarCargue_Exp_*`/`_Pdf_*`/`_Correo_*`.
- **Resultado del cargue confirmado, en modal**: `Views/Datos/CargueExcel.cshtml` muestra el resultado (`TempData["ResultadoCarga"]`) apenas carga la página, en un SweetAlert2 (`.swal-res-compact`) en vez de solo en el banner/tabla de abajo. Éxito: resumen compacto — chips de empresa/año/modo/escenario, stats grandes de total registros/hojas, y el detalle por hoja (de `ObtenerConteoPorHoja`) en una lista corta; al cerrarlo recarga `CargueExcel` en limpio. Error: mensaje simple, sin recargar (para no perder el detalle por hoja ni el botón "Descargar log"). Los campos estructurados (`NombreEmpresa`/`Anio`/`ModoTexto`/`IdEscenario`/`NotaExtra`) se agregaron a `ResultadoCargaExcel` solo para esto — `DatosController.ConfirmarCargue` los llena; `Mensaje` sigue llevando la frase completa para el banner y `NotificacionesService`.

### Gestor de hojas de cargue (hojas estándar y personalizadas por empresa)

Las hojas `Z_` del Excel de cargue y su tabla destino **ya no están en código**: viven en `dbo.CargueHojas` (`NombreHoja` Z_ → `TablaIni` Ini_, `Alcance` `ESTANDAR`|`PERSONALIZADA`, `Activo`, `Orden`, `Origen`, `VigenteDesde`) y `dbo.CargueHojasEmpresas` (empresas de cada personalizada, `FechaAsignacion`) — script `Sql/021_GestorHojasCargue.sql` (crea las tablas, siembra las 9 estándar + `Z_HistPrecios_Churido` personalizada para Churido 6 / Ucrania 8 desde `dbo.EmpresaPersonalizaciones`, que queda sin uso, y la opción de menú `ADMIN_CONFIG_HOJAS_CARGUE`). Las **columnas** no se guardan en el catálogo: la fuente de verdad es el esquema físico de `Ini_X`/`Staging_Ini_X` (`INFORMATION_SCHEMA`).

- **Gestor (Super Admin)**: `GestorHojasCargueController` (`[ValidarSesion][SoloSuperAdmin]`) → `~/Views/Configuracion/GestorHojasCargue.cshtml` + `Assets/css/gestor-hojas-cargue.css` (prefijo `hc-`), pantalla única por AJAX: lista con filtros (con la lista completa, sin filtro ni búsqueda, las hojas se **reordenan arrastrándolas** como en el gestor de menús → `ActualizarOrden(ids, ordenes)`, orden 10, 20, 30…, debounce + una petición a la vez, auditado), detalle con pestañas General / Columnas / Alcance / Uso y diagnóstico, y asistente "Nueva hoja" (identificación → columnas → alcance → revisar el SQL → crear). **Excepción de tema a pedido del usuario**: en este gestor TODOS los modales son claros — también las confirmaciones (`sw()` en la vista = SweetAlert con `customClass` `hc-swal`, estilos en `gestor-hojas-cargue.css`) y el overlay de proceso (`hc-overlay-claro` + `_LoaderDots "dark"`); "Plantilla de prueba" es el modal `#hcModalPrueba` con vista previa de las hojas. Textos `HojasCargue_*`. Auditado como `AuditoriaTipo.HojasCargue` (el SQL ejecutado va en `valorNuevo`). Sin el script 021 la pantalla muestra el catálogo de respaldo en solo lectura.
- **DDL desde la app** (`Services/CargueHojasService.cs`; el usuario de BD es `db_ddladmin`): crear hoja = `CREATE TABLE Ini_X` + `Staging_Ini_X` + índices en UNA transacción con el `INSERT` del catálogo (misma convención de columnas de sistema que `Sql/020`); si `Ini_X` ya existe se **registra** con su estructura (y se crea su staging si falta). Agregar columna: siempre (Ini + staging). Modificar/eliminar columna o `DROP` de tablas al eliminar una hoja: **solo con tablas vacías y sin módulos SQL que las mencionen** (`sys.sql_modules`; p. ej. `sp_ValidarCargueStaging` nombra columnas de las Staging estándar). `TablaIni` no se renombra. Reglas: hoja `^Z_[A-Za-z0-9_]{1,29}$` (31 = límite de Excel), tabla `^Ini_...` (el prefijo es obligatorio: los SP descubren tablas por `Staging_Ini_%`), tipos de lista blanca (varchar, nvarchar, int, bigint, decimal, money, date, datetime, bit), default validado por tipo, NOT NULL exige default, **`Año` obligatoria** (el cargue borra/versiona/exporta por año). Diagnóstico (Ini vs staging, columnas de sistema, Año/Mes): una hoja con errores no se puede activar.
- **Lectura en toda la app**: `Helpers/CargueHojasCacheHelper` (MemoryCache 5 min, `Invalidar()` al guardar) con **respaldo** = el catálogo anterior en código si el script 021 no se ha ejecutado o la BD falla. `TablasCargueHelper` conserva su API (`MapeoParaEmpresa`, `TablasIniParaEmpresa`, `HojasParaEmpresa`, `HojasPersonalizadasParaEmpresa`, `MapeoCompleto`, `MapeoParaHojas`) pero respaldada por el catálogo; `MapeoZaIni`/`TablasIni` = estándar activas. Operaciones de datos (hojas exigidas en `CargarExcel`, `Eliminar*DeIni`, snapshot, cierre de año) usan `*ParaEmpresa`; `MapeoCompleto` (todas, incluso inactivas) solo para operaciones filtradas por `IdLote` en `CargueStagingService` (`TablasStaging` se lee en cada uso). **Los SP de staging/confirmación no cambiaron**: ya trabajan por `IdLote` y descubren tablas dinámicamente; los bloques 10/13 de `sp_ValidarCargueStaging` validan Año/Mes/Empresa de cualquier `Staging_Ini_*` que tenga esas columnas.
- **Plantilla generada** (`Services/PlantillaCargueService`): base `Assets/Plantillas/PlantillaBUFINS.xlsx` (formato, validaciones, hoja `Datos`) − hojas del catálogo que no aplican + hojas que la base no trae (encabezados = columnas de negocio de `Ini_X`), ordenadas por `Orden`. La usan `DatosController.DescargarPlantilla`, `PlantillaConDatosService` y el botón "Plantilla de prueba" del gestor. Ya **no hay archivos por empresa** (se retiró `App_Data/PlantillasEmpresa/PlantillaBUFINS_Churido.xlsx`).
- **Visibilidad (más estricta que el acceso por grupo, a propósito)**: `PersonalizacionesEmpresaHelper.VisiblesPara(usuario, idEmpresa)` — Super Admin ve todas las personalizadas de la empresa; el resto solo las que **su propia empresa** también tiene. `HojasPlantillaPara` = estándar + visibles (lo que se descarga). `PuedeOperarCargue` bloquea `CargarExcel`/`RevisarCargue`/`ConfirmarCargue` (`Datos_PlantillaPersonalizadaSinPermiso`) a quien no vea todas.
- **Descarga por empresa (sin selector)**: en Cargue de Excel los botones "Plantilla" y "Plantilla con datos" exigen empresa seleccionada (bloqueados con tooltip hasta elegirla) y entregan SIEMPRE la plantilla de esa empresa: `DatosController.HojasParaDescarga(idEmpresa)` = `PersonalizacionesEmpresaHelper.HojasPlantillaPara` (estándar + personalizadas visibles; Super Admin ve todas las de la empresa). Ya no existe el modal ni el parámetro `plantilla`. `DescargarPlantilla` sin empresa válida o si la generación falla → mensaje (`Datos_PlantillaSinEmpresa` / `Datos_PlantillaError`) y vuelve a Cargue de Excel, nunca entrega la base como respaldo.
- **Rollback**: restaura todas las tablas que traiga el snapshot de la versión (lista blanca = catálogo) y exige el snapshot de cada hoja que aplique hoy a la empresa con `VigenteDesdePara(empresa)` ≤ `FechaCargue` (vigencia de la hoja / fecha de asignación, que se renuevan al activar, volver estándar o asignar). Con el seed (estándar en 1900, Churido con su fecha real) el comportamiento es el de antes.
- **`GuardarEnIni`**: además de numéricas y texto, convierte explícitamente columnas `date/datetime/bit` (`ConvertirCeldaFecha`/`ConvertirCeldaBit`); solo las tienen hojas diseñadas en el gestor.
- **Agregar una hoja** = desde el gestor (sin código ni despliegue). Reglas de validación propias de esa hoja = un bloque nuevo en `sp_ValidarCargueStaging`.

### Historial de Versiones de Cargues (rollback)

Every successful Excel upload (ejecución **and** histórico) writes a version row to `HistorialVersionesCargues` plus one `SnapshotsCargues` row per `Ini_` table, inside the same DB transaction as the upload. `HistorialVersionesCarguesService`:
- **Snapshot is atomic**: wrapped in a SQL savepoint (`CrearSnapshotEnTransaccion` → `CrearSnapshotInterno`). If it fails mid-way it rolls back only its own work and the upload continues **without** a history row — never a partial version. `SerializarTabla` no longer swallows errors (a real read failure aborts the whole snapshot; a genuinely empty table serializes to `"[]"`).
- **Snapshot payload is GZip-compressed**: `Comprimir`/`Descomprimir` in the service GZip **UTF-8** bytes; stored in `SnapshotsCargues.DatosGzip` (`VARBINARY(MAX)`), `DatosJson` left `''`. Rollback reads `DatosGzip`, falling back to `DatosJson` for any legacy plain-text row. Requires the column: `ALTER TABLE dbo.SnapshotsCargues ADD DatosGzip VARBINARY(MAX) NULL;`. Note: T-SQL `COMPRESS(DatosJson)` would GZip the UTF-16 bytes of an `nvarchar` — not readable by `Descomprimir` — so never populate `DatosGzip` from SQL.
- **Retention**: `MaxVersionesPorLlaveCargue = 2` (const; renamed from `MaxVersionesPorEscenario` once "Escenario" became a real business concept — see "Escenarios de datos" below — to avoid the name collision). Per **llave de cargue** = (IdEmpresa, Anio, Modo, IdEscenario), only the 2 most recent versions are kept — ranked `EsVersionActual DESC, FechaCargue DESC` so the active version is never purged even after a rollback. `PurgarVersionesAntiguas` runs on every upload, deletes the surplus versions **and their `SnapshotsCargues` children explicitly** (no reliance on `ON DELETE CASCADE`) and sweeps any orphan snapshot rows. There is no time-based purge and no UI maintenance action — changing the const takes effect for each llave de cargue on its next upload.
- **Rollback** (`EjecutarRollback`) aborts (returns false, no changes) if the version is missing any expected `Ini_` snapshot, instead of partially restoring.
- **Qué guarda cada versión**: el snapshot se toma ANTES del DELETE del cargue, o sea guarda el estado **previo** a ese cargue. Restaurar la versión de un cargue = deshacer ese cargue; restaurar la versión «Vigente» (la del último cargue) es válido y ya no se bloquea. El modal de la pantalla lo dice así.
- **Respaldo automático antes de restaurar** (`Sql/034_RespaldosRestauracion.sql`, manual): `EjecutarRollback(..., out idRespaldo)` guarda en `dbo.RespaldosRestauracion` los datos que va a reemplazar, en la MISMA transacción y antes de borrar (si falla por un error real, no restaura; si la tabla no existe, restaura como antes). `RecuperarRespaldo` aplica un respaldo y guarda otro (ir y volver). Retención `Cargue_RespaldosRestauracion` (3, rango 1-10) por llave; no cuenta en el límite de 2 versiones. UI: tarjeta «Respaldos automáticos antes de restaurar» (botón Recuperar) en Historial de versiones. Auditado `Cargues`/`Restaurar`.
- **Protección de cargues y auditoría detallada** (`Sql/033`, manual): `CargueImpactoService` (parámetros `Cargue_UmbralReemplazoPct` 50, `Cargue_UmbralBloqueoPct` 10 [`Sql/035`], `Cargue_ArchivosConservados` 5 y `Cargue_RespaldosRestauracion` 3 [`Sql/034`]), archivo sin filas del año bloqueado, confirmación reforzada por reducción fuerte, detalle por tabla (`CargueDetalleAuditoria`) y últimos N Excel (`CargueArchivos`). **Regla de oro: nunca dejar un año vacío** (cargue, Deshacer y Recuperar usan la MISMA regla `ImpactoCargue`: < `Cargue_UmbralBloqueoPct` 10 % de cualquier tabla o año en 0 = bloqueado para TODOS, incluido Super Admin; entre 10 % y 50 % = casilla de confirmación; segunda barrera de recuento dentro de la transacción; cierre de año exige escribir el año). Detalle y mantenimiento en `docs-claude/modulos/Auditoria.md`; guía de usuario final (solo cómo funciona hoy: nada de historia ni detalle técnico) en la biblioteca «Documentación BUFINS» (clave `cargues-auditoria`) y en `docs/Informe_Auditoria_Cargues_Blindaje.html`. **Si cambia algo de esto, actualizar ese documento (3 copias iguales: `docs/`, `App_Data/Documentacion/Biblioteca/`).**

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
- **Deliberately out of scope**: `InformeTablasDatosController` (hoy solo el backend AJAX de Análisis IA),
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


### IA (AI) — resumen (detalle en `docs-claude/modulos/IA.md`)

> **REGLA DE DATOS (MANDATORY):** la IA **solo puede tomar datos de las tablas `dbo.Modelo*`** (`ModeloBalance`, `ModeloBalanceDiff`, `ModeloBalancePpto`, `ModeloFlujoCaja`, `ModeloFlujoEfectivo`, `ModeloLineasNegocio`, `ModeloPYG`, `ModeloTesoreriaPpto`), nunca de `Ini_*` ni de ninguna otra tabla. Se aplica con `InformeTablasDatosService.ObtenerTablasModelos()` (solo modelos activos); `ConsultarConIA` rechaza otras tablas y `AnalisisIAController` solo lista esas. Cualquier punto de IA nuevo debe cumplirla.

> **IAGateway es el punto único de ejecución (MANDATORY):** un punto de IA nuevo NO llama a `IAService` directo. Hace: (1) `ValidarAccesoIA(usuario, esAdmin, idEmpresa, IAFuncion.X)` (helper de `BaseController`; devuelve el JSON de error traducido o `null`), (2) arma el `IAConsultaRequest`, (3) `await new IAGateway().EjecutarAsync(new IASolicitud { Funcion = IAFuncion.X, Traducir = R, Idioma = IdiomaIA, ... })`. Agrega una constante a `IAFuncion` para que el consumo quede medido (`AuditoriaAnalisisIA`, `dbo.IAUsoLog`, `dbo.IAUsoMensual`).

- **Proveedor**: OpenAI (`IAService`; modelo `OpenAIModel`, default `gpt-4o`; clave `OpenAIApiKey` en `ConfiguracionSistema`, que manda sobre Web.config). Cambiar de proveedor solo toca `IAService.cs`. Parámetros leídos con `ConfiguracionSistemaService`.
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

## Deuda técnica y pendientes conocidos

- **Tablas de BD sin uso en el código** (se pueden borrar con script manual cuando decidas): `dbo.DashboardTarjetas` (widgets del Home eliminados), `dbo.EmpresaTablasResumenIA` (resumen IA del Home retirado; `Sql/Limpieza_CodigoMuerto_2026-10-03.sql` la elimina). `dbo.EmpresaPersonalizaciones` **no** es huérfana todavía: `CargueHojasService` la usa como respaldo si no existe el catálogo `CargueHojas` (script 021).
- **Opciones de menú por crear en `/MenuOpciones`**: `INFORMES_PYG_GERENCIAL` e `INFORMES_BALANCE_GERENCIAL` (los informes funcionan, solo falta la opción para que aparezcan en el sidebar) y las que cada informe marque como pendientes en `docs-claude/informes/`.
- **Auditorías antiguas separadas**: `AuditoriaCargues` y `AuditoriaAnalisisIA` (con servicio y vista propios) no se han migrado a `Auditoria` como `Tipo`.
- **Botón Exportar duplicado**: `Datos/RevisarCargue.cshtml` conserva su copia inline `.rc-exp*`; migrarla a `_ExportMenu`. Otros pendientes de migrar: Cargue de Excel, módulo de Auditoría, Relacionamientos, PUC, Variables PBI, Navegación y Análisis IA.
- **Scripts `Sql/*.sql`**: los que documenta este archivo son pasos manuales; no asumas que ya corrieron en cada entorno. Pendientes de la tanda de informes (2026-10): `040_ComentariosGestion.sql`, `041_Menu_LineasNegocio_GerenciaMensual.sql` y `042_IndicadoresUmbrales.sql`.

## Important Conventions

- **SQL compatible con SQL Server 2012 o superior (OBLIGATORIO):** todo T-SQL — en strings de C#, stored procedures, scripts de migración y scripts en comentarios — debe correr en SQL Server 2012 (nivel de compatibilidad 110). La instancia actual es 2016 (compat 130), así que algo de 2016+ funcionaría hoy y fallaría en una instancia 2012: no usar la prueba "corre en el servidor". **Prohibido**: `STRING_AGG` (usar `FOR XML PATH` + `STUFF`), `STRING_SPLIT`, `CONCAT_WS`, `TRIM` (usar `LTRIM(RTRIM(...))`), `TRANSLATE`, `CREATE OR ALTER` (usar `IF OBJECT_ID(...) IS NOT NULL DROP ...` + `CREATE`, o `CREATE` + `ALTER`), `DROP ... IF EXISTS` (usar `IF OBJECT_ID('dbo.X','U') IS NOT NULL DROP TABLE dbo.X`), funciones JSON (`OPENJSON`, `JSON_VALUE`, `FOR JSON`, `ISJSON` — serializar en C#), `AT TIME ZONE`, `DATEDIFF_BIG`, `COMPRESS`/`DECOMPRESS`, `SESSION_CONTEXT`, `GREATEST`/`LEAST`, `GENERATE_SERIES`, `DATETRUNC`, tablas temporales de sistema (`FOR SYSTEM_TIME`) e in-memory. **Sí permitido** (2012): `OFFSET/FETCH`, `THROW`, `TRY_CONVERT`/`TRY_CAST`/`TRY_PARSE`, `IIF`, `CHOOSE`, `CONCAT`, `FORMAT`, `EOMONTH`, `DATEFROMPARTS`, `LAG`/`LEAD`/`FIRST_VALUE`, `SEQUENCE`, `DECLARE @x INT = 0`, `+=`. Revisión hecha el 2026-10-09: el código C# y los 36 módulos de la BD (SP/funciones/vistas) ya cumplen.
- Passwords: Always `.Trim()` before hashing. Use `HashearContrasena()` (BCrypt, work factor 12) for new passwords. `ConvertirSha256()` is kept only for the SHA256→BCrypt migration path in login — do NOT use it for new code. `VerificarContrasena()` handles both formats transparently. **Strength policy is centralized in `Helpers/PoliticaContrasena.cs`** — `Validar(clave, usuario, out errorKey, comprobarFiltracion)` returns a resx *key* (caller does `R(errorKey)`): min 12 chars, complexity, no 3× repeats, not a common password, must not contain the username, and (unless `HibpCheckEnabled=false` in Web.config) not present in Have I Been Pwned's Pwned Passwords (k-anonymity range API, fail-open on any network error). Used by `AccesoController` (Registrar / RestablecerClave) and `UsuarioController.EsClaveSegura` (thin wrapper). The login POST never calls it.
- Client IP: use `Helpers/ClientIpHelper.ObtenerIp()` (or the `HttpRequestBase` overload) — not `Request.UserHostAddress` directly. It honours `X-Forwarded-For` / `CF-Connecting-IP` **only** when the direct connection comes from an IP/CIDR listed in `appSettings["TrustedProxies"]` (empty by default ⇒ identical to `UserHostAddress`); `TrustCloudflareHeader` gates the CF header. Wired into `AccesoController` rate-limiting/audit, `AuditoriaService`, `AppLogger`.
- Login timing: the login POST always runs one BCrypt verify — against `HASH_SENUELO` when the identifier doesn't exist — so "unknown user" and "wrong password" take the same time (no user enumeration by timing).
- Login per-IP rate limit: **`Services/RateLimitLoginService.cs`** (`Comprobar` / `RegistrarFallo` / `Limpiar`), state in `dbo.IntentosLoginIP` (create-table script in the file header) so it survives app-pool recycles and works multi-node, with an in-memory `HttpRuntime.Cache` fallback if the table is missing / DB errors. Threshold `LoginRateLimit_Umbral` (default 8) fails → IP blocked 15 min. A system-wide spike (`SUM(Intentos)` in the last 10 min ≥ `LoginRateLimit_UmbralGlobal`, default 100) flips **defensive mode**: per-IP threshold drops to 3 and Super Admins get a notification (≤1×/30 min). `AccesoController` no longer keeps its own `_rl_` cache helpers.
- Security-audit noise: failed logins against a *nonexistent* identifier are logged to `AppLogger` on every hit but written to the `Auditoria` table at most once per IP per `AUDIT_THROTTLE_MIN` (10) min; `AuditoriaService.PurgarSeguridadAnonimaAntigua(dias)` (called opportunistically from the login path, ≤1×/day) deletes anonymous `SEGURIDAD` rows older than 90 days. A *real* account getting locked still writes every event **and** fires internal notifications (see notifications table).
- Errors returned to the browser as JSON: use `BaseController.MensajeError(ex)` instead of `ex.Message` — it logs the full exception with a reference code and only shows technical errors (system SQL, null, I/O, timeouts) as a generic translated text (`Err_InternoRef`); business-rule messages pass through unchanged. CSP: the enforced policy still allows `unsafe-inline`; a stricter `Content-Security-Policy-Report-Only` reports to `CspReportController` (`CSP-REPORT` in `App_Data/logs`). Never put user data in `Html.Raw`; pass strings to JS with `HttpUtility.JavaScriptStringEncode`. Profile images are validated by byte signature (`UsuarioController.TipoImagenPorFirma`).
- Messages between redirects: Use `SetErrorMessage/SetSuccessMessage/SetInfoMessage` (TempData keys: `"ErrorMessage"`, `"SuccessMessage"`, `"InfoMessage"`)
- New controllers must inherit from `BaseController`; new services from `BaseService`
- Use `[ValidarSesion]` at class level on all new authenticated controllers (note: some existing controllers like EmpresaController, UsuarioController, ReportesController, AuditoriaCarguesController lack it and use manual session checks instead)
- For permission checks, prefer `[RequierePermiso("CODE")]` on the action/controller (see "Menu-Based Permissions System"); use `UsuarioSesionHelper.TienePermiso("CODE")` inline only for conditional logic or mixed gates.
- Excel templates stored in `Assets/Plantillas/`
- Connection string key is `"DefaultConnection"` in Web.config
- File upload limit: `maxRequestLength="102400"` (100 MB) and `executionTimeout="3600"` (1 hour). IIS-level limit `maxAllowedContentLength="104857600"` (100 MB) in `system.webServer`.
- **i18n OBLIGATORIO — SIEMPRE en ambos idiomas:** Cada string visible para el usuario (vistas, JS, controladores) debe agregarse a AMBOS archivos de recursos antes de implementar la UI: `App_GlobalResources/Strings.resx` (es-CO) y `App_GlobalResources/Strings.en-US.resx` (en-US). Nunca hardcodear texto en vistas ni JS. Ver sección "Internationalization (i18n)" para detalles completos.
- **Estilos — SIEMPRE usar el sistema de diseño del sitio:** Toda vista nueva o modificada DEBE seguir los mismos estilos visuales del sitio. Ver sección "UI Style System — MANDATORY" para la referencia completa.
- **Notificaciones internas — PREGUNTAR SIEMPRE:** Al implementar cualquier feature nuevo que tenga un resultado observable (cargue, exportación, ejecución, creación/eliminación de entidades, rollback, etc.), preguntar explícitamente al usuario si desea agregar notificaciones internas para esa acción antes de cerrar el task. Si el usuario dice sí, agregar las llamadas a `NotificacionesService.Crear(...)` en el controller correspondiente, las claves a ambos `.resx`, y actualizar `docs-claude/modulos/Notificaciones_activas.md`.
- **Auditoría — PREGUNTAR/SUGERIR SIEMPRE:** Al crear o modificar cualquier acción que cambie estado persistente (crear/editar/eliminar entidades, guardar configuración, asignar permisos/empresas, cierres, rollbacks, ejecuciones, eventos de seguridad, etc.), sugerir explícitamente al usuario registrarla en la auditoría central antes de cerrar el task. Si acepta: añadir una línea `new AuditoriaService().RegistrarCambio(AuditoriaTipo.X, AuditoriaAccion.Y, entidad, entidadId, descripcion, valorAnterior, valorNuevo, idEmpresa)` tras la operación exitosa (o `RegistrarSeguridad(...)` para login/bloqueos), crear el código en `AuditoriaTipo` si hace falta, y actualizar la lista "Ya instrumentado" en `docs-claude/modulos/Auditoria.md`. `AuditoriaService` nunca lanza, así que no se envuelve en try/catch.
- **Documentación — ¿AGREGARLA A LA BIBLIOTECA «Documentación BUFINS»? PREGUNTAR SIEMPRE (OBLIGATORIO):** cada vez que se cree documentación nueva dentro de una opción (o cualquier documento nuevo), preguntar explícitamente si se desea agregarla a la biblioteca (Configuración → Documentación BUFINS). Por defecto NO se agrega: la biblioteca solo tiene lo oficial y lo que el usuario pida expresamente (ver «Biblioteca Documentación BUFINS»).
- **Documentación — PREGUNTAR SIEMPRE (OBLIGATORIO):** cada vez que el usuario pida crear algo nuevo con pantalla propia (informe, análisis, gestor o módulo), **preguntar explícitamente si quiere incluir su documentación** y, si acepta (o si ya lo pidió), entregarla con el estándar definido en "Documentación de informes — ESTÁNDAR": HTML en `App_Data/Documentacion/Informe_<Nombre>.html` (nunca en `Assets`), una línea en `DocumentacionCatalogo`, cápsula "Documentación" en el título, visor `_DocumentacionVisor` + `DocVisor`, marca de agua y auditoría vía `DocumentacionController`/`DocumentacionProtegidaHelper`, textos en ambos `.resx`, y copia en `docs/` y en la carpeta de documentación técnica. No crear acciones, modales ni CSS de documentación propios. Si se modifica un informe que ya tiene documentación, preguntar si se regenera.
- **Documentación — MANTENERLA ACTUALIZADA (OBLIGATORIO):** cada vez que se cree, modifique o amplíe algo en una opción que ya tiene documentación (informe, cálculo, filtro, fórmula, rango, texto, exportación, pantalla), **validar y ajustar su documentación en la misma tarea** (secciones, capturas, tablas de cálculo; regenerar y copiar a `App_Data/Documentacion`, `docs/` y la carpeta técnica) y **reportar en la respuesta final** si quedó actualizada o si se revisó sin cambios. Pasos y checklist en "Documentación de informes — ESTÁNDAR → Mantenimiento".
- **Informes — botón Exportar estándar (OBLIGATORIO):** todo informe o consulta con resultados (nuevo o que se modifique) DEBE exportar con el botón desplegable compartido `_ExportMenu` (Excel · PDF · Copiar como imagen · Enviar por correo): mismo diseño, mismo menú, mismo modal de correo (`_ExportCorreoModal`) y mismos textos `Common_Exp_*`/`Common_ExpCorreo_*`, en TODO el sitio. Nunca crear botones sueltos de Excel/PDF/correo ni un menú de exportación propio. Si el informe no tiene alguna salida (p. ej. una grilla sin PDF) se oculta con `Opciones`, no se reemplaza el componente. El envío por correo siempre pasa por `InformeCorreoHelper` + `EmailService.EnviarInforme` y se audita (`AuditoriaTipo.Informes`). Receta completa en "Botón Exportar desplegable — componente ESTÁNDAR para informes".
- **Informes — logo de la empresa (OBLIGATORIO):** todo informe o consulta por empresa (nuevo o que se modifique) debe mostrar el logo de la empresa consultada junto a su nombre y llevarlo en TODAS sus exportaciones (PDF, imagen, Excel y correo), con las piezas compartidas (`empresa-logo.js/.css`, `ExcelLogoHelper`, `EnviarInforme(..., logoEmpresa)`), sin quitar el logo de Bufins y sin cambios cuando la empresa no tiene logo. Receta y reglas del archivo en "Logo de la empresa en los informes — ESTÁNDAR".
- **Informes — tema de colores (OBLIGATORIO):** todo informe con resultados por empresa (nuevo o que se modifique) debe incluir el selector «Paleta de colores del informe» al final de sus filtros y respetar el tema en pantalla, gráficos, PDF, imagen, Excel y correo (Bufins por defecto, sin recordar la elección, logo y semánticos intactos), con las piezas compartidas (`TemaInforme`, `informe-tema.js`, `_TemaInformeSelector`, `ExcelTemaHelper`). Receta en «Tema de colores de los informes — ESTÁNDAR».
- **Modales de confirmación/alerta y overlays de carga — SIEMPRE con el tema oscuro:** Todo modal nuevo de confirmación, eliminación, alerta o confirmación de guardado DEBE llevar la clase `modal-confirm` en su `.modal` exterior (nunca en modales de crear/editar/gestionar, que se quedan con el look claro de Bootstrap de siempre). Todo overlay nuevo de "cargando/procesando" de pantalla completa DEBE usar las clases `.overlay-cargando`/`.overlay-cargando-card`/`.overlay-cargando-icon`/`.overlay-cargando-title`/`.overlay-cargando-text`/`.overlay-cargando-timer` en vez de estilos inline propios. Todo `Swal.fire(...)` hereda el tema oscuro automáticamente, sin nada que hacer. Ver sección "Confirmation/alert modals — unified dark theme" para la referencia completa y ejemplos de markup. **Excepción (a pedido del usuario): las opciones de cargue — Cargue de Excel, Revisar cargue, Historial de versiones de cargues y Auditoría de cargues — llevan TODOS sus modales/avisos/overlays en tema CLARO**: cada una de esas vistas enlaza `Assets/css/cargues-claro.css` y agrega `document.body.classList.add('pg-claro')`; el CSS está acotado a `body.pg-claro` (SweetAlert, overlay «procesando», modal del historial y «Detalle del cargue»). Un modal nuevo en esas pantallas debe ir claro (no usar `.modal-confirm` ni colores `rgba(255,255,255,…)` en línea; usar clases como `cg-conf-*`). El resto del sitio conserva el tema oscuro. **Módulo «Historial de cargues» (2026-10-17)**: sus dos pestañas (Versiones = `HistorialVersionesCarguesController`, Registro = `AuditoriaCarguesController`) comparten SIEMPRE el mismo encabezado (`powerbi-header` con título «Historial de cargues»), la página anfitriona `HistorialCargues.cshtml` (pestañas `.cg-tab`; el contenido de cada una se pide por AJAX con `Assets/js/cg-tabs-panel.js`, sin recargar la página: las vistas `HistorialVersionesCargues.cshtml` y `AuditoriaCargues.cshtml` son FRAGMENTOS que solo se entregan con `?embed=1`; sin él los controladores devuelven la anfitriona), la frase `.cg-intro` y las piezas de `Assets/css/cargues-modulo.css` (cápsulas `.cg-pill-*`, botón de fila `.cg-btn`, tabla clara). Cualquier pestaña o cápsula nueva del módulo debe usar esas clases, no inventar estilos propios. «Versiones» se dibuja con `Assets/js/historial-versiones.js`. Los enlaces a CSS/JS propios usan `AssetVersion.Url(...)`.

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

### Active notifications — lista vigente
La lista completa se mantiene en `docs-claude/modulos/Notificaciones_activas.md` (o se obtiene con `grep -rn "NotificacionesService().Crear(" Controllers`). Al agregar una notificación, actualiza ese archivo.

### When to create a notification (server-side)
> **MANDATORY:** When finishing any new feature, explicitly ask the user whether to add internal notifications before closing the task. If yes: add `NotificacionesService.Crear(...)` calls, add keys to both `.resx` files, and update `docs-claude/modulos/Notificaciones_activas.md`.

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
El partial `Views/Shared/_ModeloBufinsTag.cshtml` usa la clase compartida `.header-tag-modelo` (en `bufins-components.css`) y los textos `Common_ModeloBufinsTag` / `Common_ModeloBufinsTagTooltip` (ya en ambos `.resx`) — no se duplica markup, CSS ni textos por vista. Hoy la llevan: Análisis IA, Informe de Modelos, Línea de Tiempo, Estado de Resultados (PYG), Balance General, Informe de tesorería del presupuesto, los Informes de Flujo de caja y Flujo de efectivo y el Informe de indicadores. **Ejecución de Modelos (`Datos/Modelo.cshtml`) NO la lleva**: esa pantalla *genera* las tablas, no las consume. Al crear una opción nueva que lea de `Modelo*`, añadir la cápsula y agregarla a esta lista.

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

Existing modals already marked `.modal-confirm`: `confirmDeleteModal` (Usuarios, Empresas, MaestroReportes), `eliminarModal` (GestorPrompts, GruposEmpresariales, ModelosEjecucion, ConfiguracionGlobal), `modalCierreAnio` (ConfiguracionesEmpresas).

The CSS (scoped under `.modal-confirm`, in `Assets/css/bufins-components.css`) restyles `.modal-content` (dark card `#1a1240`, rounded, top gradient accent bar using `--content-gradient`), `.modal-header`/`.modal-body`/`.modal-footer` (dark background, light text — including neutralizing old one-off header classes like `bg-danger`/`.modal-header-rojo`), `.close` (circular button), and footer buttons by their existing Bootstrap class — `.btn-primary`/`.btn-success` get the brand gradient, `.btn-secondary` gets the neutral dark-glass look, `.btn-danger` gets the red gradient. SweetAlert2's `.swal2-popup`/`.swal2-confirm`/`.swal2-cancel`/`.swal2-deny`/`.swal2-loader` are styled globally (unscoped) to match, with zero JS changes required.

**Blocking "cargando/procesando" overlays** (full-screen wait dialogs shown during long operations — Excel upload, relationship config upload, PBI variable processing, model export) use the same dark theme via reusable classes `.overlay-cargando` (outer fixed backdrop) / `.overlay-cargando-card` (inner card) / `.overlay-cargando-title` / `.overlay-cargando-text` / `.overlay-cargando-timer`, instead of the old per-view inline `style="background:white;..."`. The element keeps its own inline `style="display:none;"` untouched — JS toggles it directly (`overlay.style.display = 'flex'/'none'`) and that keeps working unmodified; only the color/shape properties moved into the shared classes. Existing usages: `#overlayProcesando` (CargueExcel, ConfiguracionRelacionamiento, ConfiguracionVariablesPBI), `#overlayExportacion` (Datos/Modelo). The purely in-page (non-overlay) `#loadingEjecucion` spinner in Datos/Modelo is NOT part of this system — it's an inline page state, not a blocking modal, and keeps its original light look.

### Modales de formulario (crear/editar) — estilo estándar «Limpio con acento» (OBLIGATORIO)

Todos los modales de crear/editar/gestionar (todos los gestores) usan **`Assets/css/bufins-modal.css`** (registrado al final del bundle `css-custom`, después de `layout.css`). Aplica a todo `.modal` **excepto** `.modal-confirm` (confirmaciones oscuras) y `.hc-modal` (gestor de hojas); eso reemplaza la regla anterior de «look claro de Bootstrap» para los modales de formulario. Referencia visual y especificación: `docs-claude/modales/` (capturas, `ejemplo-modal-empresa.html`, `LEEME-PROMPT-CLAUDE-CODE.md`); modelo ya hecho: `Views/Configuracion/Empresas.cshtml`.

Receta de un modal nuevo (sin colores ni tamaños propios):
- `<div class="modal-dialog modal-dialog-centered">` (+ `bf-modal-md` ≈ 760 px o `bf-modal-wide` ≈ 1040 px si hay columnas). El cuerpo hace scroll interno si no cabe (el pie siempre visible, también en 14").
- Cabecera: `<h5 class="modal-title"><i class="fas fa-x"></i> Título</h5>` (el `<i>` se vuelve el tile con degradado; opcional `<span class="bf-modal-titles"><span>Título</span><span class="bf-modal-sub">Nombre • NIT</span></span>`).
- Cuerpo: secciones con `<div class="bf-section">Título</div>`; cada campo = `<label>` **sin icono** + `<div class="bf-input"><i class="fas fa-x"></i><input class="form-control"></div>` (nunca `form-control-sm`; los `select2` y `textarea` van sin icono). Ayudas cortas en `<small class="form-text">`.
- Pie: `<span class="bf-footer-note"><b>*</b> @Resources.Strings.Common_CamposObligatorios</span>` + Cancelar (`btn btn-secondary`, sin `btn-sm`) + acción (`btn btn-primary`). **No** se repite «* Campos obligatorios» al inicio del cuerpo.
- Bloque de imagen/logo: `.bf-logo-box` (clicable, admite arrastrar) + `.bf-logo-actions` (`.bf-btn-outline` + `.bf-btn-icon-danger`).
- Los gestores existentes se migraron con una pasada mecánica (iconos de las etiquetas dentro del campo, nota de obligatorios al pie, sin tamaños sueltos); las secciones `.bf-section` solo están donde se agrupó a mano (Empresas): agregarlas al tocar cada modal.

### Botón "Exportar" desplegable — componente ESTÁNDAR para informes

Botón único con menú **Excel · PDF · Copiar como imagen · Enviar por correo**, igual en todo el sitio (nació del botón Exportar de `Datos/RevisarCargue.cshtml`; ese conserva su copia inline `.rc-exp*` — pendiente opcional migrarlo para tener una sola copia). **Regla (ver Important Conventions): todo informe nuevo lo usa.**

**Ya lo usan**: Informe de modelos (`excel,correo`: es una grilla, sin PDF ni imagen), Estado de resultados PYG, Balance general, Informe de tesorería del presupuesto, Flujo de caja, Flujo de efectivo, Indicadores, Línea de tiempo dinámica e Informe histórico de precios (las cuatro opciones). Pendientes de migrar (tienen sus propias exportaciones): Cargue de Excel/Revisar cargue (copia propia), módulo de Auditoría, Relacionamientos, PUC, Variables PBI, Navegación, Análisis IA.

**Piezas compartidas**
- `Views/Shared/_ExportMenu.cshtml` (modelo `Models/ExportMenuVM.cs`: `Id`, `Opciones`, `BtnTexto`, `BtnTooltip`, `MenuTitulo`, `ExcelDesc`, `PdfDesc`, `ImagenDesc`, `CorreoDesc`; lo no indicado usa `Common_Exp_*`).
- `Assets/css/export-menu.css` (`.exp*` + estilos del modal de correo `.exp-mail-*`), `Assets/js/export-menu.js` (`window.ExportMenu`: `init`, `correo`, `copiarImagen`, `aviso`, `descargarBlob`).
- `Views/Shared/_ExportCorreoModal.cshtml` (modelo `ExportCorreoVM`: `Titulo`, `Adjuntos`, `SoloExcel`): modal claro de Bootstrap con correo registrado del usuario / otros (máx. 10) / mensaje opcional.
- Servidor: `Helpers/InformeCorreoHelper` (`TryDestinatarios`, `PdfDesdeBase64` → solo `%PDF` ≤ 8 MB, `Adjuntos`) y `EmailService.EnviarInforme(...)` (correo único para todos los informes: chips de contexto + tarjetas de indicadores + tabla corta opcionales; el detalle completo va en los adjuntos).

**Receta para un informe nuevo**
```
<link href="~/Assets/css/export-menu.css" rel="stylesheet" />
<script src="~/Assets/js/html2canvas.min.js"></script>   @* solo si se usa "imagen" *@
<script src="~/Assets/js/export-menu.js"></script>
@Html.Partial("_ExportMenu", new bufinscustomers.Models.ExportMenuVM { Id = "miExp", Opciones = "excel,pdf,imagen,correo", ExcelDesc = ... })
@Html.Partial("_ExportCorreoModal", new bufinscustomers.Models.ExportCorreoVM())          @* SoloExcel = true si no hay PDF *@
var correo = ExportMenu.correo({ url, token: fn, datos: fn → filtros del informe consultado, pdf: fn → doc.output('datauristring'), pills: fn → [contexto], textos });
ExportMenu.init('miExp', { acciones: { excel: fn, pdf: fn, imagen: fn, correo: function () { correo.abrir(); } }, textos });
```
Controlador: `GenerarExcel(...)` (el mismo libro para la descarga y el correo) + `EnviarPorCorreo(filtros..., string correos, bool incluirmeAMi, string mensaje, string pdfBase64)`: valida acceso/empresa, `InformeCorreoHelper.TryDestinatarios`, **reconstruye el informe en el servidor con los filtros recibidos (nunca confía en cifras del navegador)**, arma adjuntos con `InformeCorreoHelper.Adjuntos`, llama `EmailService.EnviarInforme`, audita (`AuditoriaTipo.Informes` + `AuditoriaAccion.Enviar`, severidad Advertencia — datos que salen del sistema) y responde `{ exito, mensaje }`. El constructor del PDF debe **devolver** el documento (con `__nombre`) en vez de descargarlo, para reutilizarlo en el correo.

**Comportamiento**: cada acción puede devolver una Promise: mientras tanto el botón muestra "Generando…" y si falla sale un aviso de error; `correo` abre el modal y no deja el botón ocupado; `pdf` puede abrir un modal propio (Línea de Tiempo pide "gráfico / gráfico + datos"). `Opciones` oculta las salidas que no apliquen. "Imagen" captura todo el contenedor del informe con html2canvas → portapapeles (respaldo: descarga PNG). Para deshabilitar el botón mientras no hay datos: `$('#miExp .exp-btn').prop('disabled', true)`. La tarjeta que contiene el botón debe tener `overflow: visible`.

### Brand loader (wordmark + gradient) - reusable partial

Animated loader built from the Bufins wordmark images (`Assets/img/Bufins_Wordmark_Aqua.png` for dark backgrounds, `Assets/img/Bufins_Wordmark_Dark.png` - purple `#160933` - for light backgrounds) plus the brand gradient. Pure CSS + image, no JS/library. CSS lives in `Assets/css/bufins-components.css` (classes `.l3-*`); it is wrapped as the Razor partial `Views/Shared/_LoaderDots.cshtml` (static logo + 4 bouncing gradient dots), the one wired into the `.overlay-cargando` loading overlays above. It takes a `string` model - `"aqua"` (default, dark/purple backgrounds) or `"dark"` (white/light backgrounds):
```csharp
@Html.Partial("_LoaderDots", "aqua")   @* dark modal/card background *@
```
Use it inside any new `.overlay-cargando-card` or `.modal-confirm .modal-body` that needs a lightweight loading state.

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
