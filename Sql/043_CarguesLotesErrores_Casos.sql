/* ============================================================================
   043_CarguesLotesErrores_Casos.sql  (MANUAL - SQL Server 2012+)
   Hallazgos del cargue ESTRUCTURADOS POR CASO para la pantalla "Revisar cargue"
   (lista de validaciones + detalle de altura fija).

   1) Agrega a dbo.CarguesLotesErrores (todas NULL, retrocompatibles):
        Periodo     'AAAA-MM' (la pantalla lo muestra como "Jun 2026")
        Referencia  cuenta / nivel ('@N4' = Nivel 4 dígitos) / 'Cuenta 23'
        Detalle / DetalleEn   texto corto del caso
        Diferencia  valor de la diferencia (puede ser negativa)
        Regla / ReglaEn         descripción de la regla (igual en todos los casos)
        Sugerencia / SugerenciaEn
   2) Reemplaza dbo.sp_ValidarCargueStaging: los bloques 6 (ecuación contable),
      11 (movimientos descuadrados), 15 (CteYnoCte vs Balance) y 17 (Balance vs PYG
      acumulado) ahora escriben UN hallazgo POR CASO en vez de un solo hallazgo con
      una lista en el texto. LA LÓGICA DE LAS VALIDACIONES NO CAMBIA (mismos filtros
      y tolerancias). Consecuencia: TotalErrores del lote pasa a contar casos reales.
   Sin ejecutar este script la aplicación sigue funcionando: la pantalla muestra
   cada hallazgo antiguo como un caso único de su validación.
   ============================================================================ */
SET NOCOUNT ON;

IF COL_LENGTH('dbo.CarguesLotesErrores', 'Periodo') IS NULL      ALTER TABLE dbo.CarguesLotesErrores ADD Periodo NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'Referencia') IS NULL   ALTER TABLE dbo.CarguesLotesErrores ADD Referencia NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'Detalle') IS NULL      ALTER TABLE dbo.CarguesLotesErrores ADD Detalle NVARCHAR(400) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'DetalleEn') IS NULL    ALTER TABLE dbo.CarguesLotesErrores ADD DetalleEn NVARCHAR(400) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'Diferencia') IS NULL   ALTER TABLE dbo.CarguesLotesErrores ADD Diferencia DECIMAL(38, 4) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'Regla') IS NULL        ALTER TABLE dbo.CarguesLotesErrores ADD Regla NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'ReglaEn') IS NULL      ALTER TABLE dbo.CarguesLotesErrores ADD ReglaEn NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'Sugerencia') IS NULL   ALTER TABLE dbo.CarguesLotesErrores ADD Sugerencia NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.CarguesLotesErrores', 'SugerenciaEn') IS NULL ALTER TABLE dbo.CarguesLotesErrores ADD SugerenciaEn NVARCHAR(500) NULL;
GO

