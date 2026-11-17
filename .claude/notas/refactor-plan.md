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

## Orden
F2 limpieza sin riesgo → F3 datos → F4 DI → F5 dividir/base informes → F6 plataforma (nulabilidad, async, Compat) → F7 frontend → **F9 resumen del refactor (qué se redujo y cómo se hizo; se entrega ANTES de la F8; métricas contra la etiqueta `base-pre-refactor-2026-11-16`)** → F8 documentación técnica (se mantiene intacta tal como se definió) (`App_Data/Documentacion/Tecnica/`, con glosario de C#/.NET con ejemplos del propio código). Comentarios de una línea (español, solo el «porqué») se agregan módulo a módulo en cada fase.
