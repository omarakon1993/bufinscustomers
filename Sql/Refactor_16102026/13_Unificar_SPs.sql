-- Unifica cada SP oficial con su _Core (8 SPs en vez de 16): bloqueo + limpieza dentro del mismo SP, RETURN -> GOTO Fin.
USE [bufinscustomers];
GO
SET NOCOUNT ON;
DECLARE @Nombre SYSNAME, @D NVARCHAR(MAX), @p INT, @Fin INT, @CRLF NCHAR(2) = NCHAR(13) + NCHAR(10);
DECLARE @Limpia NVARCHAR(MAX) = N'SET @Drop = N''''; SELECT @Drop += N''DROP TABLE '' + QUOTENAME(name) + N'';'' FROM tempdb.sys.tables WHERE name LIKE N''##Cache%[_]'' + CAST(@@SPID AS NVARCHAR(10)); IF @Drop <> N'''' EXEC sp_executesql @Drop;';
DECLARE @Ini NVARCHAR(MAX) =
	N'    -- Solo la ejecución raíz bloquea y limpia; las llamadas anidadas reutilizan dentro de la misma ejecución' + @CRLF +
	N'    DECLARE @Raiz BIT = CASE WHEN APPLOCK_MODE(''public'', ''sp_Modelo'', ''Session'') = ''NoLock'' THEN 1 ELSE 0 END, @rc INT, @Drop NVARCHAR(MAX);' + @CRLF +
	N'    IF @Raiz = 1' + @CRLF +
	N'    BEGIN' + @CRLF +
	N'        EXEC @rc = sp_getapplock @Resource = ''sp_Modelo'', @LockMode = ''Exclusive'', @LockOwner = ''Session'', @LockTimeout = 600000;' + @CRLF +
	N'        IF @rc < 0 BEGIN SELECT 0 AS CodMessage, ''Tiempo de espera agotado: otro modelo en ejecución.'' AS ErrorMessage; RETURN; END' + @CRLF +
	N'        ' + @Limpia + @CRLF +
	N'    END' + @CRLF;
DECLARE @Cierre NVARCHAR(MAX) = @CRLF +
	N'Fin:' + @CRLF +
	N'    -- Siempre: limpia y libera el bloqueo en la ejecución raíz' + @CRLF +
	N'    IF @Raiz = 1 BEGIN ' + @Limpia + N' EXEC sp_releaseapplock @Resource = ''sp_Modelo'', @LockOwner = ''Session''; END' + @CRLF +
	N'END';
DECLARE @SPs TABLE (Nombre SYSNAME PRIMARY KEY);
INSERT @SPs VALUES (N'sp_ModeloBalanceDiff'),(N'sp_ModeloBalancePpto'),(N'sp_ModeloBalance'),(N'sp_ModeloFlujoCaja'),
				   (N'sp_ModeloFlujoEfectivo'),(N'sp_ModeloTesoreriaPpto'),(N'sp_ModeloLineasNegocio'),(N'sp_ModeloPYG');
BEGIN TRY
	BEGIN TRANSACTION;
	DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Nombre FROM @SPs;
	OPEN c; FETCH NEXT FROM c INTO @Nombre;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @D = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @Nombre + N'_Core'));
		IF @D IS NULL THROW 50001, N'_Core no encontrado', 1;
		-- Nombre propio y llamadas internas apuntan a los oficiales
		SET @D = REPLACE(@D, N'_Core', N'');
		-- Todos los RETURN pasan por la limpieza final
		SET @D = REPLACE(@D, N'RETURN;', N'GOTO Fin;');
		-- Bloqueo + limpieza antes del primer BEGIN TRY
		SET @p = CHARINDEX(N'BEGIN TRY', @D);
		IF @p = 0 THROW 50002, N'BEGIN TRY no encontrado', 1;
		SET @D = STUFF(@D, @p, 0, @Ini + N'    ');
		-- Etiqueta Fin tras el último END CATCH (reemplaza el END final)
		SET @Fin = LEN(@D) - CHARINDEX(REVERSE(N'END CATCH'), REVERSE(@D)) + 1;
		IF @Fin <= 0 OR LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(SUBSTRING(@D, @Fin + 1, 4000), NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''))) <> N'END'
			THROW 50003, N'Final del SP no reconocido', 1;
		SET @D = LEFT(@D, @Fin) + @Cierre;
		-- ALTER conserva permisos del SP oficial
		SET @p = CHARINDEX(N'CREATE PROCEDURE', @D);
		IF @p = 0 THROW 50004, N'CREATE PROCEDURE no encontrado', 1;
		SET @D = STUFF(@D, @p, 6, N'ALTER');
		EXEC sp_executesql @D;
		PRINT N'OK ' + @Nombre;
		FETCH NEXT FROM c INTO @Nombre;
	END
	CLOSE c; DEALLOCATE c;

	-- Elimina los _Core (ya no se usan)
	DECLARE @Drop NVARCHAR(MAX) = N'';
	SELECT @Drop += N'DROP PROCEDURE dbo.' + QUOTENAME(Nombre + N'_Core') + N';' FROM @SPs;
	EXEC sp_executesql @Drop;
	COMMIT;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0 ROLLBACK;
	THROW;
END CATCH
GO

-- Verificación: 8 filas OK y ningún _Core restante
SELECT p.name,
	   CASE WHEN m.definition LIKE N'%[_]Core%' THEN 'REVISAR'
			WHEN m.definition LIKE N'%sp_getapplock%' AND m.definition LIKE N'%Fin:%' AND m.definition LIKE N'%sp_releaseapplock%' THEN 'OK'
			ELSE 'REVISAR' END AS Estado
FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE p.name LIKE N'sp[_]Modelo%' AND p.name NOT LIKE N'%[_]bk[_]%'
ORDER BY p.name;
