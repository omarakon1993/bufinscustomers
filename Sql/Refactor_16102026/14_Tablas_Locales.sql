-- 14: Reemplaza ##Final* / ##Cache* por tablas locales # de cada ejecucion y quita el bloqueo global.
-- Solo se serializa el DELETE+INSERT final por SP/empresa/escenario. Usa ALTER (conserva permisos).
-- Despues ejecutar 99_Validar_Resultados.sql.
USE [bufinscustomers];
GO
SET NOCOUNT ON; SET XACT_ABORT ON;

-- Tablas de la ejecucion (Anio -> A+n tilde+o para no depender de la codificacion del archivo)
DECLARE @Blk NVARCHAR(MAX) = REPLACE(N'-- Tablas locales de la ejecucion: aisladas por sesion y visibles en las llamadas anidadas
		CREATE TABLE #CtxModelo (x BIT);
		CREATE TABLE #OutPYG (IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorAcumulado MONEY, ValorForecast MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY, ValorPresupuesto MONEY, ValorPresupuestoAcumulado MONEY, ValorPresupuestoConAjuste MONEY);
		CREATE TABLE #OutBalance (IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY);
		CREATE TABLE #OutBalanceDiff (IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorPresupuesto MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY);
		CREATE TABLE #OutBalancePpto (IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorFuturo MONEY, Tabla VARCHAR(500));
		CREATE TABLE #OutTesoreriaPpto (IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(100), Descripcion VARCHAR(200), Valor MONEY, ValorAcumulado MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY, ValorForecast MONEY, Tabla VARCHAR(200));
		-- Reuso solo dentro de esta ejecucion: se eliminan solas al terminar el SP raiz
		CREATE TABLE #CachePYG (IdEmpresa INT, VersionCache INT, FechaCache DATETIME2, IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorAcumulado MONEY, ValorForecast MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY, ValorPresupuesto MONEY, ValorPresupuestoAcumulado MONEY, ValorPresupuestoConAjuste MONEY);
		CREATE TABLE #CacheBalance (IdEmpresa INT, VersionCache INT, FechaCache DATETIME2, IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY);
		CREATE TABLE #CacheBalanceDiff (IdEmpresa INT, IncluirTesoreria TINYINT, VersionCache INT, FechaCache DATETIME2, IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorPresupuesto MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY);
		CREATE TABLE #CacheBalancePpto (IdEmpresa INT, IncluirTesoreriaCache TINYINT, VersionCache INT, FechaCache DATETIME2, IdEscenario TINYINT, Anio INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorFuturo MONEY, Tabla VARCHAR(500));',
	N'Anio', N'A' + NCHAR(241) + N'o');
DECLARE @CrLf NCHAR(2) = NCHAR(13) + NCHAR(10);

