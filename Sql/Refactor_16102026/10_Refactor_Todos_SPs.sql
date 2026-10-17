-- Opción B: genera los 8 SPs nuevos a partir de los _bk_16102026 sin reescribir la lógica de cálculo.
--   dbo.sp_ModeloX_Core = copia exacta del bk (llamadas internas entre _Core).
--   dbo.sp_ModeloX      = envoltorio: serializa la cadena (##Final* sin cruces) y limpia el caché al inicio (cambios visibles al instante).
-- Todo en una transacción: si algo falla no queda nada a medias.
USE [bufinscustomers];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SPs TABLE (SP SYSNAME PRIMARY KEY);
INSERT @SPs VALUES (N'sp_ModeloPYG'),(N'sp_ModeloBalance'),(N'sp_ModeloBalanceDiff'),(N'sp_ModeloBalancePpto'),
				   (N'sp_ModeloTesoreriaPpto'),(N'sp_ModeloFlujoCaja'),(N'sp_ModeloFlujoEfectivo'),(N'sp_ModeloLineasNegocio');

-- Prerrequisito: deben existir los 8 backups
IF EXISTS (SELECT 1 FROM @SPs s WHERE OBJECT_ID(N'dbo.' + s.SP + N'_bk_16102026', 'P') IS NULL)
	THROW 50001, 'Faltan SPs _bk_16102026. Ejecute primero 00_Backup_SPs_Modelo.sql.', 1;

DECLARE @SP SYSNAME, @Def NVARCHAR(MAX), @Ini INT, @Fin INT, @Hdr NVARCHAR(MAX), @Args NVARCHAR(MAX), @Sql NVARCHAR(MAX);

BEGIN TRY
	BEGIN TRAN;
	DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT SP FROM @SPs;
	OPEN c; FETCH NEXT FROM c INTO @SP;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @Def = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @SP + N'_bk_16102026'));
		SET @Ini = CHARINDEX(N'CREATE PROCEDURE', @Def);
		IF @Ini = 0 THROW 50002, 'No se encontró CREATE PROCEDURE en un backup.', 1;

		-- 1) Core: copia exacta del backup, llamadas internas apuntando a _Core
		IF OBJECT_ID(N'dbo.' + @SP + N'_Core', 'P') IS NOT NULL
		BEGIN SET @Sql = N'DROP PROCEDURE dbo.' + QUOTENAME(@SP + N'_Core') + N';'; EXEC (@Sql); END
		SET @Sql = REPLACE(SUBSTRING(@Def, @Ini, LEN(@Def)), N'_bk_16102026', N'_Core');
		EXEC (@Sql);

		-- 2) Encabezado (parámetros y defaults) del original, hasta la línea "AS"
		SET @Fin = PATINDEX(N'%' + CHAR(10) + N'AS[' + CHAR(13) + CHAR(10) + CHAR(9) + N' ]%', @Def);
		IF @Fin = 0 OR @Fin < @Ini THROW 50003, 'No se encontró la línea AS del encabezado.', 1;
		SET @Hdr = REPLACE(STUFF(SUBSTRING(@Def, @Ini, @Fin - @Ini), 1, 6, N'ALTER'), N'_bk_16102026', N'');
		SET @Args = STUFF((SELECT N', ' + name + N' = ' + name FROM sys.parameters
						   WHERE object_id = OBJECT_ID(N'dbo.' + @SP + N'_bk_16102026') ORDER BY parameter_id FOR XML PATH('')), 1, 2, N'');

		-- 3) Envoltorio con el nombre original
		SET @Sql = @Hdr + N'
AS
BEGIN
	SET NOCOUNT ON;
	DECLARE @Raiz BIT = CASE WHEN APPLOCK_MODE(''public'', ''sp_Modelo'', ''Session'') = ''NoLock'' THEN 1 ELSE 0 END, @rc INT, @Drop NVARCHAR(MAX) = N'''';
	IF @Raiz = 1
	BEGIN
		-- Una cadena a la vez: las ##Final* no se cruzan entre sesiones
		EXEC @rc = sp_getapplock @Resource = ''sp_Modelo'', @LockMode = ''Exclusive'', @LockOwner = ''Session'', @LockTimeout = 600000;
		IF @rc < 0 BEGIN SELECT 0 AS CodMessage, ''Tiempo de espera agotado: otro modelo en ejecución.'' AS ErrorMessage; RETURN; END
		-- Caché solo vive dentro de esta ejecución
		SELECT @Drop += N''DROP TABLE '' + QUOTENAME(name) + N'';'' FROM tempdb.sys.tables WHERE name LIKE N''##Cache%[_]'' + CAST(@@SPID AS NVARCHAR(10));
		IF @Drop <> N'''' EXEC sp_executesql @Drop;
	END
	BEGIN TRY
		EXEC dbo.' + QUOTENAME(@SP + N'_Core') + N' ' + @Args + N';
	END TRY
	BEGIN CATCH
		SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
	END CATCH
	IF @Raiz = 1 EXEC sp_releaseapplock @Resource = ''sp_Modelo'', @LockOwner = ''Session'';
END';
		EXEC (@Sql);
		FETCH NEXT FROM c INTO @SP;
	END
	CLOSE c; DEALLOCATE c;
	COMMIT;
	PRINT 'OK: 8 SPs _Core creados y 8 envoltorios actualizados.';
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0 ROLLBACK;
	IF CURSOR_STATUS('local', 'c') >= -1 BEGIN CLOSE c; DEALLOCATE c; END
	PRINT 'ERROR en ' + ISNULL(@SP, '?') + ': ' + ERROR_MESSAGE() + ' -> ROLLBACK, nada cambió.';
	THROW;
END CATCH
GO

-- Verificación: los _Core solo deben llamar a otros _Core (ninguna fila con Problema = 1)
SELECT OBJECT_NAME(d.referencing_id) AS Origen, d.referenced_entity_name AS Llama,
	   CASE WHEN d.referenced_entity_name LIKE 'sp[_]Modelo%' AND d.referenced_entity_name NOT LIKE '%[_]Core' THEN 1 ELSE 0 END AS Problema
FROM sys.sql_expression_dependencies d
WHERE OBJECT_NAME(d.referencing_id) LIKE 'sp[_]Modelo%[_]Core' AND d.referenced_entity_name LIKE 'sp[_]Modelo%'
ORDER BY Problema DESC, Origen;
