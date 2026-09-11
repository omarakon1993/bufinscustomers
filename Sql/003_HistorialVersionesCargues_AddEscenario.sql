/*
    003_HistorialVersionesCargues_AddEscenario.sql
    Agrega IdEscenario a HistorialVersionesCargues (SnapshotsCargues NO cambia: hereda el
    escenario vía IdHistorial -> HistorialVersionesCargues.Id).

    Requiere haber corrido antes 001_Escenarios_CreateTable.sql.
    Seguro de re-ejecutar.
*/
USE [bufinscustomers]
GO

IF OBJECT_ID('dbo.HistorialVersionesCargues', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.HistorialVersionesCargues') AND name = 'IdEscenario'
    )
        ALTER TABLE dbo.HistorialVersionesCargues ADD IdEscenario TINYINT NOT NULL DEFAULT (1);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_HistorialVersionesCargues_Escenario')
        ALTER TABLE dbo.HistorialVersionesCargues
            ADD CONSTRAINT FK_HistorialVersionesCargues_Escenario
            FOREIGN KEY (IdEscenario) REFERENCES dbo.Escenarios(Id);
END
GO
