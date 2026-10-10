/* =====================================================================================
   020 · Personalizaciones por empresa + hoja Z_HistPrecios_Churido
   -------------------------------------------------------------------------------------
   Compatible con SQL Server 2012 (nivel 110). Re-ejecutable (idempotente).

   1. dbo.EmpresaPersonalizaciones  → qué "paquete de personalización" tiene activo cada empresa.
      Los paquetes se definen en código (Helpers/PersonalizacionesEmpresaHelper.cs: plantilla
      propia + hojas Z_ adicionales → tabla Ini_). Aquí solo se ASIGNAN a empresas, así que
      habilitar un paquete existente para otra empresa es un INSERT, sin desplegar.
   2. Asignación del paquete CHURIDO_HIST_PRECIOS a Churido (EmpId 6) y Ucrania (EmpId 8).
   3. dbo.Ini_HistPrecios_Churido + su espejo dbo.Staging_Ini_HistPrecios_Churido
      (misma convención que las demás Ini_/Staging_Ini_: columnas _Log, IdEscenario, IdLote).
   4. sp_ConfirmarCargueStaging: la lista de tablas deja de estar fija y se arma con todas
      las Staging_Ini_X que tengan su Ini_X. Así cualquier hoja personalizada futura se
      confirma sin volver a tocar el SP (cada lote solo tiene filas en las tablas que cargó).
   ===================================================================================== */

SET NOCOUNT ON;

/* ── 1. Catálogo de asignaciones ───────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.EmpresaPersonalizaciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmpresaPersonalizaciones (
        IdEmpresa      INT          NOT NULL,
        Codigo         VARCHAR(50)  NOT NULL,
        Activo         BIT          NOT NULL CONSTRAINT DF_EmpresaPersonalizaciones_Activo DEFAULT (1),
        FechaCreacion  DATETIME     NOT NULL CONSTRAINT DF_EmpresaPersonalizaciones_Fecha  DEFAULT (GETDATE()),
        CONSTRAINT PK_EmpresaPersonalizaciones PRIMARY KEY (IdEmpresa, Codigo),
        CONSTRAINT FK_EmpresaPersonalizaciones_Empresa FOREIGN KEY (IdEmpresa) REFERENCES dbo.Empresas (EmpId)
    );
END
GO

/* ── 2. Churido (6) y Ucrania (8) → paquete CHURIDO_HIST_PRECIOS ──────────────────── */
INSERT INTO dbo.EmpresaPersonalizaciones (IdEmpresa, Codigo)
SELECT e.EmpId, 'CHURIDO_HIST_PRECIOS'
FROM dbo.Empresas e
WHERE e.EmpId IN (6, 8)
  AND NOT EXISTS (SELECT 1 FROM dbo.EmpresaPersonalizaciones p
                  WHERE p.IdEmpresa = e.EmpId AND p.Codigo = 'CHURIDO_HIST_PRECIOS');
GO

