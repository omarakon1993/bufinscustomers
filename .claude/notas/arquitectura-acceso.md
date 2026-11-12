# Arquitectura, BD, sesión, 2FA/passkeys, roles, grupos empresariales y permisos de menú

> Detalle completo movido desde CLAUDE.md (2026-11-11). CLAUDE.md conserva las reglas obligatorias en resumen; este archivo es la referencia completa. Si cambias el módulo, actualiza este archivo.

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
- `CadenaConexion` - Centralized connection string (`ConnectionStrings:DefaultConnection`, from `secretsSettings.json`)
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

**Token anti-falsificación inválido (2026-11-12)**: lo atiende el filtro GLOBAL `Filters/AntiforgeryFallidoFilter` (`IAlwaysRunResultFilter`, registrado en `Program.cs`). En ASP.NET Core `[ValidateAntiForgeryToken]` es un filtro de autorización: al fallar corta la tubería ANTES de crear el controlador, así que el antiguo `OnResultExecuting` dentro de `AccesoController` nunca corría y el 400 se veía como la página «500 Algo salió mal» (sin registro en el log). Causa típica: formulario de login abierto desde antes de un reinicio del sitio (llaves de Data Protection en memoria en Conexcol). Ahora: AJAX → JSON `sessionExpired`; `Acceso/*` o sin sesión → `Login?tokenExpirado=true`; con sesión → página de origen (mismo host) con `Common_FormularioExpirado`. Queda WARN `AntiforgeryFallido` en el log. Las sesiones y las llaves de formularios sobreviven a reinicios con `Sql/045` (ver `plataforma-net10.md`); `CambiarClaveInicial` calcula el hash una sola vez y actualiza la huella del respaldo de sesión. El POST de login además tolera `Correo`/`Clave` vacíos (null en Core) y la vista quita el estado «cargando» en `pageshow` (volver con «atrás»).

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
