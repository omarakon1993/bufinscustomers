-- =============================================
-- Script de Verificación de Tablas de Datos
-- =============================================
-- Uso: Ejecutar en SQL Server Management Studio
-- Base de datos: bufinscustomers
-- =============================================

USE bufinscustomers;
GO

PRINT '========================================';
PRINT 'VERIFICACIÓN DE TABLAS DE DATOS';
PRINT '========================================';
PRINT '';

-- 1. Verificar que las tablas existen
PRINT '1. VERIFICANDO EXISTENCIA DE TABLAS...';
PRINT '----------------------------------------';

DECLARE @TablasRequeridas TABLE (NombreTabla VARCHAR(100));
INSERT INTO @TablasRequeridas VALUES
    ('TableBalance_Datos_VT'),
    ('TablePYG_Datos_VT'),
    ('TableEbitda_Datos_VT'),
    ('TableFlujoCaja_Datos_VT'),
    ('TableFlujoTesoreria_Datos_VT'),
    ('TableGasFijosYVar_Datos_VT'),
    ('TableTakeRate_Datos_VT'),
    ('TableIngCosGas_Datos_VT'),
    ('TableIngLineasVenta_Datos_VT'),
    ('TablePYGAjustado_Datos_VT');

SELECT
    tr.NombreTabla,
    CASE
        WHEN t.TABLE_NAME IS NOT NULL THEN '✓ EXISTE'
        ELSE '✗ NO EXISTE'
    END AS Estado
FROM @TablasRequeridas tr
LEFT JOIN INFORMATION_SCHEMA.TABLES t
    ON tr.NombreTabla = t.TABLE_NAME
ORDER BY tr.NombreTabla;

PRINT '';
PRINT '2. VERIFICANDO ESTRUCTURA DE COLUMNAS...';
PRINT '----------------------------------------';

-- 2. Verificar que tienen las columnas requeridas
DECLARE @tabla VARCHAR(100);
DECLARE tabla_cursor CURSOR FOR
    SELECT NombreTabla FROM @TablasRequeridas;

OPEN tabla_cursor;
FETCH NEXT FROM tabla_cursor INTO @tabla;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tabla)
    BEGIN
        PRINT '';
        PRINT 'Tabla: ' + @tabla;

        -- Verificar columnas requeridas
        DECLARE @tieneIdEmpresa BIT = 0;
        DECLARE @tieneAnio BIT = 0;
        DECLARE @tieneMes BIT = 0;
        DECLARE @tieneVariable BIT = 0;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tabla AND COLUMN_NAME = 'IdEmpresa')
            SET @tieneIdEmpresa = 1;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tabla AND COLUMN_NAME = 'Año')
            SET @tieneAnio = 1;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tabla AND COLUMN_NAME = 'Mes')
            SET @tieneMes = 1;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tabla AND COLUMN_NAME = 'Variable')
            SET @tieneVariable = 1;

        PRINT '  - IdEmpresa: ' + CASE WHEN @tieneIdEmpresa = 1 THEN '✓' ELSE '✗ FALTA' END;
        PRINT '  - Año: ' + CASE WHEN @tieneAnio = 1 THEN '✓' ELSE '✗ FALTA' END;
        PRINT '  - Mes: ' + CASE WHEN @tieneMes = 1 THEN '✓' ELSE '✗ FALTA' END;
        PRINT '  - Variable: ' + CASE WHEN @tieneVariable = 1 THEN '✓' ELSE '✗ FALTA' END;

        -- Mostrar todas las columnas
        SELECT COLUMN_NAME AS 'Columna', DATA_TYPE AS 'Tipo'
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = @tabla
        ORDER BY ORDINAL_POSITION;
    END
    ELSE
    BEGIN
        PRINT '';
        PRINT 'Tabla: ' + @tabla + ' - ✗ NO EXISTE';
    END

    FETCH NEXT FROM tabla_cursor INTO @tabla;
END

CLOSE tabla_cursor;
DEALLOCATE tabla_cursor;

PRINT '';
PRINT '3. VERIFICANDO DATOS...';
PRINT '----------------------------------------';

-- 3. Contar registros en cada tabla
SELECT
    tr.NombreTabla,
    CASE
        WHEN t.TABLE_NAME IS NOT NULL THEN
            CAST((SELECT COUNT(*)
                  FROM sys.tables st
                  INNER JOIN sys.partitions sp ON st.object_id = sp.object_id
                  WHERE st.name = tr.NombreTabla AND sp.index_id IN (0,1)) AS VARCHAR(20))
        ELSE 'N/A'
    END AS 'Total Registros (aprox)'
FROM @TablasRequeridas tr
LEFT JOIN INFORMATION_SCHEMA.TABLES t
    ON tr.NombreTabla = t.TABLE_NAME
ORDER BY tr.NombreTabla;

PRINT '';
PRINT '4. VERIFICANDO EMPRESAS...';
PRINT '----------------------------------------';

-- 4. Verificar que hay empresas en la tabla Empresas
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Empresas')
BEGIN
    SELECT EmpId, EmpNombre
    FROM Empresas
    ORDER BY EmpNombre;

    PRINT '';
    PRINT 'Total de empresas: ' + CAST((SELECT COUNT(*) FROM Empresas) AS VARCHAR(10));
END
ELSE
BEGIN
    PRINT '✗ La tabla Empresas no existe';
END

PRINT '';
PRINT '========================================';
PRINT 'VERIFICACIÓN COMPLETADA';
PRINT '========================================';
PRINT '';
PRINT 'Si todas las tablas muestran ✓, el sistema está listo para usarse.';
PRINT 'Si alguna muestra ✗, necesitas crear la tabla o agregar las columnas faltantes.';
PRINT '';

-- 5. Query de ejemplo para ver datos de una tabla
PRINT '5. EJEMPLO DE CONSULTA DE DATOS';
PRINT '----------------------------------------';
PRINT 'Para ver datos de ejemplo de una tabla, ejecuta:';
PRINT '';
PRINT 'SELECT TOP 10 * FROM TableBalance_Datos_VT ORDER BY Año DESC, Mes DESC;';
PRINT '';

GO
