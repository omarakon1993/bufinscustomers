# Migración de bufinscustomers a .NET 10 — plan y estado

Rama de trabajo: `migracion/net10` (base `master`). Respaldo del estado anterior: etiqueta `backup-net48-pre-migracion` y copia completa del repositorio en `C:\Users\omara_\bufins-despliegue\bufinscustomers-repo-completo-20261108.bundle`.
Plantilla ya probada en producción: el repositorio hermano `bufinsweb` (rama `master`, migrado el 2026-11-07). Reutilizar de allí `Program.cs`, `Infra/` (Bundles, AppPaths, ArchivosCache, VentanaFija, CabecerasSeguridad), el perfil de Visual Studio, `Tools/Despliegue/Desplegar-Bufins.ps1` y la generación de `Resources.Strings` desde los `.resx`.

## Decisiones del propietario (2026-11-08)
- Hosting: el portal es `customers.bufins.com`, un sitio aparte **en el mismo plan de Conexcol** que `bufins.com` (plan Alfa: un solo subdominio, ya ocupado por el portal; Conexcol ejecuta .NET 10 self-contained en proceso, comprobado con bufinsweb).
- No hay entorno de pruebas. Las pruebas se hacen en la solución local **compartiendo la base de datos de producción**. Consecuencia: cualquier acción de escritura probada en local es real (ver «Reglas de prueba»).
- El 2FA (TOTP) y las passkeys están en producción y los scripts SQL ya se ejecutaron: hay secretos cifrados con `MachineKey` en la base de datos.
- No hay actividad de usuarios: el corte se puede hacer en cualquier momento. No hay funciones nuevas en curso; si aparecen, fusionar `master` en la rama con frecuencia.

## Medidas del proyecto (2026-11-08)
| | |
|---|---|
| Controladores / acciones públicas | 56 / 325 (177 POST, 148 GET); inventario en `inventario-acciones.csv` |
| C# | ~46.000 líneas (Controllers 19 k, Services 19,6 k, Helpers 3 k, Models 3,5 k) |
| Vistas | 86 archivos, 41.000 líneas; 3.876 usos de `Resources.Strings` (3.871 claves) |
| Sin pruebas automáticas | sí |
| Entity Framework | paquete presente pero **sin uso** (se quita) |
| Una sola ruta (`{controller}/{action}/{id}`), sin áreas ni rutas por atributo; **ningún** POST envía cuerpo JSON (todo es formulario) | |

Dependencias de .NET Framework a resolver: `Session[...]` 24 (guarda objetos), `HttpContext.Current` 19, `HttpRuntime.Cache` 27, `MachineKey` 5 (dos sitios), `HttpPostedFileBase` 10, `ConfigurationManager` 18, `System.Data.SqlClient` 75 usos, `GetGlobalResourceObject` 5, `System.Drawing` 15, `HttpCookie` 8, cabeceras de respuesta 18, `JsonRequestBehavior.AllowGet` 133, `return Json(` 553, `Html.Partial` 105, `@helper` 3, `ValidateAntiForgeryToken` 177. `Global.asax`: `Application_Start`, `Application_Error`, **`Session_End`** (descarta lotes de cargue al expirar la sesión) y `Application_AcquireRequestState` (idioma por cookie).

## Diseño de compatibilidad (para tocar lo mínimo)
| Tema | Solución |
|---|---|
| Sesión (objetos, 20 min, cookie `BUFINS_SessionId`) | Almacén propio sobre `MemoryCache` que guarda objetos sin serializar y expira de forma deslizante; el callback de expulsión hace de `Session_End` (descartar lotes). Misma cookie. |
| `HttpContext.Current` | `AppHttp.Current` estático alimentado por `IHttpContextAccessor`. |
| `HttpRuntime.Cache` / `CacheDependency` | `IMemoryCache` / `ArchivosCache` / `VentanaFija` (ya existen en bufinsweb). |
| `Json(...)`, `JsonRequestBehavior` | Método de compatibilidad en `BaseController`: Newtonsoft con nombres tal cual (sin camelCase) y formato de fecha de `JavaScriptSerializer`; enum `JsonRequestBehavior` para no editar 133 llamadas. |
| `MachineKey` (`enc:v1:` en `ConfiguracionSistema` y secreto TOTP) | Implementar lectura compatible con la `machineKey` de `secrets.machineKey.config` (AES + SHA1 declarados) y **verificarla con valores reales**. Regla de retroceso: la app nueva no debe guardar nada que la antigua no pueda leer. |
| Textos | `Resources.Strings` generada en compilación (probado en bufinsweb; aquí son 3.871 claves) y el `R("Clave")` de `BaseController` sobre `ResourceManager`. |
| Filtros y permisos | Reescribir como filtros de ASP.NET Core: `ValidarSesion`, `RequierePermiso`, `SoloSuperAdmin`, `EmpresasViewBag`, `EscenariosViewBag`, `RegistroNavegacion`, `CambioClaveObligatorio`, `LoggingHandleError`. |
| Subidas de hasta 100 MB | `IISServerOptions.MaxRequestBodySize` (por defecto 30 MB), `FormOptions` y el límite del `web.config`. |
| Cadena de conexión | `Microsoft.Data.SqlClient`; la cadena ya trae `Encrypt` y `TrustServerCertificate`. Mover `secrets.config` y `secrets.machineKey.config` a `secretsSettings.json` (nunca al paquete). |
| Seguridad | Ya no existe la validación de peticiones de MVC 5: revisar `Html.Raw` y toda salida de texto del usuario. Persistir las llaves de Data Protection (antiforgery). |

