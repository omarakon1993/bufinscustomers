/* =====================================================================================
   036_EmpresaLogos.sql  (SCRIPT MANUAL)
   Logo de cada empresa (gestor de Empresas) para los informes y sus exportaciones.

   - Tabla aparte (no toca dbo.Empresas ni sus SP): una fila por empresa, con el logo YA
     NORMALIZADO por la aplicacion (PNG, maximo 600x200 px, sin metadatos).
   - Compatible con SQL Server 2012 (sin CREATE OR ALTER / DROP IF EXISTS).
   - Idempotente: se puede ejecutar mas de una vez.
   - Sin este script la aplicacion sigue funcionando: el gestor avisa que falta y los
     informes simplemente no muestran logo de la empresa.
   ===================================================================================== */

IF OBJECT_ID('dbo.EmpresaLogos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmpresaLogos
    (
        IdEmpresa            INT            NOT NULL,
        Imagen               VARBINARY(MAX) NOT NULL,           -- PNG normalizado
        TipoImagen           VARCHAR(50)    NOT NULL CONSTRAINT DF_EmpresaLogos_Tipo DEFAULT ('image/png'),
        Ancho                INT            NOT NULL,           -- px de la imagen guardada
        Alto                 INT            NOT NULL,
        Bytes                INT            NOT NULL,
        Version              VARCHAR(16)    NOT NULL,           -- hash corto: cambia si cambia la imagen (cache del navegador)
        NombreOriginal       NVARCHAR(260)  NULL,
        FechaActualizacion   DATETIME       NOT NULL CONSTRAINT DF_EmpresaLogos_Fecha DEFAULT (GETDATE()),
        IdUsuarioActualizo   INT            NULL,
        CONSTRAINT PK_EmpresaLogos PRIMARY KEY CLUSTERED (IdEmpresa),
        CONSTRAINT FK_EmpresaLogos_Empresas FOREIGN KEY (IdEmpresa) REFERENCES dbo.Empresas (EmpId) ON DELETE CASCADE
    );
END
GO
