<!-- Detalle técnico extraído de CLAUDE.md. Si cambias este módulo, actualiza este archivo. -->

# Notificaciones activas

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
| Upload sheet created in the Gestor de hojas de cargue (notifies every Super Admin) | `success` | `GestorHojasCargueController` | `Notif_HojaCargueCreada` |
| Upload sheet deleted, with or without dropping its tables (notifies every Super Admin) | `warning` | `GestorHojasCargueController` | `Notif_HojaCargueEliminada` |
| Session expiring (client-side) | `warning` | `_Layout.cshtml` (JS only) | `Notif_SesionExpiraTitulo` |
| Real account locked after failed logins (notifies the locked user) | `warning` | `AccesoController` | `Notif_CuentaBloqueadaTitulo` |
| Real account locked after failed logins (notifies every Super Admin) | `error` | `AccesoController` | `Notif_CuentaBloqueadaAdminTitulo` |
| Login rate-limit entered defensive mode — global spike (notifies every Super Admin in-app, ≤1×/30 min; also emails `appSettings["SeguridadAlertasDestino"]` via `EmailService.EnviarAlertaSeguridad`, queued background) | `error` | `AccesoController` | `Notif_ModoDefensivoTitulo` |
| Version rollback success | `success` | `HistorialVersionesCarguesController` | `Notif_RollbackEjecutado` |
| Version rollback error | `error` | `HistorialVersionesCarguesController` | `Notif_ErrorRollback` |
| AI query audit log cleared (full or keep-last-N-months) | `warning` | `AuditoriaConsultasIAController` | `Notif_AuditoriaLimpiada` |
| General audit log cleared (full / keep-N-months, all or one company) | `warning` | `AuditoriaController` | `Notif_AuditoriaLimpiada` |
| Navigation audit log cleared (full / keep-N-months, all or one company; Super Admin only) | `warning` | `InformeNavegacionController` | `Notif_NavegacionLimpiada` |
