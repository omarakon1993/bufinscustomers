/* ============================================================================
   026 - DOBLE FACTOR DE AUTENTICACION (2FA) - Estructura y configuracion
   ----------------------------------------------------------------------------
   Script MANUAL. Compatible con SQL Server 2012+. Se puede ejecutar varias veces.
   Ejecutar primero en la BD local / de pruebas; en produccion solo cuando las
   pruebas esten OK. Sin este script el login funciona como siempre.

   Contenido:
     PARTE 1 - Estructura (columnas y tabla). No cambia el comportamiento.
     PARTE 2 - Configuracion: aqui se enciende o apaga el 2FA. EDITA LOS VALORES
               de las dos lineas marcadas "<-- EDITAR" y ejecuta.
   ============================================================================ */


/* ============================ PARTE 1 - ESTRUCTURA ======================== */

-- Datos del autenticador de cada usuario
IF COL_LENGTH('dbo.Usuarios', 'TotpActivo') IS NULL
    ALTER TABLE dbo.Usuarios ADD TotpActivo BIT NOT NULL CONSTRAINT DF_Usuarios_TotpActivo DEFAULT (0);
GO
IF COL_LENGTH('dbo.Usuarios', 'TotpSecretoCifrado') IS NULL
    ALTER TABLE dbo.Usuarios ADD TotpSecretoCifrado NVARCHAR(500) NULL;   -- secreto cifrado, nunca en claro
GO
IF COL_LENGTH('dbo.Usuarios', 'TotpActivadoEn') IS NULL
    ALTER TABLE dbo.Usuarios ADD TotpActivadoEn DATETIME NULL;
GO
IF COL_LENGTH('dbo.Usuarios', 'TotpUltimoPaso') IS NULL
    ALTER TABLE dbo.Usuarios ADD TotpUltimoPaso BIGINT NULL;              -- evita reutilizar un mismo codigo
GO

-- Codigos de recuperacion (10 por usuario, un solo uso, guardados con hash)
IF OBJECT_ID('dbo.UsuarioCodigosRecuperacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UsuarioCodigosRecuperacion
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UsuarioCodigosRecuperacion PRIMARY KEY,
        IdUsuario     INT         NOT NULL,
        HashCodigo    VARCHAR(64) NOT NULL,
        UsadoEn       DATETIME    NULL,
        FechaCreacion DATETIME    NOT NULL CONSTRAINT DF_UsuarioCodigosRecuperacion_Fecha DEFAULT (GETDATE())
    );
    CREATE INDEX IX_UsuarioCodigosRecuperacion_Usuario ON dbo.UsuarioCodigosRecuperacion (IdUsuario, HashCodigo);
END
GO


/* ============================ PARTE 2 - CONFIGURACION ===================== */
/*
   @Habilitado : 'true'  = el 2FA se exige.   'false' = login como siempre (apagado).
   @Alcance    : 'superadmin' = solo Super Admin.   'todos' = todos los usuarios.
   El cambio se aplica en maximo 30 segundos, sin reiniciar la aplicacion.
   Por seguridad, los valores por defecto dejan el 2FA APAGADO.
*/
DECLARE @Habilitado VARCHAR(10) = 'false';        -- <-- EDITAR: 'true' o 'false'
DECLARE @Alcance    VARCHAR(20) = 'superadmin';   -- <-- EDITAR: 'superadmin' o 'todos'

IF EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'TwoFactorHabilitado')
    UPDATE dbo.ConfiguracionSistema SET Valor = @Habilitado WHERE Clave = 'TwoFactorHabilitado';
ELSE
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('TwoFactorHabilitado', @Habilitado, 'Doble factor (2FA): true = activo, false = apagado');

IF EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'TwoFactorAlcance')
    UPDATE dbo.ConfiguracionSistema SET Valor = @Alcance WHERE Clave = 'TwoFactorAlcance';
ELSE
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('TwoFactorAlcance', @Alcance, 'Doble factor (2FA): superadmin = solo Super Admin, todos = todos los usuarios');

SELECT Clave, Valor, Descripcion FROM dbo.ConfiguracionSistema WHERE Clave LIKE 'TwoFactor%';
GO