/* ── 3a. Tabla real ────────────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.Ini_HistPrecios_Churido', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Ini_HistPrecios_Churido (
        Empresa                 VARCHAR(100)  NULL,
        TipoFruta               VARCHAR(200)  NULL,
        [Año]                   VARCHAR(10)   NULL,
        Mes                     VARCHAR(10)   NULL,
        Valor                   MONEY         NULL,
        Llave                   VARCHAR(500)  NULL,
        IdEmpresa_Log           INT           NULL,
        IdUsuarioCargue_Log     INT           NULL,
        FechaCargue_Log         DATETIME      NULL,
        Historico_Log           TINYINT       NOT NULL CONSTRAINT DF_Ini_HistPrecios_Churido_Historico DEFAULT (0),
        IdUsuarioEjecucion_Log  INT           NULL,
        FechaEjecucion_Log      DATETIME      NULL,
        Observacion_Log         VARCHAR(500)  NULL,
        IdEscenario             TINYINT       NOT NULL CONSTRAINT DF_Ini_HistPrecios_Churido_Escenario DEFAULT (1),
        CONSTRAINT FK_Ini_HistPrecios_Churido_Escenario FOREIGN KEY (IdEscenario) REFERENCES dbo.Escenarios (Id)
    );

    CREATE NONCLUSTERED INDEX IX_Ini_HistPrecios_Churido_Empresa_Anio_Escenario
        ON dbo.Ini_HistPrecios_Churido (IdEmpresa_Log, [Año], IdEscenario, Historico_Log);
END
GO

/* ── 3b. Espejo de staging (sin las 3 columnas de ejecución; + IdLote/NumeroFilaExcel) ── */
IF OBJECT_ID('dbo.Staging_Ini_HistPrecios_Churido', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Staging_Ini_HistPrecios_Churido (
        Empresa              VARCHAR(100)  NULL,
        TipoFruta            VARCHAR(200)  NULL,
        [Año]                VARCHAR(10)   NULL,
        Mes                  VARCHAR(10)   NULL,
        Valor                MONEY         NULL,
        Llave                VARCHAR(500)  NULL,
        IdEmpresa_Log        INT           NULL,
        IdUsuarioCargue_Log  INT           NULL,
        FechaCargue_Log      DATETIME      NULL,
        Historico_Log        TINYINT       NOT NULL,
        IdEscenario          TINYINT       NOT NULL,
        IdLote               BIGINT        NOT NULL CONSTRAINT DF_Staging_Ini_HistPrecios_Churido_IdLote DEFAULT (0),
        NumeroFilaExcel      INT           NULL,
        CONSTRAINT FK_Staging_Ini_HistPrecios_Churido_Lote FOREIGN KEY (IdLote)
            REFERENCES dbo.CarguesLotes (IdLote) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_Staging_Ini_HistPrecios_Churido_Lote
        ON dbo.Staging_Ini_HistPrecios_Churido (IdLote);
END
GO

/* ── 4. sp_ConfirmarCargueStaging con lista de tablas dinámica ─────────────────────── */
IF OBJECT_ID('dbo.sp_ConfirmarCargueStaging', 'P') IS NULL
    EXEC ('CREATE PROCEDURE dbo.sp_ConfirmarCargueStaging AS SELECT 1;');
GO

ALTER PROCEDURE dbo.sp_ConfirmarCargueStaging
    @IdLote               BIGINT,
    @IdHistorialGenerado  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        -- ────────────────────────────────────────────────────────────────────
        -- Bloque 1 · Guardas: el lote debe existir y estar en un estado confirmable
        -- ────────────────────────────────────────────────────────────────────
        DECLARE @Estado VARCHAR(20);
        SELECT @Estado = Estado FROM dbo.CarguesLotes WHERE IdLote = @IdLote;

        IF @Estado IS NULL
        BEGIN
            SELECT 0 AS CodMessage, N'El lote de cargue indicado no existe.' AS ErrorMessage;
            RETURN;
        END

        IF @Estado NOT IN ('ValidadoOk', 'ConAdvertencias')
        BEGIN
            SELECT 0 AS CodMessage, N'El lote no está en un estado confirmable (estado actual: ' + @Estado + N').' AS ErrorMessage;
            RETURN;
        END

        -- ────────────────────────────────────────────────────────────────────
        -- Bloque 2 · Por cada tabla Ini_x que tenga espejo Staging_Ini_x: INSERT...SELECT
        -- del lote, con la lista de columnas armada por intersección de esquemas.
        -- La lista de tablas es DINÁMICA (todas las Staging_Ini_* con su Ini_*): las
        -- hojas personalizadas por empresa (p. ej. Ini_HistPrecios_Churido) entran solas.
        -- Como todo se filtra por IdLote, un lote solo mueve las tablas que cargó.
        -- ────────────────────────────────────────────────────────────────────
        DECLARE @tablas TABLE (NombreIni SYSNAME);
        INSERT INTO @tablas (NombreIni)
        SELECT SUBSTRING(t.name, LEN('Staging_') + 1, 200)
        FROM sys.tables t
        WHERE t.schema_id = SCHEMA_ID('dbo')
          AND t.name LIKE 'Staging[_]Ini[_]%'
          AND OBJECT_ID('dbo.' + QUOTENAME(SUBSTRING(t.name, LEN('Staging_') + 1, 200)), 'U') IS NOT NULL;

        DECLARE @nombreIni SYSNAME, @nombreStaging SYSNAME, @cols NVARCHAR(MAX), @sql NVARCHAR(MAX);

        DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT NombreIni FROM @tablas ORDER BY NombreIni;
        OPEN cur;
        FETCH NEXT FROM cur INTO @nombreIni;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @nombreStaging = N'Staging_' + @nombreIni;
            SET @cols = NULL;

            -- FOR XML PATH + STUFF en vez de STRING_AGG (no disponible en SQL Server 2012).
            SELECT @cols = STUFF((
                SELECT ',' + QUOTENAME(ic.COLUMN_NAME)
                FROM INFORMATION_SCHEMA.COLUMNS ic
                WHERE ic.TABLE_NAME = @nombreIni
                  AND EXISTS (
                        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS sc
                        WHERE sc.TABLE_NAME = @nombreStaging AND sc.COLUMN_NAME = ic.COLUMN_NAME
                      )
                ORDER BY ic.ORDINAL_POSITION
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'), 1, 1, '');

            IF @cols IS NOT NULL
            BEGIN
                SET @sql = N'INSERT INTO dbo.' + QUOTENAME(@nombreIni) + N' (' + @cols + N') ' +
                           N'SELECT ' + @cols + N' FROM dbo.' + QUOTENAME(@nombreStaging) + N' WHERE IdLote = @IdLoteParam;';
                EXEC sp_executesql @sql, N'@IdLoteParam BIGINT', @IdLoteParam = @IdLote;
            END

            FETCH NEXT FROM cur INTO @nombreIni;
        END
        CLOSE cur;
        DEALLOCATE cur;

        -- ────────────────────────────────────────────────────────────────────
        -- Bloque 3 · Marcar el lote como confirmado
        -- ────────────────────────────────────────────────────────────────────
        UPDATE dbo.CarguesLotes
        SET Estado = 'Confirmado',
            FechaConfirmacion = GETDATE(),
            IdHistorialGenerado = @IdHistorialGenerado
        WHERE IdLote = @IdLote;

        SELECT 1 AS CodMessage, NULL AS ErrorMessage;
    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
