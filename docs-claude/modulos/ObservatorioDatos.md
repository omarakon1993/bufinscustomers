# Datos del Observatorio (actualizacion desde el portal)

Pantalla **Configuracion > Datos del Observatorio** (`ObservatorioDatosController`, solo Super Admin; enlace en la barra de `ConfiguracionGlobal`). Permite ver el estado de los datos publicados en `bufins.com/observatorio` y lanzar su actualizacion.

- **Servicio:** `Services/ObservatorioActualizacionService.cs`. Lee `meta.json` y `etl_status.json` de la carpeta de datos del sitio publico, consulta (cache 10 min) la fecha de actualizacion de los 4 datasets de datos.gov.co (`api/views/{id}.json`) y lanza `ObservatorioEtl.exe` con `Process.Start` (proceso aparte; no corre dentro de IIS).
- **Estado:** "en ejecucion" = archivo `etl.lock` tomado por el ETL. Si `etl_status.json` dice `corriendo` pero no hay bloqueo, se muestra `interrumpido`.
- **Rutas (sin rutas "quemadas"):** orden de resolucion: 1) clave de `dbo.ConfiguracionSistema` (`ObservatorioEtlExe`, `ObservatorioDatosDir`; se editan en la tarjeta "Rutas del servidor" de la propia pantalla), 2) `appSettings` con el mismo nombre (vacias por defecto), 3) autodeteccion: `ObservatorioEtl.exe` en `bin\` del portal (llega ahi porque `bufinscustomers.csproj` referencia el proyecto `bufinsweb\Tools\ObservatorioEtl` si el repo hermano existe y esta restaurado) o `App_Data\ObservatorioEtl\`, y los datos en la carpeta hermana `..ufinsweb\App_Data\observatorio`. Una ruta relativa o con `~/` se cuenta desde la raiz del portal. En produccion, la carpeta de datos de bufins.com se indica una sola vez en la pantalla (queda en BD). El usuario del grupo de aplicaciones debe poder ejecutar el .exe y escribir en esa carpeta.
- **Despliegue del .exe:** viaja solo en `bin\` al publicar el portal (compilar con el repo `bufinsweb` presente). Si no esta, compilar `bufinsweb\Tools\ObservatorioEtl` y copiar la salida a `App_Data\ObservatorioEtl\` o indicar la ruta en la pantalla.
- **Seguridad:** POST con anti-forgery, auditado (`AuditoriaTipo.Configuracion` / `AuditoriaAccion.Ejecutar`, entidad `ObservatorioDatos`). Sin tablas ni scripts SQL (no hay fila de menu).
- **Textos:** claves `ObsDatos_*` en `Strings.resx` y `Strings.en-US.resx`.

## Uso del Observatorio
Tarjeta "Uso del Observatorio (ultimos 30 dias)": `ObservatorioDatosController.Uso` (GET JSON) -> `ObservatorioActualizacionService.ObtenerUso`, que suma los contadores anonimos de `DatosDir\uso\yyyy-MM.json` (los escribe bufins.com). Sin SQL ni datos personales. Textos `ObsDatos_Uso*` en ambos `.resx`.
