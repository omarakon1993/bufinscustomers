# Sistema de estilos UI (componentes, modales, exportar, loader, formularios)

> Detalle completo movido desde CLAUDE.md (2026-11-11). CLAUDE.md conserva las reglas obligatorias en resumen; este archivo es la referencia completa. Si cambias el módulo, actualiza este archivo.

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
