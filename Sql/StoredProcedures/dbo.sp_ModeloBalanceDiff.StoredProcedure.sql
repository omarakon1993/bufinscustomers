/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1) y se
    propagó a los 3 EXEC anidados (sp_ModeloBalance, sp_ModeloPYG, sp_ModeloBalancePpto).
    SP compuesto puro: no lee ninguna tabla Ini_* directamente.
    La tabla final propia (##FinalBalanceDiff) y el SELECT final devuelven IdEscenario como
    PRIMERA columna (consumida con SELECT * por sp_ModeloTesoreriaPpto/FlujoCaja/FlujoEfectivo).
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Created by GitHub Copilot in SSMS - review carefully before executing
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloBalanceDiff]
    @IdEmpresa INT, @IdUsuario INT,
    @Masivo TINYINT = 0, @Externo TINYINT = 0,
    @IncluirTesoreria TINYINT = 1, @IdEscenario TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY

        DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);
        DECLARE @Año           INT          = (SELECT TOP 1 AnioEjecucion FROM dbo.ConfiguracionesEmpresas WHERE IdEmpresa = @IdEmpresa);

        -- Balance real
        EXEC dbo.sp_ModeloBalance @IdEmpresa, @IdUsuario, 0, 1, @IdEscenario;
        SELECT Año, Mes, Ord, CuentaPUC, Descripcion, Valor INTO #FinalBalance FROM ##FinalBalance;
        DROP TABLE ##FinalBalance;

        -- Ingresos PYG
        EXEC dbo.sp_ModeloPYG @IdEmpresa, @IdUsuario, 0, 1, @IdEscenario;
        SELECT Año, Mes, SUM(ISNULL(Valor, 0)) AS ValorIngresos
        INTO #Ingresos
        FROM ##FinalPYG
        WHERE Descripcion = 'Ingresos'
        GROUP BY Año, Mes;

        DROP TABLE ##FinalPYG;

        -- Diferencia real mensual
        SELECT B.Año, B.Mes, B.Ord, B.CuentaPUC, LTRIM(RTRIM(B.Descripcion)) AS Descripcion,
               CASE WHEN ISNULL(ING.ValorIngresos, 0) = 0 THEN 0
                    WHEN R.Signo = 'Cambiar signo'
                    THEN (B.Valor - ISNULL(LAG(B.Valor) OVER (PARTITION BY B.Descripcion, B.CuentaPUC ORDER BY B.Año, B.Mes), 0)) * -1
                    ELSE (B.Valor - ISNULL(LAG(B.Valor) OVER (PARTITION BY B.Descripcion, B.CuentaPUC ORDER BY B.Año, B.Mes), 0))
               END AS Valor
        INTO #FinalBalanceDiff
        FROM #FinalBalance B
            INNER JOIN dbo.REL_BalanceDiff R ON R.CuentaPUC = B.CuentaPUC AND R.Descripcion = B.Descripcion
            LEFT  JOIN #Ingresos ING          ON ING.Año = B.Año AND ING.Mes = B.Mes;

        INSERT INTO #FinalBalanceDiff (Año, Mes, Ord, CuentaPUC, Descripcion, Valor)
        SELECT Año, Mes, MIN(Ord), MIN(CuentaPUC), 'Deuda actual', SUM(Valor)
        FROM #FinalBalanceDiff
        WHERE Descripcion IN ('Total obligaciones financieras corrientes', 'Obligaciones financieras no corrientes')
        GROUP BY Año, Mes;

        -- Saldos REALES Dic año-1 (anchor Mes=1)
        SELECT LTRIM(RTRIM(Descripcion)) AS Descripcion, Año, SUM(ISNULL(Valor, 0)) AS Valor
        INTO #BalanceMes12Ant
        FROM #FinalBalance
        WHERE Mes = 12
        GROUP BY LTRIM(RTRIM(Descripcion)), Año;

        INSERT INTO #BalanceMes12Ant (Descripcion, Año, Valor)
        SELECT 'Deuda actual', Año, SUM(Valor)
        FROM #BalanceMes12Ant
        WHERE Descripcion IN ('Total obligaciones financieras corrientes', 'Obligaciones financieras no corrientes')
        GROUP BY Año;

        -- Balance ppto (saldos futuros)
        EXEC dbo.sp_ModeloBalancePpto @IdEmpresa, @IdUsuario, 0, 1, @IncluirTesoreria, @IdEscenario;
        SELECT Año, Mes, Ord, CuentaPUC, LTRIM(RTRIM(Descripcion)) AS Descripcion, Valor, ValorFuturo
        INTO #FinalBalancePpto
        FROM ##FinalBalancePpto;
        DROP TABLE ##FinalBalancePpto;

        -- Diferencia ppto mensual
        ;WITH BaseConMesAnterior AS (
            SELECT B.Año, B.Mes, B.Ord, B.CuentaPUC, B.Descripcion, B.Valor, R.Signo,
                   ISNULL(LAG(B.Valor) OVER (PARTITION BY B.Descripcion, B.CuentaPUC ORDER BY B.Año, B.Mes), 0) AS ValorMesAnteriorPpto
            FROM #FinalBalancePpto B
                INNER JOIN dbo.REL_BalanceDiff R ON R.Descripcion = B.Descripcion
            WHERE B.Descripcion <> 'Deuda actual'
        )
        SELECT B.Año, B.Mes, B.Ord, B.CuentaPUC, B.Descripcion,
               CASE WHEN B.Signo = 'Cambiar signo'
                    THEN (B.Valor - CASE WHEN B.Mes = 1 THEN ISNULL(BAL12.Valor, 0) ELSE B.ValorMesAnteriorPpto END) * -1
                    ELSE (B.Valor - CASE WHEN B.Mes = 1 THEN ISNULL(BAL12.Valor, 0) ELSE B.ValorMesAnteriorPpto END)
               END AS ValorPresupuesto
        INTO #FinalBalanceDiffPpto
        FROM BaseConMesAnterior B
            LEFT JOIN #BalanceMes12Ant BAL12
                ON BAL12.Descripcion = B.Descripcion AND BAL12.Año = B.Año - 1;

        INSERT INTO #FinalBalanceDiffPpto (Año, Mes, Ord, CuentaPUC, Descripcion, ValorPresupuesto)
        SELECT Año, Mes, MIN(Ord), MIN(CuentaPUC), 'Deuda actual', SUM(ValorPresupuesto)
        FROM #FinalBalanceDiffPpto
        WHERE Descripcion IN ('Total obligaciones financieras corrientes', 'Obligaciones financieras no corrientes')
        GROUP BY Año, Mes;

        -- =========================================================================================
        -- ValorFuturo se calcula PRIMERO sobre el saldo ppto (garantiza 12 meses)
        -- =========================================================================================

        -- Variación futura por Descripcion (columna ValorFuturo del BalancePpto, ya calculada allá)
        SELECT Descripcion, Año, Mes,
               SUM(ISNULL(ValorFuturo, 0)) AS SaldoFuturo,
               MIN(Ord) AS Ord, MIN(CuentaPUC) AS CuentaPUC
        INTO #SaldoFut
        FROM #FinalBalancePpto
        WHERE Descripcion <> 'Deuda actual'
        GROUP BY Descripcion, Año, Mes;

        -- 'Deuda actual'
        INSERT INTO #SaldoFut (Descripcion, Año, Mes, SaldoFuturo, Ord, CuentaPUC)
        SELECT 'Deuda actual', Año, Mes, SUM(SaldoFuturo), MIN(Ord), MIN(CuentaPUC)
        FROM #SaldoFut
        WHERE Descripcion IN ('Total obligaciones financieras corrientes', 'Obligaciones financieras no corrientes')
        GROUP BY Año, Mes;

        CREATE NONCLUSTERED INDEX IX_SaldoFut ON #SaldoFut (Descripcion, Año, Mes);

        -- ValorFuturo: la columna ValorFuturo de sp_ModeloBalancePpto YA viene como la variación
        -- mensual (ValorFuturo del mes - ValorFuturo del mes inmediatamente anterior; en enero
        -- contra el real de diciembre del año anterior). Por lo tanto NO se vuelve a restar aquí;
        -- sólo se aplica la convención de signo de REL_BalanceDiff, igual que Valor y ValorPresupuesto.
        SELECT S.Descripcion, S.Año, S.Mes, S.Ord, S.CuentaPUC, S.SaldoFuturo,
               CASE WHEN S.Año = @Año
                    THEN S.SaldoFuturo * CASE WHEN R.Signo = 'Cambiar signo' THEN -1 ELSE 1 END
                    ELSE 0
               END AS ValorFuturo
        INTO #ValorFuturoCalc
        FROM #SaldoFut S
            OUTER APPLY (SELECT TOP 1 Signo FROM dbo.REL_BalanceDiff
                         WHERE Descripcion = S.Descripcion) R;

        -- Unifica real + ppto + futuro (FULL OUTER para no perder filas del ppto sin real)
        SELECT ISNULL(F.Año, VF.Año) AS Año,
               ISNULL(F.Mes, VF.Mes) AS Mes,
               ISNULL(F.Ord, VF.Ord) AS Ord,
               ISNULL(F.CuentaPUC, VF.CuentaPUC) AS CuentaPUC,
               ISNULL(F.Descripcion, VF.Descripcion) AS Descripcion,
               ISNULL(F.Valor, 0) AS Valor,
               ISNULL(PP.ValorPresupuesto, 0) AS ValorPresupuesto,
               ISNULL(VF.ValorFuturo, 0) AS ValorFuturo
        INTO #FinalBalanceDiffUniBase
        FROM #FinalBalanceDiff F
            FULL OUTER JOIN #ValorFuturoCalc VF
                ON VF.Año = F.Año AND VF.Mes = F.Mes AND VF.Descripcion = F.Descripcion
            LEFT JOIN #FinalBalanceDiffPpto PP
                ON PP.Año = ISNULL(F.Año, VF.Año)
               AND PP.Mes = ISNULL(F.Mes, VF.Mes)
               AND PP.Descripcion = ISNULL(F.Descripcion, VF.Descripcion);

        -- Acumulado. IdEscenario va primero en la tabla final para que quede visible en
        -- cualquier consumo directo (##FinalBalanceDiff se lee con SELECT * en varios SP).
        SELECT @IdEscenario AS IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorPresupuesto, ValorFuturo,
               SUM(ValorFuturo) OVER (PARTITION BY Año, Descripcion ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorFuturoAcumulado
        INTO #FinalBalanceDiffUni
        FROM #FinalBalanceDiffUniBase;

        IF @Externo = 0
            SELECT IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorPresupuesto, ValorFuturo, ValorFuturoAcumulado
            FROM #FinalBalanceDiffUni
            ORDER BY Año, Mes, Ord, CASE WHEN Descripcion = 'Deuda actual' THEN 1 ELSE 0 END;
        ELSE
        BEGIN
            IF OBJECT_ID('tempdb..##FinalBalanceDiff') IS NOT NULL DROP TABLE ##FinalBalanceDiff;
            SELECT IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorPresupuesto, ValorFuturo, ValorFuturoAcumulado
            INTO ##FinalBalanceDiff
            FROM #FinalBalanceDiffUni;
        END

        IF @Masivo = 0 AND @Externo = 0
            SELECT 1 AS CodMessage, 'Modelo diferencia balance de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloBalanceDiff 1, 1, 0, 0, 1, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
