/* =====================================================================================
   021 · Gestor de hojas de cargue (catálogo de hojas Z_ → tablas Ini_ en BD)
   -------------------------------------------------------------------------------------
   Compatible con SQL Server 2012 (nivel 110). Re-ejecutable (idempotente).

   Reemplaza el catálogo que vivía en código (TablasCargueHelper.MapeoZaIni +
   PersonalizacionesEmpresaHelper.Catalogo) por dos tablas que administra el
   Gestor de hojas de cargue (/GestorHojasCargue, Super Admin):

   1. dbo.CargueHojas          → una fila por hoja del Excel (Z_X) con su tabla destino (Ini_X),
                                 alcance ESTANDAR (todas las empresas) o PERSONALIZADA, activo y orden.
   2. dbo.CargueHojasEmpresas  → empresas a las que aplica cada hoja PERSONALIZADA.

   Las columnas de cada hoja NO se guardan aquí: la fuente de verdad es el esquema físico de
   dbo.Ini_X / dbo.Staging_Ini_X (INFORMATION_SCHEMA), igual que ya hace el cargue.

   3. Seed con el estado actual: las 9 hojas estándar + Z_HistPrecios_Churido (personalizada),
      migrando sus empresas desde dbo.EmpresaPersonalizaciones (que se conserva, sin uso).
      FechaCreacion = 1900-01-01 en las estándar y la fecha real de la personalización en la de
      Churido, para que el rollback de versiones anteriores siga exigiendo exactamente las mismas
      tablas que antes (las estándar sí; la de Churido solo en versiones posteriores a ella).

   Sin este script la aplicación funciona igual que antes (usa el catálogo de respaldo en código).
   ===================================================================================== */

SET NOCOUNT ON;

/* ── 1. Catálogo de hojas ──────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.CargueHojas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargueHojas (
        IdHoja                 INT            IDENTITY(1,1) NOT NULL,
        NombreHoja             VARCHAR(100)   NOT NULL,
        TablaIni               VARCHAR(120)   NOT NULL,
        Descripcion            NVARCHAR(500)  NULL,
        Alcance                VARCHAR(20)    NOT NULL CONSTRAINT DF_CargueHojas_Alcance DEFAULT ('ESTANDAR'),
        Activo                 BIT            NOT NULL CONSTRAINT DF_CargueHojas_Activo  DEFAULT (1),
        Orden                  INT            NOT NULL CONSTRAINT DF_CargueHojas_Orden   DEFAULT (100),
        Origen                 VARCHAR(20)    NOT NULL CONSTRAINT DF_CargueHojas_Origen  DEFAULT ('GESTOR'),
        FechaCreacion          DATETIME       NOT NULL CONSTRAINT DF_CargueHojas_Fecha   DEFAULT (GETDATE()),
        -- Desde cuándo la hoja aplica con su alcance actual (se renueva al activarla o volverla
        -- ESTANDAR). El rollback solo exige el snapshot de la tabla a versiones posteriores.
        VigenteDesde           DATETIME       NOT NULL CONSTRAINT DF_CargueHojas_Vigente DEFAULT (GETDATE()),
        IdUsuarioCreacion      INT            NULL,
        FechaModificacion      DATETIME       NULL,
        IdUsuarioModificacion  INT            NULL,
        CONSTRAINT PK_CargueHojas PRIMARY KEY (IdHoja),
        CONSTRAINT UX_CargueHojas_NombreHoja UNIQUE (NombreHoja),
        CONSTRAINT UX_CargueHojas_TablaIni   UNIQUE (TablaIni),
        CONSTRAINT CK_CargueHojas_Alcance    CHECK (Alcance IN ('ESTANDAR', 'PERSONALIZADA')),
        CONSTRAINT CK_CargueHojas_Origen     CHECK (Origen IN ('EXISTENTE', 'GESTOR')),
        CONSTRAINT CK_CargueHojas_Prefijos   CHECK (NombreHoja LIKE 'Z[_]%' AND TablaIni LIKE 'Ini[_]%')
    );
END
GO

/* ── 2. Empresas de cada hoja personalizada ────────────────────────────────────────── */
IF OBJECT_ID('dbo.CargueHojasEmpresas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargueHojasEmpresas (
        IdHoja           INT       NOT NULL,
        IdEmpresa        INT       NOT NULL,
        Activo           BIT       NOT NULL CONSTRAINT DF_CargueHojasEmpresas_Activo DEFAULT (1),
        FechaAsignacion  DATETIME  NOT NULL CONSTRAINT DF_CargueHojasEmpresas_Fecha  DEFAULT (GETDATE()),
        CONSTRAINT PK_CargueHojasEmpresas PRIMARY KEY (IdHoja, IdEmpresa),
        CONSTRAINT FK_CargueHojasEmpresas_Hoja    FOREIGN KEY (IdHoja)    REFERENCES dbo.CargueHojas (IdHoja) ON DELETE CASCADE,
        CONSTRAINT FK_CargueHojasEmpresas_Empresa FOREIGN KEY (IdEmpresa) REFERENCES dbo.Empresas (EmpId)
    );
END
GO

