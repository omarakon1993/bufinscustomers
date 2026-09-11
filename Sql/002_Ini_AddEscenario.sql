/*
    002_Ini_AddEscenario.sql
    Agrega la columna IdEscenario a las 9 tablas Ini_* (fuente: Helpers/TablasCargueHelper.cs).
    DEFAULT 1 => todas las filas cargadas hasta hoy quedan automáticamente en "Escenario 1"
    (operación de metadatos en SQL Server 2012+, sin reescritura de tabla ni downtime).

    Requiere haber corrido antes 001_Escenarios_CreateTable.sql.
    Seguro de re-ejecutar (cada bloque valida si la columna/índice/FK ya existe). Cada paso de
    cada tabla va en su propio TRY/CATCH: si algo falla en una tabla (p. ej. el índice, ver nota
    más abajo) las demás tablas se siguen procesando igual, y solo se imprime un aviso.

    NOTA: en algunas tablas Ini_* la columna Año no quedó creada como varchar(10) sino como un
    tipo no indexable (TEXT/NTEXT/VARCHAR(MAX)/NVARCHAR(MAX) — inconsistencia histórica del
    esquema, no algo que este script cambie). SQL Server no permite usar esas columnas como
    columna clave de un índice ("Column 'Año' ... is of a type that is invalid for use as a key
    column in an index."), así que el paso 3 (índice recomendado, no bloqueante) se omite
    automáticamente para esas tablas — la columna y el FK de Escenario sí se agregan sin problema.
*/
USE [bufinscustomers]
GO

DECLARE @tablas TABLE (Nombre SYSNAME);
INSERT INTO @tablas (Nombre) VALUES
    ('Ini_BalancePrueba'), ('Ini_CteYnoCte'), ('Ini_EjecPCH'), ('Ini_PCH'),
    ('Ini_PptoPYG'), ('Ini_PptoPYGConAjuste'), ('Ini_PresupuestoBalance'),
    ('Ini_PYG'), ('Ini_PYGDetalladoConAjuste');

DECLARE @tabla SYSNAME, @sql NVARCHAR(MAX), @anioIndexable BIT;
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT Nombre FROM @tablas;
OPEN cur;
FETCH NEXT FROM cur INTO @tabla;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID('dbo.' + @tabla, 'U') IS NOT NULL
    BEGIN
        -- 1. Columna
        BEGIN TRY
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('dbo.' + @tabla) AND name = 'IdEscenario'
            )
            BEGIN
                SET @sql = N'ALTER TABLE dbo.[' + @tabla + N'] ADD IdEscenario TINYINT NOT NULL DEFAULT (1);';
                EXEC sp_executesql @sql;
            END
        END TRY
        BEGIN CATCH
            PRINT 'Aviso: no se pudo agregar la columna IdEscenario en ' + @tabla + ': ' + ERROR_MESSAGE();
        END CATCH

        -- 2. FK a Escenarios
        BEGIN TRY
            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_' + @tabla + '_Escenario'
            )
            BEGIN
                SET @sql = N'ALTER TABLE dbo.[' + @tabla + N'] ADD CONSTRAINT [FK_' + @tabla + N'_Escenario] '
                         + N'FOREIGN KEY (IdEscenario) REFERENCES dbo.Escenarios(Id);';
                EXEC sp_executesql @sql;
            END
        END TRY
        BEGIN CATCH
            PRINT 'Aviso: no se pudo agregar el FK de Escenario en ' + @tabla + ': ' + ERROR_MESSAGE();
        END CATCH

        -- 3. Índice recomendado: hoy estas tablas no tienen ningún índice y ya se filtran
        --    por (IdEmpresa_Log, Año, Historico_Log) en 5 puntos del código; con Escenario
        --    sumado, un índice ayuda a que cargue/historial/plantilla/modelos no hagan
        --    table scan completo en cada consulta. Se omite si Año no es un tipo indexable
        --    (TEXT/NTEXT/VARCHAR(MAX)/NVARCHAR(MAX)/IMAGE/XML) en esa tabla en particular.
        BEGIN TRY
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes WHERE name = 'IX_' + @tabla + '_Empresa_Anio_Escenario'
            )
            BEGIN
                SELECT @anioIndexable = CASE
                        WHEN t.name IN ('text', 'ntext', 'image', 'xml') THEN 0
                        WHEN c.max_length = -1 THEN 0  -- VARCHAR(MAX)/NVARCHAR(MAX)/VARBINARY(MAX)
                        ELSE 1
                    END
                FROM sys.columns c
                    INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID('dbo.' + @tabla) AND c.name = 'Año';

                IF ISNULL(@anioIndexable, 0) = 1
                BEGIN
                    SET @sql = N'CREATE INDEX [IX_' + @tabla + N'_Empresa_Anio_Escenario] '
                             + N'ON dbo.[' + @tabla + N'] (IdEmpresa_Log, [Año], IdEscenario, Historico_Log);';
                    EXEC sp_executesql @sql;
                END
                ELSE
                BEGIN
                    PRINT 'Aviso: se omitió el índice recomendado en ' + @tabla
                        + ' porque la columna Año no admite ser columna de índice en esta tabla (tipo TEXT/NTEXT/(N)VARCHAR(MAX) u otro no indexable).';
                END
            END
        END TRY
        BEGIN CATCH
            PRINT 'Aviso: no se pudo crear el índice recomendado en ' + @tabla + ': ' + ERROR_MESSAGE();
        END CATCH
    END
    FETCH NEXT FROM cur INTO @tabla;
END
CLOSE cur;
DEALLOCATE cur;
GO
