# Scripts SQL — Escenarios de datos

Carpeta nueva (antes no había ningún script SQL versionado en el repo). Ejecutar en **SSMS**, en
este orden, idealmente primero contra un ambiente de pruebas.

## Orden de ejecución

1. `001_Escenarios_CreateTable.sql` — catálogo `dbo.Escenarios` (Escenario 1 = principal, Escenario 2).
2. `002_Ini_AddEscenario.sql` — agrega `IdEscenario` (+ FK + índice recomendado) a las 9 tablas `Ini_*`.
3. `003_HistorialVersionesCargues_AddEscenario.sql` — agrega `IdEscenario` a `HistorialVersionesCargues`.
4. `004_AuditoriaCargues_AddEscenario.sql` — agrega `IdEscenario` a `AuditoriaCargues`.
5. `StoredProcedures\dbo.sp_Modelo*.sql` (8 archivos, cualquier orden — usan `CREATE OR ALTER`):
   `sp_ModeloBalance`, `sp_ModeloPYG`, `sp_ModeloBalancePpto`, `sp_ModeloLineasNegocio`,
   `sp_ModeloTesoreriaPpto`, `sp_ModeloBalanceDiff`, `sp_ModeloFlujoCaja`, `sp_ModeloFlujoEfectivo`.
6. `005_MenuOpcion_Escenarios_OPCIONAL.sql` — **opcional**: crea la opción de menú "Escenarios"
   (`GestorEscenariosController`) directo en `MenuOpciones` para que aparezca en el sidebar sin
   pasar por la UI de `/MenuOpciones`. Ver el encabezado del script.

Todos los scripts son **idempotentes** (seguro re-ejecutarlos): los `ALTER TABLE` verifican si la
columna/FK/índice ya existe antes de crearlo, y los SP usan `CREATE OR ALTER PROCEDURE`.

## Qué hace cada grupo

- **002**: si tu instalación tiene la columna `Año` declarada como `TEXT`/`NTEXT`/`VARCHAR(MAX)`/
  `NVARCHAR(MAX)` en alguna tabla `Ini_*` (inconsistencia histórica de esquema en algunas
  instalaciones, no algo que introduzca esta migración), SQL Server no permite usarla como
  columna de índice y el paso 3 (índice recomendado) se omite automáticamente para esa tabla con
  un `PRINT` de aviso — la columna `IdEscenario` y su FK sí se agregan sin problema. Cada paso de
  cada tabla va en su propio `TRY/CATCH`, así que un problema en una tabla no detiene el resto.
- **001–004**: son aditivos con `DEFAULT 1` en la columna nueva → cero downtime, cero filas rotas.
  Todo lo cargado hasta hoy queda automáticamente en "Escenario 1". Para agregar un "Escenario 3"
  a futuro: `INSERT INTO dbo.Escenarios (Id, Nombre, Orden, Activo) VALUES (3, N'Escenario 3', 3, 1);`
  — no requiere ningún otro cambio de esquema.
- **Los 8 SP**: cada uno recibe ahora `@IdEscenario TINYINT = 1` (default = Escenario 1, compatible
  con cualquier llamador que aún no lo pase) y filtra `AND ISNULL(IdEscenario,1) = @IdEscenario` en
  cada lectura directa de una tabla `Ini_*`. Los SP que internamente llaman a otros SP de modelo
  (`sp_ModeloBalanceDiff`, `sp_ModeloBalancePpto`, `sp_ModeloTesoreriaPpto`, `sp_ModeloFlujoCaja`,
  `sp_ModeloFlujoEfectivo`) propagan `@IdEscenario` en cada `EXEC` anidado — si no se propagara, el
  escenario se "perdería" en la cadena y esos modelos siempre calcularían sobre Escenario 1 sin
  importar lo que pida el usuario.
