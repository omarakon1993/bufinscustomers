/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1) y se
    propagó a los 3 EXEC anidados (sp_ModeloPYG, sp_ModeloBalanceDiff, sp_ModeloBalance) usando
    la forma @IdEscenario = @IdEscenario (los llamados originales no pasaban todos los
    parámetros posicionales anteriores, así que se usa forma nombrada para no desalinear los
    parámetros existentes). SP compuesto puro: no lee ninguna tabla Ini_* directamente.
    #DatosBalDiff (SELECT * de ##FinalBalanceDiff) ganó IdEscenario como primera columna para
    coincidir con lo que sp_ModeloBalanceDiff ahora devuelve. El SELECT final (no publica tabla
    global; nada compone sobre este SP) también devuelve IdEscenario como PRIMERA columna.

    EXEC a sp_ModeloBalanceDiff: se agregó @IncluirTesoreria = 0 explícito (antes no se pasaba y
    quedaba en su default = 1). Rompe la cadena sp_ModeloBalanceDiff -> sp_ModeloBalancePpto ->
    sp_ModeloTesoreriaPpto -> sp_ModeloBalanceDiff (otra vez) al llegar aquí desde FlujoCaja,
    igual que ya hacía sp_ModeloTesoreriaPpto en su propia llamada a sp_ModeloBalanceDiff.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloFlujoCaja]
    @IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT = 0, @IdEscenario TINYINT = 1 -- 1 = solo datos, 0 = datos + mensaje
