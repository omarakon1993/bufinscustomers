/*
    004_AuditoriaCargues_AddEscenario.sql
    Agrega IdEscenario a AuditoriaCargues. DatosController.RegistrarAuditoria ya queda
    escribiendo esta columna en cada cargue; el grid de Auditoría de Cargues (vista +
    sp_ObtenerAuditoriaCarguesPorEmpresa) NO se modifica en esta entrega porque ese SP no
    fue compartido — la columna queda guardada, lista para mostrarse cuando se actualice
    ese procedimiento en una entrega posterior.

    Requiere haber corrido antes 001_Escenarios_CreateTable.sql.
    Seguro de re-ejecutar.
*/
USE [bufinscustomers]
GO

IF OBJECT_ID('dbo.AuditoriaCargues', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.AuditoriaCargues') AND name = 'IdEscenario'
    )
        ALTER TABLE dbo.AuditoriaCargues ADD IdEscenario TINYINT NOT NULL DEFAULT (1);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AuditoriaCargues_Escenario')
        ALTER TABLE dbo.AuditoriaCargues
            ADD CONSTRAINT FK_AuditoriaCargues_Escenario
            FOREIGN KEY (IdEscenario) REFERENCES dbo.Escenarios(Id);
END
GO
