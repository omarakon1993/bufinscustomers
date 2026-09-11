/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1) y el
    filtro AND ISNULL(IdEscenario,1) = @IdEscenario en cada lectura/actualización de
    Ini_BalancePrueba e Ini_CteYnoCte. SP hoja (no compone otros SP de modelo).
    La tabla final (##FinalBalance) y el SELECT final devuelven IdEscenario como PRIMERA columna.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloBalance]
    @IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT=0, @Externo TINYINT=0, @IdEscenario TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY

        CREATE TABLE #BalIni (Tabla VARCHAR(200), Cuenta VARCHAR(200), US MONEY, Año INT, Mes INT, Corriente VARCHAR(200));

        DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);

        IF NOT EXISTS (SELECT 1 FROM dbo.Ini_BalancePrueba WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
        BEGIN
            SELECT 0 AS CodMessage, 'La empresa "' + ISNULL(@NombreEmpresa, '') + '" no tiene datos de plantilla cargados.' AS ErrorMessage;
            RETURN;
        END

        UPDATE dbo.Ini_BalancePrueba SET IdUsuarioEjecucion_Log = @IdUsuario, FechaEjecucion_Log = GETDATE() WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;
        UPDATE dbo.Ini_CteYnoCte     SET IdUsuarioEjecucion_Log = @IdUsuario, FechaEjecucion_Log = GETDATE() WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;

        -- Carga inicial desde plantillas
        INSERT INTO #BalIni (Tabla, Cuenta, US, Año, Mes, Corriente)
        SELECT 'Z_BalancePrueba', Cuenta, ISNULL(NuevoSaldo, 0), TRY_CAST(Año AS INT), TRY_CAST(Mes AS INT), 'No aplica'
        FROM dbo.Ini_BalancePrueba WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;

        INSERT INTO #BalIni (Tabla, Cuenta, US, Año, Mes, Corriente)
        SELECT 'Z_CteYnoCte', Cuenta, ISNULL(NuevoSaldo, 0), TRY_CAST(Año AS INT), TRY_CAST(Mes AS INT),
               CASE WHEN AcPa LIKE '%NO CORRIENTE%' THEN 'No corriente' WHEN AcPa IS NULL THEN NULL ELSE 'Corriente' END
        FROM dbo.Ini_CteYnoCte WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;

        -- Aplica cambio de signo según REL_Balance
        UPDATE B SET B.US = CASE WHEN UPPER(R.Signo) = 'CAMBIAR SIGNO' THEN ISNULL(B.US, 0) * -1 ELSE ISNULL(B.US, 0) END
        FROM #BalIni B
            INNER JOIN dbo.REL_Balance R ON R.CuentaPUC_Calc = B.Cuenta AND R.Tabla = B.Tabla AND ISNULL(R.Corriente, '') = ISNULL(B.Corriente, '')
        WHERE R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO';

        -- Construye grilla completa Año × Mes con saldos reales
        ;WITH
        Meses   AS (SELECT Mes FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS M(Mes)),
        Años    AS (SELECT DISTINCT Año FROM #BalIni WHERE Año IS NOT NULL),
        Cuentas AS (
            SELECT Y.Año, MAX(R.Id) AS Ord, R.CuentaPUC, R.Descripcion, R.Tabla, R.Corriente
            FROM dbo.REL_Balance R CROSS JOIN Años Y
            WHERE R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO'
            GROUP BY Y.Año, R.CuentaPUC, R.Descripcion, R.Tabla, R.Corriente),
        Detalle AS (
            SELECT B.Año, B.Mes, R.CuentaPUC, R.Descripcion, R.Tabla, R.Corriente, SUM(ISNULL(B.US, 0)) AS Valor
            FROM dbo.REL_Balance R
                INNER JOIN #BalIni B ON R.CuentaPUC_Calc = B.Cuenta AND R.Tabla = B.Tabla AND ISNULL(R.Corriente, '') = ISNULL(B.Corriente, '')
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND B.Año IS NOT NULL
            GROUP BY B.Año, B.Mes, R.CuentaPUC, R.Descripcion, R.Tabla, R.Corriente)
        SELECT C.Año, M.Mes, C.Ord, C.CuentaPUC, C.Descripcion, ISNULL(D.Valor, 0) AS Valor
        INTO #FinalBalance
        FROM Cuentas C CROSS JOIN Meses M
            LEFT JOIN Detalle D ON C.Año = D.Año AND C.CuentaPUC = D.CuentaPUC AND C.Descripcion = D.Descripcion
                                AND C.Tabla = D.Tabla AND ISNULL(C.Corriente, '') = ISNULL(D.Corriente, '') AND M.Mes = D.Mes;

        -- Staging para subtotales (Tipo = 'CALCULO')
        CREATE TABLE #Valores (Id INT NULL, Descripcion VARCHAR(500) NOT NULL, Año INT NOT NULL, Mes INT NOT NULL, Valor MONEY NOT NULL);

        INSERT INTO #Valores (Descripcion, Año, Mes, Valor)
        SELECT Descripcion, Año, Mes, Valor FROM #FinalBalance;

        CREATE NONCLUSTERED INDEX IX_ValBalance ON #Valores (Descripcion, Año, Mes);

        CREATE TABLE #ParsedTerms (Term VARCHAR(500), Signo INT);
        DECLARE @Id INT, @Descripcion VARCHAR(500), @Formula VARCHAR(2000), @remaining VARCHAR(2000),
                @sign INT, @matchDesc VARCHAR(500), @matchLen INT;

        -- Cursor secuencial: cada subtotal puede referenciar subtotales anteriores
        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, Formula FROM dbo.REL_Balance WHERE UPPER(Tipo) = 'CALCULO' ORDER BY Id;

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

            -- Calcula subtotal para todos los años × 12 meses
            INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
            SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0)
            FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT  JOIN #ParsedTerms T ON 1 = 1
                LEFT  JOIN #Valores     V ON V.Descripcion = T.Term AND V.Año = Y.Año AND V.Mes = M.Mes
            GROUP BY Y.Año, M.Mes;

            FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;
        END

        CLOSE cur; DEALLOCATE cur;

        -- Agrega subtotales calculados a #FinalBalance
        INSERT INTO #FinalBalance (Año, Mes, Ord, CuentaPUC, Descripcion, Valor)
        SELECT V.Año, V.Mes, R.Id, R.CuentaPUC, V.Descripcion, V.Valor
        FROM   #Valores V INNER JOIN dbo.REL_Balance R ON R.Id = V.Id
        WHERE  V.Id IS NOT NULL;

        IF OBJECT_ID('tempdb..##FinalBalance') IS NOT NULL DROP TABLE ##FinalBalance;

        -- Expone resultado vía tabla global para consumo por otros SPs.
        -- IdEscenario va primero en la tabla final para que quede visible en cualquier
        -- consumo directo de ##FinalBalance (y en el SELECT final de este mismo SP).
        SELECT @IdEscenario AS IdEscenario, * INTO ##FinalBalance FROM #FinalBalance;

        IF @Externo = 0
        BEGIN
            SELECT IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor FROM ##FinalBalance ORDER BY Año, Mes, Ord;
            DROP TABLE ##FinalBalance;
        END

        IF @Masivo = 0 AND @Externo = 0
            SELECT 1 AS CodMessage, 'Modelo balance de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloBalance 1, 1, 0, 0, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