-- Operaciones en orden. SP NULL = los 8. Fin <> NULL = corta desde Old hasta Fin. Cnt = ocurrencias exactas exigidas.
DECLARE @Ops TABLE (Id INT IDENTITY PRIMARY KEY, SP SYSNAME NULL, Old NVARCHAR(MAX), New NVARCHAR(MAX), Fin NVARCHAR(200) NULL, Cnt INT NULL);
INSERT @Ops (SP, Old, New, Fin, Cnt) VALUES
 -- Control: sin bloqueo global; solo bloqueo de persistencia por SP/empresa/escenario
 (NULL, N'IF @Raiz = 1 BEGIN SET @Drop', N'IF @Persist = 1 EXEC sp_releaseapplock @Resource = @LockPersist, @LockOwner = ''Session'';', N'END', 1),
 (NULL, N'SET @Drop = N''''; SELECT @Drop', N'', N'EXEC sp_executesql @Drop;', 1),
 (NULL, N'IF @rc < 0 BEGIN', N'', N'RETURN; END', 1),
 (NULL, N'EXEC @rc = sp_getapplock @Resource = ''sp_Modelo''', @Blk, N'@LockTimeout = 600000;', 1),
 (NULL, N'APPLOCK_MODE(''public'', ''sp_Modelo'', ''Session'') = ''NoLock''', N'OBJECT_ID(''tempdb..#CtxModelo'') IS NULL', NULL, 1),
 (NULL, N'@rc INT, @Drop NVARCHAR(MAX);', N'@rc INT, @Persist BIT = 0, @LockPersist NVARCHAR(255) = OBJECT_NAME(@@PROCID) + N''|'' + CAST(@IdEmpresa AS NVARCHAR(10)) + N''|'' + CAST(@IdEscenario AS NVARCHAR(10));', NULL, 1),
 (NULL, N'DELETE FROM dbo.Modelo', N'EXEC @rc = sp_getapplock @Resource = @LockPersist, @LockMode = ''Exclusive'', @LockOwner = ''Session'', @LockTimeout = 600000; IF @rc >= 0 SET @Persist = 1;' + @CrLf + N'            DELETE FROM dbo.Modelo', NULL, 1),
 (NULL, N'bloquea y limpia; las llamadas anidadas reutilizan', N'crea las tablas # de la ejecucion; las llamadas anidadas las reutilizan', NULL, NULL),
 (NULL, N'limpia y libera el bloqueo en la ejecuci', N'libera el bloqueo de persistencia de esta ejecuci', NULL, NULL),
 -- Productores: SELECT INTO ## -> INSERT INTO #Out
 (N'sp_ModeloBalance', N'SELECT @IdEscenario AS IdEscenario, * INTO ##FinalBalance FROM #FinalBalance;', N'INSERT INTO #OutBalance SELECT @IdEscenario AS IdEscenario, * FROM #FinalBalance;', NULL, 1),
 (N'sp_ModeloBalance', N'SET @SqlCache = N''SELECT IdEscenario, A', N'SET @SqlCache = N''INSERT INTO #OutBalance SELECT IdEscenario, A', NULL, 1),
 (N'sp_ModeloBalance', N'INTO ##FinalBalance FROM '' + @TC', N'FROM '' + @TC', NULL, 1),
 (N'sp_ModeloBalanceDiff', N'N''SELECT '' + @Cols + N'' INTO ##FinalBalanceDiff FROM ''', N'N''DELETE FROM #OutBalanceDiff; INSERT INTO #OutBalanceDiff SELECT '' + @Cols + N'' FROM ''', NULL, 1),
 (N'sp_ModeloBalanceDiff', N'SELECT IdEscenario, A', N'INSERT INTO #OutBalanceDiff SELECT IdEscenario, A', NULL, 1),
 (N'sp_ModeloBalanceDiff', N'INTO ##FinalBalanceDiff', N'', NULL, 1),
 (N'sp_ModeloBalancePpto', N'SET @SqlCache = N''SELECT IdEscenario, A', N'SET @SqlCache = N''INSERT INTO #OutBalancePpto SELECT IdEscenario, A', NULL, 1),
 (N'sp_ModeloBalancePpto', N'INTO ##FinalBalancePpto FROM '' + @TC', N'FROM '' + @TC', NULL, 1),
 (N'sp_ModeloBalancePpto', N'SELECT @IdEscenario AS IdEscenario, A', N'INSERT INTO #OutBalancePpto SELECT @IdEscenario AS IdEscenario, A', NULL, 1),
 (N'sp_ModeloBalancePpto', N'INTO ##FinalBalancePpto', N'', NULL, 1),
 (N'sp_ModeloPYG', N'N''SELECT '' + @Cols + N'' INTO ##FinalPYG FROM ''', N'N''DELETE FROM #OutPYG; INSERT INTO #OutPYG SELECT '' + @Cols + N'' FROM ''', NULL, 1),
 (N'sp_ModeloPYG', N'SELECT  @IdEscenario AS IdEscenario,', N'INSERT INTO #OutPYG SELECT  @IdEscenario AS IdEscenario,', NULL, 1),
 (N'sp_ModeloPYG', N'INTO ##FinalPYG', N'', NULL, 1),
 (N'sp_ModeloTesoreriaPpto', N'SELECT @IdEscenario AS IdEscenario, * INTO ##FinalTesoreriaPpto FROM #FinalTesoreriaPpto;', N'INSERT INTO #OutTesoreriaPpto SELECT @IdEscenario AS IdEscenario, * FROM #FinalTesoreriaPpto;', NULL, 1);

