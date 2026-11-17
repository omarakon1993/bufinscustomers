# Refactor / reingeniería post-.NET 10 (plan vivo)

Rama `refactor/net10-limpieza`; punto de retorno: etiqueta `base-pre-refactor-2026-11-16`. Regla: no cambia comportamiento, rutas, SP ni textos. Sin commit sin el «sí» del usuario; no se publica.

## Fase 0 — red de seguridad (HECHA 2026-11-16)
- Línea base: `dotnet build` = 0 advertencias, 0 errores.
- Proyecto de pruebas `Tests/Bufins.Pruebas` (xUnit, solo lógica pura, nunca BD): `TotpHelper` (vectores RFC 6238), `PoliticaContrasena`, `TemaInforme`. 23 pruebas en verde. Excluido del build del sitio en el `.csproj` (`Tests\**`, igual que `Tools\`). Ejecutar: `dotnet test Tests\Bufins.Pruebas\Bufins.Pruebas.csproj`.
- Verificación tras cada cambio: build limpio + pruebas + humo manual en GET (local usa la BD de producción: ningún POST automático).

## Fase 1 — diagnóstico (HECHA, solo lectura)
Tamaño: Controllers 17,8k / Services 23,8k / Helpers 3,4k / Models 3,6k / Views 42k líneas; CSS propio ≈3,6k (el resto es sb-admin-2 y bootstrap-icons minificados); JS propio 5,4k.

| Hallazgo | Dato | Acción propuesta (fase) |
|---|---|---|
| Boilerplate de BD | 365 `new SqlConnection`, 901 `AddWithValue`; más repetidos: IntegracionesService 16, CargueHojasService 14, PermisosService 13, AccesoController 13 | Helper central en `BaseService` y migrar servicio a servicio (F3) |
| Sin DI | 413 `new XService()`, 0 registros en el contenedor, 0 interfaces | Registrar servicios y migrar controlador a controlador (F4) |
| Archivos gigantes | CargueOrquestadorService 2199, DatosController 1448, AccesoController 1433, CargueHojasService 923, UsuarioController 846 | Dividir por responsabilidad (F5) |
| Nulabilidad | Con `Nullable=enable` hay ≈2.100 avisos (CS8618 726, CS8601 334, CS8625 289, CS8600 279) | Activar por archivo con `#nullable enable`, empezando por Helpers (F6) |
| Avisos ocultos | `NoWarn` oculta CS0168/CS0219/CS1998/CS0618 pero hoy no hay ninguno real | Retirar esos `NoWarn` (F2) |
| Analizadores | 121 `using` sobrantes, 15 parámetros sin usar (IDE0060), 6 asignaciones inútiles (IDE0059), 1 privado sin uso (IDE0051), 1 campo sin leer (IDE0052) | Limpiar (F2) |
| Reglas CA | CA1822 (métodos que podrían ser estáticos) ≈143, CA1861/CA1825 (arrays constantes), CA1854 (`TryGetValue`), CA1850 (`HashData`), CA5350/5351 (SHA1/MD5/3DES: revisar si son solo TOTP/HIBP/compatibilidad) | Aplicar los seguros (F2); revisar CA53xx (F2, con cuidado) |
| `catch {}` vacíos | 57 (HistorialVersionesCarguesService 8, PermisosService 7, AuditoriaService 5…) | Clasificar: intencionales («toleran tabla/columna ausente») llevan comentario; el resto `AppLogger.Warn` (F2) |
| Código muerto C# | Ningún método público sin referencias (el código está ajustado) | — |
| Clones C# | Bloques repetidos en controladores de informes (Index/filtros/usuario/empresas) y `EmailService` | Base común para los controladores Informe* (F5) |
| Serializadores | Newtonsoft 24 usos vs System.Text.Json 3 | Mantener Newtonsoft donde afecte el formato; lo nuevo con STJ (F6) |
| Capa Compat | 41 usos de `HttpContext.Current`/`System.Web` | Retirar por puntos (F6) |
| Async | Solo 38 `async` | Solo correo/IA/API donde haya espera real (F6) |
| Paquetes | Sin vulnerables. Atrasados: Data.SqlClient 6.1.2→7.x, MailKit 4.16→5, NewtonsoftJson 10→11 (saltos mayores: evaluar aparte), ClosedXML/Newtonsoft/BCrypt parches | Parches seguros en F2; mayores solo con acuerdo |
| bootstrap-icons | `bootstrap-icons.min.css` (8.340 líneas) + fuentes, usado en UNA vista (`ConfiguracionesEmpresas`, 3 iconos) | Reemplazar por Font Awesome y borrar (F7) |
| JS duplicado en vistas | 287 `<script>` en vistas; el generador de PDF se repite ≈7 veces (`nuevaPagina`, `tituloSeccion`, `tarjeta`, `pildora`, `encabezadoPagina`, `degradado`, `rgb`, `fuente`, `ancho`…) y el slider de rango ≈5 (`iniciarArrastreSlider`, `moverExtremo`, `indiceDesdeClientX`…) | Extraer a `Assets/js/pdf-informe.js` y `rango-slider.js` (F7); CSP: sin handlers en línea |
| Vistas enormes | InformePYG 2233, InformeBalance 1851, InformeFlujo 1844, InformeLineaTiempo 1781 líneas | Se reducen al sacar el JS compartido (F7) |
| CSS | 1.768 hex literales y 689 `style=` en vistas | Pasar a variables/clases existentes solo donde ya haya clase (F7) |

## Fase 2 — limpieza sin riesgo (HECHA 2026-11-16, sin comitear)
- 121 `using` sobrantes quitados (107 archivos); parámetros/variables sin uso quitados (ApiV1, InformeHistPrecios, Permisos, Reportes, InformeFlujo, InformeTesoreriaPpto, ConfiguracionVariablesPBI, TemaInforme, TemaInformeService, DocumentacionAcceso). Quedan a propósito: parámetros de `Compat/`/`Infra/` (firma emulada) y `ArmarFilaReporte` de PYG (varias llamadas multilínea).
- `NoWarn` reducido a `CA1416`: el build sigue en 0 avisos sin ocultar CS0168/CS0219/CS1998/CS0618/ASP0019/MVC1000/SYSLIB.
- Nuevo `Helpers/TransaccionHelper.RollbackSeguro(tx)` reemplaza 15 `try { tx.Rollback(); } catch { }`. Los demás `catch {}` se revisaron: son intencionales (compatibilidad de columnas `IndexOutOfRangeException`, configuración con valor por defecto, caché/limpieza). Único cambio de comportamiento: `Compat/HttpCompat2.QueueBackgroundWorkItem` ahora REGISTRA con `AppLogger.Error` los errores de trabajos en segundo plano (antes se tragaban).
- Paquetes (parches): ClosedXML 0.105.0→0.105.1, Newtonsoft.Json 13.0.3→13.0.4. Existe un stash antiguo en master («Prueba actualizacion librerias»): no tocarlo.
- Comentarios de una línea: Program.cs, BaseService, EmpresaAccesoHelper (se amplían módulo a módulo en las fases siguientes).
- Verificación: build 0/0, 23 pruebas verdes, sitio arrancado en local (GET `/`→302, `/Acceso/Login`→200, estático 200, `/Error/Status?code=404`→404, `/api/v1/ping`→401).
- Pendiente de F2 por riesgo/valor: CA1822 (métodos estáticos) y CA1861/CA1825 en lote → se hacen archivo a archivo junto a las fases 3-5.

## Fase 3 — acceso a datos reutilizable (HECHA 2026-11-16, sin comitear)
- Nuevo `Helpers/Bd.cs`: `Ejecutar` (INSERT/UPDATE/DELETE/SP → filas), `Escalar<T>`, `Lista<T>`, `Primero<T>`, `Leer` (recorre filas con lambda), `LeerPrimera` (solo la primera fila). Parámetros como objeto anónimo (`new { Id = id }` → `@Id`; null → DBNull). Opcionales `esSp: true` y `timeoutSeg:`. Cada llamada abre/cierra su conexión (pool). Probado sin BD en `Tests/Bufins.Pruebas/BdTests.cs`.
- Migración automática con una herramienta Roslyn (no versionada, vivió en el scratchpad de la sesión): solo reescribe bloques `using (var cn = new SqlConnection(...))` cuya forma coincide EXACTAMENTE (comando estándar, `AddWithValue` con nombre literal, `cn.Open()`, y un único `ExecuteNonQuery`/`ExecuteScalar` con `Convert.ToInt32/64` o `while/if (r.Read())` sin return/break/await/ref-out). El resto queda a mano a propósito.
- Resultado: 157 bloques migrados (70 Ejecutar, 64 Leer, 17 Escalar, 6 LeerPrimera) en 47 archivos; `new SqlConnection` 365 → 203; ≈1.040 líneas menos. Build 0/0, pruebas verdes, GET de humo OK.
- Cambio de matiz documentado: `Escalar<T>` devuelve `default` si la celda es NULL (antes `Convert.ToInt32(DBNull)` lanzaba excepción); un parámetro `null` ahora viaja como DBNull (antes `AddWithValue(…, null)` fallaba).
- Lo que sigue a mano (por diseño): transacciones (`SqlTransaction`), parámetros OUTPUT, varios result sets, `SqlBulkCopy`, bloques con lógica intercalada, `Parameters.Add(tipo)`.
- Regla para código NUEVO: usar `Bd.*` en lugar de abrir `SqlConnection` a mano (salvo los casos anteriores).

## Fase 4 — inyección de dependencias (HECHA 2026-11-16, sin comitear)
- `Infra/ServiciosExtensions.AddServiciosBufins()` (llamado en `Program.cs`): registra por reflexión como transitorios todos los `*Service` públicos, no estáticos/abstractos, de `bufinscustomers.Services` con constructor público sin parámetros (se crean siempre con ese constructor).
- 50 controladores pasan a constructor primario (C# 12): 68 campos `= new XService()` eliminados y 230 `new XService()` sueltos reemplazados por la dependencia inyectada. `new XService()` en `Controllers/`: 305 → 9. Los servicios con estado mutable de instancia solo se inyectaron donde ya eran un campo (no se comparten entre llamadas).
- Omitidos a propósito (3): clases base de otros controladores y las que ya tenían constructor explícito (AccesoController, ConfiguracionEmpresaController…); quedan para una pasada manual.
- Los servicios, helpers y filtros NO cambian (siguen `new`), así no se rompen sus ~400 puntos de uso.
- Prueba nueva `Tests/Bufins.Pruebas/InyeccionDependenciasTests.cs`: todo parámetro de constructor de todo controlador debe ser resoluble.
- Balance de líneas: ≈ +30 netas (un constructor primario cuesta casi lo mismo que los campos que reemplaza); el beneficio es arquitectónico (dependencias explícitas y comprobables).

## Fase 5 — dividir lo grande (HECHA 2026-11-16, sin comitear)
Orden por riesgo; ninguna ruta, permiso ni lógica cambió (la herramienta verificó que el contenido de cada miembro movido es idéntico).
1. `NombreMes` y `TieneAccesoEmpresa` (copias idénticas en 13 controladores) subieron a `BaseController` (`protected`).
2. `DatosController` (1.448 líneas) → `partial`: `.cs` (plantillas y descargas, 241) + `.Modelos` (741) + `.Cargue` (253) + `.Hallazgos` (267). `AccesoController` (1.433) → `.cs` (login y sesión, 609) + `.DobleFactor` (348) + `.Recuperacion` (298) + `.AlertasSeguridad` (183). Mismo número de acciones públicas antes y después (20 y 26). Los controladores NO se separaron en clases nuevas para no cambiar URLs (`/Datos/...`, `/Acceso/...`) ni `MenuOpciones`.
3. `CargueOrquestadorService` (2.199) → `partial` en 5 archivos + modelos a `CargueOrquestadorModelos.cs`; las 5 conversiones puras (`ConvertirCeldaFecha/Bit`, `ExtraerDecimal`, `SanitizarValorNumerico`, `DetectarErrorNumerico`) se extrajeron a `Helpers/CargueConversion.cs` con 42 pruebas de caracterización (pasaron a la primera: confirman que la heurística colombiana de cifras se lee como se pensaba). Paridad Excel↔API: sin cambios (misma lógica, mismos métodos públicos).
- Pasada de `using` sobrantes tras mover código (-91 en total en la fase). Pruebas: 71 verdes. Build 0/0. Humo GET OK.
- No se creó una clase base para todos los informes (13 controladores demasiado distintos). Pendiente opcional: pasar `AccesoController`/`ConfiguracionEmpresaController` (constructor explícito) a constructor primario.

## Fase 6 — plataforma (HECHA 2026-11-16, sin comitear; alcance acotado a lo seguro)
- **Compat nivel 1 retirado** (atajos de MVC 5 sin lógica): `Json(x, JsonRequestBehavior.AllowGet)` → `Json(x)` (128 llamadas), `new HttpStatusCodeResult(n)` → `new StatusCodeResult(n)` (18), `HttpNotFound()`/`new HttpNotFoundResult()` → `NotFound()`/`new NotFoundResult()` (8), `HttpPostedFileBase` → `IFormFile` (9) y `HttpRequestBase` → `HttpRequest` (2). Se borraron esos tipos y los alias globales; BaseController perdió 2 atajos. Comportamiento idéntico (los atajos solo delegaban).
- **Compat nivel 2 se CONSERVA a propósito**: `HttpRuntime.Cache` (26 usos), `AppHttp.Current` (21), `ConfigurationManager` (21), `MachineKey`, `SesionCompat`/`Session[…]` (24+), `HttpCookie`, `HostingEnvironment`. Tocarlos significa rehacer sesión, cookies y caché (login, 2FA, passkeys): riesgo alto para un beneficio bajo hoy. Reabrir solo con pruebas de integración del login.
- **Nulabilidad por archivo** (`#nullable enable`): `Bd`, `CargueConversion`, `AlertaUsuarioCorreo`, `AssetVersion`, `SecretosProtegidos`, `EmpresaAccesoHelper`, `PoliticaContrasena`, `ClientIpHelper` — 0 avisos. El resto queda sin activar: activarlo en todo el proyecto da ≈2.080 avisos (Models 518: propiedades POCO sin inicializar; Services 832; Controllers 457). Regla: **cada archivo que se toque a partir de ahora entra con `#nullable enable`** (anotar `string?`; nunca cambiar `null` por `""` para callar un aviso: alteraría datos).
- **Async: no se hizo masivamente.** Los `.Result`/`.GetAwaiter().GetResult()` (IAGateway, Webhooks, PoliticaContrasena/HIBP) son sync-over-async deliberados en métodos síncronos con muchos llamadores; convertirlos obliga a cambiar la firma de controladores y servicios en cascada. En ASP.NET Core no hay SynchronizationContext, así que no hay riesgo de interbloqueo; el costo es un hilo bloqueado durante la llamada. Los `Task.Run` de segundo plano (Webhooks, ApiEjecuciones) ya capturan y registran sus errores.
- **Serializador**: se mantiene Newtonsoft (24 usos, define el formato de salida de las pantallas y de la API); lo nuevo puede usar System.Text.Json.
- **No se hicieron** namespaces de archivo (`namespace X;`): reindentar 200 archivos destruiría el `git blame` y toca literales multilínea sin beneficio funcional.

## Fase 7 — frontend (HECHA 2026-11-16 en lo seguro, sin comitear; el resto queda como propuesta)
- **Hecho:** retirado `bootstrap-icons` (`bootstrap-icons.min.css` 8.340 líneas + `.woff` + `.woff2` ≈ 430 KB): lo usaba UNA vista con 3 iconos (`ConfiguracionesEmpresas`), ahora Font Awesome (`fas fa-building`, `fa-cog`, `fa-list-ul`). Revisado: `jquery-ui` SÍ se usa (`.sortable` en esa misma vista) y no hay otros CSS/JS sin referencia.
- **Corrección del diagnóstico de la Fase 1:** el JS duplicado entre informes es mucho menos extraíble de lo estimado. Análisis con un parser (acorn) de las 12 vistas de informes (≈12.000 líneas de JS en línea): solo 375 líneas están repetidas BIT A BIT en 2+ vistas y, de ellas, solo ≈95 son funciones puras (sin variables de la página): `capturarChartPdf` (×3), `evaluarFiltroCampo` (×2), `variacionRelativa` (×3), `ocultarResultados` (×2) y algunos de una línea (`esc`, `escHtml`, `limpiar`, `fmt`). Extraerlas ahorraría ≈ 75 líneas y crearía otro archivo global: **no se hizo** (ganancia ≈ 0).
- **Las "familias" grandes NO son copias idénticas sino copia-y-modifica:** PDF (`tarjeta`, `tituloSeccion`, `encabezadoPagina`, `pildora`, `degradado`, `nuevaPagina`: 2-5 variantes cada una; dependen de `pdf`, `C`, `W`, `M`, `rgb`, `fuente`… del cierre de cada página) y slider de rango (`iniciarArrastreSlider`, `moverExtremo`, `indiceDesdeClientX`, `moverKnobConTeclado`…: 5 variantes cada una). Unificarlas = diseñar módulos `informe-pdf.js` y `rango-slider.js` con configuración por página. Ahorro realista: 500-800 líneas de 12.000, con riesgo de romper PDFs y filtros que solo se verifican en un navegador con sesión real.
- **Propuesta (no ejecutada, requiere tu verificación en navegador):** migrar UN informe (el de PYG) a los módulos compartidos, que lo pruebes (generar, PDF, imagen, correo, tema) y, si todo coincide, repetir informe por informe. Para comparar sin riesgo se grabaría la secuencia de llamadas a `pdf.*` antes y después con un `pdf` simulado en Node.
- Pendiente de nulabilidad detectado en esta fase: un build INCREMENTAL puede ocultar avisos; verificar siempre con `dotnet build --no-incremental`. Se corrigieron 3 avisos nuevos en `ClientIpHelper` y `PoliticaContrasena` (anotaciones, sin cambio de comportamiento).

## Fase 9 — resumen del refactor (ENTREGADO 2026-11-16; se hizo antes de la Fase 8 a petición del usuario)
Cifras contra la etiqueta `base-pre-refactor-2026-11-16` (script de medición en el scratchpad de la sesión; reproducible con `git ls-tree`/`git show`).

| Métrica | Antes | Ahora | Dif |
|---|---|---|---|
| C# del sitio (líneas) | 51.476 | 50.524 | −952 |
| · Services / Controllers | 23.774 / 17.842 | 22.625 / 17.744 | −1.149 / −98 |
| · Helpers (nuevos `Bd`, `CargueConversion`, `TransaccionHelper`) | 3.365 | 3.680 | +315 |
| Archivo C# más grande | 2.199 | 900 | −1.299 |
| `new SqlConnection` / `AddWithValue` | 365 / 903 | 206 / 594 | −159 / −309 |
| `new XService()` en controladores | 305 | 9 | −296 |
| `catch {}` vacíos | 57 | 40 | −17 |
| `JsonRequestBehavior` | 131 | 0 | −131 |
| Copias de `NombreMes`/`TieneAccesoEmpresa` | 13 | 0 (1 en BaseController) | −13 |
| `NoWarn` del proyecto | 9 códigos | 1 (`CA1416`) | −8 |
| CSS (líneas) | 23.550 | 15.210 | −8.340 (bootstrap-icons + 2 fuentes ≈ 430 KB) |
| Pruebas automáticas | 0 | 71 (6 archivos, 363 líneas) | +71 |
| Clases `partial` por responsabilidad | 0 | 10 archivos | |
| `#nullable enable` | 0 | 8 helpers | |
| Build | 0 avisos | 0 avisos | = |

Lo que NO se redujo: los Helpers crecieron (+315: código nuevo reutilizable) y la DI no ahorra líneas (≈ +30). El ahorro real de C# está en acceso a datos (−1.040) y duplicados; el mayor ahorro de bytes es el frontend (−8.340 líneas de CSS). Total del repo contra la base: 186 archivos, +6.132 / −14.949 líneas (incluye las 8.340 de CSS y ≈ 360 de pruebas).

Cómo fue cada fase: F0 rama + etiqueta + pruebas; F1 diagnóstico (con un error de estimación del JS duplicado corregido en la F7); F2 limpieza (121 `using`, parámetros sin uso, `NoWarn`, `TransaccionHelper`, log en segundo plano); F3 `Bd` + codemod Roslyn (157 bloques); F4 DI con constructor primario (50 controladores); F5 partial por responsabilidad + `CargueConversion` con 42 pruebas; F6 atajos MVC 5 fuera de Compat + nulabilidad por archivo; F7 bootstrap-icons fuera; F9 este resumen.

Pendientes con decisión tomada: Compat nivel 2 (sesión, cookies, caché, config) se conserva; async masivo no; piloto de módulos JS compartidos (PDF, slider) solo con verificación del usuario en navegador; nulabilidad global no (≈2.080 avisos, sobre todo `Models`). Antes de publicar: probar a mano en un entorno de pruebas las pantallas que cambiaron de acceso a datos (Configuración, Escenarios, Integraciones, Permisos, Auditoría) y el flujo de cargue por Excel y por API, porque los POST no se pueden probar contra la BD de producción.

## Fase 8 — documentación de cómo está construido el sitio (HECHA 2026-11-16)
- Nueva **Guía de arquitectura y código de BUFINS** (`App_Data/Documentacion/Tecnica/Guia_Arquitectura_y_Codigo_BUFINS.html`, 54 KB, 17 secciones, 31 bloques de código real, 3 diagramas de flujo): cómo usarla, mapa de capas, carpetas con cifras, recorrido de una petición, ejemplo de las tres capas con el módulo de Escenarios, acceso a datos (`Bd`), inyección de dependencias, seguridad y sesión, cargue, informes/IA, interfaz, pruebas, receta de pantalla nueva, glosario de 22 conceptos de C#/.NET con ejemplos del propio código, ruta de lectura con ejercicios, refactor y deuda, preguntas frecuentes. Solo español. Estilo único (`Informe_Indicadores.html`) + CSS extra para código.
- Generador `Tools/Documentacion/gen_doc_arquitectura.py` + `doc_arquitectura.py`. Registrada en la biblioteca «Documentación BUFINS» (`tecnica-arquitectura`, solo Super Admin por defecto) a petición del usuario.
- Se actualizó también la **Documentación técnica** existente (`doc_tecnica.py`, FECHA 2026-11-16, es/en): ClosedXML 0.105.1, Newtonsoft.Json 13.0.4, tamaños, inyección de dependencias, `Bd` y las 71 pruebas (antes decía «no hay pruebas automáticas»).
- Verificación: los 33 archivos citados con ruta existen; el HTML renderiza (17 secciones, 3 flujos, 31 bloques de código).
- Hallazgo anotado en la guía como deuda: ≈39 puntos de controladores aún envían `ex.Message` al navegador (regla: `MensajeError(ex)`).

## Orden
F2 limpieza sin riesgo → F3 datos → F4 DI → F5 dividir/base informes → F6 plataforma (nulabilidad, async, Compat) → F7 frontend → **F9 resumen del refactor (qué se redujo y cómo se hizo; se entrega ANTES de la F8; métricas contra la etiqueta `base-pre-refactor-2026-11-16`)** → F8 documentación técnica (se mantiene intacta tal como se definió) (`App_Data/Documentacion/Tecnica/`, con glosario de C#/.NET con ejemplos del propio código). Comentarios de una línea (español, solo el «porqué») se agregan módulo a módulo en cada fase.
