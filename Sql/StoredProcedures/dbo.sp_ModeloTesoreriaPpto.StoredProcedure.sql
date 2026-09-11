/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1), el
    filtro AND ISNULL(IdEscenario,1) = @IdEscenario en cada lectura directa de Ini_PptoPYG e
    Ini_BalancePrueba, y se propagó @IdEscenario a los 3 EXEC anidados
    (sp_ModeloPYG, sp_ModeloBalanceDiff, sp_ModeloBalance). Las tablas #DatosPYGPpto (INSERT...EXEC
    de sp_ModeloPYG) y #DatosBalDiffPpto (SELECT * de ##FinalBalanceDiff) ganaron IdEscenario como
    primera columna para coincidir con lo que esos SP ahora devuelven. La tabla final propia
    (##FinalTesoreriaPpto) y el SELECT final también devuelven IdEscenario como PRIMERA columna.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloTesoreriaPpto]
    @IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT = 0, @Externo TINYINT = 0, @IdEscenario TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY

        IF OBJECT_ID('tempdb..##FinalTesoreriaPpto') IS NOT NULL DROP TABLE ##FinalTesoreriaPpto;

        CREATE TABLE #FinalTesoreriaPpto (Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(100), Descripcion VARCHAR(200), Valor MONEY, ValorAcumulado MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY, ValorForecast MONEY, Tabla VARCHAR(200));

        DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);
        DECLARE @AñoEjecucion  INT          = (SELECT TOP 1 AnioEjecucion FROM dbo.ConfiguracionesEmpresas WHERE IdEmpresa = @IdEmpresa);

        IF @AñoEjecucion IS NULL
        BEGIN
            SELECT 0 AS CodMessage, 'No existe configuración de año de ejecución para la empresa "' + ISNULL(@NombreEmpresa, '') + '".' AS ErrorMessage;
            RETURN;
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.Rel_TesoreriaPpto)
        BEGIN
            SELECT 0 AS CodMessage, 'No existe configuración en Rel_TesoreriaPpto.' AS ErrorMessage;
            RETURN;
        END

        -- Fuentes: PYG ppto y diferencias de balance (solo año de ejecución, fuente = ValorPresupuesto)
        -- IdEscenario va primero: sp_ModeloPYG ahora lo devuelve como primera columna de su
        -- SELECT final (INSERT ... EXEC exige coincidencia posicional de columnas).
        CREATE TABLE #DatosPYGPpto (IdEscenario TINYINT, Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(200), Descripcion VARCHAR(200), Valor MONEY,
                                    ValorAcumulado MONEY, ValorForecast MONEY, ValorFuturo MONEY,ValorFuturoAcumulado MONEY, ValorPresupuesto MONEY, ValorPresupuestoAcumulado MONEY,
                                    ValorPresupuestoConAjuste MONEY);

        IF EXISTS (SELECT 1 FROM dbo.Ini_PptoPYG WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
        BEGIN TRY
            INSERT INTO #DatosPYGPpto EXEC dbo.sp_ModeloPYG @IdEmpresa, @IdUsuario, 1, 0, @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalPYG') IS NOT NULL DROP TABLE ##FinalPYG;
        END TRY BEGIN CATCH END CATCH

        -- IdEscenario va primero: sp_ModeloBalanceDiff ahora lo devuelve como primera columna
        -- (aquí se consume con SELECT * FROM ##FinalBalanceDiff, que exige el mismo orden).
        CREATE TABLE #DatosBalDiffPpto (IdEscenario TINYINT, Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorPresupuesto MONEY, ValorFuturo MONEY, ValorFuturoAcumulado MONEY);

        -- @Externo=1 escribe ##FinalBalanceDiff para evitar INSERT EXEC anidado
        IF EXISTS (SELECT 1 FROM dbo.Ini_BalancePrueba WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
        BEGIN TRY
            EXEC dbo.sp_ModeloBalanceDiff @IdEmpresa, @IdUsuario, 1, 1, 0, @IdEscenario; -- @IncluirTesoreria=0 rompe el ciclo: TesoreriaPpto → BalanceDiff → BalancePpto
            IF OBJECT_ID('tempdb..##FinalBalanceDiff') IS NOT NULL
            BEGIN
                INSERT INTO #DatosBalDiffPpto SELECT * FROM ##FinalBalanceDiff;
                DROP TABLE ##FinalBalanceDiff;
            END
        END TRY BEGIN CATCH END CATCH

        -- Saldos REALES de diciembre por año (origen: sp_ModeloBalance, NO sp_ModeloBalancePpto).
        -- Son el ancla de enero para las cuentas que llegan como saldo acumulado.
        CREATE TABLE #BalanceDic (Descripcion VARCHAR(500), CuentaPUC VARCHAR(500), Año INT, Valor MONEY);
        BEGIN TRY
            EXEC dbo.sp_ModeloBalance @IdEmpresa, @IdUsuario, 0, 1, @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalBalance') IS NOT NULL
            BEGIN
                INSERT INTO #BalanceDic (Descripcion, CuentaPUC, Año, Valor)
                SELECT LTRIM(RTRIM(Descripcion)), LTRIM(RTRIM(CuentaPUC)), Año, SUM(ISNULL(Valor, 0))
                FROM   ##FinalBalance
                WHERE  Mes = 12 AND Descripcion IS NOT NULL
                GROUP BY LTRIM(RTRIM(Descripcion)), LTRIM(RTRIM(CuentaPUC)), Año;
                DROP TABLE ##FinalBalance;
            END
        END TRY BEGIN CATCH IF OBJECT_ID('tempdb..##FinalBalance') IS NOT NULL DROP TABLE ##FinalBalance; END CATCH

        CREATE TABLE #Valores (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);
        CREATE TABLE #ValoresFuturo (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);
        CREATE TABLE #ValoresFuturoAcum (Descripcion VARCHAR(500), Año INT, Mes INT, FuturoAcumulado MONEY);

        INSERT INTO #Valores (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorPresupuesto FROM #DatosPYGPpto    WHERE Descripcion IS NOT NULL AND Año = @AñoEjecucion UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorPresupuesto FROM #DatosBalDiffPpto WHERE Descripcion IS NOT NULL AND Año = @AñoEjecucion;

        -- ValorFuturoAcumulado según el origen definido en Rel_TesoreriaPpto.Tabla
        -- 1) Filas directas de PYG: se matchean por CuentaPUC_Calc y se renombran con R.Descripcion
        --    (ej. 'Intereses' -> 'Gastos financieros')
        INSERT INTO #ValoresFuturoAcum (Descripcion, Año, Mes, FuturoAcumulado)
        SELECT LTRIM(RTRIM(R.Descripcion)), P.Año, P.Mes, ISNULL(P.ValorFuturoAcumulado, 0)
        FROM   #DatosPYGPpto P
               INNER JOIN dbo.Rel_TesoreriaPpto R ON LTRIM(RTRIM(R.CuentaPUC_Calc)) = LTRIM(RTRIM(P.Descripcion))
        WHERE  P.Descripcion IS NOT NULL AND P.Año = @AñoEjecucion AND R.Tabla = 'PYGPpto'
        UNION ALL
        -- 2) Filas directas de BalanceDiff
        SELECT LTRIM(RTRIM(R.Descripcion)), B.Año, B.Mes, ISNULL(B.ValorFuturoAcumulado, 0)
        FROM   #DatosBalDiffPpto B
               INNER JOIN dbo.Rel_TesoreriaPpto R ON LTRIM(RTRIM(R.CuentaPUC_Calc)) = LTRIM(RTRIM(B.Descripcion))
        WHERE  B.Descripcion IS NOT NULL AND B.Año = @AñoEjecucion AND R.Tabla = 'BalanceDiffPpto'
        UNION ALL
        -- 3) Componentes base (PYG y Balance) que no son filas de Rel_TesoreriaPpto pero se usan en fórmulas
        SELECT LTRIM(RTRIM(P.Descripcion)), P.Año, P.Mes, ISNULL(P.ValorFuturoAcumulado, 0)
        FROM   #DatosPYGPpto P
        WHERE  P.Descripcion IS NOT NULL AND P.Año = @AñoEjecucion
           AND NOT EXISTS (SELECT 1 FROM dbo.Rel_TesoreriaPpto R WHERE LTRIM(RTRIM(R.Descripcion)) = LTRIM(RTRIM(P.Descripcion)))
           AND NOT EXISTS (SELECT 1 FROM dbo.Rel_TesoreriaPpto R WHERE LTRIM(RTRIM(R.CuentaPUC_Calc)) = LTRIM(RTRIM(P.Descripcion)) AND R.Tabla = 'PYGPpto')
        UNION ALL
        SELECT LTRIM(RTRIM(B.Descripcion)), B.Año, B.Mes, ISNULL(B.ValorFuturoAcumulado, 0)
        FROM   #DatosBalDiffPpto B
        WHERE  B.Descripcion IS NOT NULL AND B.Año = @AñoEjecucion
           AND NOT EXISTS (SELECT 1 FROM dbo.Rel_TesoreriaPpto R WHERE LTRIM(RTRIM(R.Descripcion)) = LTRIM(RTRIM(B.Descripcion)))
           AND NOT EXISTS (SELECT 1 FROM dbo.Rel_TesoreriaPpto R WHERE LTRIM(RTRIM(R.CuentaPUC_Calc)) = LTRIM(RTRIM(B.Descripcion)) AND R.Tabla = 'BalanceDiffPpto');

        -- ValorFuturo del PYG: se toma tal cual (ya viene como movimiento del mes).
        INSERT INTO #ValoresFuturo (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ISNULL(ValorFuturo, 0)
        FROM   #DatosPYGPpto
        WHERE  Descripcion IS NOT NULL AND Año = @AñoEjecucion;

        -- ValorFuturo del BalanceDiff: llega como SALDO ACUMULADO, así que aquí se convierte a
        -- la VARIACIÓN del mes: ValorFuturo(mes) - ValorFuturo(mes-1); en enero contra el saldo
        -- REAL de diciembre del año anterior (#BalanceDic, de sp_ModeloBalance), ajustado con la
        -- convención de signo de REL_BalanceDiff. El LAG conserva la granularidad original.
        ;WITH SaldoConPrev AS (
            SELECT B.Año, B.Mes, B.Ord, B.CuentaPUC,
                   LTRIM(RTRIM(B.Descripcion)) AS Descripcion,
                   ISNULL(B.ValorFuturo, 0) AS SaldoFuturo,
                   LAG(ISNULL(B.ValorFuturo, 0)) OVER (PARTITION BY B.Descripcion, B.CuentaPUC, B.Ord
                                                       ORDER BY B.Año, B.Mes) AS SaldoMesAnterior
            FROM   #DatosBalDiffPpto B
            WHERE  B.Descripcion IS NOT NULL
        )
        INSERT INTO #ValoresFuturo (Descripcion, Año, Mes, Valor)
        SELECT S.Descripcion, S.Año, S.Mes,
               S.SaldoFuturo - CASE WHEN S.Mes = 1
                                    THEN ISNULL(DIC.Valor, ISNULL(DICPUC.Valor, 0)) * CASE WHEN SG.Signo = 'Cambiar signo' THEN -1 ELSE 1 END
                                    ELSE ISNULL(S.SaldoMesAnterior, 0)
                               END
        FROM   SaldoConPrev S
               -- Ancla de enero: 1º por Descripcion exacta; si no existe en el balance real
               -- (ej. 'Deuda actual', que allí se llama 'Total obligaciones financieras corrientes'
               -- / 'Obligaciones financieras no corrientes'), se cae al saldo de la CuentaPUC.
               OUTER APPLY (SELECT TOP 1 D.Valor FROM #BalanceDic D
                            WHERE D.Descripcion = S.Descripcion AND D.Año = S.Año - 1) DIC
               OUTER APPLY (SELECT SUM(D.Valor) AS Valor FROM #BalanceDic D
                            WHERE D.CuentaPUC = LTRIM(RTRIM(S.CuentaPUC)) AND D.Año = S.Año - 1) DICPUC
               OUTER APPLY (SELECT TOP 1 R.Signo FROM dbo.REL_BalanceDiff R
                            WHERE R.Descripcion = S.Descripcion) SG
        WHERE  S.Año = @AñoEjecucion;

        -- Último mes con información REAL ejecutada en el año de ejecución
        DECLARE @UltMesReal INT = ISNULL((SELECT MAX(Mes) FROM (
            SELECT Mes FROM #DatosPYGPpto     WHERE Año = @AñoEjecucion AND ISNULL(Valor, 0) <> 0
            UNION ALL
            SELECT Mes FROM #DatosBalDiffPpto WHERE Año = @AñoEjecucion AND ISNULL(Valor, 0) <> 0
        ) R), 0);

        DROP TABLE #DatosPYGPpto; DROP TABLE #DatosBalDiffPpto; DROP TABLE #BalanceDic;

        CREATE NONCLUSTERED INDEX IX_Val        ON #Valores           (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValFuturo  ON #ValoresFuturo     (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValFutAcum ON #ValoresFuturoAcum (Descripcion, Año, Mes);

        CREATE TABLE #ParsedTerms (Term VARCHAR(500), Signo INT);
        DECLARE @Id INT, @Descripcion VARCHAR(500), @Formula VARCHAR(2000), @remaining VARCHAR(2000),
                @sign INT, @matchDesc VARCHAR(500), @matchLen INT,
                @IsAcumulado BIT, @IsConditional BIT, @Operator VARCHAR(10), @SourceDesc VARCHAR(500), @Tabla VARCHAR(200);

        -- Cursor secuencial: cada fila puede referenciar resultados de filas anteriores
        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, CuentaPUC_Calc, Tabla FROM dbo.Rel_TesoreriaPpto ORDER BY Id;

        OPEN cur;
        FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula, @Tabla;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            DELETE FROM #ParsedTerms;

            SELECT @IsAcumulado = 0, @IsConditional = 0, @Operator = NULL, @SourceDesc = NULL;

            -- ACUM:Desc
            IF LEFT(@Formula, 5) = 'ACUM:'
            BEGIN
                SET @IsAcumulado = 1;
                SET @SourceDesc  = LTRIM(RTRIM(SUBSTRING(@Formula, 6, LEN(@Formula))));
            END
            -- IF>=0:Desc o IF<=0:Desc → fila condicional (excedentes / déficit)
            ELSE IF LEFT(@Formula, 2) = 'IF'
            BEGIN
                SET @IsConditional = 1;
                SET @Operator      = SUBSTRING(@Formula, 3, CHARINDEX(':', @Formula) - 3);
                SET @SourceDesc    = LTRIM(RTRIM(SUBSTRING(@Formula, CHARINDEX(':', @Formula) + 1, LEN(@Formula))));
            END

            IF @IsAcumulado = 1
            BEGIN
                INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
                SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(VA.ValorAcumulado, 0)
                FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                    CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                    LEFT JOIN (SELECT Descripcion, Año, Mes,
                                      SUM(Valor) OVER (PARTITION BY Descripcion, Año ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorAcumulado,
                                      ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                               FROM #Valores) VA ON VA.Descripcion = @SourceDesc AND VA.Año = Y.Año AND VA.Mes = M.Mes AND VA.rn = 1;

                -- ACUM: el ValorFuturo de la fila es la suma corrida del futuro de su origen.
                -- EXCEPCIÓN 'Caja neta acumulada': en los meses ya ejecutados (reales) va CERO;
                -- en los meses futuros es la suma corrida del ValorFuturo de 'Caja neta'
                -- REINICIADA en el primer mes futuro (no arrastra los meses reales).
                IF LTRIM(RTRIM(@Descripcion)) = 'Caja neta acumulada'
                BEGIN
                    INSERT INTO #ValoresFuturo (Id, Descripcion, Año, Mes, Valor)
                    SELECT @Id, @Descripcion, Y.Año, M.Mes,
                           CASE WHEN Y.Año = @AñoEjecucion AND M.Mes <= @UltMesReal
                                THEN 0
                                ELSE ISNULL((SELECT SUM(ISNULL(VF2.Valor, 0))
                                             FROM (SELECT Año, Mes, Valor,
                                                          ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                                                   FROM #ValoresFuturo
                                                   WHERE Descripcion = @SourceDesc) VF2
                                             WHERE VF2.Año = Y.Año AND VF2.rn = 1
                                               AND VF2.Mes > CASE WHEN Y.Año = @AñoEjecucion THEN @UltMesReal ELSE 0 END
                                               AND VF2.Mes <= M.Mes), 0)
                           END
                    FROM  (SELECT DISTINCT Año FROM #ValoresFuturo WHERE Año IS NOT NULL) Y
                        CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes);
                END
                ELSE
                BEGIN
                    INSERT INTO #ValoresFuturo (Id, Descripcion, Año, Mes, Valor)
                    SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(VA.FuturoAcumulado, 0)
                    FROM  (SELECT DISTINCT Año FROM #ValoresFuturo WHERE Año IS NOT NULL) Y
                        CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                        LEFT JOIN (SELECT Descripcion, Año, Mes,
                                          SUM(Valor) OVER (PARTITION BY Descripcion, Año ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS FuturoAcumulado,
                                          ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                                   FROM #ValoresFuturo) VA ON VA.Descripcion = @SourceDesc AND VA.Año = Y.Año AND VA.Mes = M.Mes AND VA.rn = 1;
                END

                -- ACUM: el futuro acumulado de la fila hereda el de su origen
                DELETE FROM #ValoresFuturoAcum WHERE Descripcion = @Descripcion;
                INSERT INTO #ValoresFuturoAcum (Descripcion, Año, Mes, FuturoAcumulado)
                SELECT @Descripcion, Año, Mes, FuturoAcumulado FROM #ValoresFuturoAcum WHERE Descripcion = @SourceDesc;
            END
            ELSE IF @IsConditional = 1
            BEGIN
                INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
                SELECT @Id, @Descripcion, Y.Año, M.Mes,
                       CASE
                           WHEN @Operator = '>=0' THEN CASE WHEN ISNULL(VA.Valor, 0) >= 0 THEN ISNULL(VA.Valor, 0) ELSE 0 END
                           WHEN @Operator = '<=0' THEN CASE WHEN ISNULL(VA.Valor, 0) <= 0 THEN ABS(ISNULL(VA.Valor, 0)) ELSE 0 END
                           ELSE 0
                       END
                FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                    CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                    LEFT JOIN (SELECT Descripcion, Año, Mes, Valor,
                                      ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                                                   FROM #Valores) VA ON VA.Descripcion = @SourceDesc AND VA.Año = Y.Año AND VA.Mes = M.Mes AND VA.rn = 1;

                                   -- IF>=0 / IF<=0: mismo criterio sobre el ValorFuturo del origen
                                   INSERT INTO #ValoresFuturo (Id, Descripcion, Año, Mes, Valor)
                                   SELECT @Id, @Descripcion, Y.Año, M.Mes,
                                          CASE
                                              WHEN @Operator = '>=0' THEN CASE WHEN ISNULL(VF.Valor, 0) >= 0 THEN ISNULL(VF.Valor, 0) ELSE 0 END
                                              WHEN @Operator = '<=0' THEN CASE WHEN ISNULL(VF.Valor, 0) <= 0 THEN ABS(ISNULL(VF.Valor, 0)) ELSE 0 END
                                              ELSE 0
                                          END
                                   FROM  (SELECT DISTINCT Año FROM #ValoresFuturo WHERE Año IS NOT NULL) Y
                                       CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                                       LEFT JOIN (SELECT Descripcion, Año, Mes, Valor,
                                                         ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                                                  FROM #ValoresFuturo) VF ON VF.Descripcion = @SourceDesc AND VF.Año = Y.Año AND VF.Mes = M.Mes AND VF.rn = 1;

                                   -- IF>=0 / IF<=0: se aplica el mismo criterio sobre el futuro acumulado del origen
                                   DELETE FROM #ValoresFuturoAcum WHERE Descripcion = @Descripcion;
                                   INSERT INTO #ValoresFuturoAcum (Descripcion, Año, Mes, FuturoAcumulado)
                                   SELECT @Descripcion, Año, Mes,
                                          CASE
                                              WHEN @Operator = '>=0' THEN CASE WHEN ISNULL(FuturoAcumulado, 0) >= 0 THEN ISNULL(FuturoAcumulado, 0) ELSE 0 END
                                              WHEN @Operator = '<=0' THEN CASE WHEN ISNULL(FuturoAcumulado, 0) <= 0 THEN ABS(ISNULL(FuturoAcumulado, 0)) ELSE 0 END
                                              ELSE 0
                                          END
                                   FROM #ValoresFuturoAcum WHERE Descripcion = @SourceDesc;
                               END
                               ELSE
                               BEGIN
                -- Fórmula aritmética: parser greedy extrae (signo, término más largo)
                SET @remaining = CASE WHEN LEFT(LTRIM(@Formula), 1) IN ('+', '-') THEN LTRIM(@Formula) ELSE '+' + LTRIM(@Formula) END;

                WHILE LEN(ISNULL(@remaining, '')) > 0
                BEGIN
                    SET @sign = CASE LEFT(@remaining, 1) WHEN '-' THEN -1 ELSE 1 END;
                    IF LEFT(@remaining, 1) IN ('+', '-') SET @remaining = SUBSTRING(@remaining, 2, LEN(@remaining));
                    IF LEN(ISNULL(@remaining, '')) = 0 BREAK;

                    SELECT @matchDesc = NULL, @matchLen = 0;
                    SELECT TOP 1 @matchDesc = Descripcion, @matchLen = LEN(Descripcion)
                    FROM  (SELECT DISTINCT Descripcion FROM #Valores WHERE LEN(Descripcion) > 0) V
                    WHERE  LEFT(@remaining, LEN(Descripcion)) = Descripcion
                    ORDER BY LEN(Descripcion) DESC;

                    IF @matchDesc IS NOT NULL INSERT INTO #ParsedTerms VALUES (@matchDesc, @sign);
                    SET @remaining = CASE WHEN @matchDesc IS NOT NULL AND @matchLen > 0
                        THEN LTRIM(SUBSTRING(@remaining, @matchLen + 1, LEN(@remaining))) ELSE '' END;
                END

                INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
                SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0)
                FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                    CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                    LEFT  JOIN #ParsedTerms T ON 1 = 1
                    LEFT  JOIN (SELECT Descripcion, Año, Mes, Valor,
                                       ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                                FROM #Valores) V ON V.Descripcion = T.Term AND V.Año = Y.Año AND V.Mes = M.Mes AND V.rn = 1
                GROUP BY Y.Año, M.Mes;

                -- Mismo parseo aplicado al ValorFuturo
                INSERT INTO #ValoresFuturo (Id, Descripcion, Año, Mes, Valor)
                SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0)
                FROM  (SELECT DISTINCT Año FROM #ValoresFuturo WHERE Año IS NOT NULL) Y
                    CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                    LEFT  JOIN #ParsedTerms T ON 1 = 1
                    LEFT  JOIN (SELECT Descripcion, Año, Mes, Valor,
                                       ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                                FROM #ValoresFuturo) V ON V.Descripcion = T.Term AND V.Año = Y.Año AND V.Mes = M.Mes AND V.rn = 1
                GROUP BY Y.Año, M.Mes;

                -- Recalcula el futuro acumulado sólo para subtotales/cálculos (no para filas 1:1 con su fuente)
                IF @Tabla = 'TesoreriaPpto' OR (SELECT COUNT(*) FROM #ParsedTerms) > 1
                BEGIN
                    DELETE FROM #ValoresFuturoAcum WHERE Descripcion = @Descripcion;

                    INSERT INTO #ValoresFuturoAcum (Descripcion, Año, Mes, FuturoAcumulado)
                    SELECT @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(VFA.FuturoAcumulado, 0) * T.Signo), 0)
                    FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                        CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                        LEFT  JOIN #ParsedTerms T ON 1 = 1
                        LEFT  JOIN (SELECT Descripcion, Año, Mes, FuturoAcumulado,
                                           ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Descripcion) AS rn
                                    FROM #ValoresFuturoAcum) VFA ON VFA.Descripcion = T.Term AND VFA.Año = Y.Año AND VFA.Mes = M.Mes AND VFA.rn = 1
                    GROUP BY Y.Año, M.Mes;
                END
            END

            FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula, @Tabla;
        END
        CLOSE cur; DEALLOCATE cur;

        -- Resultado: ValorFuturo[M] = AcumPpto[1..M] + Ppto[M+1..12]
        -- Materializa el JOIN antes de aplicar funciones de ventana
        SELECT V.Año, V.Mes, V.Id, R.OrdenReal, R.CuentaPUC, V.Descripcion,
               ISNULL(V.Valor, 0) AS Valor,
               ISNULL(VFA.FuturoAcumulado, 0) AS FuturoAcumulado,
               ISNULL(VF.Valor, 0) AS ValorFuturo,
               R.Tabla
        INTO   #FinalCalc
        FROM   #Valores V
               INNER JOIN dbo.Rel_TesoreriaPpto R ON R.Id = V.Id
               LEFT  JOIN #ValoresFuturoAcum VFA ON VFA.Descripcion = V.Descripcion AND VFA.Año = V.Año AND VFA.Mes = V.Mes
               LEFT  JOIN #ValoresFuturo VF ON VF.Id = V.Id AND VF.Año = V.Año AND VF.Mes = V.Mes
        WHERE  V.Id IS NOT NULL;

        INSERT INTO #FinalTesoreriaPpto (Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado, ValorFuturo, ValorFuturoAcumulado, ValorForecast, Tabla)
        SELECT Año, Mes, OrdenReal, CuentaPUC, Descripcion, Valor,
               -- EXCEPCIÓN: 'Caja neta acumulada', 'Excedentes de tesorería' y 'Deuda nueva (Déficit de
               -- tesorería)' ya vienen acumuladas desde el cursor (ACUM: / IF>=0: / IF<=0:), por lo que
               -- NO se vuelven a acumular: su ValorAcumulado es el propio Valor.
               CASE WHEN LTRIM(RTRIM(Descripcion)) IN ('Caja neta acumulada', 'Excedentes de tesorería', 'Deuda nueva (Déficit de tesorería)')
                    THEN Valor
                    ELSE AcumPpto
               END AS ValorAcumulado,
               ValorFuturo,
               -- EXCEPCIÓN (misma razón): su ValorFuturoAcumulado es el propio ValorFuturo.
               CASE WHEN LTRIM(RTRIM(Descripcion)) IN ('Caja neta acumulada', 'Excedentes de tesorería', 'Deuda nueva (Déficit de tesorería)')
                    THEN ValorFuturo
                    ELSE ValorFuturoAcumulado
               END AS ValorFuturoAcumulado,
               -- Igual que en sp_ModeloFlujoCaja: TotalPpto - AcumPpto + FuturoAcumulado
               CASE WHEN Año = @AñoEjecucion THEN TotalPpto - AcumPpto + FuturoAcumulado ELSE 0 END AS ValorForecast,
               Tabla
        FROM (
            SELECT Año, Mes, Id, OrdenReal, CuentaPUC, Descripcion, Valor, FuturoAcumulado, Tabla, ValorFuturo,
                   SUM(Valor)       OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS AcumPpto,
                   SUM(Valor)       OVER (PARTITION BY Año, Id) AS TotalPpto,
                   SUM(ValorFuturo) OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorFuturoAcumulado
            FROM #FinalCalc
        ) C;

        IF OBJECT_ID('tempdb..##FinalTesoreriaPpto') IS NOT NULL DROP TABLE ##FinalTesoreriaPpto;

        -- Expone resultado vía tabla global para consumo por otros SPs
        -- IdEscenario va primero en la tabla final para que quede visible en cualquier
        -- consumo directo de ##FinalTesoreriaPpto (y en el SELECT final de este mismo SP).
        SELECT @IdEscenario AS IdEscenario, * INTO ##FinalTesoreriaPpto FROM #FinalTesoreriaPpto;

        IF @Externo = 0
        BEGIN
            SELECT IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado, ValorFuturo, ValorFuturoAcumulado, ValorForecast FROM ##FinalTesoreriaPpto ORDER BY Año, Mes, Ord;
            DROP TABLE ##FinalTesoreriaPpto;
        END

        IF @Masivo = 0 AND @Externo = 0
            SELECT 1 AS CodMessage, 'Modelo Tesorería Presupuesto de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloTesoreriaPpto 1, 1, 0, 0, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
