/*
    001_Escenarios_CreateTable.sql
    Catálogo de escenarios de datos (Escenario 1 = principal, Escenario 2 = paralelo).
    Parametrizable a futuro: agregar un escenario nuevo es un solo INSERT a esta tabla,
    sin cambios de código ni de esquema.

    Ejecutar UNA sola vez. Es seguro re-ejecutar (el IF NOT EXISTS evita duplicados).
*/
USE [bufinscustomers]
GO

IF OBJECT_ID('dbo.Escenarios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Escenarios (
        Id      TINYINT       NOT NULL PRIMARY KEY,
        Nombre  NVARCHAR(100) NOT NULL,
        Orden   INT           NOT NULL DEFAULT 0,
        Activo  BIT           NOT NULL DEFAULT 1
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Escenarios WHERE Id = 1)
    INSERT INTO dbo.Escenarios (Id, Nombre, Orden, Activo) VALUES (1, N'Escenario 1', 1, 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Escenarios WHERE Id = 2)
    INSERT INTO dbo.Escenarios (Id, Nombre, Orden, Activo) VALUES (2, N'Escenario 2', 2, 1);
GO
