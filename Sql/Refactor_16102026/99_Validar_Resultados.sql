-- Ejecuta cada SP en versión _bk_16102026 y nueva, y compara resultados (EXCEPT en ambos sentidos) y tiempos.
-- Correr DESPUÉS de 00_Backup y de desplegar los SPs nuevos. Ajustar @Empresas.
USE [bufinscustomers];
GO
SET NOCOUNT ON;
DECLARE @IdUsuario INT = 1, @IdEscenario TINYINT = 1;
DECLARE @Empresas TABLE (IdEmpresa INT PRIMARY KEY);
INSERT @Empresas SELECT DISTINCT IdEmpresa_Log FROM dbo.Ini_PYG WHERE IdEmpresa_Log IS NOT NULL;

-- SP -> tabla que persiste. BalanceDiff no persiste: se valida a través de FlujoCaja/FlujoEfectivo/TesoreriaPpto.
DECLARE @SPs TABLE (Orden INT PRIMARY KEY, SP SYSNAME, Tabla SYSNAME, Cols NVARCHAR(MAX));
INSERT @SPs (Orden, SP, Tabla) VALUES
 (1,N'sp_ModeloPYG',N'ModeloPYG'),(2,N'sp_ModeloBalance',N'ModeloBalance'),(3,N'sp_ModeloBalancePpto',N'ModeloBalancePpto'),
 (4,N'sp_ModeloTesoreriaPpto',N'ModeloTesoreriaPpto'),(5,N'sp_ModeloFlujoCaja',N'ModeloFlujoCaja'),
 (6,N'sp_ModeloFlujoEfectivo',N'ModeloFlujoEfectivo'),(7,N'sp_ModeloLineasNegocio',N'ModeloLineasNegocio');

-- Columnas a comparar: todas menos auditoría.
UPDATE s SET Cols = STUFF((SELECT N',' + QUOTENAME(c.name) FROM sys.columns c
						  WHERE c.object_id = OBJECT_ID(N'dbo.' + s.Tabla) AND c.name NOT IN (N'IdUsuario', N'FechaEjecucion')
						  ORDER BY c.column_id FOR XML PATH('')), 1, 1, N'')
FROM @SPs s;

IF OBJECT_ID('tempdb..#Res') IS NOT NULL DROP TABLE #Res;
CREATE TABLE #Res (SP SYSNAME, IdEmpresa INT, FilasBk INT, FilasNuevo INT, SoloBk INT, SoloNuevo INT, MsBk INT, MsNuevo INT, Error NVARCHAR(4000));

DECLARE @SP SYSNAME, @Tabla SYSNAME, @Cols NVARCHAR(MAX), @Emp INT, @t DATETIME2, @MsBk INT, @MsNuevo INT, @Sql NVARCHAR(MAX), @Exec NVARCHAR(500), @Drop NVARCHAR(MAX);

DECLARE c CURSOR LOCAL FAST_FORWARD FOR
	SELECT s.SP, s.Tabla, s.Cols, e.IdEmpresa FROM @SPs s CROSS JOIN @Empresas e ORDER BY e.IdEmpresa, s.Orden;