-- Consumidores y resto: DROP ## -> DELETE #Out; ##FinalX -> #OutX (nombres largos primero)
INSERT @Ops (SP, Old, New, Fin, Cnt)
SELECT NULL, v.Old, v.New, NULL, NULL
FROM (VALUES (1,N'BalanceDiff'),(2,N'BalancePpto'),(3,N'TesoreriaPpto'),(4,N'PYG'),(5,N'Balance')) x(o, X)
CROSS APPLY (VALUES
 (1, N'IF OBJECT_ID(''tempdb..##Final' + X + N''') IS NOT NULL DROP TABLE ##Final' + X + N';', N'DELETE FROM #Out' + X + N';'),
 (2, N'DROP TABLE ##Final' + X + N';', N'DELETE FROM #Out' + X + N';'),
 (3, N'##Final' + X, N'#Out' + X)) v(k, Old, New)
ORDER BY x.o, v.k;

-- Caché por ejecución: ##CacheX_<SPID> -> #CacheX
INSERT @Ops (SP, Old, New, Fin, Cnt) VALUES
 (NULL, N'_'' + CAST(@@SPID AS NVARCHAR(10))', N'''', NULL, NULL),
 (NULL, N'N''##Cache', N'N''#Cache', NULL, NULL),
 (N'sp_ModeloPYG', N'la tabla ## puede no existir', N'la tabla # puede no existir', NULL, NULL);

-- Cada productor vacia su salida al iniciar: si falla, el consumidor no lee datos de una llamada anterior
INSERT @Ops (SP, Old, New, Fin, Cnt)
SELECT N'sp_Modelo' + X, N'CAST(@IdEscenario AS NVARCHAR(10));',
       N'CAST(@IdEscenario AS NVARCHAR(10));' + @CrLf + N'    IF OBJECT_ID(''tempdb..#Out' + X + N''') IS NOT NULL DELETE FROM #Out' + X + N';', NULL, 1
FROM (VALUES (N'PYG'),(N'Balance'),(N'BalanceDiff'),(N'BalancePpto'),(N'TesoreriaPpto')) v(X);

IF OBJECT_ID('tempdb..#Def') IS NOT NULL DROP TABLE #Def;
SELECT p.name AS SP, m.definition AS D INTO #Def
FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE p.name IN (N'sp_ModeloPYG', N'sp_ModeloBalance', N'sp_ModeloBalanceDiff', N'sp_ModeloBalancePpto',
				 N'sp_ModeloTesoreriaPpto', N'sp_ModeloFlujoCaja', N'sp_ModeloFlujoEfectivo', N'sp_ModeloLineasNegocio');
IF (SELECT COUNT(*) FROM #Def) <> 8 BEGIN RAISERROR(N'No se encontraron los 8 SPs.', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM #Def WHERE D LIKE N'%#CtxModelo%') BEGIN RAISERROR(N'El 14 ya fue aplicado.', 16, 1); RETURN; END

DECLARE @Id INT, @SP SYSNAME, @Old NVARCHAR(MAX), @New NVARCHAR(MAX), @Fin NVARCHAR(200), @Cnt INT, @D NVARCHAR(MAX), @n INT, @p INT, @q INT, @Msg NVARCHAR(2000);

-- Aplica las operaciones validando ocurrencias
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
	SELECT o.Id, d.SP, o.Old, o.New, o.Fin, o.Cnt FROM @Ops o JOIN #Def d ON o.SP IS NULL OR o.SP = d.SP ORDER BY d.SP, o.Id;
OPEN c; FETCH NEXT FROM c INTO @Id, @SP, @Old, @New, @Fin, @Cnt;
WHILE @@FETCH_STATUS = 0
BEGIN
	SELECT @D = D FROM #Def WHERE SP = @SP;
	SET @n = (DATALENGTH(@D) - DATALENGTH(REPLACE(@D, @Old, N''))) / DATALENGTH(@Old);
	IF @Cnt IS NOT NULL AND @n <> @Cnt
	BEGIN
		SET @Msg = @SP + N': op ' + CAST(@Id AS NVARCHAR(10)) + N' encontro ' + CAST(@n AS NVARCHAR(10)) + N' ocurrencias (esperadas ' + CAST(@Cnt AS NVARCHAR(10)) + N').';
		CLOSE c; DEALLOCATE c; RAISERROR(@Msg, 16, 1); RETURN;
	END
	IF @n > 0
	BEGIN
		IF @Fin IS NULL SET @D = REPLACE(@D, @Old, @New);
		ELSE
		BEGIN
			SET @p = CHARINDEX(@Old, @D);
			SET @q = CHARINDEX(@Fin, @D, @p + DATALENGTH(@Old) / 2);
			SET @D = STUFF(@D, @p, @q + DATALENGTH(@Fin) / 2 - @p, @New);
		END
		UPDATE #Def SET D = @D WHERE SP = @SP;
	END
	FETCH NEXT FROM c INTO @Id, @SP, @Old, @New, @Fin, @Cnt;
END
CLOSE c; DEALLOCATE c;

-- Validaciones finales antes de tocar la base
IF EXISTS (SELECT 1 FROM #Def WHERE D LIKE N'%##%' OR D LIKE N'%''sp[_]Modelo''%' OR D NOT LIKE N'%#CtxModelo%' OR D LIKE N'%@Drop%')
BEGIN
	SELECT SP, CASE WHEN D LIKE N'%##%' THEN N'Quedan ##' WHEN D LIKE N'%@Drop%' THEN N'Queda @Drop' WHEN D LIKE N'%''sp[_]Modelo''%' THEN N'Queda bloqueo global' ELSE N'Falta bloque raiz' END AS Problema
	FROM #Def WHERE D LIKE N'%##%' OR D LIKE N'%''sp[_]Modelo''%' OR D NOT LIKE N'%#CtxModelo%' OR D LIKE N'%@Drop%';
	RAISERROR(N'Validacion fallida: no se aplico ningun cambio.', 16, 1); RETURN;
END

-- CREATE -> ALTER (conserva permisos) y aplica todo en una transaccion
UPDATE #Def SET D = STUFF(D, CHARINDEX(N'CREATE PROCEDURE', D), 16, N'ALTER PROCEDURE');
BEGIN TRY
	BEGIN TRAN;
	DECLARE a CURSOR LOCAL FAST_FORWARD FOR SELECT SP, D FROM #Def ORDER BY SP;
	OPEN a; FETCH NEXT FROM a INTO @SP, @D;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		EXEC sp_executesql @D;
		FETCH NEXT FROM a INTO @SP, @D;
	END
	CLOSE a; DEALLOCATE a;
	COMMIT;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0 ROLLBACK;
	SET @Msg = N'Error en ' + ISNULL(@SP, N'?') + N': ' + ERROR_MESSAGE();
	RAISERROR(@Msg, 16, 1); RETURN;
END CATCH

-- Verificacion
SELECT p.name AS SP, p.modify_date,
	   CASE WHEN m.definition LIKE N'%##%' THEN N'NO' ELSE N'SI' END AS SinGlobales,
	   CASE WHEN m.definition LIKE N'%''sp[_]Modelo''%' THEN N'NO' ELSE N'SI' END AS SinBloqueoGlobal,
	   CASE WHEN m.definition LIKE N'%#CtxModelo%' THEN N'SI' ELSE N'NO' END AS TablasLocales
FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE p.name IN (SELECT SP FROM #Def) ORDER BY p.name;