ALTER PROCEDURE [dbo].[sp_ValidarCargueStaging]
	@IdLote BIGINT
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	BEGIN TRY
		DECLARE @IdEmpresa INT, @Anio INT, @Modo TINYINT, @IdEscenario TINYINT, @NombreEmpresaLote NVARCHAR(200);

		SELECT @IdEmpresa = IdEmpresa, @Anio = Anio, @Modo = Modo, @IdEscenario = IdEscenario, @NombreEmpresaLote = NombreEmpresa
		FROM dbo.CarguesLotes WHERE IdLote = @IdLote;

		IF @IdEmpresa IS NULL
		BEGIN
			SELECT 0 AS CodMessage, N'El lote de cargue indicado no existe.' AS ErrorMessage;
			RETURN;
		END

		BEGIN TRANSACTION;

		/* INICIO - LIMPIA HALLAZGOS PREVIOS DEL LOTE (PERMITE REVALIDAR SIN DUPLICAR) */
		DELETE FROM dbo.CarguesLotesErrores WHERE IdLote = @IdLote;

		/* BLOQUE 1 - PAÍS NO REGISTRADO EN dbo.Paises (ERROR) */
		IF EXISTS (SELECT 1
				   FROM (SELECT Pais, IdLote FROM dbo.Staging_Ini_BalancePrueba UNION ALL
						 SELECT Pais, IdLote FROM dbo.Staging_Ini_CteYnoCte UNION ALL
						 SELECT Pais, IdLote FROM dbo.Staging_Ini_PYG UNION ALL
						 SELECT Pais, IdLote FROM dbo.Staging_Ini_PYGDetalladoConAjuste UNION ALL
						 SELECT Pais, IdLote FROM dbo.Staging_Ini_PresupuestoBalance UNION ALL
						 SELECT Pais, IdLote FROM dbo.Staging_Ini_PptoPYG UNION ALL
						 SELECT Pais, IdLote FROM dbo.Staging_Ini_PptoPYGConAjuste) L (Pais, IdLote)
				   WHERE L.IdLote = @IdLote AND NULLIF(LTRIM(RTRIM(L.Pais)), '') IS NOT NULL
					 AND NOT EXISTS (SELECT 1 FROM dbo.Paises p WHERE LTRIM(RTRIM(p.Nombre)) = LTRIM(RTRIM(L.Pais)) COLLATE DATABASE_DEFAULT))
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, NULL, NULL, NULL, N'Pais', 'Error', 'PAIS_INCONSISTENTE', N'País inconsistente', N'Inconsistent country',
					N'El archivo contiene países que no coinciden con los registrados en la tabla de países. Verifique que estén escritos igual.',
					N'The file contains countries that do not match the registered countries list. Check they are spelled the same way.');

		/* BLOQUE 2 - PAÍS VACÍO O NULO, INDICANDO LA(S) HOJA(S) (ADVERTENCIA) */
		DECLARE @HojasPaisVacio NVARCHAR(MAX) = STUFF((
			SELECT N', ' + ISNULL(h.NombreHoja, L.Tabla)
			FROM (SELECT DISTINCT L0.Tabla
				  FROM (SELECT N'Ini_BalancePrueba', Pais, IdLote FROM dbo.Staging_Ini_BalancePrueba UNION ALL
						SELECT N'Ini_CteYnoCte', Pais, IdLote FROM dbo.Staging_Ini_CteYnoCte UNION ALL
						SELECT N'Ini_PYG', Pais, IdLote FROM dbo.Staging_Ini_PYG UNION ALL
						SELECT N'Ini_PYGDetalladoConAjuste', Pais, IdLote FROM dbo.Staging_Ini_PYGDetalladoConAjuste UNION ALL
						SELECT N'Ini_PresupuestoBalance', Pais, IdLote FROM dbo.Staging_Ini_PresupuestoBalance UNION ALL
						SELECT N'Ini_PptoPYG', Pais, IdLote FROM dbo.Staging_Ini_PptoPYG UNION ALL
						SELECT N'Ini_PptoPYGConAjuste', Pais, IdLote FROM dbo.Staging_Ini_PptoPYGConAjuste) L0 (Tabla, Pais, IdLote)
				  WHERE L0.IdLote = @IdLote AND NULLIF(LTRIM(RTRIM(L0.Pais)), '') IS NULL) L
			LEFT JOIN dbo.CargueHojas h ON h.TablaIni = L.Tabla COLLATE DATABASE_DEFAULT
			ORDER BY ISNULL(h.Orden, 999), L.Tabla
			FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'');

		IF @HojasPaisVacio IS NOT NULL
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, NULL, NULL, NULL, N'Pais', 'Advertencia', 'PAIS_VACIO', N'País vacío', N'Empty country',
					N'Se encontraron filas con el país vacío o sin valor en la(s) hoja(s): ' + @HojasPaisVacio + N'. Verifique que todas las filas tengan país.',
					N'Rows with an empty or missing country were found in the sheet(s): ' + @HojasPaisVacio + N'. Check that every row has a country.');

		/* BLOQUE 3 - INGRESOS VARÍAN >1000% VS AÑO GUARDADO MÁS CERCANO, SOLO SI HAY MÁS DE UN AÑO (ADVERTENCIA) */
		DECLARE @IngresosLote DECIMAL(38,4) = (
			SELECT SUM(ISNULL(s.NuevoSaldo, 0) * -1) FROM dbo.Staging_Ini_PYG s
			WHERE s.IdLote = @IdLote
			  AND EXISTS (SELECT 1 FROM dbo.Rel_PYG r WHERE r.CuentaPUC_Calc = s.Cuenta AND r.Descripcion = 'Ingresos' AND r.Tabla = 'Z_PYGDetallado'));

		IF @IngresosLote IS NOT NULL
		BEGIN
			;WITH Ing AS (
				SELECT TRY_CAST(i.Año AS INT) AS Anio, SUM(ISNULL(i.NuevoSaldo, 0) * -1) AS Total
				FROM dbo.Ini_PYG i
				WHERE i.IdEmpresa_Log = @IdEmpresa AND ISNULL(i.IdEscenario, 1) = @IdEscenario AND TRY_CAST(i.Año AS INT) IS NOT NULL
				  AND EXISTS (SELECT 1 FROM dbo.Rel_PYG r WHERE r.CuentaPUC_Calc = i.Cuenta AND r.Descripcion = 'Ingresos' AND r.Tabla = 'Z_PYGDetallado')
				GROUP BY TRY_CAST(i.Año AS INT)),
			Variacion AS (
				SELECT TOP (1) v.Anio, CAST(ABS((@IngresosLote - v.Total) * 100.0 / ABS(v.Total)) AS DECIMAL(18,2)) AS Pct
				FROM Ing v
				WHERE v.Anio <> @Anio AND ISNULL(v.Total, 0) <> 0
				  AND (SELECT COUNT(*) FROM Ing) > 1          -- solo si hay más de un año guardado
				ORDER BY ABS(v.Anio - @Anio))                 -- año guardado más cercano
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			SELECT @IdLote, N'Z_PYGDetallado', N'Ini_PYG', NULL, NULL, 'Advertencia', 'DIFERENCIA_UNIDAD_MEDIDA',
				   N'Posible diferencia de unidad de medida', N'Possible unit-of-measure difference',
				   N'Los ingresos del año ' + CAST(@Anio AS NVARCHAR(4)) + N' varían ' + CAST(Pct AS NVARCHAR(30)) + N'% frente al año ' + CAST(Anio AS NVARCHAR(4)) + N' ya guardado — revise si cambió la unidad de medida (pesos vs. miles/millones).',
				   N'Revenue for year ' + CAST(@Anio AS NVARCHAR(4)) + N' varies ' + CAST(Pct AS NVARCHAR(30)) + N'% from the already-saved year ' + CAST(Anio AS NVARCHAR(4)) + N' — check whether the measurement unit changed (pesos vs. thousands/millions).'
			FROM Variacion WHERE Pct > 1000.00;
		END

		/* BLOQUE 4 - PRESUPUESTO CON AÑO DISTINTO AL AÑO DE EJECUCIÓN DE LA EMPRESA (ERROR) */
		DECLARE @AnioEjecucion INT = (SELECT TRY_CAST(c.AnioEjecucion AS INT) FROM dbo.ConfiguracionesEmpresas c WHERE c.IdEmpresa = @IdEmpresa),
				@AniosPpto NVARCHAR(MAX);

		IF @AnioEjecucion IS NOT NULL
		BEGIN
			SET @AniosPpto = STUFF((
				SELECT N', ' + A.Anio
				FROM (SELECT DISTINCT LTRIM(RTRIM(CAST(L.[Año] AS NVARCHAR(20)))) AS Anio
					  FROM (SELECT [Año], IdLote FROM dbo.Staging_Ini_PptoPYG UNION ALL
							SELECT [Año], IdLote FROM dbo.Staging_Ini_PptoPYGConAjuste) L ([Año], IdLote)
					  WHERE L.IdLote = @IdLote AND NULLIF(LTRIM(RTRIM(CAST(L.[Año] AS NVARCHAR(20)))), '') IS NOT NULL
						AND ISNULL(TRY_CAST(L.[Año] AS INT), -1) <> @AnioEjecucion) A
				ORDER BY A.Anio
				FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

			IF @AniosPpto IS NOT NULL
				INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
				VALUES (@IdLote, NULL, NULL, NULL, N'Año', 'Error', 'ANIO_PPTO_INCONSISTENTE', N'Año de presupuesto inconsistente', N'Inconsistent budget year',
						N'El año de ejecución de la empresa es ' + CAST(@AnioEjecucion AS NVARCHAR(4)) + N', pero el presupuesto contiene el año ' + @AniosPpto +
						N'. El presupuesto sólo se puede cargar para el año de ejecución (' + CAST(@AnioEjecucion AS NVARCHAR(4)) + N').',
						N'The company execution year is ' + CAST(@AnioEjecucion AS NVARCHAR(4)) + N', but the budget contains year ' + @AniosPpto +
						N'. The budget can only be loaded for the execution year (' + CAST(@AnioEjecucion AS NVARCHAR(4)) + N').');
		END

		/* BLOQUE 5 - ACPA VACÍO EN CORRIENTES Y NO CORRIENTES (ERROR) */
		IF EXISTS (SELECT 1 FROM dbo.Staging_Ini_CteYnoCte s
				   WHERE s.IdLote = @IdLote
					 AND NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(CAST(s.AcPa AS NVARCHAR(100)),
						 NCHAR(160), N' '), CHAR(9), N' '), CHAR(13), N' '), CHAR(10), N' '))), N'') IS NULL)
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, N'Z_CteYnoCte', N'Ini_CteYnoCte', NULL, N'AcPa', 'Error', 'ACPA_VACIO', N'AcPa vacío', N'Empty AcPa',
					N'El archivo contiene filas con la columna AcPa nula o vacía. Verifique que todas las filas tengan AcPa diligenciada.',
					N'The file contains rows with a null or empty AcPa column. Check that every row has AcPa filled in.');

		/* BLOQUE 6 - ECUACIÓN CONTABLE: SUMA DE CUENTAS 1..7 POR AÑO-MES Y POR NIVEL (1, 2, 4, 6 DÍGITOS) DEBE SER CERO (ERROR)
		   Un hallazgo POR CASO (Año-Mes-nivel): la pantalla los agrupa por CodigoRegla. Periodo = 'AAAA-MM', Referencia = '@N<nivel>'. */
		DECLARE @ToleranciaEcuacion MONEY = 10;   -- diferencias de redondeo/centavos hasta este valor se ignoran

		;WITH EcuacionNivel AS (
			SELECT s.[Año], s.[Mes], LEN(LTRIM(RTRIM(s.Cuenta))) AS Nivel, SUM(ISNULL(s.NuevoSaldo, 0)) AS Diferencia
			FROM dbo.Staging_Ini_BalancePrueba s
			WHERE s.IdLote = @IdLote AND LEFT(LTRIM(s.Cuenta), 1) IN ('1','2','3','4','5','6','7')
			  AND LEN(LTRIM(RTRIM(s.Cuenta))) IN (1, 2, 4, 6)
			GROUP BY s.[Año], s.[Mes], LEN(LTRIM(RTRIM(s.Cuenta)))),
		Descuadres AS (
			SELECT [Año], [Mes], Nivel, Diferencia,
				   CASE WHEN TRY_CAST([Año] AS INT) IS NOT NULL AND TRY_CAST([Mes] AS INT) BETWEEN 1 AND 12
						THEN CAST(TRY_CAST([Año] AS INT) AS NVARCHAR(4)) + N'-' + RIGHT(N'0' + CAST(TRY_CAST([Mes] AS INT) AS NVARCHAR(2)), 2)
						ELSE LTRIM(RTRIM(CAST([Año] AS NVARCHAR(20)))) + N'-' + LTRIM(RTRIM(CAST([Mes] AS NVARCHAR(20)))) END AS Periodo
			FROM EcuacionNivel WHERE ABS(Diferencia) > @ToleranciaEcuacion)
		INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn,
											 Periodo, Referencia, Detalle, DetalleEn, Diferencia, Regla, ReglaEn, Sugerencia, SugerenciaEn)
		SELECT @IdLote, N'Z_BalancePrueba', N'Ini_BalancePrueba', NULL, N'NuevoSaldo', 'Error', 'ECUACION_CONTABLE_BALANCE', N'Ecuación contable del balance', N'Accounting balance equation',
			   N'Año-Mes ' + d.Periodo + N' | Nivel ' + CAST(d.Nivel AS NVARCHAR(2)) + N' dígito(s): la suma de las cuentas 1 a 7 es ' + CONVERT(NVARCHAR(30), CAST(d.Diferencia AS MONEY), 1) + N' y debe ser cero.',
			   N'Year-Month ' + d.Periodo + N' | Level ' + CAST(d.Nivel AS NVARCHAR(2)) + N' digit(s): the sum of accounts 1 to 7 is ' + CONVERT(NVARCHAR(30), CAST(d.Diferencia AS MONEY), 1) + N' and must be zero.',
			   d.Periodo, N'@N' + CAST(d.Nivel AS NVARCHAR(2)), N'Suma cuentas 1 a 7 ≠ 0', N'Sum of accounts 1 to 7 ≠ 0', CAST(d.Diferencia AS DECIMAL(38, 4)),
			   N'La suma de las cuentas 1 a 7 debe dar cero en cada mes y nivel.', N'The sum of accounts 1 to 7 must be zero in every month and level.',
			   N'Empiece por el nivel de menos dígitos: si cuadra ahí, el error está en un nivel más detallado del mismo mes.', N'Start with the level with the fewest digits: if it balances there, the error is in a more detailed level of the same month.'
		FROM Descuadres d
		ORDER BY d.Nivel, TRY_CAST(d.[Año] AS INT), TRY_CAST(d.[Mes] AS INT);


		/* BLOQUE 7 - CUENTAS CON 8 O MÁS CARACTERES (ERROR) */
		IF EXISTS (SELECT 1
				   FROM (SELECT Cuenta, IdLote FROM dbo.Staging_Ini_BalancePrueba UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_CteYnoCte UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_EjecPCH UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_PCH UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_PYG UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_PYGDetalladoConAjuste UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_PresupuestoBalance UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_PptoPYG UNION ALL
						 SELECT Cuenta, IdLote FROM dbo.Staging_Ini_PptoPYGConAjuste) L (Cuenta, IdLote)
				   WHERE L.IdLote = @IdLote AND LEN(LTRIM(RTRIM(L.Cuenta))) >= 8)
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, NULL, NULL, NULL, N'Cuenta', 'Error', 'CUENTA_LONGITUD_INVALIDA', N'Cuentas con longitud no permitida', N'Accounts with invalid length',
					N'El archivo contiene cuentas con 8 o más caracteres de longitud. Verifique que las cuentas tengan el número de dígitos permitido.',
					N'The file contains accounts with 8 or more characters in length. Check that the accounts have the allowed number of digits.');

		/* BLOQUE 8 - CUENTAS 41 SIN LÍNEA DE NEGOCIO (ERROR) */
		IF EXISTS (SELECT 1
				   FROM (SELECT Cuenta, LineaNegocio, IdLote FROM dbo.Staging_Ini_PYG UNION ALL
						 SELECT Cuenta, LineaNegocio, IdLote FROM dbo.Staging_Ini_PYGDetalladoConAjuste UNION ALL
						 SELECT Cuenta, LineaNegocio, IdLote FROM dbo.Staging_Ini_PptoPYG UNION ALL
						 SELECT Cuenta, LineaNegocio, IdLote FROM dbo.Staging_Ini_PptoPYGConAjuste) L (Cuenta, LineaNegocio, IdLote)
				   WHERE L.IdLote = @IdLote AND LEFT(LTRIM(L.Cuenta), 2) = '41'
					 AND NULLIF(LTRIM(RTRIM(CAST(L.LineaNegocio AS NVARCHAR(200)))), '') IS NULL)
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, NULL, NULL, NULL, N'LineaNegocio', 'Error', 'LINEANEGOCIO_OBLIGATORIA', N'Línea de negocio obligatoria', N'Line of business required',
					N'El archivo contiene cuentas que empiezan por 41 sin la línea de negocio. Este dato es obligatorio para esas cuentas.',
					N'The file contains accounts starting with 41 missing the line of business. This value is required for those accounts.');

		/* BLOQUE 9 - CUENTAS 41 (EXCEPTO 4175): SI UN AÑO-MES TIENE TIPO, TODAS SUS FILAS DEBEN TENERLO (ERROR) */
		IF EXISTS (SELECT 1
				   FROM (SELECT Cuenta, [Año], [Mes], Tipo, IdLote FROM dbo.Staging_Ini_PYG UNION ALL
						 SELECT Cuenta, [Año], [Mes], Tipo, IdLote FROM dbo.Staging_Ini_PYGDetalladoConAjuste UNION ALL
						 SELECT Cuenta, [Año], [Mes], Tipo, IdLote FROM dbo.Staging_Ini_PptoPYG UNION ALL
						 SELECT Cuenta, [Año], [Mes], Tipo, IdLote FROM dbo.Staging_Ini_PptoPYGConAjuste) L (Cuenta, [Año], [Mes], Tipo, IdLote)
				   WHERE L.IdLote = @IdLote AND LEFT(LTRIM(L.Cuenta), 2) = '41' AND LEFT(LTRIM(L.Cuenta), 4) <> '4175'
				   GROUP BY L.[Año], L.[Mes]
				   HAVING COUNT(NULLIF(LTRIM(RTRIM(CAST(L.Tipo AS NVARCHAR(200)))), '')) BETWEEN 1 AND COUNT(*) - 1)
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, NULL, NULL, NULL, N'Tipo', 'Error', 'TIPO_INCOMPLETO_POR_PERIODO', N'Tipo incompleto por periodo', N'Incomplete type by period',
					N'Para una combinación Año-Mes, si al menos una fila tiene tipo o línea de venta, todas las filas de ese Año-Mes deben tenerlo. Se encontraron periodos donde unas filas tienen tipo y otras no.',
					N'For an Year-Month combination, if at least one row has a type or sales line, every row in that Year-Month must have it. Periods were found where some rows have a type and others do not.');

		/* BLOQUE 10 - FILAS SIN AÑO O MES EN CUALQUIER HOJA, EXCEPTO Z_EJECPCH Y Z_PCH (ERROR) */
		DECLARE @TablasConVacios TABLE (TablaStaging SYSNAME);
		DECLARE @sql NVARCHAR(MAX) = N'', @Hojas NVARCHAR(MAX);

		SELECT @sql = @sql + CASE WHEN @sql = N'' THEN N'' ELSE N' UNION ALL ' END +
			   N'SELECT ' + QUOTENAME(t.name, '''') + N' WHERE EXISTS (SELECT 1 FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) +
			   N' WHERE IdLote = @IdLote AND (NULLIF(LTRIM(RTRIM(CAST([Año] AS NVARCHAR(50)))), '''') IS NULL' +
			   N' OR NULLIF(LTRIM(RTRIM(CAST([Mes] AS NVARCHAR(50)))), '''') IS NULL))'
		FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
		WHERE t.name LIKE 'Staging_Ini_%' AND t.name NOT IN ('Staging_Ini_EjecPCH', 'Staging_Ini_PCH')
		  AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'Año')
		  AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'Mes');

		IF @sql <> N''
			INSERT INTO @TablasConVacios (TablaStaging) EXEC sp_executesql @sql, N'@IdLote BIGINT', @IdLote = @IdLote;

		SET @Hojas = STUFF((SELECT N', ' + ISNULL(h.NombreHoja, REPLACE(v.TablaStaging, N'Staging_', N''))
							FROM @TablasConVacios v LEFT JOIN dbo.CargueHojas h ON h.TablaIni = REPLACE(v.TablaStaging, N'Staging_', N'')
							ORDER BY ISNULL(h.NombreHoja, REPLACE(v.TablaStaging, N'Staging_', N''))
							FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

		IF @Hojas IS NOT NULL
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, @Hojas, NULL, NULL, N'Año/Mes', 'Error', 'ANIO_O_MES_VACIO', N'Año o mes vacío', N'Empty year or month',
					N'Se encontraron filas sin año o sin mes en la(s) hoja(s): ' + @Hojas + N'. Verifique que todas las filas tengan año y mes diligenciados.',
					N'Rows with a missing year or month were found in sheet(s): ' + @Hojas + N'. Check that every row has year and month filled in.');

		/* BLOQUE 11 - MOVIMIENTOS DESCUADRADOS: SALDOANTERIOR + DÉBITO - CRÉDITO <> NUEVOSALDO (ERROR)
		   Un hallazgo POR FILA del Excel (hoja, fila, periodo, cuenta, saldo del archivo vs. calculado y diferencia). */
		;WITH Movimientos AS (
			SELECT N'Z_BalancePrueba' AS Hoja, N'Ini_BalancePrueba' AS Tabla, 1 AS OrdenHoja, NumeroFilaExcel, [Año], [Mes], Cuenta, Descripcion, SaldoAnterior, Debito, Credito, NuevoSaldo
			FROM dbo.Staging_Ini_BalancePrueba WHERE IdLote = @IdLote
			UNION ALL
			SELECT N'Z_CteYnoCte', N'Ini_CteYnoCte', 2, NumeroFilaExcel, [Año], [Mes], Cuenta, Descripcion, SaldoAnterior, Debito, Credito, NuevoSaldo
			FROM dbo.Staging_Ini_CteYnoCte WHERE IdLote = @IdLote),
		Descuadres AS (
			SELECT Hoja, Tabla, OrdenHoja, NumeroFilaExcel, [Año], [Mes], LTRIM(RTRIM(Cuenta)) AS Cuenta, LTRIM(RTRIM(Descripcion)) AS Descripcion,
				   ISNULL(SaldoAnterior,0) + ISNULL(Debito,0) - ISNULL(Credito,0) AS SaldoCalculado,
				   ISNULL(NuevoSaldo,0) AS NuevoSaldo,
				   ISNULL(SaldoAnterior,0) + ISNULL(Debito,0) - ISNULL(Credito,0) - ISNULL(NuevoSaldo,0) AS Diferencia,
				   CASE WHEN TRY_CAST([Año] AS INT) IS NOT NULL AND TRY_CAST([Mes] AS INT) BETWEEN 1 AND 12
						THEN CAST(TRY_CAST([Año] AS INT) AS NVARCHAR(4)) + N'-' + RIGHT(N'0' + CAST(TRY_CAST([Mes] AS INT) AS NVARCHAR(2)), 2)
						ELSE LTRIM(RTRIM(CAST([Año] AS NVARCHAR(20)))) + N'-' + LTRIM(RTRIM(CAST([Mes] AS NVARCHAR(20)))) END AS Periodo
			FROM Movimientos
			WHERE LEFT(LTRIM(Cuenta), 1) IN ('1','2','3','4','5','6','7')
			  AND ROUND(ISNULL(SaldoAnterior,0) + ISNULL(Debito,0) - ISNULL(Credito,0) - ISNULL(NuevoSaldo,0), 0) <> 0)
		INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn,
											 Periodo, Referencia, Detalle, DetalleEn, Diferencia, Regla, ReglaEn, Sugerencia, SugerenciaEn)
		SELECT @IdLote, d.Hoja, d.Tabla, d.NumeroFilaExcel, N'NuevoSaldo', 'Error', 'MOVIMIENTOS_DESCUADRADOS',
			   N'Movimientos descuadrados', N'Movements out of balance',
			   N'Hoja ' + d.Hoja + N' | Año-Mes ' + d.Periodo + N' | Cuenta ' + d.Cuenta + ISNULL(N' ' + NULLIF(d.Descripcion, N''), N'') +
			   N': Saldo anterior + Débito - Crédito = ' + CONVERT(NVARCHAR(30), CAST(d.SaldoCalculado AS MONEY), 1) + N', pero el Nuevo saldo es ' + CONVERT(NVARCHAR(30), CAST(d.NuevoSaldo AS MONEY), 1) + N'.',
			   N'Sheet ' + d.Hoja + N' | Year-Month ' + d.Periodo + N' | Account ' + d.Cuenta + ISNULL(N' ' + NULLIF(d.Descripcion, N''), N'') +
			   N': Previous balance + Debit - Credit = ' + CONVERT(NVARCHAR(30), CAST(d.SaldoCalculado AS MONEY), 1) + N', but the New balance is ' + CONVERT(NVARCHAR(30), CAST(d.NuevoSaldo AS MONEY), 1) + N'.',
			   d.Periodo, LEFT(d.Cuenta + ISNULL(N' ' + NULLIF(d.Descripcion, N''), N''), 200),
			   N'Archivo ' + CONVERT(NVARCHAR(30), CAST(d.NuevoSaldo AS MONEY), 1) + N' · Debería ' + CONVERT(NVARCHAR(30), CAST(d.SaldoCalculado AS MONEY), 1),
			   N'File ' + CONVERT(NVARCHAR(30), CAST(d.NuevoSaldo AS MONEY), 1) + N' · Expected ' + CONVERT(NVARCHAR(30), CAST(d.SaldoCalculado AS MONEY), 1),
			   CAST(d.Diferencia AS DECIMAL(38, 4)),
			   N'Saldo anterior + Débito – Crédito debe ser igual al Nuevo saldo.', N'Previous balance + Debit – Credit must equal the New balance.',
			   N'Vaya a la fila indicada y revise débitos y créditos del mes.', N'Go to the indicated row and review the month''s debits and credits.'
		FROM Descuadres d
		ORDER BY ABS(d.Diferencia) DESC, d.OrdenHoja, d.NumeroFilaExcel;


		/* BLOQUE 12 - CUENTA 36 'RESULTADO DEL EJERCICIO' EN ENERO DEBE SER CERO (ERROR) */
		DECLARE @ResultadoEneroErr INT = (
			SELECT COUNT(*) FROM dbo.Staging_Ini_BalancePrueba s
			WHERE s.IdLote = @IdLote AND TRY_CAST(s.Mes AS INT) = 1 AND LTRIM(RTRIM(s.Cuenta)) = '36'
			  AND LTRIM(RTRIM(s.Descripcion)) = 'Resultado del ejercicio' COLLATE DATABASE_DEFAULT
			  AND ROUND(ISNULL(s.NuevoSaldo, 0), 2) <> 0);

		IF @ResultadoEneroErr > 0
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, N'Z_BalancePrueba', N'Ini_BalancePrueba', NULL, N'NuevoSaldo', 'Error', 'RESULTADO_EJERCICIO_ENERO', N'Resultado del ejercicio en enero', N'Period result in January',
					N'Se encontraron ' + CAST(@ResultadoEneroErr AS NVARCHAR(10)) + N' fila(s) en enero con la cuenta 36 ''Resultado del ejercicio'' diferente a cero. En enero de cada año esta cuenta siempre debe ser cero.',
					N'Found ' + CAST(@ResultadoEneroErr AS NVARCHAR(10)) + N' row(s) in January with account 36 ''Resultado del ejercicio'' different from zero. In January of each year this account must always be zero.');

		/* BLOQUE 13 - EMPRESA DEL ARCHIVO DISTINTA A LA EMPRESA DEL LOTE (ERROR) */
		IF NULLIF(LTRIM(RTRIM(@NombreEmpresaLote)), N'') IS NOT NULL
		BEGIN
			DECLARE @HayEmpresaDistinta BIT = 0, @sqlEmp NVARCHAR(MAX) = N'';

			SELECT @sqlEmp = @sqlEmp + CASE WHEN @sqlEmp = N'' THEN N'' ELSE N' UNION ALL ' END +
				   N'SELECT 1 FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) +
				   N' WHERE IdLote = @IdLote AND NULLIF(LTRIM(RTRIM(CAST([Empresa] AS NVARCHAR(200)))), '''') IS NOT NULL' +
				   N' AND LTRIM(RTRIM(CAST([Empresa] AS NVARCHAR(200)))) <> LTRIM(RTRIM(@NombreEmpresaLote)) COLLATE DATABASE_DEFAULT'
			FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
			WHERE t.name LIKE 'Staging_Ini_%'
			  AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'Empresa');

			IF @sqlEmp <> N''
			BEGIN
				SET @sqlEmp = N'SELECT @HayEmpresaDistinta = CASE WHEN EXISTS (' + @sqlEmp + N') THEN 1 ELSE 0 END';
				EXEC sp_executesql @sqlEmp, N'@IdLote BIGINT, @NombreEmpresaLote NVARCHAR(200), @HayEmpresaDistinta BIT OUTPUT',
					 @IdLote = @IdLote, @NombreEmpresaLote = @NombreEmpresaLote, @HayEmpresaDistinta = @HayEmpresaDistinta OUTPUT;
			END

			IF @HayEmpresaDistinta = 1
				INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
				VALUES (@IdLote, NULL, NULL, NULL, N'Empresa', 'Error', 'EMPRESA_DISTINTA', N'Empresa distinta a la del cargue', N'Company different from the one being loaded',
						N'El archivo contiene información de una empresa distinta a la que está cargando (' + @NombreEmpresaLote + N'). No se permite el cargue. Verifique que el archivo corresponda a la empresa seleccionada.',
						N'The file contains information from a company different from the one you are loading (' + @NombreEmpresaLote + N'). The load is not allowed. Check that the file belongs to the selected company.');
		END

		/* BLOQUE 14 - CUENTAS NO PERMITIDAS EN CTEYNOCTE: 11, 14, 16, 17, INICIAN EN 3 Y 15 NO CORRIENTE (ERROR) */
		DECLARE @CuentasNoPermitidasCte INT = (
			SELECT COUNT(*) FROM dbo.Staging_Ini_CteYnoCte s
			WHERE s.IdLote = @IdLote
			  AND (LTRIM(RTRIM(s.Cuenta)) IN ('11','14','16','17') OR LEFT(LTRIM(s.Cuenta), 1) = '3'
				   OR (LTRIM(RTRIM(s.Cuenta)) = '15' AND LTRIM(RTRIM(CAST(s.AcPa AS NVARCHAR(100)))) LIKE N'%NO CORRIENTE%' COLLATE DATABASE_DEFAULT)));

		IF @CuentasNoPermitidasCte > 0
			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			VALUES (@IdLote, N'Z_CteYnoCte', N'Ini_CteYnoCte', NULL, N'Cuenta', 'Error', 'CUENTAS_NO_PERMITIDAS_CTE',
					N'Cuentas no permitidas en Corrientes y No Corrientes', N'Not allowed accounts in Current and Non-Current',
					N'Se encontraron ' + CAST(@CuentasNoPermitidasCte AS NVARCHAR(10)) + N' fila(s) con cuentas que no deben existir en la hoja de Corrientes y No Corrientes: las cuentas 11, 14, 16, 17 y todas las que empiezan por 3 no están permitidas; la cuenta 15 no se permite cuando AcPa contiene ''No Corriente''. Revise y elimine o corrija esas cuentas.',
					N'Found ' + CAST(@CuentasNoPermitidasCte AS NVARCHAR(10)) + N' row(s) with accounts that must not exist in the Current and Non-Current sheet: accounts 11, 14, 16, 17 and any account starting with 3 are not allowed; account 15 is not allowed when AcPa contains ''Non-Current''. Review and remove or fix those accounts.');

		/* BLOQUE 15 - SUMA CTEYNOCTE POR GRUPO (2 DÍGITOS) VS CUENTA DEL BALANCE POR AÑO-MES (ERROR)
		   Un hallazgo POR CASO (Año-Mes-grupo). Referencia = 'Cuenta <grupo>'. */
		;WITH CteAgrupado AS (
			SELECT s.[Año], s.[Mes], LEFT(LTRIM(s.Cuenta), 2) AS Grupo, SUM(ISNULL(s.NuevoSaldo, 0)) AS TotalCte
			FROM dbo.Staging_Ini_CteYnoCte s
			WHERE s.IdLote = @IdLote AND LEN(LTRIM(RTRIM(s.Cuenta))) >= 2
			GROUP BY s.[Año], s.[Mes], LEFT(LTRIM(s.Cuenta), 2)),
		BalanceGrupo AS (
			SELECT b.[Año], b.[Mes], LTRIM(RTRIM(b.Cuenta)) AS Grupo, SUM(ISNULL(b.NuevoSaldo, 0)) AS TotalBalance
			FROM dbo.Staging_Ini_BalancePrueba b
			WHERE b.IdLote = @IdLote AND LEN(LTRIM(RTRIM(b.Cuenta))) = 2
			GROUP BY b.[Año], b.[Mes], LTRIM(RTRIM(b.Cuenta))),
		Descuadres AS (
			SELECT c.[Año], c.[Mes], c.Grupo, c.TotalCte, ISNULL(b.TotalBalance, 0) AS TotalBalance,
				   CASE WHEN TRY_CAST(c.[Año] AS INT) IS NOT NULL AND TRY_CAST(c.[Mes] AS INT) BETWEEN 1 AND 12
						THEN CAST(TRY_CAST(c.[Año] AS INT) AS NVARCHAR(4)) + N'-' + RIGHT(N'0' + CAST(TRY_CAST(c.[Mes] AS INT) AS NVARCHAR(2)), 2)
						ELSE LTRIM(RTRIM(CAST(c.[Año] AS NVARCHAR(20)))) + N'-' + LTRIM(RTRIM(CAST(c.[Mes] AS NVARCHAR(20)))) END AS Periodo
			FROM CteAgrupado c
			LEFT JOIN BalanceGrupo b ON b.[Año] = c.[Año] AND b.[Mes] = c.[Mes] AND b.Grupo = c.Grupo COLLATE DATABASE_DEFAULT
			WHERE ROUND(c.TotalCte - ISNULL(b.TotalBalance, 0), 2) <> 0)
		INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn,
											 Periodo, Referencia, Detalle, DetalleEn, Diferencia, Regla, ReglaEn, Sugerencia, SugerenciaEn)
		SELECT @IdLote, N'Z_CteYnoCte', N'Ini_CteYnoCte', NULL, N'NuevoSaldo', 'Error', 'CTE_VS_BALANCE_POR_GRUPO',
			   N'Descuadre entre Corrientes/No Corrientes y Balance', N'Mismatch between Current/Non-Current and Balance',
			   N'Año-Mes ' + d.Periodo + N' | Cuenta ' + d.Grupo + N': Corrientes y No Corrientes suma ' + CONVERT(NVARCHAR(30), CAST(d.TotalCte AS MONEY), 1) + N' y el Balance de Prueba ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1) + N'.',
			   N'Year-Month ' + d.Periodo + N' | Account ' + d.Grupo + N': Current/Non-Current adds up to ' + CONVERT(NVARCHAR(30), CAST(d.TotalCte AS MONEY), 1) + N' and the Trial Balance to ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1) + N'.',
			   d.Periodo, N'Cuenta ' + d.Grupo,
			   N'CteYnoCte ' + CONVERT(NVARCHAR(30), CAST(d.TotalCte AS MONEY), 1) + N' · Balance ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1),
			   N'CteYnoCte ' + CONVERT(NVARCHAR(30), CAST(d.TotalCte AS MONEY), 1) + N' · Balance ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1),
			   CAST(d.TotalCte - d.TotalBalance AS DECIMAL(38, 4)),
			   N'La suma de Corrientes y No Corrientes por cuenta de 2 dígitos debe coincidir con el Balance de Prueba en cada mes.', N'The sum of Current and Non-Current by 2-digit account must match the Trial Balance in every month.',
			   N'Compare la cuenta indicada entre las hojas CteYnoCte y BalancePrueba del mismo mes.', N'Compare the indicated account between the CteYnoCte and BalancePrueba sheets for the same month.'
		FROM Descuadres d
		ORDER BY TRY_CAST(d.[Año] AS INT), TRY_CAST(d.[Mes] AS INT), d.Grupo;


		/* BLOQUE 16 - SEMESTRE DISTINTO DE 1 O 2 EN Z_HISTPRECIOS_CHURIDO, UN ERROR POR FILA (MÁX. 200 + RESUMEN) */
		IF OBJECT_ID(N'dbo.Staging_Ini_HistPrecios_Churido', N'U') IS NOT NULL
		   AND COL_LENGTH(N'dbo.Staging_Ini_HistPrecios_Churido', N'Semestre') IS NOT NULL
		   AND EXISTS (SELECT 1 FROM dbo.CargueHojas h JOIN dbo.CargueHojasEmpresas he ON he.IdHoja = h.IdHoja
					   WHERE h.TablaIni = N'Ini_HistPrecios_Churido' AND h.Activo = 1 AND he.Activo = 1 AND he.IdEmpresa = @IdEmpresa)
		BEGIN
			DECLARE @SemestresInvalidos INT = (
				SELECT COUNT(*) FROM dbo.Staging_Ini_HistPrecios_Churido s
				WHERE s.IdLote = @IdLote AND ISNULL(TRY_CONVERT(DECIMAL(10, 2), NULLIF(LTRIM(RTRIM(s.Semestre)), '')), 0) NOT IN (1, 2));

			INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
			SELECT TOP (200) @IdLote, N'Z_HistPrecios_Churido', N'Ini_HistPrecios_Churido', s.NumeroFilaExcel, N'Semestre', 'Error', 'SEMESTRE_INVALIDO',
				   N'Semestre inválido', N'Invalid semester',
				   N'El semestre de esta fila es ' + ISNULL(N'«' + LEFT(NULLIF(LTRIM(RTRIM(s.Semestre)), ''), 30) + N'»', N'vacío') + N'. Solo se permite 1 (enero-junio) o 2 (julio-diciembre).',
				   N'The semester in this row is ' + ISNULL(N'«' + LEFT(NULLIF(LTRIM(RTRIM(s.Semestre)), ''), 30) + N'»', N'empty') + N'. Only 1 (January-June) or 2 (July-December) is allowed.'
			FROM dbo.Staging_Ini_HistPrecios_Churido s
			WHERE s.IdLote = @IdLote AND ISNULL(TRY_CONVERT(DECIMAL(10, 2), NULLIF(LTRIM(RTRIM(s.Semestre)), '')), 0) NOT IN (1, 2)
			ORDER BY s.NumeroFilaExcel;

			IF @SemestresInvalidos > 200
				INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
				VALUES (@IdLote, N'Z_HistPrecios_Churido', N'Ini_HistPrecios_Churido', NULL, N'Semestre', 'Error', 'SEMESTRE_INVALIDO', N'Semestre inválido', N'Invalid semester',
						N'Hay ' + CAST(@SemestresInvalidos - 200 AS NVARCHAR(10)) + N' fila(s) más con un semestre distinto de 1 o 2 (arriba se listan las primeras 200).',
						N'There are ' + CAST(@SemestresInvalidos - 200 AS NVARCHAR(10)) + N' more row(s) with a semester other than 1 or 2 (the first 200 are listed above).');
		END

		/* BLOQUE 17 - BALANCE CLASES 4,5,6,7 POR AÑO-MES VS PYG ACUMULADO (ENERO A ESE MES) (ERROR)
		   Un hallazgo POR CASO (Año-Mes-clase). Referencia = 'Cuenta <clase>'. */
		DECLARE @ToleranciaPYG MONEY = 10;   -- diferencias de redondeo/centavos hasta este valor se ignoran

		;WITH BalanceClase AS (
			SELECT TRY_CAST(b.[Año] AS INT) AS Anio, TRY_CAST(b.[Mes] AS INT) AS Mes, LTRIM(RTRIM(b.Cuenta)) AS Clase, SUM(ISNULL(b.NuevoSaldo, 0)) AS TotalBalance
			FROM dbo.Staging_Ini_BalancePrueba b
			WHERE b.IdLote = @IdLote AND LTRIM(RTRIM(b.Cuenta)) IN ('4','5','6','7')
			  AND TRY_CAST(b.[Año] AS INT) IS NOT NULL AND TRY_CAST(b.[Mes] AS INT) IS NOT NULL
			GROUP BY TRY_CAST(b.[Año] AS INT), TRY_CAST(b.[Mes] AS INT), LTRIM(RTRIM(b.Cuenta))),
		PeriodosBalance AS (
			SELECT DISTINCT Anio, Mes FROM BalanceClase),
		PygMes AS (
			SELECT TRY_CAST(p.[Año] AS INT) AS Anio, TRY_CAST(p.[Mes] AS INT) AS Mes, LEFT(LTRIM(p.Cuenta), 1) AS Clase, SUM(ISNULL(p.NuevoSaldo, 0)) AS Total
			FROM dbo.Staging_Ini_PYG p
			WHERE p.IdLote = @IdLote AND LEFT(LTRIM(p.Cuenta), 1) IN ('4','5','6','7')
			  AND TRY_CAST(p.[Año] AS INT) IS NOT NULL AND TRY_CAST(p.[Mes] AS INT) IS NOT NULL
			GROUP BY TRY_CAST(p.[Año] AS INT), TRY_CAST(p.[Mes] AS INT), LEFT(LTRIM(p.Cuenta), 1)),
		Comparacion AS (
			SELECT pb.Anio, pb.Mes, c.Clase,
				   ISNULL(b.TotalBalance, 0) AS TotalBalance,
				   ISNULL((SELECT SUM(pm.Total) FROM PygMes pm
						   WHERE pm.Anio = pb.Anio AND pm.Mes <= pb.Mes AND pm.Clase = c.Clase COLLATE DATABASE_DEFAULT), 0) AS TotalPygAcum
			FROM PeriodosBalance pb
			CROSS JOIN (VALUES ('4'),('5'),('6'),('7')) c (Clase)
			LEFT JOIN BalanceClase b ON b.Anio = pb.Anio AND b.Mes = pb.Mes AND b.Clase = c.Clase COLLATE DATABASE_DEFAULT),
		Descuadres AS (
			SELECT Anio, Mes, Clase, TotalBalance, TotalPygAcum, TotalBalance - TotalPygAcum AS Diferencia,
				   CAST(Anio AS NVARCHAR(4)) + N'-' + RIGHT(N'0' + CAST(Mes AS NVARCHAR(2)), 2) AS Periodo
			FROM Comparacion
			WHERE ABS(TotalBalance - TotalPygAcum) > @ToleranciaPYG)
		INSERT INTO dbo.CarguesLotesErrores (IdLote, NombreHoja, NombreTabla, NumeroFilaExcel, Columna, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn,
											 Periodo, Referencia, Detalle, DetalleEn, Diferencia, Regla, ReglaEn, Sugerencia, SugerenciaEn)
		SELECT @IdLote, N'Z_BalancePrueba', N'Ini_BalancePrueba', NULL, N'NuevoSaldo', 'Error', 'BALANCE_VS_PYG_ACUMULADO',
			   N'Descuadre entre Balance de Prueba y PYG acumulado', N'Mismatch between Trial Balance and accumulated P&L',
			   N'Año-Mes ' + d.Periodo + N' | Cuenta ' + d.Clase + N': el Balance de Prueba trae ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1) + N' y el PYG acumulado de enero a ese mes suma ' + CONVERT(NVARCHAR(30), CAST(d.TotalPygAcum AS MONEY), 1) + N'.',
			   N'Year-Month ' + d.Periodo + N' | Account ' + d.Clase + N': the Trial Balance shows ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1) + N' and the P&L accumulated from January to that month adds up to ' + CONVERT(NVARCHAR(30), CAST(d.TotalPygAcum AS MONEY), 1) + N'.',
			   d.Periodo, N'Cuenta ' + d.Clase,
			   N'Balance ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1) + N' · PYG acum. ' + CONVERT(NVARCHAR(30), CAST(d.TotalPygAcum AS MONEY), 1),
			   N'Balance ' + CONVERT(NVARCHAR(30), CAST(d.TotalBalance AS MONEY), 1) + N' · Accum. P&L ' + CONVERT(NVARCHAR(30), CAST(d.TotalPygAcum AS MONEY), 1),
			   CAST(d.Diferencia AS DECIMAL(38, 4)),
			   N'El saldo de las cuentas 4, 5, 6 y 7 del Balance de Prueba debe coincidir con el PYG acumulado de enero a ese mes.', N'The balance of accounts 4, 5, 6 and 7 in the Trial Balance must match the P&L accumulated from January to that month.',
			   N'Revise primero el mes más antiguo con diferencia: los siguientes suelen arrastrarla.', N'Review the oldest month with a difference first: the following ones usually carry it forward.'
		FROM Descuadres d
		ORDER BY d.Anio, d.Mes, d.Clase;


		/* CIERRE - CALCULA TOTALES Y ACTUALIZA EL ESTADO DEL LOTE */
		DECLARE @TotalErrores INT, @TotalAdvertencias INT;

		SELECT @TotalErrores      = COUNT(CASE WHEN Severidad = 'Error'       THEN 1 END),
			   @TotalAdvertencias = COUNT(CASE WHEN Severidad = 'Advertencia' THEN 1 END)
		FROM dbo.CarguesLotesErrores WHERE IdLote = @IdLote;

		UPDATE dbo.CarguesLotes
		SET TotalErrores = @TotalErrores, TotalAdvertencias = @TotalAdvertencias, FechaValidacion = GETDATE(),
			Estado = CASE WHEN @TotalErrores > 0 THEN 'ConErrores' WHEN @TotalAdvertencias > 0 THEN 'ConAdvertencias' ELSE 'ValidadoOk' END
		WHERE IdLote = @IdLote;

		COMMIT TRANSACTION;

		SELECT 1 AS CodMessage, NULL AS ErrorMessage, @TotalErrores AS TotalErrores, @TotalAdvertencias AS TotalAdvertencias;
	END TRY
	BEGIN CATCH
		-- Sin rollback quedaría el DELETE aplicado y el lote sin validar.
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
	END CATCH
END