- **Todas las tablas/SELECT finales muestran `IdEscenario` como PRIMERA columna** (tabla global
  `##Final*` que cada SP publica para consumo de otros SP, y el `SELECT` que se devuelve al llamador).
  Como cada ejecución de un SP procesa un solo escenario, `IdEscenario` se agrega como columna
  literal (`@IdEscenario AS IdEscenario`) justo en el punto de publicación — no se necesita
  arrastrarla por los cálculos intermedios (CTEs, cursores, joins), lo que mantiene el cambio de
  bajo riesgo. Donde un SP compuesto recibe la tabla final de otro con `INSERT ... EXEC` o
  `SELECT * FROM ##Final...` (que exigen coincidencia posicional exacta de columnas), la tabla
  temporal receptora (`#DatosPptoPYG2`, `#DatosBalance2`, `#DatosPYGPpto`, `#DatosBalDiffPpto`,
  `#DatosBalDiff` en `sp_ModeloFlujoCaja`/`sp_ModeloFlujoEfectivo`) también ganó `IdEscenario` como
  primera columna para no romper esas llamadas. `DatosController` no necesitó ningún cambio para
  esto: `EjecutarModeloYEscribirHoja`/`EjecutarModeloAjax` ya leen las columnas del `SELECT` final
  dinámicamente (`reader.GetName(i)`), así que `IdEscenario` aparece automáticamente como primera
  columna en la grilla y en el Excel exportado.

## Grafo de composición de los 8 SP (para referencia al revisar los cambios)

```
sp_ModeloBalance          (hoja: Ini_BalancePrueba, Ini_CteYnoCte)
sp_ModeloPYG              (hoja: Ini_PYG, Ini_PptoPYG, Ini_PptoPYGConAjuste)
sp_ModeloBalancePpto      (hoja + compone: sp_ModeloPYG, sp_ModeloBalance, sp_ModeloTesoreriaPpto)
sp_ModeloLineasNegocio    (hoja: Ini_PYG — no compone)
sp_ModeloTesoreriaPpto    (hoja + compone: sp_ModeloPYG, sp_ModeloBalanceDiff, sp_ModeloBalance)
sp_ModeloBalanceDiff      (compuesto puro: sp_ModeloBalance, sp_ModeloPYG, sp_ModeloBalancePpto)
sp_ModeloFlujoCaja        (compuesto puro: sp_ModeloPYG, sp_ModeloBalanceDiff, sp_ModeloBalance)
sp_ModeloFlujoEfectivo    (compuesto puro: sp_ModeloPYG, sp_ModeloBalanceDiff)
```

## Si `sp_ModeloFlujoCaja`/`sp_ModeloFlujoEfectivo` (o cualquier otro) da "A severe error occurred on
the current command" al probarlo

Estos SP se llaman unos a otros de forma anidada y comparten tablas temporales **globales**
(`##FinalBalance`, `##FinalPYG`, `##FinalBalancePpto`, `##FinalTesoreriaPpto`, `##FinalBalanceDiff`)
que persisten en `tempdb` hasta que se eliminan explícitamente o se cierra la conexión que las creó.
Si una ejecución de prueba anterior quedó a medias (p. ej. otro `EXEC` que falló a mitad de camino
mientras estabas probando), puede quedar una de estas tablas "vieja" en `tempdb` con una estructura
distinta a la que el SP espera ahora, y eso se manifiesta como este error genérico.

`sp_ModeloPYG` (creación de `##FinalPYG`) y `sp_ModeloBalancePpto` (creación de `##FinalBalancePpto`)
ya quedaron con un `IF OBJECT_ID(...) IS NOT NULL DROP TABLE ...` antes de crear su propia tabla
final (los demás SP ya lo tenían) — así que, con los scripts actualizados, un residuo de una
ejecución anterior se limpia solo. Aun así, si el error persiste:

1. Cierra y vuelve a abrir la conexión/pestaña de SSMS (las tablas `##` viven mientras viva la
   conexión que las creó) — es la forma más segura de partir de cero.
2. O límpialas a mano antes de reintentar:
   ```sql
   IF OBJECT_ID('tempdb..##FinalBalance')       IS NOT NULL DROP TABLE ##FinalBalance;
   IF OBJECT_ID('tempdb..##FinalPYG')           IS NOT NULL DROP TABLE ##FinalPYG;
   IF OBJECT_ID('tempdb..##FinalBalancePpto')   IS NOT NULL DROP TABLE ##FinalBalancePpto;
   IF OBJECT_ID('tempdb..##FinalTesoreriaPpto') IS NOT NULL DROP TABLE ##FinalTesoreriaPpto;
   IF OBJECT_ID('tempdb..##FinalBalanceDiff')   IS NOT NULL DROP TABLE ##FinalBalanceDiff;
   ```
