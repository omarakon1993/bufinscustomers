-- Opción B: elimina el caché entre ejecuciones de los _Core y la limpieza de caché de los wrappers.
USE [bufinscustomers];
GO
SET NOCOUNT ON;
DECLARE @Nombre SYSNAME, @D NVARCHAR(MAX), @i1 INT, @e1 INT, @i2 INT, @e2 INT;
BEGIN TRY
	BEGIN TRANSACTION;
	-- 1) _Core: quita lectura (HIT) y guardado del caché
	DECLARE c CURSOR LOCAL FAST_FORWARD FOR
		SELECT name FROM sys.procedures WHERE name IN (N'sp_ModeloPYG_Core',N'sp_ModeloBalance_Core',N'sp_ModeloBalanceDiff_Core',N'sp_ModeloBalancePpto_Core');
	OPEN c; FETCH NEXT FROM c INTO @Nombre;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @D = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @Nombre));
		SET @i1 = CHARINDEX(N'-- CACHE POR SESIÓN', @D);
		SET @e1 = CHARINDEX(N'END', @D, CHARINDEX(N'RETURN;', @D, CHARINDEX(N'IF @CacheHit = 1', @D))) + 3;
		SET @i2 = CHARINDEX(N'-- Guarda el caché de esta sesión', @D);
		SET @e2 = CHARINDEX(N';', @D, CHARINDEX(N'EXEC sp_executesql @SqlCache, @ParmsCache', @D, @i2)) + 1;
		IF @i1 = 0 OR @i2 = 0 OR @e1 <= @i1 OR @e2 <= @i2 OR @i2 < @e1 THROW 50001, N'Bloque de caché no encontrado', 1;
		SET @D = STUFF(STUFF(@D, @i2, @e2 - @i2, N''), @i1, @e1 - @i1, N'');
		IF @D LIKE N'%Cache%' THROW 50002, N'Quedan referencias a caché', 1;
		SET @D = STUFF(@D, CHARINDEX(N'CREATE', @D), 6, N'ALTER');
		EXEC sp_executesql @D;
		PRINT N'OK ' + @Nombre;
		FETCH NEXT FROM c INTO @Nombre;
	END
	CLOSE c; DEALLOCATE c;

	-- 2) Wrappers: se mantiene el applock (aísla las ##Final*), se quita el borrado de caché
	DECLARE w CURSOR LOCAL FAST_FORWARD FOR
		SELECT p.name FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
		WHERE p.name LIKE N'sp[_]Modelo%' AND p.name NOT LIKE N'%[_]Core' AND p.name NOT LIKE N'%[_]bk[_]%' AND m.definition LIKE N'%##Cache%';
	OPEN w; FETCH NEXT FROM w INTO @Nombre;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @D = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @Nombre));
		SET @i1 = CHARINDEX(N'-- Caché solo vive', @D);
		SET @e1 = CHARINDEX(N'EXEC sp_executesql @Drop;', @D) + LEN(N'EXEC sp_executesql @Drop;');
		IF @i1 = 0 OR @e1 <= @i1 THROW 50003, N'Bloque de wrapper no encontrado', 1;
		SET @D = REPLACE(STUFF(@D, @i1, @e1 - @i1, N''), N', @Drop NVARCHAR(MAX) = N''''', N'');
		SET @D = STUFF(@D, CHARINDEX(N'CREATE', @D), 6, N'ALTER');
		EXEC sp_executesql @D;
		PRINT N'OK ' + @Nombre;
		FETCH NEXT FROM w INTO @Nombre;
	END
	CLOSE w; DEALLOCATE w;
	COMMIT;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0 ROLLBACK;
	THROW;
END CATCH
GO

-- Verificación: todo debe salir en 0
SELECT p.name, CASE WHEN m.definition LIKE N'%Cache%' THEN 'REVISAR' ELSE 'OK' END AS Estado
FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE p.name LIKE N'sp[_]Modelo%' AND p.name NOT LIKE N'%[_]bk[_]%'
ORDER BY p.name;
-- Elimina cachés huérfanos que queden en tempdb
DECLARE @Drop NVARCHAR(MAX) = N'';
SELECT @Drop += N'IF OBJECT_ID(''tempdb..' + name + N''') IS NOT NULL DROP TABLE ' + QUOTENAME(name) + N';' FROM tempdb.sys.tables WHERE name LIKE N'##Cache%';
IF @Drop <> N'' EXEC sp_executesql @Drop;
