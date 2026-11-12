/* =====================================================================================================
   045 - Reinicios del sitio sin efectos para el usuario (2026-11-12)
   -----------------------------------------------------------------------------------------------------
   Manual. Compatible con SQL Server 2012. Idempotente (se puede ejecutar varias veces).

   1) dbo.DataProtectionKeys: llaves de ASP.NET Core Data Protection (firman los formularios -antiforgery-
      y TempData). Se guardan CIFRADAS con la MachineKey del sitio (secretsSettings.json): la base sola no
      basta para usarlas. Sin esta tabla la aplicación sigue con llaves en memoria (comportamiento anterior).

   2) dbo.SesionesActivas: permite reconstruir la sesión de un usuario después de un reinicio del sitio.
      - HuellaSesion = SHA-256 del identificador de la cookie de sesión (el identificador NUNCA se guarda).
      - HuellaClave  = SHA-256 del hash de contraseña vigente al iniciar sesión: si la clave cambia, la
        sesión ya no se reconstruye.
      - Solo se reconstruye si UltimaActividad está dentro de los 20 minutos de inactividad permitidos.
      Las filas se borran al cerrar sesión o al expirar; la aplicación purga las vencidas cada hora.

   Después de ejecutarlo: reiniciar el sitio (la aplicación comprueba las tablas al arrancar).
   ===================================================================================================== */

IF OBJECT_ID('dbo.DataProtectionKeys', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataProtectionKeys
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataProtectionKeys PRIMARY KEY,
        FriendlyName  NVARCHAR(200)     NULL,
        Xml           NVARCHAR(MAX)     NOT NULL,
        FechaCreacion DATETIME          NOT NULL CONSTRAINT DF_DataProtectionKeys_Fecha DEFAULT (GETDATE())
    );
END
GO

IF OBJECT_ID('dbo.SesionesActivas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SesionesActivas
    (
        HuellaSesion    CHAR(64) NOT NULL CONSTRAINT PK_SesionesActivas PRIMARY KEY,
        IdUsuario       INT      NOT NULL,
        HuellaClave     CHAR(64) NULL,
        FechaLogin      DATETIME NOT NULL,
        UltimaActividad DATETIME NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SesionesActivas_UltimaActividad' AND object_id = OBJECT_ID('dbo.SesionesActivas'))
    CREATE INDEX IX_SesionesActivas_UltimaActividad ON dbo.SesionesActivas (UltimaActividad);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SesionesActivas_IdUsuario' AND object_id = OBJECT_ID('dbo.SesionesActivas'))
    CREATE INDEX IX_SesionesActivas_IdUsuario ON dbo.SesionesActivas (IdUsuario);
GO
