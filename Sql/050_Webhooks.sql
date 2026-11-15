-- =====================================================================================
-- 050 · API de ingesta (fase 2): webhook saliente
-- Compatible con SQL Server 2012. MANUAL: ejecutar despues del 046 (es idempotente).
--
--  dbo.Integraciones.WebhookUrl       URL https a la que BUFINS avisa los eventos de la integracion (NULL = sin webhook).
--  dbo.Integraciones.WebhookSecreto   Secreto con el que se firma cada aviso (HMAC-SHA256). Se guarda CIFRADO (enc:v1:, MachineKey);
--                                     se necesita en claro para firmar, por eso no es un hash. Se muestra una sola vez.
--  dbo.IntegracionesWebhookEntregas   Un aviso por fila: payload, estado (Pendiente | Entregado | Fallido), intentos y ultimo error.
--                                     Hasta 3 intentos (inmediato, +30 s, +5 min); lo que quede pendiente se reintenta en la siguiente
--                                     actividad de la API (el hosting no permite tareas programadas). Se conservan 30 dias.
-- =====================================================================================

IF COL_LENGTH('dbo.Integraciones', 'WebhookUrl') IS NULL
    ALTER TABLE dbo.Integraciones ADD WebhookUrl NVARCHAR(500) NULL;
GO
IF COL_LENGTH('dbo.Integraciones', 'WebhookSecreto') IS NULL
    ALTER TABLE dbo.Integraciones ADD WebhookSecreto NVARCHAR(600) NULL;
GO

IF OBJECT_ID('dbo.IntegracionesWebhookEntregas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegracionesWebhookEntregas
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegracionesWebhookEntregas PRIMARY KEY,
        IdIntegracion       INT              NOT NULL,
        IdEvento            UNIQUEIDENTIFIER NOT NULL,
        Evento              VARCHAR(40)      NOT NULL,
        Payload             NVARCHAR(MAX)    NOT NULL,
        Estado              VARCHAR(12)      NOT NULL,
        Intentos            INT              NOT NULL CONSTRAINT DF_IWE_Intentos DEFAULT (0),
        UltimoCodigo        INT              NULL,
        UltimoError         NVARCHAR(300)    NULL,
        Fecha               DATETIME         NOT NULL CONSTRAINT DF_IWE_Fecha DEFAULT (GETDATE()),
        FechaProximoIntento DATETIME         NULL,
        FechaEntrega        DATETIME         NULL,
        CONSTRAINT FK_IWE_Integracion FOREIGN KEY (IdIntegracion) REFERENCES dbo.Integraciones (Id)
    );
    CREATE INDEX IX_IWE_Integracion ON dbo.IntegracionesWebhookEntregas (IdIntegracion, Id DESC);
    CREATE INDEX IX_IWE_Pendientes  ON dbo.IntegracionesWebhookEntregas (Estado, FechaProximoIntento);
END
GO
