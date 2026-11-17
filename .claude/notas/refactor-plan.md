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

## Orden
F2 limpieza sin riesgo → F3 datos → F4 DI → F5 dividir/base informes → F6 plataforma (nulabilidad, async, Compat) → F7 frontend → F8 documentación técnica (`App_Data/Documentacion/Tecnica/`, con glosario de C#/.NET con ejemplos del propio código). Comentarios de una línea (español, solo el «porqué») se agregan módulo a módulo en cada fase.
