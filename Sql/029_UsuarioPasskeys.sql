/* ============================================================================
   029 - PASSKEYS (WebAuthn) como segundo factor adicional
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Requiere haber ejecutado antes el 026 (doble factor con app autenticadora).

   PARTE 1 - Estructura: tabla con las passkeys de cada usuario.
   PARTE 2 - Configuracion: aqui se enciende o apaga el uso de passkeys.
             EDITA LOS VALORES marcados "<-- EDITAR" y ejecuta.
   ============================================================================ */


/* ============================ PARTE 1 - ESTRUCTURA ======================== */

IF OBJECT_ID('dbo.UsuarioPasskeys', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UsuarioPasskeys
    (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UsuarioPasskeys PRIMARY KEY,
        IdUsuario     INT             NOT NULL,
        CredentialId  VARBINARY(512)  NOT NULL,          -- identificador de la credencial (lo entrega el dispositivo)
        ClavePublica  VARBINARY(MAX)  NOT NULL,          -- clave publica en formato COSE
        Algoritmo     INT             NOT NULL,          -- -7 = ES256, -257 = RS256
        ContadorFirmas BIGINT         NOT NULL CONSTRAINT DF_UsuarioPasskeys_Contador DEFAULT (0),
        Nombre        NVARCHAR(100)   NOT NULL,          -- p. ej. "Mi laptop"
        FechaCreacion DATETIME        NOT NULL CONSTRAINT DF_UsuarioPasskeys_Fecha DEFAULT (GETDATE()),
        UltimoUso     DATETIME        NULL
    );
    CREATE UNIQUE INDEX UX_UsuarioPasskeys_Credencial ON dbo.UsuarioPasskeys (CredentialId);
    CREATE INDEX IX_UsuarioPasskeys_Usuario ON dbo.UsuarioPasskeys (IdUsuario);
END
GO


/* ============================ PARTE 2 - CONFIGURACION ===================== */
/*
   @Passkeys : 'true'  = los usuarios pueden registrar y usar passkeys.
               'false' = passkeys apagadas (solo app autenticadora).
   @MaxPorUsuario : cantidad maxima de passkeys por usuario (1 a 20; por defecto 5).
   @RpId     : dominio del sitio SIN https ni puerto, p. ej. 'customers.bufins.com'.
               Dejalo vacio ('') para usar automaticamente el dominio de la peticion.
               IMPORTANTE: si el dominio cambia, las passkeys ya registradas dejan de servir.
*/
DECLARE @Passkeys VARCHAR(10)  = 'false';   -- <-- EDITAR: 'true' o 'false'
DECLARE @MaxPorUsuario VARCHAR(5) = '5';    -- <-- EDITAR (opcional): 1 a 20
DECLARE @RpId     VARCHAR(200) = '';        -- <-- EDITAR (opcional)

IF EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'PasskeysHabilitado')
    UPDATE dbo.ConfiguracionSistema SET Valor = @Passkeys WHERE Clave = 'PasskeysHabilitado';
ELSE
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('PasskeysHabilitado', @Passkeys, 'Passkeys (WebAuthn): true = permitidas, false = apagadas');

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'PasskeysMaximoPorUsuario')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('PasskeysMaximoPorUsuario', @MaxPorUsuario, 'Passkeys: cantidad maxima por usuario (1 a 20)');
ELSE IF @MaxPorUsuario <> '5'
    UPDATE dbo.ConfiguracionSistema SET Valor = @MaxPorUsuario WHERE Clave = 'PasskeysMaximoPorUsuario';

IF @RpId <> ''
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'WebAuthnRpId')
        UPDATE dbo.ConfiguracionSistema SET Valor = @RpId WHERE Clave = 'WebAuthnRpId';
    ELSE
        INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
        VALUES ('WebAuthnRpId', @RpId, 'Passkeys: dominio (RP ID) del sitio, sin https ni puerto');
END

-- Clasifica los parametros en el tipo "Seguridad" (si ya corriste el script 028)
IF OBJECT_ID('dbo.ConfiguracionSistemaTipos', 'U') IS NOT NULL AND COL_LENGTH('dbo.ConfiguracionSistema', 'IdTipo') IS NOT NULL
    EXEC('UPDATE c SET c.IdTipo = t.Id
            FROM dbo.ConfiguracionSistema c
            JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N''Seguridad''
           WHERE c.IdTipo IS NULL AND (c.Clave = ''PasskeysHabilitado'' OR c.Clave = ''WebAuthnRpId'' OR c.Clave = ''PasskeysMaximoPorUsuario'')');

SELECT Clave, Valor, Descripcion FROM dbo.ConfiguracionSistema WHERE Clave IN ('PasskeysHabilitado', 'WebAuthnRpId', 'PasskeysMaximoPorUsuario');
GO