/* ── 3a. Seed: hojas actuales (orden = el de la plantilla PlantillaBUFINS.xlsx) ────── */
DECLARE @seed TABLE (NombreHoja VARCHAR(100), TablaIni VARCHAR(120), Alcance VARCHAR(20), Orden INT, Descripcion NVARCHAR(500));
INSERT INTO @seed VALUES
    ('Z_BalancePrueba',             'Ini_BalancePrueba',         'ESTANDAR',      10, N'Balance de prueba'),
    ('Z_CteYnoCte',                 'Ini_CteYnoCte',             'ESTANDAR',      20, N'Corriente y no corriente'),
    ('Z_PYGDetallado',              'Ini_PYG',                   'ESTANDAR',      30, N'PYG detallado'),
    ('Z_PYGDetalladoConAjuste',     'Ini_PYGDetalladoConAjuste', 'ESTANDAR',      40, N'PYG detallado con ajuste'),
    ('Z_PresupuestoBalance',        'Ini_PresupuestoBalance',    'ESTANDAR',      50, N'Presupuesto de balance'),
    ('Z_PptoPYGDetallado',          'Ini_PptoPYG',               'ESTANDAR',      60, N'Presupuesto PYG detallado'),
    ('Z_PptoPYGDetalladoConAjuste', 'Ini_PptoPYGConAjuste',      'ESTANDAR',      70, N'Presupuesto PYG detallado con ajuste'),
    ('Z_PCH',                       'Ini_PCH',                   'ESTANDAR',      80, N'PCH'),
    ('Z_EjecPCH',                   'Ini_EjecPCH',               'ESTANDAR',      90, N'Ejecución PCH'),
    ('Z_HistPrecios_Churido',       'Ini_HistPrecios_Churido',   'PERSONALIZADA', 100, N'Histórico de precios por tipo de fruta');

-- Fecha real de la personalización de Churido: las versiones del historial anteriores a ella no
-- traen su snapshot y el rollback no debe exigirlo (mismo comportamiento que antes del gestor).
DECLARE @fechaChurido DATETIME = '19000101';
IF OBJECT_ID('dbo.EmpresaPersonalizaciones', 'U') IS NOT NULL
    SELECT @fechaChurido = ISNULL(MIN(FechaCreacion), @fechaChurido)
    FROM dbo.EmpresaPersonalizaciones WHERE Codigo = 'CHURIDO_HIST_PRECIOS';

INSERT INTO dbo.CargueHojas (NombreHoja, TablaIni, Descripcion, Alcance, Activo, Orden, Origen, FechaCreacion, VigenteDesde)
SELECT s.NombreHoja, s.TablaIni, s.Descripcion, s.Alcance, 1, s.Orden, 'EXISTENTE',
       CASE WHEN s.Alcance = 'PERSONALIZADA' THEN @fechaChurido ELSE '19000101' END,
       CASE WHEN s.Alcance = 'PERSONALIZADA' THEN @fechaChurido ELSE '19000101' END
FROM @seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.CargueHojas h WHERE h.NombreHoja = s.NombreHoja);
GO

/* ── 3b. Seed: empresas de Z_HistPrecios_Churido desde dbo.EmpresaPersonalizaciones ── */
IF OBJECT_ID('dbo.EmpresaPersonalizaciones', 'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.CargueHojasEmpresas (IdHoja, IdEmpresa, Activo, FechaAsignacion)
    SELECT h.IdHoja, p.IdEmpresa, 1, p.FechaCreacion
    FROM dbo.EmpresaPersonalizaciones p
    INNER JOIN dbo.CargueHojas h ON h.NombreHoja = 'Z_HistPrecios_Churido'
    WHERE p.Codigo = 'CHURIDO_HIST_PRECIOS'
      AND p.Activo = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.CargueHojasEmpresas x WHERE x.IdHoja = h.IdHoja AND x.IdEmpresa = p.IdEmpresa);
END
GO

/* ── 4. Opción de menú (Super Admin), junto al gestor de Escenarios ────────────────── */
IF OBJECT_ID('dbo.MenuOpciones', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_CONFIG_HOJAS_CARGUE')
BEGIN
    INSERT INTO dbo.MenuOpciones (Codigo, Nombre, Descripcion, Icono, Orden, Activo, Controller, Action,
                                  OrdenCategoria, SoloSuperAdmin, SoloAdminEmpresa, IdGrupo, NombreEN, IdCategoria, EsDestacado)
    SELECT 'ADMIN_CONFIG_HOJAS_CARGUE', N'Gestor de hojas de cargue',
           N'Hojas del Excel de cargue, sus tablas Ini_ y a qué empresas aplican',
           'fas fa-file-excel', e.Orden, 1, 'GestorHojasCargue', 'Index',
           e.OrdenCategoria, 1, 0, e.IdGrupo, N'Upload sheets manager', e.IdCategoria, 0
    FROM dbo.MenuOpciones e
    WHERE e.Codigo = 'ADMIN_CONFIG_ESCENARIOS';
END
GO

/* ── Verificación ──────────────────────────────────────────────────────────────────── */
SELECT h.IdHoja, h.NombreHoja, h.TablaIni, h.Alcance, h.Activo, h.Orden,
       (SELECT COUNT(*) FROM dbo.CargueHojasEmpresas e WHERE e.IdHoja = h.IdHoja AND e.Activo = 1) AS Empresas
FROM dbo.CargueHojas h
ORDER BY h.Orden;
