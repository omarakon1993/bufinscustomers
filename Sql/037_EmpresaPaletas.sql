/* =====================================================================================
   037_EmpresaPaletas.sql  (SCRIPT MANUAL)
   Colores corporativos de cada empresa: hasta 2 paletas por empresa (Slot 1 y 2).
   Hoy los usan los informes (selector «Paleta de colores del informe») y quedan
   registrados para usos futuros (documentos, correos, etc.).

   - Tabla aparte (no toca dbo.Empresas ni sus SP).
   - Compatible con SQL Server 2012 (sin CREATE OR ALTER / DROP IF EXISTS).
   - Idempotente: se puede ejecutar mas de una vez.
   - Sin este script la aplicacion sigue funcionando: los informes ofrecen solo las paletas
     predeterminadas y el modal «Colores corporativos» avisa que falta ejecutarlo.
   ===================================================================================== */

IF OBJECT_ID('dbo.EmpresaPaletas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmpresaPaletas
    (
        IdEmpresa            INT           NOT NULL,
        Slot                 TINYINT       NOT NULL,            -- 1 o 2
        Nombre               NVARCHAR(60)  NULL,                -- opcional; si es NULL la app muestra «{Empresa} 1/2»
        Primario             CHAR(7)       NOT NULL,            -- #RRGGBB
        Secundario           CHAR(7)       NOT NULL,
        Acento               CHAR(7)       NULL,
        FechaActualizacion   DATETIME      NOT NULL CONSTRAINT DF_EmpresaPaletas_Fecha DEFAULT (GETDATE()),
        IdUsuarioActualizo   INT           NULL,
        CONSTRAINT PK_EmpresaPaletas PRIMARY KEY CLUSTERED (IdEmpresa, Slot),
        CONSTRAINT CK_EmpresaPaletas_Slot CHECK (Slot IN (1, 2)),
        CONSTRAINT FK_EmpresaPaletas_Empresas FOREIGN KEY (IdEmpresa) REFERENCES dbo.Empresas (EmpId) ON DELETE CASCADE
    );
END
GO
