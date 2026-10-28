-- =====================================================================================================================
-- 040 · Comentarios de gestión de los informes
-- Tabla dbo.ComentariosGestion: notas que los usuarios de una empresa dejan sobre el cierre (año/mes) de un informe.
-- Script MANUAL (compatible con SQL Server 2012). Sin la tabla los informes siguen funcionando: la tarjeta de comentarios
-- avisa que no está disponible y no guarda nada.
-- =====================================================================================================================
IF OBJECT_ID('dbo.ComentariosGestion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComentariosGestion
    (
        Id             BIGINT         IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComentariosGestion PRIMARY KEY,
        IdEmpresa      INT            NOT NULL,
        Informe        VARCHAR(40)    NOT NULL,   -- LineasNegocio | PYG | Balance | Tesoreria | FlujoCaja | FlujoEfectivo | Indicadores | GerenciaMensual
        Anio           INT            NOT NULL,   -- año del cierre al que se refiere
        Mes            INT            NOT NULL,   -- mes del cierre (1-12)
        Clave          NVARCHAR(150)  NULL,       -- opcional: a qué se refiere (p. ej. una línea de negocio); NULL = comentario general
        Texto          NVARCHAR(1000) NOT NULL,
        IdUsuario      INT            NOT NULL,
        NombreUsuario  NVARCHAR(150)  NOT NULL,
        FechaCreacion  DATETIME       NOT NULL CONSTRAINT DF_ComentariosGestion_Fecha DEFAULT (GETDATE()),
        FechaEdicion   DATETIME       NULL,
        Activo         BIT            NOT NULL CONSTRAINT DF_ComentariosGestion_Activo DEFAULT (1),
        CONSTRAINT CK_ComentariosGestion_Mes CHECK (Mes BETWEEN 1 AND 12)
    );

    CREATE INDEX IX_ComentariosGestion_Cierre ON dbo.ComentariosGestion (IdEmpresa, Anio, Mes) INCLUDE (Informe, Activo);
END
GO