OPEN c; FETCH NEXT FROM c INTO @SP, @Tabla, @Cols, @Emp;
WHILE @@FETCH_STATUS = 0
BEGIN
	BEGIN TRY
		IF OBJECT_ID('tempdb..#Bk') IS NOT NULL DROP TABLE #Bk;
		IF OBJECT_ID('tempdb..#Nuevo') IS NOT NULL DROP TABLE #Nuevo;
		SET @Exec = N' @IdEmpresa = @e, @IdUsuario = @u, @Masivo = 1, @IdEscenario = @esc;';

		-- Versión backup (sin caché previo)
		SET @Drop = N''; SELECT @Drop += N'DROP TABLE ' + QUOTENAME(name) + N';' FROM tempdb.sys.tables WHERE name LIKE N'##Cache%[_]' + CAST(@@SPID AS NVARCHAR(10));
		IF @Drop <> N'' EXEC sp_executesql @Drop;
		SET @t = SYSDATETIME();
		SET @Sql = N'EXEC dbo.' + @SP + N'_bk_16102026' + @Exec;
		EXEC sp_executesql @Sql, N'@e INT, @u INT, @esc TINYINT', @Emp, @IdUsuario, @IdEscenario;
		SET @MsBk = DATEDIFF(MILLISECOND, @t, SYSDATETIME());
		IF OBJECT_ID('tempdb..##ValBk') IS NOT NULL DROP TABLE ##ValBk;
		SET @Sql = N'SELECT ' + @Cols + N' INTO ##ValBk FROM dbo.' + QUOTENAME(@Tabla) + N' WHERE IdEmpresa = @e AND IdEscenario = @esc;';
		EXEC sp_executesql @Sql, N'@e INT, @esc TINYINT', @Emp, @IdEscenario;

		-- Versión nueva (sin caché previo)
		SET @Drop = N''; SELECT @Drop += N'DROP TABLE ' + QUOTENAME(name) + N';' FROM tempdb.sys.tables WHERE name LIKE N'##Cache%[_]' + CAST(@@SPID AS NVARCHAR(10));
		IF @Drop <> N'' EXEC sp_executesql @Drop;
		SET @t = SYSDATETIME();
		SET @Sql = N'EXEC dbo.' + @SP + @Exec;
		EXEC sp_executesql @Sql, N'@e INT, @u INT, @esc TINYINT', @Emp, @IdUsuario, @IdEscenario;
		SET @MsNuevo = DATEDIFF(MILLISECOND, @t, SYSDATETIME());

		-- Comparación exacta en ambos sentidos
		SET @Sql = N'
			;WITH N AS (SELECT ' + @Cols + N' FROM dbo.' + QUOTENAME(@Tabla) + N' WHERE IdEmpresa = @e AND IdEscenario = @esc)
			INSERT INTO #Res (SP, IdEmpresa, FilasBk, FilasNuevo, SoloBk, SoloNuevo, MsBk, MsNuevo)
			SELECT @sp, @e, (SELECT COUNT(*) FROM ##ValBk), (SELECT COUNT(*) FROM N),
				   (SELECT COUNT(*) FROM (SELECT * FROM ##ValBk EXCEPT SELECT * FROM N) x),
				   (SELECT COUNT(*) FROM (SELECT * FROM N EXCEPT SELECT * FROM ##ValBk) y),
				   @mb, @mn;';
		EXEC sp_executesql @Sql, N'@e INT, @esc TINYINT, @sp SYSNAME, @mb INT, @mn INT', @Emp, @IdEscenario, @SP, @MsBk, @MsNuevo;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK;
		INSERT INTO #Res (SP, IdEmpresa, Error) VALUES (@SP, @Emp, ERROR_MESSAGE());
	END CATCH
	FETCH NEXT FROM c INTO @SP, @Tabla, @Cols, @Emp;
END
CLOSE c; DEALLOCATE c;
IF OBJECT_ID('tempdb..##ValBk') IS NOT NULL DROP TABLE ##ValBk;

-- Resultado: Estado debe ser IGUAL en todas las filas.
SELECT SP, IdEmpresa, FilasBk, FilasNuevo, SoloBk, SoloNuevo, MsBk, MsNuevo,
	   CASE WHEN Error IS NOT NULL THEN 'ERROR' WHEN SoloBk = 0 AND SoloNuevo = 0 AND FilasBk = FilasNuevo THEN 'IGUAL' ELSE 'DIFERENTE' END AS Estado,
	   Error
FROM #Res ORDER BY Estado DESC, SP, IdEmpresa;

-- Resumen de tiempos por SP
SELECT SP, SUM(MsBk) AS MsBkTotal, SUM(MsNuevo) AS MsNuevoTotal,
	   CAST(100.0 * (SUM(MsNuevo) - SUM(MsBk)) / NULLIF(SUM(MsBk), 0) AS DECIMAL(9,1)) AS PctCambio
FROM #Res WHERE Error IS NULL GROUP BY SP ORDER BY SP;