3. Vuelve a desplegar los 8 `CREATE OR ALTER PROCEDURE` (por si probaste una versión intermedia) y
   reintenta el `EXEC` de prueba.

**Causa real encontrada y corregida:** `sp_ModeloFlujoCaja` y `sp_ModeloFlujoEfectivo` llaman a
`sp_ModeloBalanceDiff` sin pasar `@IncluirTesoreria`, así que ese parámetro quedaba en su valor por
defecto (`1`). Con `@IncluirTesoreria = 1`, `sp_ModeloBalanceDiff` → `sp_ModeloBalancePpto` →
`sp_ModeloTesoreriaPpto` → `sp_ModeloBalanceDiff` (otra vez) — una cadena recursiva de SP anidados
compartiendo tablas temporales **globales** (`##Final*`) que dispara "A severe error occurred on
the current command" en SQL Server al llegar al segundo nivel de recursión. `sp_ModeloTesoreriaPpto`
ya evitaba este mismo ciclo en su propia llamada a `sp_ModeloBalanceDiff` pasando
`@IncluirTesoreria = 0` explícito; ahora `sp_ModeloFlujoCaja` y `sp_ModeloFlujoEfectivo` hacen lo
mismo en su propio `EXEC dbo.sp_ModeloBalanceDiff ...`, rompiendo el ciclo un nivel antes.

> Se probó primero forzar `OPTION (MAXDOP 1)` (plan serie, sin paralelismo) en las sentencias con
> funciones de ventana de los 7 SP de la cadena — **no resolvió el error** y ya se revirtió por
> completo; el causante real era la recursión, no el paralelismo del plan.

**Trade-off a tener en cuenta:** con `@IncluirTesoreria = 0`, el cálculo interno de Balance
Presupuestado que hace `sp_ModeloBalanceDiff` dentro de `sp_ModeloFlujoCaja`/`sp_ModeloFlujoEfectivo`
**no incluye** los ajustes que vienen de Tesorería sobre las filas "Deuda actual"/"Deuda nueva
(Déficit de tesorería)" — igual que ya ocurre hoy en el propio `sp_ModeloTesoreriaPpto`. Si estos
dos reportes necesitaran esa fidelidad completa, habría que investigar una forma de romper el ciclo
sin perder ese dato (por ejemplo, materializando el resultado de Tesorería una sola vez antes de la
cadena en vez de forzar `@IncluirTesoreria = 0`).

Si aun con esto el error persistiera en algún ambiente, el siguiente paso es capturar el error real
(el "severe error" que ves es el mensaje genérico del cliente; SQL Server casi siempre manda algo
más detallado que se pierde en la traducción). Corre esto y pásame el resultado completo:
```sql
BEGIN TRY
    EXEC dbo.sp_ModeloFlujoCaja 1, 1;
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER() AS Numero, ERROR_SEVERITY() AS Severidad, ERROR_STATE() AS Estado,
           ERROR_PROCEDURE() AS Procedimiento, ERROR_LINE() AS Linea, ERROR_MESSAGE() AS Mensaje;
END CATCH
```
También ayuda cambiar la salida de SSMS a "Resultados en texto" (Ctrl+T) en vez de grilla antes de
correr el `EXEC` — hay un problema conocido de SSMS donde la grilla muestra este mismo mensaje
genérico aunque el procedimiento haya terminado bien, cuando devuelve varios result sets de formas
distintas.

## Fuera de alcance de estos scripts

- Las vistas `*_VT` de Informes y `sp_ObtenerAuditoriaCarguesPorEmpresa` — no se tocan en esta
  entrega (ver plan). `AuditoriaCargues` ya tiene la columna `IdEscenario` (script 004) pero el
  grid de "Auditoría de Cargues" no la mostrará hasta que se actualice ese SP.
- `SP_ValidarPlantillaInicial` — no se modifica; solo recibe `@IdUsuario`, no parece depender de
  empresa/año/escenario.
