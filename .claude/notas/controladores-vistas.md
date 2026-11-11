# Rutas de vistas, controladores clave y Home

> Detalle completo movido desde CLAUDE.md (2026-11-11). CLAUDE.md conserva las reglas obligatorias en resumen; este archivo es la referencia completa. Si cambias el módulo, actualiza este archivo.

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
- **AuditoriaCarguesController** - Auditoría de cargues (`[ValidarSesion]` + gate Super Admin/Admin de Empresa en `OnActionExecuting`). Vive como pestaña junto al Historial de versiones; incluye `Detalle(id)` y `DescargarArchivo(id)` (solo Super Admin, auditado). Ver `docs-claude/modulos/Auditoria.md` («Auditoría detallada de cargues») y `App_Data/Documentacion/Cargues/Auditoria_Cargues_Blindaje.html`
- **InformeNavegacionController** - Página-visitas por usuario (`[ValidarSesion]`, gate `EsSuperAdmin() || EsAdminEmpresa()`). Endpoints: `Index`, `Detalle`, `ResumenUsuarios`, `ResumenPaginas`, `ExportarExcel`. Ver "Auditoría (central, navegación y hub)"
- **AuditoriaHubController** - Módulo unificado de auditoría, `Index` renderiza `~/Views/Informes/AuditoriaHub.cshtml`: una sola pantalla con pestañas **Cambios / Navegación / IA** (la auditoría de **Cargues** salió del hub: ahora es una pestaña del módulo de cargues, junto al Historial de versiones, dentro de la página anfitriona `Views/Informes/HistorialCargues.cshtml`), cada una cargada en línea por AJAX (`?embed=1`, layout `_LayoutFragment`). `[ValidarSesion]` + gate `EsSuperAdmin() || EsAdminEmpresa()`. Ver "Auditoría (central, navegación y hub)"

### Página principal (Home) — resumen (detalle en `docs-claude/modulos/Home.md`)

`Views/Home/Index.cshtml` sin modelo; cada bloque se llena por AJAX. Columna izquierda: indicadores (TRM) y noticias financieras con filtro por portal (solo cliente). Columna derecha: **Solicitudes de seguridad** (tarjeta solo Super Admin, solo si hay pendientes, enlaza a `/SolicitudesSeguridad`; `HomeController.ObtenerSolicitudesSeguridad`), **Alertas Bufins** (`HomeController.ObtenerAlertas` → `dbo.sp_ObtenerAlertasHome`, solo lectura; **agregar una alerta = un bloque más en el SP, sin tocar C#**), **Accesos rápidos** (top de páginas del usuario cruzado con su propio sidebar, nunca muestra algo sin permiso) y **Mi consumo de IA** (solo con `IAModuloHelper.Visible()`). `Home/Index` no se registra en la navegación.
