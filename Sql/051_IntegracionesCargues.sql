-- =====================================================================================
-- 051 · Panel de estado de las integraciones: historial de envios
-- Compatible con SQL Server 2012. MANUAL: ejecutar despues del 046 (es idempotente).
--
--  dbo.IntegracionesCargues   Una fila por cada cargue que una integracion envio por la API (Excel o por hojas): que llego, con
--                             cuantas filas/errores, y como termino (confirmado / descartado / simulado en pruebas, por una persona o
--                             por la integracion). Existe porque dbo.CarguesLotes borra los cargues confirmados a los pocos minutos y
--                             por eso no sirve de historial. Se conservan 90 dias.
--  Resolucion NULL = aun sin resolver: si su lote todavia existe esta esperando revision; si ya no existe, vencio o lo reemplazo otro envio.
-- =====================================================================================

IF OBJECT_ID('dbo.IntegracionesCargues', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegracionesCargues
    (
        IdLote          BIGINT        NOT NULL CONSTRAINT PK_IntegracionesCargues PRIMARY KEY,
        IdIntegracion   INT           NOT NULL,
        Anio            INT           NOT NULL,
        Modo            VARCHAR(10)   NOT NULL,
        IdEscenario     INT           NOT NULL,
        Archivo         NVARCHAR(260) NULL,
        Filas           INT           NOT NULL CONSTRAINT DF_IC_Filas DEFAULT (0),
        Errores         INT           NOT NULL CONSTRAINT DF_IC_Errores DEFAULT (0),
        Advertencias    INT           NOT NULL CONSTRAINT DF_IC_Adv DEFAULT (0),
        EstadoRecibido  VARCHAR(20)   NOT NULL,
        Recibido        DATETIME      NOT NULL CONSTRAINT DF_IC_Recibido DEFAULT (GETDATE()),
        Resolucion      VARCHAR(12)   NULL,            -- Confirmado | Descartado | Simulado
        FechaResolucion DATETIME      NULL,
        ResueltoPor     VARCHAR(12)   NULL,            -- persona | integracion
        CONSTRAINT FK_IC_Integracion FOREIGN KEY (IdIntegracion) REFERENCES dbo.Integraciones (Id)
    );
    CREATE INDEX IX_IC_Integracion ON dbo.IntegracionesCargues (IdIntegracion, Recibido DESC);
END
GO