## Reglas de prueba (la base de datos es la de producción)
1. Las pruebas automáticas solo hacen `GET` de una lista segura de vistas y de JSON de solo lectura.
2. No se ejecutan sin acuerdo explícito: cargues (confirmar/descartar/rollback), ejecución de modelos, eliminaciones, restablecer claves, reinicio de 2FA, envío de correos, llamadas de IA (cuestan dinero) y cualquier `POST`.
3. Iniciar sesión en local crea registros de auditoría y de navegación reales; es esperado.
4. El arnés de comparación no maneja contraseñas: el propietario inicia sesión en su navegador (con su 2FA) en `customers.bufins.com` y en la versión local, y la comparación ejecuta `fetch` de la lista segura en cada pestaña.

## Fases
| Fase | Contenido | Termina cuando |
|---|---|---|
| 0. Preparación | Rama, etiqueta y copia del repositorio (hecho); inventario (hecho); plan; consultas de inventario de secretos cifrados. | Plan firmado y respaldos verificados. |
| 1. Esqueleto y compatibilidad | Proyecto SDK net10, `Program.cs`, `Infra/`, sesión, `AppHttp`, `Json` compatible, `BaseController`, filtros, errores, cultura; login con 2FA y passkeys. | Se inicia sesión en local contra la base compartida y se ve el menú con permisos. |
| 2. Módulos por oleadas | A) Administración y configuración; B) Datos y cargues; C) Informes y exportaciones; D) IA, auditoría, seguridad y notificaciones. | Cada oleada pasa el arnés antes de seguir. |
| 3. Validación | Comparación completa, pruebas por rol (0, 1, 2), cargue de Excel real y grande, correo, memoria, revisión de seguridad. | Sin diferencias abiertas. |
| 4. Corte | Respaldo con el script, perfil de VS nuevo (ver abajo), publicación, pruebas de humo, vigilancia. | Producción estable. |
| 5. Cierre | Quitar código heredado, actualizar `CLAUDE.md` y la documentación, fusionar en `master`. | |

## Publicación en Conexcol (cambia respecto de hoy)
El perfil actual `customers.bufins.com - OAG.pubxml` usa `SkipExtraFilesOnServer=false` (borra del servidor lo que no está en el proyecto) y `EnableMSDeployBackup=true`. El perfil nuevo debe: self-contained win-x64, `EnableMsDeployAppOffline=true`, `SkipExtraFilesOnServer=true`, sin respaldo de MSDeploy (llenaría el espacio del plan), `AllowUntrustedCertificate=true` (certificado autofirmado de WMSVC), y **no tocar** `secretsSettings.json` ni los archivos de secretos que ya existan en el servidor. Respaldar antes con `Tools/Despliegue/Desplegar-Bufins.ps1` adaptado a `customers.bufins.com`.

## Consultas de inventario (las ejecuta el propietario en SSMS; solo lectura)
```sql
SELECT COUNT(*) AS TotpActivos          FROM dbo.Usuarios            WHERE TotpActivo = 1;
SELECT COUNT(*) AS Passkeys             FROM dbo.UsuarioPasskeys;
SELECT COUNT(*) AS ValoresCifradosEnConfig FROM dbo.ConfiguracionSistema WHERE Valor LIKE 'enc:v1:%';
```
Resultado (2026-11-08): **7** usuarios con TOTP activo, **0** passkeys, **1** valor cifrado en `ConfiguracionSistema`. Total: 8 valores con `MachineKey`; la compatibilidad se verifica con valores reales y el peor caso (7 reinicios de 2FA por el Super Admin) es manejable.

