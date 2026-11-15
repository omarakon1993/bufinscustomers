-- =====================================================================================
-- 049 · API de ingesta (fase 2): ejecucion de modelos asincrona
-- Compatible con SQL Server 2012. MANUAL: ejecutar despues del 046 (es idempotente).
--
--  dbo.ApiEjecuciones   Una fila por cada «POST /api/v1/modelos/ejecutar». El estado y el detalle por modelo viven en la base
--                       (no en memoria) porque el hosting recicla el proceso: si el sitio se reinicia en pleno calculo, la
--                       ejecucion queda marcada como Interrumpida en vez de perderse sin rastro.
--  Estado: EnCurso | Completada | ConErrores | Interrumpida | Simulada (integraciones de pruebas: no ejecutan nada)
--  Detalle: JSON con una entrada por modelo (modelo, estado, mensaje, segundos).
-- =====================================================================================

IF OBJECT_ID('dbo.ApiEjecuciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiEjecuciones
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApiEjecuciones PRIMARY KEY,
        IdIntegracion INT           NOT NULL,
        IdEmpresa     INT           NOT NULL,
        IdEscenario   INT           NOT NULL CONSTRAINT DF_ApiEjecuciones_Esc DEFAULT (1),
        Estado        VARCHAR(20)   NOT NULL,
        FechaInicio   DATETIME      NOT NULL CONSTRAINT DF_ApiEjecuciones_Inicio DEFAULT (GETDATE()),
        FechaFin      DATETIME      NULL,
        Detalle       NVARCHAR(MAX) NULL,
        CONSTRAINT FK_ApiEjecuciones_Integracion FOREIGN KEY (IdIntegracion) REFERENCES dbo.Integraciones (Id)
    );
    CREATE INDEX IX_ApiEjecuciones_Empresa ON dbo.ApiEjecuciones (IdEmpresa, IdEscenario, Estado);
END
GO
