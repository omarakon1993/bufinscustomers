-- =====================================================================================
-- 046 · API de ingesta de BUFINS (fase 1): integraciones, idempotencia y origen del lote
-- Compatible con SQL Server 2012. MANUAL: ejecutar una sola vez por entorno (es idempotente).
--
--  dbo.Integraciones              Una credencial por integracion (una empresa, un usuario tecnico).
--                                 La clave NUNCA se guarda: solo su hash SHA-256 y un prefijo publico.
--  dbo.IntegracionesIdempotencia  (IdIntegracion, Idempotency-Key) -> lote: un reenvio no duplica el cargue.
--  dbo.CarguesLotes               + Origen ('Excel' | 'API') e IdIntegracion. Los lotes de la API esperan
--                                 revision humana hasta 48 h (los de Excel siguen venciendo a las 2 h).
-- =====================================================================================

IF OBJECT_ID('dbo.Integraciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Integraciones
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Integraciones PRIMARY KEY,
        IdEmpresa         INT           NOT NULL,
        Nombre            NVARCHAR(100) NOT NULL,
        IdUsuarioTecnico  INT           NOT NULL,                 -- usuario de la empresa al que se atribuyen las acciones de la API
        Prefijo           VARCHAR(12)   NOT NULL,                 -- parte publica de la clave (sirve para ubicarla)
        HashClave         VARBINARY(32) NOT NULL,                 -- SHA-256 de la clave completa
        Activo            BIT           NOT NULL CONSTRAINT DF_Integraciones_Activo DEFAULT (1),
        PermiteConfirmar  BIT           NOT NULL CONSTRAINT DF_Integraciones_PermiteConfirmar DEFAULT (0),  -- 0 = confirma siempre una persona
        IpPermitidas      NVARCHAR(500) NULL,                     -- lista separada por comas; NULL = cualquier IP
        FechaCreacion     DATETIME      NOT NULL CONSTRAINT DF_Integraciones_Fecha DEFAULT (GETDATE()),
        IdUsuarioCreador  INT           NULL,
        FechaUltimoUso    DATETIME      NULL,
        IpUltimoUso       VARCHAR(45)   NULL,
        FechaRevocada     DATETIME      NULL,
        CONSTRAINT UQ_Integraciones_Prefijo UNIQUE (Prefijo),
        CONSTRAINT FK_Integraciones_Empresa FOREIGN KEY (IdEmpresa) REFERENCES dbo.Empresas (Id),
        CONSTRAINT FK_Integraciones_Usuario FOREIGN KEY (IdUsuarioTecnico) REFERENCES dbo.Usuarios (Id)
    );
    CREATE INDEX IX_Integraciones_Empresa ON dbo.Integraciones (IdEmpresa);
END
GO

IF OBJECT_ID('dbo.IntegracionesIdempotencia', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegracionesIdempotencia
    (
        IdIntegracion INT           NOT NULL,
        Clave         NVARCHAR(100) NOT NULL,
        IdLote        BIGINT        NOT NULL,
        Fecha         DATETIME      NOT NULL CONSTRAINT DF_IntegracionesIdem_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_IntegracionesIdempotencia PRIMARY KEY (IdIntegracion, Clave),
        CONSTRAINT FK_IntegracionesIdem_Integracion FOREIGN KEY (IdIntegracion) REFERENCES dbo.Integraciones (Id)
    );
END
GO

IF COL_LENGTH('dbo.CarguesLotes', 'Origen') IS NULL
    ALTER TABLE dbo.CarguesLotes ADD Origen VARCHAR(10) NOT NULL CONSTRAINT DF_CarguesLotes_Origen DEFAULT ('Excel');
GO
IF COL_LENGTH('dbo.CarguesLotes', 'IdIntegracion') IS NULL
    ALTER TABLE dbo.CarguesLotes ADD IdIntegracion INT NULL;
GO
