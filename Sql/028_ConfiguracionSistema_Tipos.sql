/* ============================================================================
   028 - Tipos (categorias) de configuracion del sistema
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Crea la tabla de tipos y la columna ConfiguracionSistema.IdTipo, y deja los
   parametros actuales ya clasificados. Los tipos se administran despues desde
   Configuraciones generales BUFINS -> General -> "Tipos".
   Sin este script la pantalla sigue funcionando con las agrupaciones fijas de antes.
   ============================================================================ */

IF OBJECT_ID('dbo.ConfiguracionSistemaTipos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionSistemaTipos
    (
        Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConfiguracionSistemaTipos PRIMARY KEY,
        Nombre   NVARCHAR(100) NOT NULL,                    -- espanol
        NombreEN NVARCHAR(100) NULL,                        -- ingles (si es NULL se usa Nombre)
        Icono    VARCHAR(60)   NOT NULL CONSTRAINT DF_CfgSisTipos_Icono DEFAULT ('fas fa-cog'),
        Orden    INT           NOT NULL CONSTRAINT DF_CfgSisTipos_Orden DEFAULT (100),
        Activo   BIT           NOT NULL CONSTRAINT DF_CfgSisTipos_Activo DEFAULT (1)
    );
END
GO

IF COL_LENGTH('dbo.ConfiguracionSistema', 'IdTipo') IS NULL
    ALTER TABLE dbo.ConfiguracionSistema ADD IdTipo INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ConfiguracionSistema_Tipo')
    ALTER TABLE dbo.ConfiguracionSistema ADD CONSTRAINT FK_ConfiguracionSistema_Tipo
        FOREIGN KEY (IdTipo) REFERENCES dbo.ConfiguracionSistemaTipos (Id);
GO

-- Tipos iniciales (solo si la tabla esta vacia)
IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistemaTipos)
BEGIN
    INSERT INTO dbo.ConfiguracionSistemaTipos (Nombre, NombreEN, Icono, Orden) VALUES
        (N'IA - Proveedor y modelo',   N'AI - Provider and model', 'fas fa-plug',   10),
        (N'IA - Presupuesto y costos', N'AI - Budget and costs',   'fas fa-coins',  20),
        (N'IA - Cache y limites',      N'AI - Cache and limits',   'fas fa-bolt',   30),
        (N'Seguridad',                 N'Security',                'fas fa-shield-alt', 40);
END
GO

-- Clasificacion de los parametros existentes (solo los que aun no tienen tipo)
UPDATE c SET c.IdTipo = t.Id
  FROM dbo.ConfiguracionSistema c
  JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N'IA - Proveedor y modelo'
 WHERE c.IdTipo IS NULL
   AND c.Clave IN ('OpenAIApiKey','OpenAIModel','IA_ModeloEconomico','IA_ModeloSimple','OpenAIMaxTokens','OpenAITemperature');

UPDATE c SET c.IdTipo = t.Id
  FROM dbo.ConfiguracionSistema c
  JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N'IA - Presupuesto y costos'
 WHERE c.IdTipo IS NULL
   AND c.Clave IN ('IA_TokensMensualesPorEmpresa','IA_CostoPor1kTokensPrompt','IA_CostoPor1kTokensRespuesta');

UPDATE c SET c.IdTipo = t.Id
  FROM dbo.ConfiguracionSistema c
  JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N'IA - Cache y limites'
 WHERE c.IdTipo IS NULL
   AND c.Clave IN ('IA_CacheHoras','IA_CacheHorasModelo','IA_MaxCaracteresPregunta');

UPDATE c SET c.IdTipo = t.Id
  FROM dbo.ConfiguracionSistema c
  JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N'Seguridad'
 WHERE c.IdTipo IS NULL
   AND c.Clave LIKE 'TwoFactor%';
GO

SELECT t.Id, t.Nombre, t.NombreEN, t.Orden, COUNT(c.Clave) AS Parametros
  FROM dbo.ConfiguracionSistemaTipos t
  LEFT JOIN dbo.ConfiguracionSistema c ON c.IdTipo = t.Id
 GROUP BY t.Id, t.Nombre, t.NombreEN, t.Orden ORDER BY t.Orden;
GO
