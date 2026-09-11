/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1), el
    filtro AND ISNULL(IdEscenario,1) = @IdEscenario en cada lectura directa de Ini_PptoPYG e
    Ini_BalancePrueba, y se propagó @IdEscenario a los 4 EXEC anidados
    (sp_ModeloPYG, sp_ModeloBalance, sp_ModeloTesoreriaPpto) para que el escenario no se
    "pierda" en la cadena de composición. Las tablas #DatosPptoPYG2/#DatosBalance2 que reciben
    esos INSERT...EXEC ganaron IdEscenario como primera columna (coincide con lo que ahora
    devuelven sp_ModeloPYG/sp_ModeloBalance). La tabla final propia (##FinalBalancePpto) y el
    SELECT final también devuelven IdEscenario como PRIMERA columna.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloBalancePpto]
    @IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT=0, @Externo TINYINT=0, @IncluirTesoreria TINYINT=1, @IdEscenario TINYINT = 1
AS
BEGIN

    SET NOCOUNT ON;

    BEGIN TRY

        DECLARE @NombreEmpresa VARCHAR(300) = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);
        DECLARE @AñoEjecucion  INT          = (SELECT TOP 1 AnioEjecucion FROM dbo.ConfiguracionesEmpresas WHERE IdEmpresa = @IdEmpresa);

        IF @AñoEjecucion IS NULL
        BEGIN
            SELECT 0 AS CodMessage, 'No existe configuración de año de ejecución para la empresa "' + ISNULL(@NombreEmpresa, '') + '".' AS ErrorMessage;
            RETURN;
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.Rel_BalancePpto)
        BEGIN
            SELECT 0 AS CodMessage, 'No existe configuración en Rel_BalancePpto.' AS ErrorMessage;
            RETURN;
        END

        -- Fuentes: PYG ppto, balance real y tesorería ppto.
        -- IdEscenario va primero porque sp_ModeloPYG ahora lo devuelve como primera columna
        -- de su SELECT final (INSERT ... EXEC exige coincidencia posicional de columnas).
        CREATE TABLE #DatosPptoPYG2 (IdEscenario TINYINT, Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(200), Descripcion VARCHAR(200), Valor MONEY,
                                     ValorAcumulado MONEY, ValorForecast MONEY,ValorFuturo MONEY,ValorFuturoAcumulado MONEY, ValorPresupuesto MONEY, ValorPresupuestoAcumulado MONEY, ValorPresupuestoConAjuste MONEY);
        CREATE TABLE #UtilidadFuturaPYG (Año INT, Mes INT, ValorFuturoAcumulado MONEY);   -- 'Utilidad neta' -> 'Resultados del ejercicio'
        CREATE TABLE #MovPYG (Año INT, Mes INT, DepCosto MONEY, DepGasto MONEY, AmoCosto MONEY, AmoGasto MONEY);  -- movimientos dep/amo

        IF EXISTS (SELECT 1 FROM dbo.Ini_PptoPYG WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
        BEGIN TRY
            INSERT INTO #DatosPptoPYG2 EXEC dbo.sp_ModeloPYG @IdEmpresa, @IdUsuario, 1, 0, @IdEscenario;

            INSERT INTO #UtilidadFuturaPYG (Año, Mes, ValorFuturoAcumulado)
            SELECT Año, Mes, ValorFuturoAcumulado FROM #DatosPptoPYG2 WHERE Descripcion = 'Utilidad neta';

            INSERT INTO #MovPYG (Año, Mes, DepCosto, DepGasto, AmoCosto, AmoGasto)
            SELECT Año, Mes,
                   SUM(CASE WHEN Descripcion = 'Depreciación costo' THEN ISNULL(ValorFuturo, 0) ELSE 0 END),
                   SUM(CASE WHEN Descripcion = 'Depreciación gasto' THEN ISNULL(ValorFuturo, 0) ELSE 0 END),
                   SUM(CASE WHEN Descripcion = 'Amortización costo' THEN ISNULL(ValorFuturo, 0) ELSE 0 END),
                   SUM(CASE WHEN Descripcion = 'Amortización gasto' THEN ISNULL(ValorFuturo, 0) ELSE 0 END)
            FROM   #DatosPptoPYG2
            WHERE  Descripcion IN ('Depreciación costo', 'Depreciación gasto', 'Amortización costo', 'Amortización gasto')
            GROUP BY Año, Mes;

            IF OBJECT_ID('tempdb..##FinalPYG') IS NOT NULL DROP TABLE ##FinalPYG;
        END TRY BEGIN CATCH END CATCH

        -- IdEscenario va primero: sp_ModeloBalance ahora lo devuelve como primera columna.
        CREATE TABLE #DatosBalance2 (IdEscenario TINYINT, Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY);

        IF EXISTS (SELECT 1 FROM dbo.Ini_BalancePrueba WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
        BEGIN TRY
            INSERT INTO #DatosBalance2 EXEC dbo.sp_ModeloBalance @IdEmpresa, @IdUsuario, 1, 0, @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalBalance') IS NOT NULL DROP TABLE ##FinalBalance;
        END TRY BEGIN CATCH END CATCH

        CREATE TABLE #DatosTesoreria (Año INT, Mes INT, Ord INT, CuentaPUC VARCHAR(500), Descripcion VARCHAR(500), Valor MONEY, ValorAcumulado MONEY);

        -- @IncluirTesoreria=0 rompe el ciclo BalancePpto -> TesoreriaPpto -> BalanceDiffPpto -> BalancePpto
        IF @IncluirTesoreria = 1
           AND EXISTS (SELECT 1 FROM dbo.Ini_BalancePrueba WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
           AND EXISTS (SELECT 1 FROM dbo.Ini_PptoPYG        WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario)
        BEGIN TRY
            EXEC dbo.sp_ModeloTesoreriaPpto @IdEmpresa, @IdUsuario, 1, 1, @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalTesoreriaPpto') IS NOT NULL
            BEGIN
                INSERT INTO #DatosTesoreria SELECT Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado FROM ##FinalTesoreriaPpto;
                DROP TABLE ##FinalTesoreriaPpto;
            END
        END TRY BEGIN CATCH END CATCH

        -- #Valores = presupuesto (columna Valor); #ValoresReal = saldos reales del balance
        CREATE TABLE #Valores     (Id INT NULL, Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);
        CREATE TABLE #ValoresReal (Descripcion VARCHAR(500), Año INT, Mes INT, Valor MONEY);

        INSERT INTO #Valores (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes,
               CASE WHEN Descripcion = 'Utilidad neta' THEN ValorPresupuestoAcumulado ELSE ValorPresupuesto END
        FROM   #DatosPptoPYG2 WHERE Descripcion IS NOT NULL
        UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor FROM #DatosBalance2 WHERE Descripcion IS NOT NULL
        UNION ALL
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor FROM #DatosTesoreria
        WHERE  Descripcion IN ('Excedentes de tesorería', 'Deuda nueva (Déficit de tesorería)');

        INSERT INTO #ValoresReal (Descripcion, Año, Mes, Valor)
        SELECT LTRIM(RTRIM(Descripcion)), Año, Mes, Valor FROM #DatosBalance2 WHERE Descripcion IS NOT NULL;

        DROP TABLE #DatosPptoPYG2; DROP TABLE #DatosBalance2; DROP TABLE #DatosTesoreria;

        IF NOT EXISTS (SELECT 1 FROM #Valores)
        BEGIN
            SELECT 0 AS CodMessage, 'No existen datos de PPTO PYG o Balance.' AS ErrorMessage;
            RETURN;
        END

        CREATE NONCLUSTERED INDEX IX_Val     ON #Valores     (Descripcion, Año, Mes);
        CREATE NONCLUSTERED INDEX IX_ValReal ON #ValoresReal (Descripcion, Año, Mes);

        DECLARE @Id INT, @Descripcion VARCHAR(500), @DescripcionBase VARCHAR(500), @Formula VARCHAR(2000),
                @MesBase INT, @MesActual INT, @MesUtilizado INT, @AñoUtilizado INT, @ValorBase MONEY,
                @ValorRel MONEY, @TempFormula VARCHAR(2000), @ValorBaseStr VARCHAR(50), @ValorRelStr VARCHAR(50),
                @CalculatedValue MONEY, @DynSQL NVARCHAR(MAX), @Result MONEY, @Tipo VARCHAR(50),
                @MesLoop INT, @UltMesReal INT;

        -- PASO 1 | Líneas base: sustituye DescripcionBase (simple o compuesta) y luego el literal 'Valor'
        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, DescripcionBase, MesBase, Mes, Formula, Valor, ISNULL(TipoUnidad, '')
            FROM   dbo.Rel_BalancePpto
            WHERE  Id IS NOT NULL AND UPPER(ISNULL(TipoUnidad, '')) <> 'CALCULO'
            ORDER BY Id, Mes;

        OPEN cur;
        FETCH NEXT FROM cur INTO @Id, @Descripcion, @DescripcionBase, @MesBase, @MesActual, @Formula, @ValorRel, @Tipo;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- MesBase = -1 => base es el mes anterior (en enero, diciembre del año previo)
            SET @MesUtilizado    = CASE WHEN @MesBase = -1 THEN CASE WHEN @MesActual = 1 THEN 12 ELSE @MesActual - 1 END ELSE @MesActual END;
            SET @AñoUtilizado    = CASE WHEN @MesBase = -1 AND @MesActual = 1 THEN @AñoEjecucion - 1 ELSE @AñoEjecucion END;
            SET @DescripcionBase = LTRIM(RTRIM(@DescripcionBase));
            SET @TempFormula     = LTRIM(RTRIM(@Formula));
            SET @ValorBase       = NULL;

            -- Caso simple: DescripcionBase existe como descripción (prioriza el Id calculado)
            SELECT TOP 1 @ValorBase = ISNULL(Valor, 0)
            FROM   #Valores
            WHERE  Descripcion = @DescripcionBase AND Año = @AñoUtilizado AND Mes = @MesUtilizado
            ORDER BY CASE WHEN Id IS NOT NULL THEN 0 ELSE 1 END, Id DESC;

            IF @ValorBase IS NOT NULL
                SET @TempFormula = REPLACE(@TempFormula, @DescripcionBase, LTRIM(STR(@ValorBase, 20, 2)));
            ELSE
            BEGIN
                -- Caso compuesto: se sustituye cada término, de mayor a menor longitud,
                -- para que 'Intangibles' no rompa 'Amortización acumulada intangibles'.
                DECLARE curReplace1 CURSOR LOCAL FAST_FORWARD FOR
                    SELECT T.Descripcion, T.Valor
                    FROM (
                        SELECT DISTINCT V.Descripcion,
                               (SELECT TOP 1 V2.Valor FROM #Valores V2
                                WHERE  V2.Descripcion = V.Descripcion AND V2.Año = @AñoUtilizado AND V2.Mes = @MesUtilizado
                                ORDER BY CASE WHEN V2.Id IS NOT NULL THEN 0 ELSE 1 END, V2.Id DESC) AS Valor
                        FROM   #Valores V
                        WHERE  V.Descripcion IS NOT NULL AND V.Año = @AñoUtilizado AND V.Mes = @MesUtilizado
                    ) T
                    WHERE T.Valor IS NOT NULL
                    ORDER BY LEN(T.Descripcion) DESC;

                OPEN curReplace1;
                FETCH NEXT FROM curReplace1 INTO @DescripcionBase, @ValorBase;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    IF CHARINDEX(@DescripcionBase, @TempFormula) > 0
                        SET @TempFormula = REPLACE(@TempFormula, @DescripcionBase, LTRIM(STR(@ValorBase, 20, 2)));
                    FETCH NEXT FROM curReplace1 INTO @DescripcionBase, @ValorBase;
                END
                CLOSE curReplace1; DEALLOCATE curReplace1;
            END

            -- El literal 'Valor' se sustituye siempre al final
            SET @TempFormula = REPLACE(@TempFormula, 'Valor', LTRIM(STR(ISNULL(@ValorRel, 0), 20, 2)));

            SET @CalculatedValue = 0;
            BEGIN TRY
                SET @DynSQL = N'SET @Result = ' + @TempFormula; SET @Result = 0;
                EXEC sp_executesql @DynSQL, N'@Result MONEY OUTPUT', @Result = @Result OUTPUT;
                SET @CalculatedValue = @Result;
            END TRY BEGIN CATCH SET @CalculatedValue = 0; END CATCH

            INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
            VALUES (@Id, @Descripcion, @AñoEjecucion, @MesActual, @CalculatedValue);

            FETCH NEXT FROM cur INTO @Id, @Descripcion, @DescripcionBase, @MesBase, @MesActual, @Formula, @ValorRel, @Tipo;
        END
        CLOSE cur; DEALLOCATE cur;

        -- PASO 2 | Subtotales CALCULO sobre el presupuesto
        -- DISTINCT: hay 12 filas por Id; sin él se insertarían 144 filas. ORDER BY Id: resuelve de menor a mayor nivel
        DECLARE curCalculo CURSOR LOCAL FAST_FORWARD FOR
            SELECT DISTINCT Id, Descripcion, Formula FROM dbo.Rel_BalancePpto
            WHERE  Id IS NOT NULL AND Formula IS NOT NULL AND UPPER(ISNULL(TipoUnidad, '')) = 'CALCULO'
            ORDER BY Id;

        OPEN curCalculo;
        FETCH NEXT FROM curCalculo INTO @Id, @Descripcion, @Formula;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @MesLoop = 1;
            WHILE @MesLoop <= 12
            BEGIN
                SET @TempFormula = LTRIM(RTRIM(@Formula));

                DECLARE curReplace2 CURSOR LOCAL FAST_FORWARD FOR
                    SELECT T.Descripcion, T.Valor
                    FROM (
                        SELECT DISTINCT V.Descripcion,
                               (SELECT TOP 1 V2.Valor FROM #Valores V2
                                WHERE  V2.Descripcion = V.Descripcion AND V2.Año = @AñoEjecucion
                                  AND  V2.Mes = @MesLoop AND V2.Id IS NOT NULL) AS Valor
                        FROM   #Valores V
                        WHERE  V.Descripcion IS NOT NULL AND V.Año = @AñoEjecucion
                          AND  V.Mes = @MesLoop AND V.Id IS NOT NULL
                    ) T
                    WHERE T.Valor IS NOT NULL
                    ORDER BY LEN(T.Descripcion) DESC;

                OPEN curReplace2;
                FETCH NEXT FROM curReplace2 INTO @DescripcionBase, @ValorBase;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    IF CHARINDEX(@DescripcionBase, @TempFormula) > 0
                        SET @TempFormula = REPLACE(@TempFormula, @DescripcionBase, LTRIM(STR(@ValorBase, 20, 2)));
                    FETCH NEXT FROM curReplace2 INTO @DescripcionBase, @ValorBase;
                END
                CLOSE curReplace2; DEALLOCATE curReplace2;

                SET @CalculatedValue = 0;
                BEGIN TRY
                    SET @DynSQL = N'SET @Result = ' + @TempFormula; SET @Result = 0;
                    EXEC sp_executesql @DynSQL, N'@Result MONEY OUTPUT', @Result = @Result OUTPUT;
                    SET @CalculatedValue = @Result;
                END TRY BEGIN CATCH SET @CalculatedValue = 0; END CATCH

                INSERT INTO #Valores (Id, Descripcion, Año, Mes, Valor)
                VALUES (@Id, @Descripcion, @AñoEjecucion, @MesLoop, @CalculatedValue);

                SET @MesLoop = @MesLoop + 1;
            END
            FETCH NEXT FROM curCalculo INTO @Id, @Descripcion, @Formula;
        END
        CLOSE curCalculo; DEALLOCATE curCalculo;

        -- Ingresos reales por Año/Mes: <> 0 marca mes ejecutado; = 0 marca mes futuro
        CREATE TABLE #IngresosReal (Año INT, Mes INT, ValorIngresos MONEY);
        BEGIN TRY
            EXEC dbo.sp_ModeloPYG @IdEmpresa, @IdUsuario, 0, 1, @IdEscenario;
            IF OBJECT_ID('tempdb..##FinalPYG') IS NOT NULL
            BEGIN
                INSERT INTO #IngresosReal (Año, Mes, ValorIngresos)
                SELECT Año, Mes, SUM(ISNULL(Valor, 0))
                FROM   ##FinalPYG
                WHERE  Descripcion = 'Ingresos'
                GROUP BY Año, Mes;
                DROP TABLE ##FinalPYG;
            END
        END TRY BEGIN CATCH IF OBJECT_ID('tempdb..##FinalPYG') IS NOT NULL DROP TABLE ##FinalPYG; END CATCH

        -- Dep/amo acumuladas: mes ejecutado => saldo real; mes futuro => último saldo real + movimientos PYG
        CREATE TABLE #AcumDepAmo (Descripcion VARCHAR(500), Año INT, Mes INT, ValorFuturo MONEY);
        CREATE TABLE #MovDepAmo  (Descripcion VARCHAR(500), Mes INT, Movimiento MONEY, PRIMARY KEY (Descripcion, Mes));
        CREATE TABLE #RealDepAmo (Descripcion VARCHAR(500), Mes INT, ValorReal MONEY, MesEjecutado TINYINT, PRIMARY KEY (Descripcion, Mes));

        -- Movimiento mensual ppto: depreciación = costo + gasto; intangibles = gasto; biológicos = costo
        INSERT INTO #MovDepAmo (Descripcion, Mes, Movimiento)
        SELECT C.Descripcion, MS.Mes,
               CASE C.Descripcion
                   WHEN 'Depreciación acumulada'             THEN ISNULL(M.DepCosto, 0) + ISNULL(M.DepGasto, 0)
                   WHEN 'Amortización acumulada intangibles' THEN ISNULL(M.AmoGasto, 0)
                   WHEN 'Amortización acumulada biológicos'  THEN ISNULL(M.AmoCosto, 0)
               END
        FROM   (VALUES ('Depreciación acumulada'),
                       ('Amortización acumulada intangibles'),
                       ('Amortización acumulada biológicos')) C(Descripcion)
               CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) MS(Mes)
               LEFT  JOIN #MovPYG M ON M.Año = @AñoEjecucion AND M.Mes = MS.Mes;

        -- Saldo real por cuenta/mes y marca de mes ejecutado
        INSERT INTO #RealDepAmo (Descripcion, Mes, ValorReal, MesEjecutado)
        SELECT D.Descripcion, MS.Mes,
               ISNULL((SELECT SUM(VR.Valor) FROM #ValoresReal VR
                       WHERE VR.Descripcion = D.Descripcion AND VR.Año = @AñoEjecucion AND VR.Mes = MS.Mes), 0),
               CASE WHEN ISNULL((SELECT SUM(ISNULL(ING.ValorIngresos, 0)) FROM #IngresosReal ING
                                 WHERE ING.Año = @AñoEjecucion AND ING.Mes = MS.Mes), 0) <> 0 THEN 1 ELSE 0 END
        FROM   (SELECT DISTINCT Descripcion FROM #MovDepAmo) D
               CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) MS(Mes);

        SET @UltMesReal = ISNULL((SELECT MAX(Mes) FROM #RealDepAmo WHERE MesEjecutado = 1), 0);

        INSERT INTO #AcumDepAmo (Descripcion, Año, Mes, ValorFuturo)
        SELECT R.Descripcion, @AñoEjecucion, R.Mes,
               CASE WHEN R.Mes <= @UltMesReal THEN R.ValorReal   -- ejecutado: saldo real sin recalcular
                    ELSE ISNULL(BASE.ValorReal, 0)               -- futuro: último real + movimientos PYG
                         + ISNULL((SELECT SUM(MV.Movimiento) FROM #MovDepAmo MV
                                   WHERE MV.Descripcion = R.Descripcion
                                     AND MV.Mes > @UltMesReal AND MV.Mes <= R.Mes), 0)
               END
        FROM   #RealDepAmo R
               LEFT JOIN #RealDepAmo BASE ON BASE.Descripcion = R.Descripcion AND BASE.Mes = @UltMesReal;

        CREATE NONCLUSTERED INDEX IX_AcumDepAmo ON #AcumDepAmo (Descripcion, Año, Mes);

        -- PASO 3 | ValorFuturo en #ValoresFuturo, en paralelo a #Valores (no altera la columna Valor)
        --   A: líneas base con reglas real/ppto y excepciones
        --   B: subtotales CALCULO reevaluados sobre ValorFuturo
        CREATE TABLE #ValoresFuturo (Descripcion VARCHAR(500), Mes INT, ValorFuturo MONEY, LenDesc INT,
                                     PRIMARY KEY (Descripcion, Mes));

        -- A: líneas base (TipoUnidad <> 'CALCULO')
        INSERT INTO #ValoresFuturo (Descripcion, Mes, ValorFuturo, LenDesc)
        SELECT B.Descripcion, B.Mes, MAX(B.ValorFuturo), LEN(B.Descripcion)
        FROM (
            SELECT V.Descripcion, V.Mes,
                   CASE
                       -- Resultados del ejercicio = utilidad neta futura acumulada del PYG
                       WHEN V.Descripcion = 'Resultados del ejercicio' THEN ISNULL(UFPYG.ValorFuturoAcumulado, 0)
                       -- Deuda nueva: solo aplica en meses futuros
                       WHEN V.Descripcion = 'Deuda nueva (Déficit de tesorería)' THEN
                            CASE WHEN ISNULL(ING.ValorIngresos, 0) = 0 THEN V.Valor ELSE 0 END
                       -- Deuda actual: futuro => ppto; ejecutado => real de obligaciones
                       WHEN V.Descripcion = 'Deuda actual' THEN
                            CASE WHEN ISNULL(ING.ValorIngresos, 0) = 0 THEN V.Valor ELSE ISNULL(TOF.Valor, 0) END
                       -- Dep/amo acumuladas: saldo proyectado en #AcumDepAmo
                       WHEN V.Descripcion IN ('Depreciación acumulada',
                                              'Amortización acumulada intangibles',
                                              'Amortización acumulada biológicos') THEN ISNULL(AC.ValorFuturo, 0)
                       -- Diferidos: siempre ppto
                       WHEN V.Descripcion = 'Amortización acumulada diferidos' THEN V.Valor
                       -- General: ejecutado => real; futuro => ppto
                       ELSE CASE WHEN ISNULL(ING.ValorIngresos, 0) <> 0 THEN ISNULL(VR.Valor, 0) ELSE V.Valor END
                   END AS ValorFuturo
            FROM   #Valores V
                   INNER JOIN (SELECT DISTINCT Id, ISNULL(TipoUnidad, '') AS TipoUnidad FROM dbo.Rel_BalancePpto) R
                           ON R.Id = V.Id AND UPPER(R.TipoUnidad) <> 'CALCULO'
                   LEFT  JOIN (SELECT Descripcion, Año, Mes, SUM(Valor) AS Valor FROM #ValoresReal
                               GROUP BY Descripcion, Año, Mes) VR
                          ON VR.Descripcion = V.Descripcion AND VR.Año = V.Año AND VR.Mes = V.Mes
                   LEFT  JOIN (SELECT Año, Mes, SUM(Valor) AS Valor FROM #ValoresReal
                               WHERE Descripcion = 'Total obligaciones financieras corrientes'
                               GROUP BY Año, Mes) TOF ON TOF.Año = V.Año AND TOF.Mes = V.Mes
                   LEFT  JOIN #IngresosReal ING ON ING.Año = V.Año AND ING.Mes = V.Mes
                   LEFT  JOIN #UtilidadFuturaPYG UFPYG ON UFPYG.Año = V.Año AND UFPYG.Mes = V.Mes
                   LEFT  JOIN #AcumDepAmo AC ON AC.Descripcion = V.Descripcion AND AC.Año = V.Año AND AC.Mes = V.Mes
            WHERE  V.Id IS NOT NULL AND V.Año = @AñoEjecucion
        ) B
        GROUP BY B.Descripcion, B.Mes;

        -- B: subtotales CALCULO sobre los ValorFuturo ya calculados
        DECLARE curCalcFut CURSOR LOCAL FAST_FORWARD FOR
            SELECT DISTINCT Id, Descripcion, Formula FROM dbo.Rel_BalancePpto
            WHERE  Id IS NOT NULL AND Formula IS NOT NULL AND UPPER(ISNULL(TipoUnidad, '')) = 'CALCULO'
            ORDER BY Id;

        OPEN curCalcFut;
        FETCH NEXT FROM curCalcFut INTO @Id, @Descripcion, @Formula;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @MesLoop = 1;
            WHILE @MesLoop <= 12
            BEGIN
                SET @TempFormula = LTRIM(RTRIM(@Formula));

                DECLARE curReplaceFut CURSOR LOCAL FAST_FORWARD FOR
                    SELECT Descripcion, ValorFuturo FROM #ValoresFuturo
                    WHERE  Mes = @MesLoop AND ValorFuturo IS NOT NULL
                    ORDER BY LenDesc DESC;

                OPEN curReplaceFut;
                FETCH NEXT FROM curReplaceFut INTO @DescripcionBase, @ValorBase;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    IF CHARINDEX(@DescripcionBase, @TempFormula) > 0
                        SET @TempFormula = REPLACE(@TempFormula, @DescripcionBase, LTRIM(STR(@ValorBase, 20, 2)));
                    FETCH NEXT FROM curReplaceFut INTO @DescripcionBase, @ValorBase;
                END
                CLOSE curReplaceFut; DEALLOCATE curReplaceFut;

                SET @CalculatedValue = 0;
                BEGIN TRY
                    SET @DynSQL = N'SET @Result = ' + @TempFormula; SET @Result = 0;
                    EXEC sp_executesql @DynSQL, N'@Result MONEY OUTPUT', @Result = @Result OUTPUT;
                    SET @CalculatedValue = @Result;
                END TRY BEGIN CATCH SET @CalculatedValue = 0; END CATCH

                -- Queda disponible para los subtotales de nivel superior
                UPDATE #ValoresFuturo SET ValorFuturo = @CalculatedValue
                WHERE  Descripcion = @Descripcion AND Mes = @MesLoop;

                IF @@ROWCOUNT = 0
                    INSERT INTO #ValoresFuturo (Descripcion, Mes, ValorFuturo, LenDesc)
                    VALUES (@Descripcion, @MesLoop, @CalculatedValue, LEN(@Descripcion));

                SET @MesLoop = @MesLoop + 1;
            END
            FETCH NEXT FROM curCalcFut INTO @Id, @Descripcion, @Formula;
        END
        CLOSE curCalcFut; DEALLOCATE curCalcFut;

        -- Salida: Valor = presupuesto; ValorFuturo = proyección (solo año de ejecución).
        -- IdEscenario va primero en la tabla final para que quede visible en cualquier
        -- consumo directo de ##FinalBalancePpto (y en el SELECT final de este mismo SP).
        -- Este SP se llama de forma anidada/recursiva desde BalanceDiff (que a su vez se
        -- puede volver a llamar desde TesoreriaPpto); si una ejecución anterior en la misma
        -- conexión quedó a medias sin llegar a su propio DROP, ##FinalBalancePpto podría
        -- seguir existiendo.
        IF OBJECT_ID('tempdb..##FinalBalancePpto') IS NOT NULL DROP TABLE ##FinalBalancePpto;

        ;WITH Base AS (
            SELECT V.Año, V.Mes, V.Id AS Ord, CAST(NULL AS VARCHAR(500)) AS CuentaPUC, V.Descripcion, V.Valor, R.Tabla,
                   VF.ValorFuturo
            FROM   #Valores V
                       INNER JOIN (SELECT DISTINCT Id, Tabla FROM dbo.Rel_BalancePpto) R ON R.Id = V.Id
                       LEFT  JOIN #ValoresFuturo VF ON VF.Descripcion = V.Descripcion AND VF.Mes = V.Mes
            WHERE  V.Id IS NOT NULL
        )
        SELECT @IdEscenario AS IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor,
               CASE WHEN Año = @AñoEjecucion THEN ISNULL(ValorFuturo, 0) ELSE 0 END AS ValorFuturo,
               Tabla
        INTO ##FinalBalancePpto
        FROM   Base
        ORDER BY Año, Mes, Ord;

        DROP TABLE #IngresosReal; DROP TABLE #MovPYG; DROP TABLE #MovDepAmo; DROP TABLE #RealDepAmo;
        DROP TABLE #AcumDepAmo; DROP TABLE #ValoresFuturo;

        IF @Externo = 0
        BEGIN
            SELECT IdEscenario, Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorFuturo FROM ##FinalBalancePpto ORDER BY Año, Mes, Ord;
            DROP TABLE ##FinalBalancePpto;
        END

        IF @Masivo = 0 AND @Externo = 0
            SELECT 1 AS CodMessage, 'Modelo Balance Presupuesto de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloBalancePpto 1, 1, 0, 0, 1, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
