/* ============================================================================
   034 - Respaldos automaticos antes de restaurar una version de cargue
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Cada vez que se restaura una version (Historial de versiones de cargues), el
   sistema guarda ANTES una copia de los datos que va a reemplazar en
   dbo.RespaldosRestauracion, para poder recuperarlos si la restauracion fue un
   error. Son independientes de las 2 versiones por llave del historial (no las
   desplazan). Se conservan los ultimos N por (empresa, anio, modo, escenario),
   N = clave Cargue_RespaldosRestauracion (3 por defecto, de 1 a 10).
   Sin este script la restauracion funciona como antes (sin respaldo previo).
   ============================================================================ */

IF OBJECT_ID('dbo.RespaldosRestauracion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RespaldosRestauracion
    (
        Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RespaldosRestauracion PRIMARY KEY,
        Fecha                 DATETIME      NOT NULL CONSTRAINT DF_RespRest_Fecha DEFAULT (GETDATE()),
        IdEmpresa             INT           NOT NULL,
        NombreEmpresa         NVARCHAR(200) NULL,
        Anio                  INT           NOT NULL,
        Modo                  TINYINT       NOT NULL,            -- 0 ejecucion, 1 historico
        IdEscenario           INT           NOT NULL CONSTRAINT DF_RespRest_Esc DEFAULT (1),
        IdUsuario             INT           NULL,                -- quien restauro
        NombreUsuario         NVARCHAR(200) NULL,
        IdHistorialRestaurado INT           NULL,                -- version que se restauro (NULL si es una recuperacion)
        TotalFilas            INT           NOT NULL CONSTRAINT DF_RespRest_Filas DEFAULT (0),
        DatosGzip             VARBINARY(MAX) NOT NULL            -- JSON { tabla : filas } comprimido con GZip
    );
    CREATE INDEX IX_RespRest_Llave ON dbo.RespaldosRestauracion (IdEmpresa, Anio, Modo, IdEscenario, Fecha DESC);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'Cargue_RespaldosRestauracion')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('Cargue_RespaldosRestauracion', '3',
            N'Cuantos respaldos automaticos (copia de los datos reemplazados al restaurar una version) se conservan por empresa, anio, modo y escenario. De 1 a 10.');
GO

IF COL_LENGTH('dbo.ConfiguracionSistema', 'IdTipo') IS NOT NULL
   AND OBJECT_ID('dbo.ConfiguracionSistemaTipos', 'U') IS NOT NULL
    EXEC(N'UPDATE c SET c.IdTipo = t.Id
             FROM dbo.ConfiguracionSistema c
             JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N''Cargues de datos''
            WHERE c.IdTipo IS NULL AND c.Clave = ''Cargue_RespaldosRestauracion''');
GO

SELECT Clave, Valor FROM dbo.ConfiguracionSistema WHERE Clave LIKE 'Cargue[_]%';
GO
