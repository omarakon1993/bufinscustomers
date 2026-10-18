/* ============================================================================
   033 - Auditoria detallada de cargues + archivos originales conservados
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Crea:
     - dbo.CargueDetalleAuditoria : una fila por tabla Ini_/Z_ afectada en cada
       cargue confirmado, cierre de anio o rollback (filas antes / filas nuevas).
     - dbo.CargueArchivos         : los ultimos N archivos Excel confirmados por
       empresa (comprimidos con GZip). N = clave Cargue_ArchivosConservados.
     - Parametros en ConfiguracionSistema (Configuraciones generales BUFINS):
         Cargue_ArchivosConservados  (5)  cuantos archivos conservar por empresa (0 = ninguno)
         Cargue_UmbralReemplazoPct   (50) si un cargue deja una tabla con menos de este %
                                          de las filas actuales, exige confirmacion reforzada
   Sin este script el cargue funciona igual (el detalle y los archivos no se guardan)
   y las alertas usan los valores por defecto 5 y 50.
   ============================================================================ */

IF OBJECT_ID('dbo.CargueDetalleAuditoria', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargueDetalleAuditoria
    (
        Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CargueDetalleAuditoria PRIMARY KEY,
        IdAuditoriaCargue INT          NULL,                 -- AuditoriaCargues.Id (NULL en rollback)
        Origen           VARCHAR(20)   NOT NULL,             -- Cargue | CierreAnio | Rollback
        Fecha            DATETIME      NOT NULL CONSTRAINT DF_CargueDetAud_Fecha DEFAULT (GETDATE()),
        IdEmpresa        INT           NOT NULL,
        NombreEmpresa    NVARCHAR(200) NULL,
        Anio             INT           NULL,
        Modo             TINYINT       NULL,                 -- 0 ejecucion, 1 historico
        IdEscenario      INT           NULL,
        IdUsuario        INT           NULL,
        NombreUsuario    NVARCHAR(200) NULL,
        NombreArchivo    NVARCHAR(300) NULL,
        IdLote           BIGINT        NULL,
        IdHistorial      INT           NULL,                 -- snapshot (version) generado antes del cambio
        NombreHoja       NVARCHAR(100) NULL,
        NombreTabla      NVARCHAR(128) NOT NULL,
        FilasAntes       INT           NOT NULL CONSTRAINT DF_CargueDetAud_Antes DEFAULT (0),   -- filas que habia (y se reemplazaron/convirtieron)
        FilasNuevas      INT           NOT NULL CONSTRAINT DF_CargueDetAud_Nuevas DEFAULT (0),  -- filas que quedaron cargadas
        Nota             NVARCHAR(500) NULL
    );
    CREATE INDEX IX_CargueDetAud_Auditoria ON dbo.CargueDetalleAuditoria (IdAuditoriaCargue);
    CREATE INDEX IX_CargueDetAud_Empresa   ON dbo.CargueDetalleAuditoria (IdEmpresa, Fecha DESC);
END
GO

IF OBJECT_ID('dbo.CargueArchivos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargueArchivos
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CargueArchivos PRIMARY KEY,
        IdLote            BIGINT        NULL,                -- lote de staging mientras esta Pendiente
        IdAuditoriaCargue INT           NULL,                -- se llena al confirmar
        Estado            VARCHAR(12)   NOT NULL CONSTRAINT DF_CargueArch_Estado DEFAULT ('Pendiente'), -- Pendiente | Confirmado
        IdEmpresa         INT           NOT NULL,
        NombreEmpresa     NVARCHAR(200) NULL,
        Anio              INT           NULL,
        Modo              TINYINT       NULL,
        IdEscenario       INT           NULL,
        IdUsuario         INT           NULL,
        NombreUsuario     NVARCHAR(200) NULL,
        NombreArchivo     NVARCHAR(300) NULL,
        FechaCarga        DATETIME      NOT NULL CONSTRAINT DF_CargueArch_Fecha DEFAULT (GETDATE()),
        TamanoBytes       INT           NOT NULL,
        Sha256            CHAR(64)      NULL,
        DatosGzip         VARBINARY(MAX) NOT NULL
    );
    CREATE INDEX IX_CargueArch_Empresa  ON dbo.CargueArchivos (IdEmpresa, Estado, FechaCarga DESC);
    CREATE INDEX IX_CargueArch_Auditoria ON dbo.CargueArchivos (IdAuditoriaCargue);
END
GO

/* ---- Tipo (categoria) para los parametros nuevos ---------------------------
   Solo si ya se ejecuto el script 028 (tipos de configuracion). */
IF OBJECT_ID('dbo.ConfiguracionSistemaTipos', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistemaTipos WHERE Nombre = N'Cargues de datos')
    INSERT INTO dbo.ConfiguracionSistemaTipos (Nombre, NombreEN, Icono, Orden)
    VALUES (N'Cargues de datos', N'Data uploads', 'fas fa-file-upload', 50);
GO

/* ---- Parametros configurables ---------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'Cargue_ArchivosConservados')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('Cargue_ArchivosConservados', '5',
            N'Cuantos archivos Excel de cargue confirmados se conservan por empresa (los mas recientes). 0 = no conservar. Maximo 50.');

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'Cargue_UmbralReemplazoPct')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('Cargue_UmbralReemplazoPct', '50',
            N'Alerta de borrado grande: si un cargue deja una tabla con menos de este porcentaje (1-100) de las filas que tiene hoy el anio que reemplaza, se exige confirmacion reforzada.');
GO

IF COL_LENGTH('dbo.ConfiguracionSistema', 'IdTipo') IS NOT NULL
   AND OBJECT_ID('dbo.ConfiguracionSistemaTipos', 'U') IS NOT NULL
    EXEC(N'UPDATE c SET c.IdTipo = t.Id
             FROM dbo.ConfiguracionSistema c
             JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N''Cargues de datos''
            WHERE c.IdTipo IS NULL AND c.Clave IN (''Cargue_ArchivosConservados'', ''Cargue_UmbralReemplazoPct'')');
GO

SELECT Clave, Valor FROM dbo.ConfiguracionSistema WHERE Clave LIKE 'Cargue[_]%';
GO
