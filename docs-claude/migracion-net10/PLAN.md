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
