/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1) y se
    propagó a los 2 EXEC anidados (sp_ModeloPYG, sp_ModeloBalanceDiff) con la forma
    @IdEscenario = @IdEscenario. SP compuesto puro: no lee ninguna tabla Ini_* directamente.
    #DatosBalDiff (SELECT * de ##FinalBalanceDiff) ganó IdEscenario como primera columna para
    coincidir con lo que sp_ModeloBalanceDiff ahora devuelve. El SELECT final (no publica tabla
    global; nada compone sobre este SP) también devuelve IdEscenario como PRIMERA columna.

    EXEC a sp_ModeloBalanceDiff: se agregó @IncluirTesoreria = 0 explícito (antes no se pasaba y
    quedaba en su default = 1). Rompe la cadena sp_ModeloBalanceDiff -> sp_ModeloBalancePpto ->
    sp_ModeloTesoreriaPpto -> sp_ModeloBalanceDiff (otra vez) al llegar aquí desde
    FlujoEfectivo, igual que ya hacía sp_ModeloTesoreriaPpto en su propia llamada a
    sp_ModeloBalanceDiff.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloFlujoEfectivo]
    @IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT = 0, @IdEscenario TINYINT = 1 -- 1 = solo datos, 0 = datos + mensaje
AS
BEGIN
    BEGIN TRY

        DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);
        DECLARE @Año           INT          = (SELECT TOP 1 AnioEjecucion FROM dbo.ConfiguracionesEmpresas WHERE IdEmpresa = @IdEmpresa);

        IF NOT EXISTS (SELECT 1 FROM dbo.Rel_FlujoEfectivo)
        BEGIN
            SELECT 0 AS CodMessage, 'No existe configuración en Rel_FlujoEfectivo.' AS ErrorMessage;
            RETURN;
        END

        -- Fuentes: PYG y diferencias de balance
        CREATE TABLE #DatosPYG (Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(200), Descripcion VARCHAR(200), Valor MONEY,
                                ValorAcumulado MONEY, ValorForecast MONEY,ValorFuturo MONEY,ValorFuturoAcumulado MONEY, ValorPresupuesto MONEY, ValorPresupuestoAcumulado MONEY,
                                ValorPresupuestoConAjuste MONEY);
        BEGIN TRY
            INSERT INTO #DatosPYG EXEC dbo.sp_ModeloPYG @IdEmpresa, @IdUsuario, 1, @IdEscenario = @IdEscenario;
        END TRY BEGIN CATCH END CATCH

        -- IdEscenario va primero: se consume con SELECT * FROM ##FinalBalanceDiff, que ahora
        -- devuelve IdEscenario como primera columna.
        CREATE TABLE #DatosBalDiff (IdEscenario TINYINT, Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorPresupuesto MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY);
        BEGIN TRY
            -- @Externo=1: escribe ##FinalBalanceDiff para evitar INSERT EXEC anidado
            -- (sp_ModeloBalanceDiff → sp_ModeloBalancePpto → sp_ModeloTesoreriaPpto → sp_ModeloBalanceDiff)
            -- @IncluirTesoreria=0 rompe ese ciclo (mismo patrón usado por sp_ModeloTesoreriaPpto).
            EXEC dbo.sp_ModeloBalanceDiff @IdEmpresa, @IdUsuario, 1, 1, @IncluirTesoreria = 0, @IdEscenario = @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalBalanceDiff') IS NOT NULL
            BEGIN
                INSERT INTO #DatosBalDiff SELECT * FROM ##FinalBalanceDiff;
                DROP TABLE ##FinalBalanceDiff;
            END
        END TRY BEGIN CATCH END CATCH

        -- Staging real y presupuesto
        CREATE TABLE #Valores     (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY, TieneReal BIT NOT NULL DEFAULT 0);
        CREATE TABLE #ValoresPpto (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);

        INSERT INTO #Valores (Descripcion, Año, Mes, Valor, TieneReal)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor, 1 FROM #DatosPYG    WHERE Descripcion IS NOT NULL UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor, 1 FROM #DatosBalDiff WHERE Descripcion IS NOT NULL;

        INSERT INTO #ValoresPpto (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorPresupuesto FROM #DatosPYG    WHERE Descripcion IS NOT NULL UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorPresupuesto FROM #DatosBalDiff WHERE Descripcion IS NOT NULL;

        DROP TABLE #DatosPYG; DROP TABLE #DatosBalDiff;

        CREATE NONCLUSTERED INDEX IX_Val     ON #Valores     (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValPpto ON #ValoresPpto (Descripcion, Año, Mes);

        CREATE TABLE #ParsedTerms (Term VARCHAR(500), Signo INT);
        DECLARE @Id INT, @Descripcion VARCHAR(500), @Formula VARCHAR(2000), @remaining VARCHAR(2000),
                @sign INT, @matchDesc VARCHAR(500), @matchLen INT;

        -- Se calcula UNA vez, antes del cursor, solo sobre filas fuente (Id IS NULL)
        DECLARE @MesCorte INT = ISNULL(
            (SELECT MAX(Mes) FROM #Valores WHERE Año = @Año AND Valor <> 0 AND Id IS NULL),
            0
        );

        -- Cursor secuencial: cada fila puede referenciar resultados de filas anteriores
        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, CuentaPUC_Calc FROM dbo.Rel_FlujoEfectivo ORDER BY Id;

        OPEN cur;
        FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            DELETE FROM #ParsedTerms;

            SET @remaining = CASE WHEN LEFT(LTRIM(@Formula), 1) IN ('+', '-') THEN LTRIM(@Formula) ELSE '+' + LTRIM(@Formula) END;

            -- Parser greedy: extrae (signo, término más largo) de #Valores
            WHILE LEN(ISNULL(@remaining, '')) > 0
            BEGIN
                SET @sign = CASE LEFT(@remaining, 1) WHEN '-' THEN -1 ELSE 1 END;
                IF LEFT(@remaining, 1) IN ('+', '-') SET @remaining = SUBSTRING(@remaining, 2, LEN(@remaining));
                IF LEN(ISNULL(@remaining, '')) = 0 BREAK;

                SELECT @matchDesc = NULL, @matchLen = 0;
                SELECT TOP 1 @matchDesc = Descripcion, @matchLen = LEN(Descripcion)
                FROM  (SELECT DISTINCT Descripcion FROM #Valores) V
                WHERE  LEFT(@remaining, LEN(Descripcion)) = Descripcion
                ORDER BY LEN(Descripcion) DESC;

                IF @matchDesc IS NOT NULL INSERT INTO #ParsedTerms VALUES (@matchDesc, @sign);
                SET @remaining = CASE WHEN @matchDesc IS NOT NULL THEN LTRIM(SUBSTRING(@remaining, @matchLen + 1, LEN(@remaining))) ELSE '' END;
            END

            -- Calcula real y presupuesto: todos los años × 12 meses
            INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor, TieneReal)
            SELECT @Id, @Descripcion, Y.Año, M.Mes,
                   ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0),
                   CAST(MAX(ISNULL(CAST(V.TieneReal AS TINYINT), 0)) AS BIT)
            FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT  JOIN #ParsedTerms T ON 1 = 1
                LEFT  JOIN (SELECT Descripcion, Año, Mes, Valor, TieneReal,
                                   ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                            FROM #Valores) V ON V.Descripcion = T.Term AND V.Año = Y.Año AND V.Mes = M.Mes AND V.rn = 1
            GROUP BY Y.Año, M.Mes;

            INSERT INTO #ValoresPpto (Id, Descripcion, Año, Mes, Valor)
            SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0)
            FROM  (SELECT DISTINCT Año FROM #ValoresPpto WHERE Año IS NOT NULL) Y
                CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT  JOIN #ParsedTerms T ON 1 = 1
                LEFT  JOIN (SELECT Descripcion, Año, Mes, Valor,
                                   ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                            FROM #ValoresPpto) V ON V.Descripcion = T.Term AND V.Año = Y.Año AND V.Mes = M.Mes AND V.rn = 1
            GROUP BY Y.Año, M.Mes;

            FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;
        END

        CLOSE cur; DEALLOCATE cur;

        -- Materializa JOIN antes de aplicar ventanas
        SELECT V.Año, V.Mes, V.Id, R.CuentaPUC, V.Descripcion,
               ISNULL(V.Valor,  0) AS Valor,
               ISNULL(VP.Valor, 0) AS ValorPresupuesto,
               V.TieneReal
        INTO #Final
        FROM   #Valores V
                   INNER JOIN dbo.Rel_FlujoEfectivo R ON R.Id  = V.Id
                   LEFT  JOIN #ValoresPpto VP          ON VP.Id = V.Id AND VP.Año = V.Año AND VP.Mes = V.Mes
        WHERE  V.Id IS NOT NULL;

        -- Pre-calcula acumulados por (Año, Id) antes del CASE.
        SELECT Año, Mes, Id, CuentaPUC, Descripcion, Valor, ValorPresupuesto, TieneReal,
               SUM(Valor)            OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS AcumValor,
               SUM(ValorPresupuesto) OVER (PARTITION BY Año, Id)                                        AS TotalPpto,
               SUM(ValorPresupuesto) OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS AcumPpto,
               CASE WHEN Año = @Año
                    THEN CASE WHEN Mes <= @MesCorte THEN Valor ELSE ValorPresupuesto END
                    ELSE 0
               END AS ValorFuturo
        INTO #FinalCalc FROM #Final;

        -- Resultado final: ValorFuturo[M] = real si Mes <= @MesCorte, ppto si no.
        -- IdEscenario va primero (mismo criterio que el resto de los SP de modelo).
        SELECT @IdEscenario AS IdEscenario, Año, Mes, Id AS Ord, CuentaPUC, Descripcion, Valor,
               AcumValor AS ValorAcumulado,
               ValorFuturo,
               SUM(ValorFuturo) OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorFuturoAcumulado,
               ValorPresupuesto,
               AcumPpto AS ValorPresupuestoAcumulado
        FROM   #FinalCalc
        ORDER BY Año, Mes, Id;

        IF @Masivo = 0
            SELECT 1 AS CodMessage, 'Modelo Flujo de Efectivo de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloFlujoEfectivo 1, 1, 0, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
