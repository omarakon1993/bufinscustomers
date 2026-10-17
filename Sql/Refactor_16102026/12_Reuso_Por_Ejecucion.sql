-- Reutiliza resultados intermedios SOLO dentro de una ejecución: el wrapper raíz los borra al iniciar y al terminar.
USE [bufinscustomers];
GO
SET NOCOUNT ON;
DECLARE @Nombre SYSNAME, @D NVARCHAR(MAX), @Limpia NVARCHAR(MAX) =
	N'SET @Drop = N''''; SELECT @Drop += N''DROP TABLE '' + QUOTENAME(name) + N'';'' FROM tempdb.sys.tables WHERE name LIKE N''##Cache%[_]'' + CAST(@@SPID AS NVARCHAR(10)); IF @Drop <> N'''' EXEC sp_executesql @Drop;';
DECLARE @SPs TABLE (Nombre SYSNAME PRIMARY KEY);
INSERT @SPs VALUES (N'sp_ModeloBalanceDiff'),(N'sp_ModeloBalancePpto'),(N'sp_ModeloBalance'),(N'sp_ModeloFlujoCaja'),
				   (N'sp_ModeloFlujoEfectivo'),(N'sp_ModeloTesoreriaPpto'),(N'sp_ModeloLineasNegocio'),(N'sp_ModeloPYG');
BEGIN TRY
	BEGIN TRANSACTION;
	DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Nombre FROM @SPs;
	OPEN c; FETCH NEXT FROM c INTO @Nombre;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		-- 1) _Core = lógica del backup (con reuso), llamadas internas a _Core, ventana amplia (vive solo en la ejecución)
		SET @D = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @Nombre + N'_bk_16102026'));
		IF @D IS NULL THROW 50001, N'Backup no encontrado', 1;
		SET @D = REPLACE(@D, N'_bk_16102026', N'_Core');
		SET @D = REPLACE(REPLACE(@D, N'@VentanaCacheSeg INT = 120;', N'@VentanaCacheSeg INT = 86400;'), N'@VentanaCacheSeg INT = 10;', N'@VentanaCacheSeg INT = 86400;');
		-- DROP + CREATE: evita depender de la posición del CREATE (comentarios previos)
		IF OBJECT_ID(N'dbo.' + @Nombre + N'_Core', N'P') IS NOT NULL EXEC (N'DROP PROCEDURE dbo.' + @Nombre + N'_Core');
		EXEC sp_executesql @D;

		-- 2)
		SET @D = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @Nombre));
		IF @D NOT LIKE N'%@Drop%'
		BEGIN
			SET @D = REPLACE(@D, N', @rc INT;', N', @rc INT, @Drop NVARCHAR(MAX);');
			SET @D = REPLACE(@D, N'AS ErrorMessage; RETURN; END', N'AS ErrorMessage; RETURN; END' + NCHAR(13) + NCHAR(10) + NCHAR(9) + NCHAR(9) + @Limpia);
			SET @D = REPLACE(@D, N'IF @Raiz = 1 EXEC sp_releaseapplock', N'IF @Raiz = 1 BEGIN ' + @Limpia + N' EXEC sp_releaseapplock');
			SET @D = REPLACE(@D, N'@LockOwner = ''Session'';' + NCHAR(13) + NCHAR(10) + N'END', N'@LockOwner = ''Session''; END' + NCHAR(13) + NCHAR(10) + N'END');
			IF (LEN(@D) - LEN(REPLACE(@D, N'EXEC sp_executesql @Drop', N''))) / 24 <> 2 THROW 50002, N'Wrapper no reconocido', 1;
			SET @D = STUFF(@D, CHARINDEX(N'CREATE', @D), 6, N'ALTER');
			EXEC sp_executesql @D;
		END
		PRINT N'OK ' + @Nombre;
		FETCH NEXT FROM c INTO @Nombre;
	END
	CLOSE c; DEALLOCATE c;
	COMMIT;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0 ROLLBACK;
	THROW;
END CATCH
GO

-- Verificación: wrappers con 2 limpiezas y _Core sin referencias a _bk
SELECT p.name,
	   CASE WHEN p.name LIKE N'%[_]Core' AND m.definition NOT LIKE N'%[_]bk[_]%' THEN 'OK'
			WHEN p.name NOT LIKE N'%[_]Core' AND (LEN(m.definition) - LEN(REPLACE(m.definition, N'EXEC sp_executesql @Drop', N''))) / 24 = 2 THEN 'OK'
			ELSE 'REVISAR' END AS Estado
FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE p.name LIKE N'sp[_]Modelo%' AND p.name NOT LIKE N'%[_]bk[_]%'
ORDER BY p.name;
