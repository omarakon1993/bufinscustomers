-- =====================================================================================================================
-- 042 · Umbrales del semáforo del Informe de indicadores, por empresa
-- Tabla dbo.IndicadoresUmbrales: cortes propios de una empresa para un indicador (verde / amarillo / naranja). Sin fila para un
-- indicador, la empresa usa los cortes generales del catálogo (IndicadoresCatalogo.cs). Los edita el Super Admin o el Admin de
-- Empresa desde el botón «Umbrales» del informe.
-- Script MANUAL (compatible con SQL Server 2012). Sin la tabla el informe sigue funcionando con los cortes generales y el botón
-- «Umbrales» avisa que no está disponible.
-- =====================================================================================================================
IF OBJECT_ID('dbo.IndicadoresUmbrales', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IndicadoresUmbrales
    (
        IdEmpresa  INT           NOT NULL,
        Codigo     VARCHAR(60)   NOT NULL,   -- código del indicador (IndicadoresCatalogo.Todos)
        Corte1     DECIMAL(18,6) NOT NULL,   -- verde
        Corte2     DECIMAL(18,6) NOT NULL,   -- amarillo
        Corte3     DECIMAL(18,6) NOT NULL,   -- naranja (por debajo/encima = rojo según la dirección del indicador)
        IdUsuario  INT           NOT NULL,
        Fecha      DATETIME      NOT NULL CONSTRAINT DF_IndicadoresUmbrales_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_IndicadoresUmbrales PRIMARY KEY (IdEmpresa, Codigo)
    );
END
GO
