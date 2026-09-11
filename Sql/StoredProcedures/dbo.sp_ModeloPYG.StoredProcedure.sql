/*
    Escenarios: se agregó el parámetro @IdEscenario TINYINT = 1 (default = Escenario 1) y el
    filtro AND ISNULL(IdEscenario,1) = @IdEscenario en cada lectura de Ini_PYG, Ini_PptoPYG e
    Ini_PptoPYGConAjuste. SP hoja (no compone otros SP de modelo).
    La tabla final (##FinalPYG) y el SELECT final devuelven IdEscenario como PRIMERA columna.
*/
USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[sp_ModeloPYG]
    @IdEmpresa INT, @IdUsuario INT, @Masivo TINYINT = 0, @Externo TINYINT=0, @IdEscenario TINYINT = 1
AS
BEGIN

    SET NOCOUNT ON;

    BEGIN TRY

        DECLARE @NombreEmpresa VARCHAR(300), @Año INT, @Id INT, @Descripcion VARCHAR(500), @Formula VARCHAR(2000),
                @remaining VARCHAR(2000), @sign INT, @matchDesc VARCHAR(500), @matchLen INT;

        SET @NombreEmpresa = (SELECT TOP 1 EmpNombre FROM dbo.Empresas WHERE EmpId = @IdEmpresa);
        SET @Año           = (SELECT TOP 1 AnioEjecucion FROM dbo.ConfiguracionesEmpresas WHERE IdEmpresa = @IdEmpresa);

        -- PYG real: carga y ajuste de signo
        CREATE TABLE #PYGIni (Tabla VARCHAR(200), Cuenta VARCHAR(200), NuevoSaldo MONEY, Año INT, Mes INT);

        IF (SELECT COUNT(1) FROM dbo.Ini_PYG WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario) = 0
        BEGIN
            SELECT 0 AS CodMessage,
                   'La empresa "' + ISNULL(@NombreEmpresa,'') + '" no tiene datos de plantilla cargados.' AS ErrorMessage;
            RETURN;
        END

        INSERT INTO #PYGIni (Tabla, Cuenta, NuevoSaldo, Año, Mes)
        SELECT 'Z_PYGDetallado', Cuenta, ISNULL(NuevoSaldo,0),
               TRY_CAST(Año AS INT), TRY_CAST(Mes AS INT)
        FROM dbo.Ini_PYG WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;

        UPDATE P
        SET P.NuevoSaldo = CASE WHEN UPPER(R.Signo) = 'CAMBIAR SIGNO' THEN ISNULL(P.NuevoSaldo,0)*-1 ELSE ISNULL(P.NuevoSaldo,0) END
        FROM #PYGIni P
            INNER JOIN dbo.Rel_PYG R ON R.CuentaPUC_Calc = P.Cuenta AND R.Tabla = P.Tabla
        WHERE R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO';

        ;WITH
        Meses   AS (SELECT Mes FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS M(Mes)),
        Años    AS (SELECT DISTINCT Año FROM #PYGIni WHERE Año IS NOT NULL),
        Cuentas AS (
            SELECT Y.Año, MAX(R.Id) AS Ord, R.CuentaPUC, R.Descripcion
            FROM dbo.Rel_PYG R CROSS JOIN Años Y
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND R.Descripcion <> 'Efecto neto en el Ebitda'
            GROUP BY Y.Año, R.CuentaPUC, R.Descripcion),
        Detalle AS (
            SELECT P.Año, P.Mes, R.CuentaPUC, R.Descripcion, SUM(ISNULL(P.NuevoSaldo,0)) AS Valor
            FROM dbo.Rel_PYG R
                INNER JOIN #PYGIni P ON R.CuentaPUC_Calc = P.Cuenta AND R.Tabla = P.Tabla
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND R.Descripcion <> 'Efecto neto en el Ebitda'
              AND P.Año IS NOT NULL
            GROUP BY P.Año, P.Mes, R.CuentaPUC, R.Descripcion)
        SELECT C.Año, M.Mes, C.Ord, C.CuentaPUC, C.Descripcion,
               ISNULL(D.Valor,0) AS Valor,
               SUM(ISNULL(D.Valor,0)) OVER (
                   PARTITION BY C.Año, C.CuentaPUC ORDER BY M.Mes ROWS UNBOUNDED PRECEDING) AS ValorAcumulado
        INTO #FinalPYG
        FROM Cuentas C CROSS JOIN Meses M
            LEFT JOIN Detalle D ON C.Año=D.Año AND C.CuentaPUC=D.CuentaPUC
                                AND C.Descripcion=D.Descripcion AND M.Mes=D.Mes;

        ALTER TABLE #FinalPYG ADD ValorForecast MONEY NOT NULL CONSTRAINT DF_FinalPYG_ValorForecast DEFAULT (0);
        ALTER TABLE #FinalPYG ADD ValorFuturo MONEY NOT NULL CONSTRAINT DF_FinalPYG_ValorFuturo DEFAULT (0);
        ALTER TABLE #FinalPYG ADD ValorFuturoAcumulado MONEY NOT NULL CONSTRAINT DF_FinalPYG_ValorFuturoAcumulado DEFAULT (0);

        -- *** FIX *** Ebitda real: se agrega aquí (antes del cálculo de ValorForecast) para que
        -- participe del mismo CTE de acumulado real + presupuesto restante que las demás cuentas
        INSERT INTO #FinalPYG (Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado, ValorForecast, ValorFuturo, ValorFuturoAcumulado)
        SELECT E.Año, E.Mes, RE.Id, RE.CuentaPUC, 'Efecto neto en el Ebitda', E.Valor,
               SUM(E.Valor) OVER (PARTITION BY E.Año ORDER BY E.Mes ROWS UNBOUNDED PRECEDING),
               0, 0, 0
        FROM (
            SELECT Y.Año, M.Mes,
                   ISNULL(SUM(CASE WHEN LEFT(LTRIM(RTRIM(P.Cuenta)),1)='4' THEN -ISNULL(P.NuevoSaldo,0) ELSE 0 END),0)
                 - ISNULL(SUM(CASE WHEN LEFT(LTRIM(RTRIM(P.Cuenta)),1)='5' THEN  ISNULL(P.NuevoSaldo,0) ELSE 0 END),0) AS Valor
            FROM (SELECT DISTINCT Año FROM #PYGIni WHERE Año IS NOT NULL) Y
            CROSS JOIN (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
            LEFT JOIN dbo.Ini_PYG P ON P.IdEmpresa_Log=@IdEmpresa AND ISNULL(P.IdEscenario,1)=@IdEscenario AND UPPER(P.Ajuste1)='EBITDA'
                AND TRY_CAST(P.Año AS INT)=Y.Año AND TRY_CAST(P.Mes AS INT)=M.Mes
            GROUP BY Y.Año, M.Mes
        ) E
        CROSS JOIN (SELECT TOP 1 Id, CuentaPUC FROM dbo.Rel_PYG WHERE Descripcion='Efecto neto en el Ebitda') RE;

        -- Presupuesto PYG: sin cambio de signo (solo aplica para real)
        CREATE TABLE #PptoIni (Tabla VARCHAR(200), Cuenta VARCHAR(200), Saldo MONEY, Año INT, Mes INT);

        INSERT INTO #PptoIni (Tabla, Cuenta, Saldo, Año, Mes)
        SELECT 'Z_PYGDetallado', Cuenta, ISNULL(Saldo,0),
               TRY_CAST(Año AS INT), TRY_CAST(Mes AS INT)
        FROM dbo.Ini_PptoPYG WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;

        /* *** FIX *** No aplica "CAMBIAR SIGNO" solo aplica para PYG */

        ;WITH
        Meses   AS (SELECT Mes FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS M(Mes)),
        Años    AS (SELECT DISTINCT Año FROM #PptoIni WHERE Año IS NOT NULL),
        Cuentas AS (
            SELECT Y.Año, MAX(R.Id) AS Ord, R.CuentaPUC, R.Descripcion
            FROM dbo.Rel_PYG R CROSS JOIN Años Y
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND R.Descripcion <> 'Efecto neto en el Ebitda'
            GROUP BY Y.Año, R.CuentaPUC, R.Descripcion),
        Detalle AS (
            SELECT P.Año, P.Mes, R.CuentaPUC, R.Descripcion, SUM(ISNULL(P.Saldo,0)) AS Valor
            FROM dbo.Rel_PYG R
                INNER JOIN #PptoIni P ON R.CuentaPUC_Calc = P.Cuenta AND R.Tabla = P.Tabla
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND R.Descripcion <> 'Efecto neto en el Ebitda'
              AND P.Año IS NOT NULL
            GROUP BY P.Año, P.Mes, R.CuentaPUC, R.Descripcion)
        SELECT C.Año, M.Mes, C.Ord, C.CuentaPUC, C.Descripcion,
               ISNULL(D.Valor,0) AS Valor,
               SUM(ISNULL(D.Valor,0)) OVER (
                   PARTITION BY C.Año, C.CuentaPUC ORDER BY M.Mes ROWS UNBOUNDED PRECEDING) AS ValorAcumulado
        INTO #FinalPpto
        FROM Cuentas C CROSS JOIN Meses M
            LEFT JOIN Detalle D ON C.Año=D.Año AND C.CuentaPUC=D.CuentaPUC
                                AND C.Descripcion=D.Descripcion AND M.Mes=D.Mes;

        -- Presupuesto con ajuste: misma estructura, fuente Ini_PptoPYGConAjuste
        CREATE TABLE #PptoCAIni (Tabla VARCHAR(200), Cuenta VARCHAR(200), Saldo MONEY, Año INT, Mes INT);

        INSERT INTO #PptoCAIni (Tabla, Cuenta, Saldo, Año, Mes)
        SELECT 'Z_PYGDetallado', Cuenta, ISNULL(Saldo,0),
               TRY_CAST(Año AS INT), TRY_CAST(Mes AS INT)
        FROM dbo.Ini_PptoPYGConAjuste WHERE IdEmpresa_Log = @IdEmpresa AND ISNULL(IdEscenario,1) = @IdEscenario;

        /* *** FIX *** No aplica "CAMBIAR SIGNO" solo aplica para PYG */

        ;WITH
        Meses   AS (SELECT Mes FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS M(Mes)),
        Años    AS (SELECT DISTINCT Año FROM #PptoCAIni WHERE Año IS NOT NULL),
        Cuentas AS (
            SELECT Y.Año, MAX(R.Id) AS Ord, R.CuentaPUC, R.Descripcion
            FROM dbo.Rel_PYG R CROSS JOIN Años Y
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND R.Descripcion <> 'Efecto neto en el Ebitda'
            GROUP BY Y.Año, R.CuentaPUC, R.Descripcion),
        Detalle AS (
            SELECT P.Año, P.Mes, R.CuentaPUC, R.Descripcion, SUM(ISNULL(P.Saldo,0)) AS Valor
            FROM dbo.Rel_PYG R
                INNER JOIN #PptoCAIni P ON R.CuentaPUC_Calc = P.Cuenta AND R.Tabla = P.Tabla
            WHERE (R.Tipo IS NULL OR UPPER(R.Tipo) <> 'CALCULO') AND R.Descripcion <> 'Efecto neto en el Ebitda'
              AND P.Año IS NOT NULL
            GROUP BY P.Año, P.Mes, R.CuentaPUC, R.Descripcion)
        SELECT C.Año, M.Mes, C.Ord, C.CuentaPUC, C.Descripcion,
               ISNULL(D.Valor,0) AS Valor,
               SUM(ISNULL(D.Valor,0)) OVER (
                   PARTITION BY C.Año, C.CuentaPUC ORDER BY M.Mes ROWS UNBOUNDED PRECEDING) AS ValorAcumulado
        INTO #FinalPptoCA
        FROM Cuentas C CROSS JOIN Meses M
            LEFT JOIN Detalle D ON C.Año=D.Año AND C.CuentaPUC=D.CuentaPUC
                                AND C.Descripcion=D.Descripcion AND M.Mes=D.Mes;

        -- Cursor unificado CALCULO: calcula ppto y ppto-con-ajuste en un solo pase
        -- Seccion 2 = Presupuesto | Seccion 3 = Presupuesto Con Ajuste
        CREATE TABLE #ValoresBudget (
            Seccion     TINYINT      NOT NULL,
            Id          INT          NULL,
            Descripcion VARCHAR(500) NOT NULL,
            Año         INT          NOT NULL,
            Mes         INT          NOT NULL,
            Valor       MONEY        NOT NULL);

        /* Cargar filas base (no-CALCULO) de ambas secciones */
        INSERT INTO #ValoresBudget (Seccion, Descripcion, Año, Mes, Valor)
        SELECT 2, Descripcion, Año, Mes, Valor FROM #FinalPpto  WHERE Descripcion <> 'Efecto neto en el Ebitda'
        UNION ALL
        SELECT 3, Descripcion, Año, Mes, Valor FROM #FinalPptoCA WHERE Descripcion <> 'Efecto neto en el Ebitda';

        /* Ebitda de ambas secciones */
        INSERT INTO #ValoresBudget (Seccion, Id, Descripcion, Año, Mes, Valor)
        SELECT 2, NULL, 'Efecto neto en el Ebitda', Y.Año, M.Mes,
               ISNULL(SUM(CASE WHEN LEFT(P.Cuenta,1)='4' THEN  ISNULL(P.Saldo,0)
                               WHEN LEFT(P.Cuenta,1)='5' THEN -ISNULL(P.Saldo,0) ELSE 0 END),0)
        FROM (SELECT DISTINCT TRY_CAST(Año AS INT) AS Año FROM #PptoIni WHERE Año IS NOT NULL) Y
        CROSS JOIN (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
        LEFT JOIN dbo.Ini_PptoPYG P ON P.IdEmpresa_Log=@IdEmpresa AND ISNULL(P.IdEscenario,1)=@IdEscenario AND UPPER(P.Ajuste1)='EBITDA'
            AND TRY_CAST(P.Año AS INT)=Y.Año AND TRY_CAST(P.Mes AS INT)=M.Mes
        GROUP BY Y.Año, M.Mes
        UNION ALL
        SELECT 3, NULL, 'Efecto neto en el Ebitda', Y.Año, M.Mes,
               ISNULL(SUM(CASE WHEN LEFT(P.Cuenta,1)='4' THEN  ISNULL(P.Saldo,0)
                               WHEN LEFT(P.Cuenta,1)='5' THEN -ISNULL(P.Saldo,0) ELSE 0 END),0)
        FROM (SELECT DISTINCT TRY_CAST(Año AS INT) AS Año FROM #PptoCAIni WHERE Año IS NOT NULL) Y
        CROSS JOIN (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
        LEFT JOIN dbo.Ini_PptoPYGConAjuste P ON P.IdEmpresa_Log=@IdEmpresa AND ISNULL(P.IdEscenario,1)=@IdEscenario AND UPPER(P.Ajuste1)='EBITDA'
            AND TRY_CAST(P.Año AS INT)=Y.Año AND TRY_CAST(P.Mes AS INT)=M.Mes
        GROUP BY Y.Año, M.Mes;

        CREATE NONCLUSTERED INDEX IX_ValBudget ON #ValoresBudget (Seccion, Descripcion, Año, Mes);

        CREATE TABLE #ParsedTerms (Term VARCHAR(500), Signo INT);

        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, Formula FROM dbo.Rel_PYG WHERE UPPER(Tipo)='CALCULO' ORDER BY Id;
        OPEN cur; FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            DELETE FROM #ParsedTerms;
            SET @remaining = CASE WHEN LEFT(LTRIM(@Formula),1) IN ('+','-') THEN LTRIM(@Formula) ELSE '+'+LTRIM(@Formula) END;
            WHILE LEN(ISNULL(@remaining,'')) > 0
            BEGIN
                SET @sign = CASE LEFT(@remaining,1) WHEN '-' THEN -1 ELSE 1 END;
                IF LEFT(@remaining,1) IN ('+','-') SET @remaining = SUBSTRING(@remaining,2,LEN(@remaining));
                IF LEN(ISNULL(@remaining,'')) = 0 BREAK;
                SELECT @matchDesc=NULL, @matchLen=0;
                SELECT TOP 1 @matchDesc=Descripcion, @matchLen=LEN(Descripcion)
                FROM (SELECT DISTINCT Descripcion FROM #ValoresBudget) V
                WHERE LEFT(@remaining,LEN(Descripcion))=Descripcion ORDER BY LEN(Descripcion) DESC;
                IF @matchDesc IS NOT NULL INSERT INTO #ParsedTerms VALUES (@matchDesc, @sign);
                SET @remaining = CASE WHEN @matchDesc IS NOT NULL
                    THEN LTRIM(SUBSTRING(@remaining,@matchLen+1,LEN(@remaining))) ELSE '' END;
            END
            -- Inserta resultado para las dos secciones simultáneamente
            INSERT INTO #ValoresBudget (Seccion, Id, Descripcion, Año, Mes, Valor)
            SELECT SY.Seccion, @Id, @Descripcion, SY.Año, M.Mes,
                   ISNULL(SUM(ISNULL(V.Valor,0)*T.Signo),0)
            FROM (SELECT DISTINCT Seccion, Año FROM #ValoresBudget WHERE Año IS NOT NULL) SY
                CROSS JOIN (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT JOIN #ParsedTerms   T ON 1=1
                LEFT JOIN #ValoresBudget V ON V.Seccion=SY.Seccion AND V.Descripcion=T.Term
                                          AND V.Año=SY.Año AND V.Mes=M.Mes
            GROUP BY SY.Seccion, SY.Año, M.Mes;
            FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;
        END
        CLOSE cur; DEALLOCATE cur;

        UPDATE V SET V.Id=R.Id FROM #ValoresBudget V INNER JOIN dbo.Rel_PYG R ON R.Descripcion=V.Descripcion
        WHERE V.Descripcion='Efecto neto en el Ebitda' AND R.Descripcion='Efecto neto en el Ebitda';

        INSERT INTO #FinalPpto (Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado)
        SELECT V.Año, V.Mes, R.Id, R.CuentaPUC, V.Descripcion, V.Valor,
               SUM(ISNULL(V.Valor,0)) OVER (PARTITION BY V.Año,V.Descripcion ORDER BY V.Mes ROWS UNBOUNDED PRECEDING)
        FROM #ValoresBudget V INNER JOIN dbo.Rel_PYG R ON R.Id=V.Id
        WHERE V.Id IS NOT NULL AND V.Seccion=2;

        INSERT INTO #FinalPptoCA (Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado)
        SELECT V.Año, V.Mes, R.Id, R.CuentaPUC, V.Descripcion, V.Valor,
               SUM(ISNULL(V.Valor,0)) OVER (PARTITION BY V.Año,V.Descripcion ORDER BY V.Mes ROWS UNBOUNDED PRECEDING)
        FROM #ValoresBudget V INNER JOIN dbo.Rel_PYG R ON R.Id=V.Id
        WHERE V.Id IS NOT NULL AND V.Seccion=3;

        -- ValorFuturo: Si el mes tiene ingresos reales, usa el valor real; si no, usa el presupuesto
        ;WITH IngresosReales AS (
            SELECT DISTINCT Año, Mes
            FROM #FinalPYG
            WHERE Año = @Año AND Descripcion = 'Ingresos' AND Valor <> 0
        )
        UPDATE F
        SET F.ValorFuturo = CASE WHEN IR.Mes IS NOT NULL THEN F.Valor ELSE ISNULL(PP.Valor, 0) END
        FROM #FinalPYG F
            LEFT JOIN IngresosReales IR ON IR.Año = F.Año AND IR.Mes = F.Mes
            LEFT JOIN #FinalPpto PP ON PP.Año = F.Año AND PP.Mes = F.Mes AND PP.Ord = F.Ord
        WHERE F.Año = @Año;

        -- Calcular ValorFuturoAcumulado
        ;WITH AcumFuturo AS (
            SELECT Año, Mes, Ord,
                   SUM(ValorFuturo) OVER (PARTITION BY Año, Ord ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorFuturoAcum
            FROM #FinalPYG WHERE Año = @Año
        )
        UPDATE F
        SET F.ValorFuturoAcumulado = ISNULL(AF.ValorFuturoAcum, 0)
        FROM #FinalPYG F
            INNER JOIN AcumFuturo AF ON AF.Año = F.Año AND AF.Mes = F.Mes AND AF.Ord = F.Ord
        WHERE F.Año = @Año;

        -- *** FIX *** ValorForecast según si el mes tiene ejecución real:
        --   Mes CON real:  AcumReal[1..M]   + Ppto[M+1..12]
        --   Mes SIN real:  AcumFuturo[1..M] + Ppto[M+1..12]
        -- En los meses sin ejecución el acumulado real deja el mes en cero pero igual descuenta
        -- su presupuesto del pendiente; por eso allí se usa el acumulado futuro (real + ppto).
        ;WITH IngresosReales AS (
            SELECT DISTINCT Año, Mes
            FROM #FinalPYG
            WHERE Año = @Año AND Descripcion = 'Ingresos' AND Valor <> 0
        ),
        AcumReal AS (
            SELECT Año, Mes, Ord,
                   SUM(Valor) OVER (PARTITION BY Año, Ord ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorEjecutadoAcum
            FROM #FinalPYG WHERE Año = @Año),
        AcumPpto AS (
            SELECT Año, Mes, Ord,
                   SUM(Valor) OVER (PARTITION BY Año, Ord ORDER BY Mes ROWS UNBOUNDED PRECEDING) AS ValorPptoAcum,
                   SUM(Valor) OVER (PARTITION BY Año, Ord)                                        AS ValorPptoTotal
            FROM #FinalPpto WHERE Año = @Año)
        UPDATE F
        SET F.ValorForecast = CASE WHEN IR.Mes IS NOT NULL
                                   THEN ISNULL(AR.ValorEjecutadoAcum, 0)
                                   ELSE ISNULL(F.ValorFuturoAcumulado, 0)
                              END
                            + ISNULL(AP.ValorPptoTotal, 0) - ISNULL(AP.ValorPptoAcum, 0)
        FROM #FinalPYG F
            LEFT JOIN IngresosReales IR ON IR.Año = F.Año AND IR.Mes = F.Mes
            LEFT JOIN AcumReal AR ON AR.Año = F.Año AND AR.Mes = F.Mes AND AR.Ord = F.Ord
            LEFT JOIN AcumPpto AP ON AP.Año = F.Año AND AP.Mes = F.Mes AND AP.Ord = F.Ord
        WHERE F.Año = @Año;

        -- CALCULO PYG real: cursor greedy sobre #ValoresPYG (ValorForecast, ValorFuturo y ValorFuturoAcumulado ya actualizados)
        CREATE TABLE #ValoresPYG (Id INT NULL, Descripcion VARCHAR(500) NOT NULL, Año INT NOT NULL, Mes INT NOT NULL, Valor MONEY NOT NULL, ValorForecast MONEY NOT NULL, ValorFuturo MONEY NOT NULL, ValorFuturoAcumulado MONEY NOT NULL);

        -- *** FIX *** ya no hace falta insertar el Ebitda aparte: viene incluido en #FinalPYG
        -- con su ValorForecast, ValorFuturo y ValorFuturoAcumulado correctamente calculados por los UPDATEs anteriores
        INSERT INTO #ValoresPYG (Descripcion, Año, Mes, Valor, ValorForecast, ValorFuturo, ValorFuturoAcumulado)
        SELECT Descripcion, Año, Mes, Valor, ValorForecast, ValorFuturo, ValorFuturoAcumulado FROM #FinalPYG;

        CREATE NONCLUSTERED INDEX IX_ValPYG ON #ValoresPYG (Descripcion, Año, Mes);

        DELETE FROM #ParsedTerms;

        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, Descripcion, Formula FROM dbo.Rel_PYG WHERE UPPER(Tipo)='CALCULO' ORDER BY Id;
        OPEN cur; FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            DELETE FROM #ParsedTerms;
            SET @remaining = CASE WHEN LEFT(LTRIM(@Formula),1) IN ('+','-') THEN LTRIM(@Formula) ELSE '+'+LTRIM(@Formula) END;
            WHILE LEN(ISNULL(@remaining,'')) > 0
            BEGIN
                SET @sign = CASE LEFT(@remaining,1) WHEN '-' THEN -1 ELSE 1 END;
                IF LEFT(@remaining,1) IN ('+','-') SET @remaining = SUBSTRING(@remaining,2,LEN(@remaining));
                IF LEN(ISNULL(@remaining,'')) = 0 BREAK;
                SELECT @matchDesc=NULL, @matchLen=0;
                SELECT TOP 1 @matchDesc=Descripcion, @matchLen=LEN(Descripcion)
                FROM (SELECT DISTINCT Descripcion FROM #ValoresPYG) V
                WHERE LEFT(@remaining,LEN(Descripcion))=Descripcion ORDER BY LEN(Descripcion) DESC;
                IF @matchDesc IS NOT NULL INSERT INTO #ParsedTerms VALUES (@matchDesc, @sign);
                SET @remaining = CASE WHEN @matchDesc IS NOT NULL
                    THEN LTRIM(SUBSTRING(@remaining,@matchLen+1,LEN(@remaining))) ELSE '' END;
            END
            INSERT INTO #ValoresPYG (Id, Descripcion, Año, Mes, Valor, ValorForecast, ValorFuturo, ValorFuturoAcumulado)
            SELECT @Id, @Descripcion, Y.Año, M.Mes,
                   ISNULL(SUM(ISNULL(V.Valor,                0)*T.Signo),0),
                   ISNULL(SUM(ISNULL(V.ValorForecast,        0)*T.Signo),0),
                   ISNULL(SUM(ISNULL(V.ValorFuturo,          0)*T.Signo),0),
                   ISNULL(SUM(ISNULL(V.ValorFuturoAcumulado, 0)*T.Signo),0)
            FROM (SELECT DISTINCT Año FROM #ValoresPYG WHERE Año IS NOT NULL) Y
                CROSS JOIN (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) M(Mes)
                LEFT JOIN #ParsedTerms T ON 1=1
                LEFT JOIN #ValoresPYG  V ON V.Descripcion=T.Term AND V.Año=Y.Año AND V.Mes=M.Mes
            GROUP BY Y.Año, M.Mes;
            FETCH NEXT FROM cur INTO @Id, @Descripcion, @Formula;
        END
        CLOSE cur; DEALLOCATE cur;

        UPDATE V SET V.Id=R.Id FROM #ValoresPYG V INNER JOIN dbo.Rel_PYG R ON R.Descripcion=V.Descripcion
        WHERE V.Descripcion='Efecto neto en el Ebitda' AND R.Descripcion='Efecto neto en el Ebitda';

        INSERT INTO #FinalPYG (Año, Mes, Ord, CuentaPUC, Descripcion, Valor, ValorAcumulado, ValorForecast, ValorFuturo, ValorFuturoAcumulado)
        SELECT V.Año, V.Mes, R.Id, R.CuentaPUC, V.Descripcion, V.Valor,
               SUM(ISNULL(V.Valor,0)) OVER (PARTITION BY V.Año,V.Descripcion ORDER BY V.Mes ROWS UNBOUNDED PRECEDING),
               V.ValorForecast,
               V.ValorFuturo,
               V.ValorFuturoAcumulado
        FROM #ValoresPYG V INNER JOIN dbo.Rel_PYG R ON R.Id=V.Id
        WHERE V.Id IS NOT NULL AND V.Descripcion <> 'Efecto neto en el Ebitda';

        -- Resultado final: une real, presupuesto y presupuesto con ajuste.
        -- IdEscenario va primero en la tabla final para que quede visible en cualquier
        -- consumo directo de ##FinalPYG (y en el SELECT final de este mismo SP).
        -- Este SP se llama de forma anidada/recursiva desde varios otros (BalancePpto,
        -- TesoreriaPpto, BalanceDiff...); si una ejecución anterior en la misma conexión
        -- quedó a medias sin llegar a su propio DROP, ##FinalPYG podría seguir existiendo.
        IF OBJECT_ID('tempdb..##FinalPYG') IS NOT NULL DROP TABLE ##FinalPYG;

        SELECT  @IdEscenario AS IdEscenario,
                F.Año, F.Mes, F.Ord, F.CuentaPUC, F.Descripcion, F.Valor, F.ValorAcumulado, F.ValorForecast, F.ValorFuturo, F.ValorFuturoAcumulado,
                ISNULL(PP.Valor,          0) AS ValorPresupuesto,
                ISNULL(PP.ValorAcumulado, 0) AS ValorPresupuestoAcumulado,
                ISNULL(PCA.Valor,         0) AS ValorPresupuestoConAjuste
        INTO ##FinalPYG
        FROM #FinalPYG F
            LEFT JOIN #FinalPpto   PP  ON F.Año=PP.Año  AND F.Mes=PP.Mes  AND F.Ord=PP.Ord
            LEFT JOIN #FinalPptoCA PCA ON F.Año=PCA.Año AND F.Mes=PCA.Mes AND F.Ord=PCA.Ord
        ORDER BY F.Año, F.Mes, F.Ord;

        IF @Externo = 0
        BEGIN
            SELECT IdEscenario,Año,Mes,Ord,CuentaPUC,Descripcion,Valor,ValorAcumulado,ValorForecast,ValorFuturo,ValorFuturoAcumulado,ValorPresupuesto,ValorPresupuestoAcumulado,ValorPresupuestoConAjuste
            FROM ##FinalPYG
            ORDER BY Año, Mes, Ord;

            DROP TABLE ##FinalPYG;
        END

        IF @Masivo = 0 AND @Externo = 0
            SELECT 1 AS CodMessage, 'Modelo PYG de "' + ISNULL(@NombreEmpresa, '') + '" ejecutado correctamente.' AS ErrorMessage;

        --EXEC dbo.sp_ModeloPYG 1, 1, 0, 0, 1

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