AS
BEGIN

    SET NOCOUNT ON;

    BEGIN TRY

        DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);
        DECLARE @Año           INT          = (SELECT TOP 1 AnioEjecucion FROM dbo.ConfiguracionesEmpresas WHERE IdEmpresa = @IdEmpresa);

        IF NOT EXISTS (SELECT 1 FROM dbo.Rel_FlujoCaja)
        BEGIN
            SELECT 0 AS CodMessage, 'No existe configuración en Rel_FlujoCaja.' AS ErrorMessage;
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
            -- (sp_ModeloFlujoCaja → sp_ModeloBalanceDiff → sp_ModeloBalancePpto → sp_ModeloTesoreriaPpto → sp_ModeloBalanceDiff)
            -- @IncluirTesoreria=0 rompe ese ciclo (mismo patrón usado por sp_ModeloTesoreriaPpto).
            EXEC dbo.sp_ModeloBalanceDiff @IdEmpresa, @IdUsuario, 1, 1, @IncluirTesoreria = 0, @IdEscenario = @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalBalanceDiff') IS NOT NULL
            BEGIN
                INSERT INTO #DatosBalDiff SELECT * FROM ##FinalBalanceDiff;
                DROP TABLE ##FinalBalanceDiff;
            END
        END TRY BEGIN CATCH END CATCH

        -- Saldos REALES de diciembre por año (origen: sp_ModeloBalance, NO sp_ModeloBalancePpto).
        -- Son el ancla de enero para las cuentas que llegan como saldo acumulado.
        CREATE TABLE #BalanceDic (Descripcion VARCHAR(500), Año INT, Valor MONEY);
        BEGIN TRY
            EXEC dbo.sp_ModeloBalance @IdEmpresa, @IdUsuario, 0, 1, @IdEscenario = @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalBalance') IS NOT NULL
            BEGIN
                INSERT INTO #BalanceDic (Descripcion, Año, Valor)
                SELECT LTRIM(RTRIM(Descripcion)), Año, SUM(ISNULL(Valor, 0))
                FROM   ##FinalBalance
                WHERE  Mes = 12 AND Descripcion IS NOT NULL
                GROUP BY LTRIM(RTRIM(Descripcion)), Año;
                DROP TABLE ##FinalBalance;
            END
        END TRY BEGIN CATCH IF OBJECT_ID('tempdb..##FinalBalance') IS NOT NULL DROP TABLE ##FinalBalance; END CATCH

        -- Staging real, presupuesto, futuro y futuro acumulado
        CREATE TABLE #Valores     (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);
        CREATE TABLE #ValoresPpto (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);
        CREATE TABLE #ValoresFuturo (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);
        CREATE TABLE #ValoresFuturoAcum (Descripcion VARCHAR(500), Año INT, Mes INT, FuturoAcumulado MONEY);

        INSERT INTO #Valores (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor            FROM #DatosPYG    WHERE Descripcion IS NOT NULL UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor            FROM #DatosBalDiff WHERE Descripcion IS NOT NULL;

        INSERT INTO #ValoresPpto (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorPresupuesto FROM #DatosPYG    WHERE Descripcion IS NOT NULL UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorPresupuesto FROM #DatosBalDiff WHERE Descripcion IS NOT NULL;

        -- Capturar ValorFuturoAcumulado según origen definido en Rel_FlujoCaja.Tabla
        INSERT INTO #ValoresFuturoAcum (Descripcion, Año, Mes, FuturoAcumulado)
        -- Del PYG: solo las descripciones que provienen de Z_PYGDetallado
        SELECT LTRIM(RTRIM(P.Descripcion)), P.Año, P.Mes, ISNULL(P.ValorFuturoAcumulado, 0)
        FROM #DatosPYG P
            INNER JOIN dbo.Rel_FlujoCaja R ON R.Descripcion = P.Descripcion
        WHERE P.Descripcion IS NOT NULL
          AND R.Tabla = 'Z_PYGDetallado'
        UNION ALL
        -- Del Balance: filas que se muestran directamente (Tabla = 'Rel_DifBal')
        -- Estas se matchean por CuentaPUC_Calc y usan R.Descripcion como nombre final
        SELECT LTRIM(RTRIM(R.Descripcion)), B.Año, B.Mes, ISNULL(B.ValorFuturoAcumulado, 0)
        FROM #DatosBalDiff B
            INNER JOIN dbo.Rel_FlujoCaja R ON R.CuentaPUC_Calc = B.Descripcion
        WHERE B.Descripcion IS NOT NULL
          AND R.Tabla = 'Rel_DifBal'
        UNION ALL
        -- Del Balance: componentes base que no aparecen directamente en FlujoCaja
        -- (solo se usan como componentes de cálculos posteriores)
        SELECT LTRIM(RTRIM(B.Descripcion)), B.Año, B.Mes, ISNULL(B.ValorFuturoAcumulado, 0)
        FROM #DatosBalDiff B
        WHERE B.Descripcion IS NOT NULL
          AND NOT EXISTS (
              -- Excluye si aparece como Descripcion en Rel_FlujoCaja (ya se procesó arriba o se calculará en cursor)
              SELECT 1 FROM dbo.Rel_FlujoCaja R WHERE R.Descripcion = B.Descripcion
          )
          AND NOT EXISTS (
              -- Excluye si aparece como CuentaPUC_Calc en Rel_DifBal (ya se procesó en el UNION anterior)
              SELECT 1 FROM dbo.Rel_FlujoCaja R WHERE R.CuentaPUC_Calc = B.Descripcion AND R.Tabla = 'Rel_DifBal'
          );
        -- Nota: Las filas con Tabla = 'Rel_FlujoCaja' se calculan SOLO en el cursor

        -- ValorFuturo del PYG: se toma tal cual (ya viene como movimiento del mes).
        INSERT INTO #ValoresFuturo (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, ValorFuturo FROM #DatosPYG WHERE Descripcion IS NOT NULL;

        -- ValorFuturo del BalanceDiff: llega como SALDO ACUMULADO, así que aquí se convierte a
        -- la VARIACIÓN del mes:
        --     ValorFuturo(mes) - ValorFuturo(mes-1)
        -- y en enero contra el saldo REAL de diciembre del año anterior (#BalanceDic, que proviene
        -- de sp_ModeloBalance). Ese saldo real se ajusta con la convención de signo de
        -- REL_BalanceDiff para que quede en la misma base que el saldo futuro.
        -- El LAG se particiona por Descripcion/CuentaPUC/Ord para no alterar la granularidad original.
        ;WITH SaldoConPrev AS (
            SELECT B.Año, B.Mes, B.Ord, B.CuentaPUC,
                   LTRIM(RTRIM(B.Descripcion)) AS Descripcion,
                   ISNULL(B.ValorFuturo, 0) AS SaldoFuturo,
                   LAG(ISNULL(B.ValorFuturo, 0)) OVER (PARTITION BY B.Descripcion, B.CuentaPUC, B.Ord
                                                       ORDER BY B.Año, B.Mes) AS SaldoMesAnterior
            FROM   #DatosBalDiff B
            WHERE  B.Descripcion IS NOT NULL
        )
        INSERT INTO #ValoresFuturo (Descripcion, Año, Mes, Valor)
        SELECT S.Descripcion, S.Año, S.Mes,
               S.SaldoFuturo - CASE WHEN S.Mes = 1
                                    THEN ISNULL(DIC.Valor, 0) * CASE WHEN SG.Signo = 'Cambiar signo' THEN -1 ELSE 1 END
                                    ELSE ISNULL(S.SaldoMesAnterior, 0)
                               END
        FROM   SaldoConPrev S
               OUTER APPLY (SELECT TOP 1 D.Valor FROM #BalanceDic D
                            WHERE D.Descripcion = S.Descripcion AND D.Año = S.Año - 1) DIC
               OUTER APPLY (SELECT TOP 1 R.Signo FROM dbo.REL_BalanceDiff R
                            WHERE R.Descripcion = S.Descripcion) SG;

        -- Meses con ejecución real: mismo criterio que sp_ModeloPYG (fila 'Ingresos' del PYG con valor <> 0).
        -- Se captura ANTES de eliminar #DatosPYG y antes de que el cursor agregue filas calculadas a #Valores.
        CREATE TABLE #MesesConReal (Año INT NOT NULL, Mes INT NOT NULL);

        INSERT INTO #MesesConReal (Año, Mes)
        SELECT DISTINCT Año, Mes
        FROM   #DatosPYG
        WHERE  LTRIM(RTRIM(Descripcion)) = 'Ingresos' AND ISNULL(Valor, 0) <> 0
          AND  Año IS NOT NULL AND Mes IS NOT NULL;

        DROP TABLE #DatosPYG; DROP TABLE #DatosBalDiff;

        CREATE NONCLUSTERED INDEX IX_Val        ON #Valores        (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValPpto    ON #ValoresPpto    (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValFuturo  ON #ValoresFuturo  (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValFutAcum ON #ValoresFuturoAcum (Descripcion, Año, Mes);

        CREATE TABLE #ParsedTerms (Term VARCHAR(500), Signo INT);
        DECLARE @Id INT, @Descripcion VARCHAR(500), @Formula VARCHAR(2000), @Tabla VARCHAR(100), @remaining VARCHAR(2000),
                @sign INT, @matchDesc VARCHAR(500), @matchLen INT;

        -- Cursor secuencial: cada fila puede referenciar resultados de filas anteriores
        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, CuentaPUC_Calc, Tabla FROM dbo.Rel_FlujoCaja ORDER BY Id;

        OPEN cur;
        FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula, @Tabla;

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
            INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
            SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0)
            FROM  (SELECT DISTINCT Año FROM #Valores WHERE Año IS NOT NULL) Y
                CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT  JOIN #ParsedTerms T ON 1 = 1
                LEFT  JOIN (SELECT Descripcion, Año, Mes, Valor,
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

            INSERT INTO #ValoresFuturo (Id, Descripcion, Año, Mes, Valor)
            SELECT @Id, @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(V.Valor, 0) * T.Signo), 0)
            FROM  (SELECT DISTINCT Año FROM #ValoresFuturo WHERE Año IS NOT NULL) Y
                CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT  JOIN #ParsedTerms T ON 1 = 1
                LEFT  JOIN (SELECT Descripcion, Año, Mes, Valor,
                                   ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Id DESC) AS rn
                            FROM #ValoresFuturo) V ON V.Descripcion = T.Term AND V.Año = Y.Año AND V.Mes = M.Mes AND V.rn = 1
            GROUP BY Y.Año, M.Mes;

            -- Calcula FuturoAcumulado sumando los componentes de la fórmula
            -- Recalcular SI:
            -- 1. Es un subtotal interno (Tabla = 'Rel_FlujoCaja'), O
            -- 2. Tiene múltiples componentes Y su fórmula es diferente a la descripción
            DECLARE @NumComponentes INT = (SELECT COUNT(*) FROM #ParsedTerms);

            IF @Tabla = 'Rel_FlujoCaja' OR (@NumComponentes > 1 AND @Formula <> @Descripcion)
            BEGIN
                DELETE FROM #ValoresFuturoAcum WHERE Descripcion = @Descripcion;
                DELETE FROM #ValoresFuturoAcum WHERE Descripcion = @Descripcion;

                INSERT INTO #ValoresFuturoAcum (Descripcion, Año, Mes, FuturoAcumulado)
                SELECT @Descripcion, Y.Año, M.Mes, ISNULL(SUM(ISNULL(VFA.FuturoAcumulado, 0) * T.Signo), 0)
                FROM  (SELECT DISTINCT Año FROM #ValoresFuturoAcum WHERE Año IS NOT NULL) Y
                    CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                    LEFT  JOIN #ParsedTerms T ON 1 = 1
                    LEFT  JOIN (SELECT Descripcion, Año, Mes, FuturoAcumulado,
                                       ROW_NUMBER() OVER (PARTITION BY Descripcion, Año, Mes ORDER BY Descripcion) AS rn
                                FROM #ValoresFuturoAcum) VFA ON VFA.Descripcion = T.Term AND VFA.Año = Y.Año AND VFA.Mes = M.Mes AND VFA.rn = 1
                GROUP BY Y.Año, M.Mes;
            END

            FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula, @Tabla;
        END

        CLOSE cur; DEALLOCATE cur;

        -- Materializa JOIN antes de aplicar ventanas
        SELECT V.Año, V.Mes, V.Id, R.CuentaPUC, V.Descripcion,
               ISNULL(V.Valor,  0) AS Valor,
               ISNULL(VP.Valor, 0) AS ValorPresupuesto,
               ISNULL(VFA.FuturoAcumulado, 0) AS FuturoAcumulado,
               ISNULL(VF.Valor, 0) AS ValorFuturo
        INTO #Final
        FROM   #Valores V
                   INNER JOIN dbo.Rel_FlujoCaja R ON R.Id  = V.Id
                   LEFT  JOIN #ValoresPpto VP      ON VP.Id = V.Id AND VP.Año = V.Año AND VP.Mes = V.Mes
                   LEFT  JOIN #ValoresFuturoAcum VFA ON VFA.Descripcion = V.Descripcion AND VFA.Año = V.Año AND VFA.Mes = V.Mes
                   LEFT  JOIN #ValoresFuturo VF    ON VF.Id = V.Id AND VF.Año = V.Año AND VF.Mes = V.Mes
        WHERE  V.Id IS NOT NULL;

        -- Pre-calcula acumulados por (Año, Id) antes del CASE.
        SELECT Año, Mes, Id, CuentaPUC, Descripcion, Valor, ValorPresupuesto, FuturoAcumulado, ValorFuturo,
               SUM(Valor)            OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS AcumValor,
               SUM(ValorPresupuesto) OVER (PARTITION BY Año, Id) AS TotalPpto,
               SUM(ValorPresupuesto) OVER (PARTITION BY Año, Id ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS AcumPpto
        INTO #FinalCalc FROM #Final;

        -- ValorForecast según si el mes tiene ejecución real (mismo criterio que sp_ModeloPYG:
        -- fila 'Ingresos' del PYG con valor <> 0, ver #MesesConReal):
        --   Mes CON real:  AcumValor[1..M]       + Ppto[M+1..12]
        --   Mes SIN real:  AcumValorFuturo[1..M] + Ppto[M+1..12]
        -- *** FIX *** Se usa la suma corrida de ValorFuturo (ya normalizado a VARIACIÓN del mes)
        -- y no #ValoresFuturoAcum: para las filas de origen Balance ese campo llega como SALDO
        -- ACUMULADO del balance, magnitud no comparable con AcumValor (suma de variaciones).
        ;WITH Acums AS (
            SELECT FC.*,
                   SUM(FC.ValorFuturo) OVER (PARTITION BY FC.Año, FC.Id ORDER BY FC.Mes ROWS UNBOUNDED PRECEDING) AS AcumValorFuturo
            FROM #FinalCalc FC
        )
        SELECT A.Año, A.Mes, A.Id, A.CuentaPUC, A.Descripcion, A.Valor, A.ValorPresupuesto,
               A.AcumValor, A.AcumPpto, A.FuturoAcumulado, A.ValorFuturo,
               A.AcumValorFuturo AS ValorFuturoAcumulado,
               CASE WHEN A.Año = @Año
                    THEN CASE WHEN MR.Mes IS NOT NULL
                              THEN ISNULL(A.AcumValor, 0)
                              ELSE ISNULL(A.AcumValorFuturo, 0)
                         END
                       + ISNULL(A.TotalPpto, 0) - ISNULL(A.AcumPpto, 0)
                    ELSE 0 END AS ValorForecast
        INTO #FinalCalc2
        FROM Acums A
            LEFT JOIN #MesesConReal MR ON MR.Año = A.Año AND MR.Mes = A.Mes;

        -- Resultado final con acumulados. IdEscenario va primero (mismo criterio que el
        -- resto de los SP de modelo).
        SELECT @IdEscenario AS IdEscenario, Año, Mes, Id AS Ord, CuentaPUC, Descripcion, Valor,
               AcumValor AS ValorAcumulado,
               ValorFuturo,
               ValorFuturoAcumulado,
               ValorForecast,
               ValorPresupuesto,
               AcumPpto AS ValorPresupuestoAcumulado
        FROM   #FinalCalc2
        ORDER BY Año, Mes, Id;

        IF @Masivo = 0
            SELECT 1 AS CodMessage, 'Modelo Flujo de Caja de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloFlujoCaja 1, 1, 0, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
