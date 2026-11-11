# Sistema de notificaciones internas

> Detalle completo movido desde CLAUDE.md (2026-11-11). CLAUDE.md conserva las reglas obligatorias en resumen; este archivo es la referencia completa. Si cambias el módulo, actualiza este archivo.

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
