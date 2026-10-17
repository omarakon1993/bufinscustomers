-- Clona los 8 sp_Modelo* como *_bk_16102026; las llamadas internas también apuntan a los _bk.
USE [bufinscustomers];
GO
SET NOCOUNT ON;
DECLARE @Suf SYSNAME = N'_bk_16102026', @Nombre SYSNAME, @Def NVARCHAR(MAX);
-- Nombres largos primero para no pisar prefijos (BalanceDiff/BalancePpto antes que Balance).
DECLARE @SPs TABLE (Orden INT PRIMARY KEY, Nombre SYSNAME);
INSERT @SPs VALUES (1,N'sp_ModeloBalanceDiff'),(2,N'sp_ModeloBalancePpto'),(3,N'sp_ModeloBalance'),(4,N'sp_ModeloFlujoCaja'),
				   (5,N'sp_ModeloFlujoEfectivo'),(6,N'sp_ModeloTesoreriaPpto'),(7,N'sp_ModeloLineasNegocio'),(8,N'sp_ModeloPYG');

BEGIN TRY
	BEGIN TRANSACTION;
	DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Nombre FROM @SPs ORDER BY Orden;
	OPEN c; FETCH NEXT FROM c INTO @Nombre;
	WHILE @@FETCH_STATUS = 0
	BEGIN
		SET @Def = OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + @Nombre));
		IF @Def IS NULL THROW 50001, N'SP no encontrado', 1;

		-- Placeholders para renombrar cada referencia una sola vez.
		SELECT @Def = REPLACE(@Def, Nombre, N'#~' + CAST(Orden AS NVARCHAR(2)) + N'~#') FROM @SPs ORDER BY Orden;
		SELECT @Def = REPLACE(@Def, N'#~' + CAST(Orden AS NVARCHAR(2)) + N'~#', Nombre + @Suf) FROM @SPs ORDER BY Orden;

		IF OBJECT_ID(N'dbo.' + @Nombre + @Suf, N'P') IS NOT NULL EXEC (N'DROP PROCEDURE dbo.' + @Nombre + @Suf);
		EXEC sp_executesql @Def;
		PRINT N'OK ' + @Nombre + @Suf;
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

-- Verificación: deben salir 8 filas en OK.
SELECT p.name,
	   CASE WHEN m.definition LIKE N'%sp[_]Modelo%' AND m.definition NOT LIKE N'%[_]bk[_]16102026%' THEN 'REVISAR' ELSE 'OK' END AS Estado
FROM sys.procedures p JOIN sys.sql_modules m ON m.object_id = p.object_id
WHERE p.name LIKE N'sp[_]Modelo%[_]bk[_]16102026'
ORDER BY p.name;
