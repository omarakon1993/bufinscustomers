-- Presupuesto mensual de tokens de IA configurable POR EMPRESA.
-- Solo existe una fila para una empresa cuando un Super Admin fija un override explícito desde
-- Configuración IA → "Por Empresa"; si no hay fila, esa empresa usa el valor global
-- ConfiguracionSistema.IA_TokensMensualesPorEmpresa (0 = ilimitado).
-- PresupuestoTokensMensual = 0 en esta tabla significa "ilimitado para ESTA empresa en particular",
-- distinto de "no hay override" (ausencia de fila).
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ConfiguracionIAEmpresa')
BEGIN
    CREATE TABLE dbo.ConfiguracionIAEmpresa (
        IdEmpresa                INT      NOT NULL PRIMARY KEY,
        PresupuestoTokensMensual BIGINT   NOT NULL,
        FechaActualizacion       DATETIME NOT NULL DEFAULT GETDATE(),
        IdUsuarioActualizacion   INT      NULL,
        CONSTRAINT FK_ConfiguracionIAEmpresa_Empresas FOREIGN KEY (IdEmpresa) REFERENCES dbo.Empresas(EmpId)
    );
END