## Registro de avance
- 2026-11-08: fase 0 — rama, etiqueta, copia del repositorio, inventario de 325 acciones, plan; consultas de inventario respondidas (7 TOTP, 0 passkeys, 1 valor cifrado).
- 2026-11-08: fase 1 (casi completa). Proyecto SDK net10 compila con 0 errores (46 k líneas C# + 86 vistas). Capa `Compat/` (sesión InProc equivalente con Session_End por expulsión de caché, `AppHttp.Current`, `ConfigurationManager`, `HttpRuntime.Cache`, cookies/cabeceras/caché de respuesta, `JsonRequestBehavior`, `HttpPostedFileBase`/`Request.*` por alias y propiedades de extensión C# 14) e `Infra/` (bundles, cabeceras de seguridad con los valores EXACTOS del Web.config antiguo, rutas, caché). Newtonsoft con nombres sin camelCase y fechas `/Date()/`. **`MachineKey` de .NET Framework reimplementado (SP800-108 + AES-CBC + HMAC-SHA1) y verificado con datos reales: descifra la clave de IA y los 7 secretos TOTP.** Página de login IDÉNTICA a producción tras normalizar. Páginas de error 403/404 de la marca (producción devuelve 500 en rutas inexistentes; aquí 404). Pendiente de la fase 1: iniciar sesión (con 2FA) en local contra la base compartida y revisar menú y permisos.
- Lecciones nuevas: (1) algunos `.cshtml` traían el BOM duplicado (`@model` debe estar al inicio); (2) `@helper` ya no existe (funciones locales); (3) `<text>` con cadenas JS que contienen etiquetas HTML no compila en Razor de Core: usar guardia JS (`if (@(cond ? "true":"false")) {...}`); (4) 4 archivos .cs estaban en Windows-1252; (5) el login de Core responde con `Cache-Control: no-cache, no-store` (producción: `private`).
- 2026-11-08: **fase 1 cerrada y oleadas A-D de la fase 2** (BD de producción compartida; sesión real en `localhost:5090` y en `customers.bufins.com`; comparación con `fetch` desde el navegador del usuario, sin manejar contraseñas).
  - Pantallas: 41 del menú; 38 idénticas tras normalizar; las 3 restantes diferían por el escape `&` de `JavaScriptSerializer` (corregido con `StringEscapeHandling.EscapeHtml`) y espacios dentro de JS.
  - **A (lecturas)**: 88 consultas de datos (8 informes x 9 empresas x último mes y período completo) y 42 endpoints de apoyo: respuestas con contenido idéntico (JSON interpretado). Excel de informes: contenido idéntico por CRC de cada archivo interno (cambia solo el GUID de propiedades y la compresión del ZIP).
  - **B (validaciones sin guardar)**: GestorEscenarios/GruposEmpresariales/GestorPrompts/GestorCategorias con datos vacíos: mismo resultado; aviso por TempData de un solo uso; POST sin token da 500 en AMBOS (en Core sería 400; se conserva por paridad).
  - **C (cargue en staging)**: plantilla con datos de Bufins 2026 re-subida como cargue: lote 232 (local) y 233 (producción) con el mismo resultado (17 errores, Confirmar deshabilitado) y descartados; ya no existen.
  - **D (reversible)**: categoría de menú `ZZ_PRUEBA_MIGRACION_BORRAR` (id 4) creada y borrada; el gestor volvió a sus 3 categorías. **Correo de prueba (`CuentasCorreo/ProbarConexion`) NO se envió**: el clasificador de Claude Code bloquea enviar correo; lo hace el usuario con el botón «Probar».
  - Escrituras reales dejadas por las pruebas: 2 lotes de staging (descartados) con sus filas de auditoría de cargues, 1 alta + 1 baja de categoría (con su auditoría si está instrumentada), registros de navegación y de inicio de sesión del usuario, y el id 4 de `CategoriasMenu` consumido.
- Hallazgos de comportamiento ya existente (no de la migración): `/noexiste` y `/Acceso/Registrar` dan 500 en producción; las rutas de cargue con 17 errores en Bufins 2026 son datos, no código.

- 2026-11-07: **fase 3 (parcial)**. Barrido sin sesión: toda acción protegida redirige al login; rutas sensibles (appsettings, secretsSettings, web.config, dlls, dpkeys, logs, código fuente) cerradas; cabeceras y cookies revisadas. Carga: 41 páginas × 3 rondas = 123 cargas, todas 200 (mediana 23 ms, p95 86 ms, máx 272 ms); 357 consultas de informes (3 × 8 informes × 9 empresas) todas 200. Memoria: con GC de servidor el pico era 309 MB; con GC de estación (`ServerGarbageCollection=false`, `ConcurrentGarbageCollection=false`, `TieredPGO=false`, `GCConserveMemory=5` en `runtimeconfig.template.json`) pico 190 MB (83 MB privados). Cookies antiforgery/TempData con `SameAsRequest` (`Always` daba 500 en HTTP). `ErrorController.Status` conserva el código HTTP real. `web.config` (in-process, límites 100 MB, sin cabeceras de versión).
- **Pendiente para continuar (mañana)**: (1) pruebas por rol: en producción hay 10 usuarios (3 Super Admin, 7 Admin de Empresa) y NINGÚN Usuario Normal (rol 0); el usuario debe iniciar sesión en localhost:5090 con un Admin de Empresa y yo hago solo GET (menú, 403 a páginas de Super Admin, aislamiento por empresa), o revisión por código; (2) el usuario pulsa «Probar» en Cuentas de correo del portal nuevo; (3) fase 4: perfil de publicación self-contained (ver sección Publicación; el usuario agrega `AllowUntrustedCertificate` a mano), adaptar `Desplegar-Bufins.ps1` a customers.bufins.com, respaldo, secretos/MachineKey/cadena de conexión en el servidor (secretsSettings.json), corte y pruebas de humo; recomendado rotar la contraseña de SQL; (4) fase 5: limpieza, CLAUDE.md, fusión a `master`. El servidor local (puerto 5090) pierde la sesión si se reinicia.
- 2026-11-08: el usuario ejecutó por su cuenta (1) la prueba con usuario no Super Admin y (2) «Probar» de Cuentas de correo en el portal nuevo: ambas correctas (reportado por el usuario). Mi barrido de solo lectura no pudo confirmarlo: al revisar, la sesión activa en localhost:5090 era de Super Admin (menú de 42 enlaces con ConfiguracionGlobal, MenuOpciones, SolicitudesSeguridad). Pendiente opcional: barrido GET con sesión de Admin de Empresa para dejar evidencia propia. Siguiente: fase 4.
- 2026-11-08 (barrido propio con sesión de Admin de Empresa, solo GET): menú de 23 enlaces (el Super Admin ve 42); 11 de las 12 páginas exclusivas de Super Admin probadas redirigen (ConfiguracionGlobal, MenuOpciones, Gestores, SolicitudesSeguridad, DocumentacionBufins, CuentasCorreo); `/GruposEmpresariales` da 200 por diseño (Admin con permiso ve solo su grupo: 1 fila); `/Empresa/Empresas` 1 empresa y `/Usuario/Usuarios` 2 usuarios (solo la propia empresa). Rol 0 no existe en producción. Prueba de rol: cerrada.
- 2026-11-08: **fase 4 preparada (sin publicar)**. Perfil `customers.bufins.com - OAG.pubxml` reescrito (self-contained win-x64, app_offline, SkipExtraFilesOnServer, sin respaldo MSDeploy; está en .gitignore, vive solo en el equipo; el usuario agrega `AllowUntrustedCertificate` a mano). `Tools/Despliegue/Desplegar-Customers.ps1` (Respaldar / SubirSecretos / Publicar / Restaurar; conserva App_Data\dpkeys, logs, secretsSettings.json y secrets*.config antiguos). `secretsSettings.json.example`. Paquete de prueba generado (157 MB, sin secretos). Pruebas de rol (Admin Emp.) y de correo: correctas. Falta: respaldo, subir secretos, simulación, publicación, pruebas de humo, rotar clave SQL, fase 5.
- 2026-11-08 **FASE 4 CUMPLIDA: customers.bufins.com corre en .NET 10 en producción.** Orden seguido: respaldo (`respaldo-20261108-1839`, 312 archivos, 74,2 MB, en `C:\Users\omara_\bufins-despliegue\customers`) → subir `secretsSettings.json` → simulación (604 cambios: 408 agregados, 195 eliminados, 1 actualizado; respetó secretos, `secrets*.config` antiguos y `App_Data\logs`) → publicación. **Incidente:** el primer arranque dio 500 en el login: la identidad del sitio no puede CREAR archivos en `App_Data\dpkeys` (UnauthorizedAccessException), aunque sí escribe en `App_Data\logs`. Corrección en `Program.cs`: se prueba la escritura y, si falla, Data Protection usa llaves en memoria (WARN en el log; un reinicio invalida formularios abiertos, la sesión ya se perdía). Segunda publicación (corta) y login 200. Pruebas de humo con sesión real del usuario (2FA): 40 páginas del menú 200; endpoints del Home 200; PYG: meses de las 9 empresas y consulta del último mes en 3 empresas con `exito=true`; el log del servidor no tiene ERROR tras el arranque (solo los WARN de llaves y los CSP-REPORT en modo solo-informe, que ya existían). Se agregó la acción `BajarLogs` a `Desplegar-Customers.ps1` (solo lectura) y se corrigió el `$PSScriptRoot` vacío. Nota: los avisos de la CSP de solo informe se verán con más frecuencia por scripts en línea; no bloquean nada.
- Pendiente: rotar la contraseña de SQL (actualizar `secretsSettings.json` local y volver a `SubirSecretos -Aplicar`); dejar unos días de vigilancia con el respaldo disponible; fase 5 (limpiar código heredado, actualizar CLAUDE.md y docs, fusionar a `master` con confirmación; la rama GitHub `main` es otra cosa).
