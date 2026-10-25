# Supuestos del balance del ppto (`Rel_BalancePpto` por empresa)

Opción de menú **`ADMIN_CONFIG_BALANCE_PPTO`** («Supuestos del balance del ppto», categoría Configuración, `SoloAdminEmpresa=1`: Super Admin y Admin de Empresa). El nombre que se muestra en los avisos sale **siempre de `MenuOpciones`** (`SupuestosBalancePptoService.ObtenerMenu`), así que renombrar el menú no obliga a tocar código.

## Datos
- `dbo.Rel_BalancePpto` ya no es un catálogo global: tiene `IdEmpresa` (NOT NULL), `IdUsuarioCarga`, `FechaCarga`, `IdUsuarioModificacion`, `FechaModificacion` e índice único `(IdEmpresa, Id, Mes)`. Columnas de negocio (inferidas del SP; el DDL no está en el repo): `Id`, `Descripcion`, `DescripcionBase`, `MesBase` (-1 = mes anterior), `Mes`, `Formula`, `Valor`, `TipoUnidad` (`CALCULO` = subtotal), `Tabla`.
- Cada línea (`Id`) tiene 12 filas (una por mes). **Solo `Valor` es editable**; las filas `TipoUnidad = 'CALCULO'` se muestran pero no se editan ni se pueden cargar (el servicio las excluye en `GuardarValores` y el Excel las ignora con advertencia).
- Scripts manuales: `Sql/038_RelBalancePpto_PorEmpresa.sql` (columnas, respaldo `Rel_BalancePpto_Respaldo_038`, filas actuales → empresa **ULTRA**, el resto de empresas con la misma estructura y `Valor` NULL, índice, `sp_InicializarSupuestosBalancePpto`, opción de menú) y `Sql/039_sp_ModeloBalancePpto_PorEmpresa.sql` (el SP filtra por `IdEmpresa` en sus 7 referencias; `@VersionCache` 4→5). Orden: 038 → 039 → desplegar.
- Sin el script 038 el módulo avisa (`SBP_NoDisponible`) y el resto de la app sigue igual (`Disponible()` mira `COL_LENGTH`).

## Código
- `Controllers/SupuestosBalancePptoController` (`[ValidarSesion]`, `[RequierePermiso]` por acción, `EmpresaAccesoHelper.TieneAcceso` siempre): `Index`, `Obtener`, `Guardar` (JSON de cambios), `Inicializar`, `CargarExcel`, `DescargarExcel`, `DescargarPlantilla` y `EstadoPendientes` (sin `RequierePermiso`: lo consulta Ejecución de Modelos; devuelve nombre y URL actuales del menú).
- `Services/SupuestosBalancePptoService`: lectura tolerante de columnas, resumen de completitud (`ObtenerResumen`: pendientes = filas no-CALCULO con `Valor` NULL, por línea y mes), guardado en una transacción, Excel (ClosedXML). Autocompletado «Aplicar este valor a todos los meses» = en el navegador (rellena y marca como cambio; se confirma con «Guardar»).
- **Excel**: hoja `Rel_BalancePpto` con las mismas columnas que antes (sin las de auditoría). Importación = actualización por clave `(Id, Mes)` **solo de `Valor`**; celda vacía = no modifica (advertencia); error (Id/Mes inválido, fila inexistente, duplicada, valor no numérico, falta Id/Mes/Valor) = **no se escribe nada**. Hallazgos con fila/columna/severidad; textos `SBP_Hall_*`.
- UI: `Views/Configuracion/SupuestosBalancePpto.cshtml` + `Assets/css/supuestos-balance-ppto.css` (prefijo `sbp-`): tablero (anillo de completitud, KPIs, «Qué falta por diligenciar», última carga/modificación), grilla pivot línea × 12 meses, filtro «Solo pendientes», búsqueda, detalle de fórmula, barra de guardado y modal de carga.

## Integraciones
- **Relacionamientos**: `ConfiguracionRelacionamientoService` e `InformeRelacionamientosService` excluyen `Rel_BalancePpto` (listado, exportar, plantilla y carga). Una hoja residual en un Excel antiguo queda «Ignorada».
- **Alta de empresa**: `EmpresaController.CrearEmpresa` → `InicializarSupuestosBalancePpto` (estructura con `Valor` NULL, auditado) y `Empresas.cshtml` muestra un segundo modal «Empresa creada · También se crearon los supuestos…» con botón «Diligenciarlos ahora».
- **Ejecución de Modelos**: `Views/Datos/Modelo.cshtml` muestra `#sbpAlertaModelo` (clase `.sbp-alerta`) si la empresa elegida tiene pendientes o no tiene filas, con enlace al nombre actual del menú. Solo informativo, no bloquea.
- **Auditoría**: `Configuracion`/`Editar` (guardado manual), `Configuracion`/`Crear` (inicializar y alta de empresa), `Cargues`/`Crear` (carga Excel, aceptada o rechazada), `Configuracion`/`Exportar` (descarga).

## Pendiente / opcional
Notificaciones internas, documentación HTML de usuario (y si va a la biblioteca «Documentación BUFINS»), cápsula «Documentación», y «Copiar desde otra empresa del grupo».
