/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1,
    compatible con cualquier llamador que aún no lo pase) y el filtro
    AND ISNULL(IdEscenario,1) = @IdEscenario en cada lectura de Ini_PYG.
    SP hoja (no compone otros SP de modelo). No publica tabla global (no lo consume ningún otro
    SP); su único SELECT (el resultado final) devuelve IdEscenario como PRIMERA columna.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloLineasNegocio]
	@IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT = 0, @IdEscenario TINYINT = 1
AS
BEGIN
	BEGIN TRY

		DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);

		IF (SELECT COUNT(1) FROM dbo.Ini_PYG WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario) = 0
		BEGIN
			SELECT 0 AS CodMessage, 'La empresa "' + ISNULL(@NombreEmpresa, '') + '" no tiene datos de plantilla cargados.' AS ErrorMessage;
			RETURN;
		END

		IF NOT EXISTS (
			SELECT 1 FROM dbo.ConfigLineasNegocio CLN
				INNER JOIN dbo.ConfiguracionesEmpresas CE ON CE.Id = CLN.IdConfiguracion
			WHERE CE.IdEmpresa = @IdEmpresa
		)
		BEGIN
			SELECT 0 AS CodMessage, 'No hay líneas de negocio configuradas para la empresa "' + ISNULL(@NombreEmpresa, '') + '".' AS ErrorMessage;
			RETURN;
		END

		-- Carga inicial filtrada por líneas configuradas para la empresa
		CREATE TABLE #PYGIni (Tabla VARCHAR(200), Cuenta VARCHAR(200), LineaNegocio VARCHAR(200), NuevoSaldo MONEY, Año INT, Mes INT);

		INSERT INTO #PYGIni (Tabla, Cuenta, LineaNegocio, NuevoSaldo, Año, Mes)
		SELECT 'Z_PYGDetallado', I.Cuenta, I.LineaNegocio, ISNULL(I.NuevoSaldo, 0), TRY_CAST(I.Año AS INT), TRY_CAST(I.Mes AS INT)
		FROM dbo.Ini_PYG I
			INNER JOIN dbo.ConfigLineasNegocio        CLN ON CLN.NombreLinea = I.LineaNegocio
			INNER JOIN dbo.ConfiguracionesEmpresas CE  ON CE.Id = CLN.IdConfiguracion AND CE.IdEmpresa = @IdEmpresa
		WHERE I.IdEmpresa_Log = @IdEmpresa AND ISNULL(I.IdEscenario,1) = @IdEscenario
			AND I.LineaNegocio IS NOT NULL AND I.LineaNegocio <> '';

		IF NOT EXISTS (SELECT 1 FROM #PYGIni)
		BEGIN
			SELECT 0 AS CodMessage, 'No hay datos con Línea de Negocio para la empresa "' + ISNULL(@NombreEmpresa, '') + '".' AS ErrorMessage;
			RETURN;
		END

		-- Aplica cambio de signo según REL_PYG
		UPDATE P SET P.NuevoSaldo = CASE WHEN UPPER(R.Signo) = 'CAMBIAR SIGNO' THEN ISNULL(P.NuevoSaldo, 0) * -1 ELSE ISNULL(P.NuevoSaldo, 0) END
		FROM #PYGIni P
			INNER JOIN dbo.REL_PYG R ON R.CuentaPUC_Calc = P.Cuenta AND R.Tabla = P.Tabla
		WHERE R.Tipo IS NULL OR UPPER(R.Tipo) NOT IN ('CALCULO', 'CALCULO SQL');

		-- Construye grilla Año × Mes × LineaNegocio con saldos reales y acumulado
		;WITH
		Meses     AS (SELECT Mes FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS M(Mes)),
		Años       AS (SELECT DISTINCT Año FROM #PYGIni WHERE Año IS NOT NULL),
		LineasNeg AS (SELECT DISTINCT LineaNegocio FROM #PYGIni),
		Cuentas AS (
			SELECT Y.Año, LN.LineaNegocio, MAX(R.Id) AS Ord, R.CuentaPUC, R.Descripcion
			FROM dbo.REL_PYG R CROSS JOIN Años Y CROSS JOIN LineasNeg LN
			WHERE R.Tipo IS NULL OR UPPER(R.Tipo) NOT IN ('CALCULO', 'CALCULO SQL')
			GROUP BY Y.Año, LN.LineaNegocio, R.CuentaPUC, R.Descripcion),
		Detalle AS (
			SELECT P.Año, P.Mes, P.LineaNegocio, R.CuentaPUC, R.Descripcion, SUM(ISNULL(P.NuevoSaldo, 0)) AS Valor
			FROM dbo.REL_PYG R
				INNER JOIN #PYGIni P ON R.CuentaPUC_Calc = P.Cuenta AND R.Tabla = P.Tabla
			WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) NOT IN ('CALCULO', 'CALCULO SQL')) AND P.Año IS NOT NULL
			GROUP BY P.Año, P.Mes, P.LineaNegocio, R.CuentaPUC, R.Descripcion)
		SELECT @IdEscenario AS IdEscenario, C.Año, M.Mes, C.Ord, C.CuentaPUC,
			      RTRIM(C.Descripcion) + ' ' + C.LineaNegocio AS Descripcion,
			      ISNULL(D.Valor, 0) AS Valor,
			      SUM(ISNULL(D.Valor, 0)) OVER (
				      PARTITION BY C.Año, C.CuentaPUC, C.LineaNegocio
				      ORDER BY M.Mes ROWS UNBOUNDED PRECEDING
			      ) AS ValorAcumulado
		FROM Cuentas C CROSS JOIN Meses M
			LEFT JOIN Detalle D ON C.Año = D.Año AND C.CuentaPUC = D.CuentaPUC
								AND C.Descripcion = D.Descripcion AND C.LineaNegocio = D.LineaNegocio AND M.Mes = D.Mes
		ORDER BY C.Año, M.Mes, C.Ord, C.LineaNegocio;

		IF @Masivo = 0
			SELECT 1 AS CodMessage, 'Modelo Líneas de Negocio de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

		--EXEC dbo.sp_ModeloLineasNegocio 1, 1, 0, 1

	END TRY
	BEGIN CATCH
		SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
	END CATCH
END
GO
