/* =====================================================================
   038_RelBalancePpto_PorEmpresa.sql          >>> SCRIPT MANUAL <<<
   Supuestos del balance del ppto: dbo.Rel_BalancePpto pasa de catálogo global
   a datos POR EMPRESA (IdEmpresa + trazabilidad de carga/modificación).

   Qué hace (idempotente, SQL Server 2012):
     1. Agrega IdEmpresa, IdUsuarioCarga, FechaCarga, IdUsuarioModificacion, FechaModificacion.
     2. Respaldo de la tabla actual (Rel_BalancePpto_Respaldo_038).
     3. Las filas actuales (globales) se asignan a la empresa ULTRA; las demás empresas
        reciben la MISMA estructura con Valor = NULL (las filas CALCULO no llevan valor).
     4. IdEmpresa NOT NULL + índice único (IdEmpresa, Id, Mes).
     5. dbo.sp_InicializarSupuestosBalancePpto: crea la estructura (Valor NULL) de una empresa
        (la usa la app al crear una empresa y el botón «Inicializar»).
     6. Opción de menú ADMIN_CONFIG_BALANCE_PPTO («Supuestos del balance del ppto»).

   ANTES DE EJECUTAR: verifique con
        SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Rel_BalancePpto';
   que existen Id, Mes, Valor y TipoUnidad (la app los usa por nombre).
   Ejecutar DESPUÉS 039_sp_ModeloBalancePpto_PorEmpresa.sql y luego desplegar la app.
   ===================================================================== */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Rel_BalancePpto','U') IS NULL
BEGIN
    RAISERROR('No existe dbo.Rel_BalancePpto.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 1. Columnas nuevas ---------------------------------------------------
IF COL_LENGTH('dbo.Rel_BalancePpto','IdEmpresa') IS NULL
    ALTER TABLE dbo.Rel_BalancePpto ADD IdEmpresa INT NULL;
IF COL_LENGTH('dbo.Rel_BalancePpto','IdUsuarioCarga') IS NULL
    ALTER TABLE dbo.Rel_BalancePpto ADD IdUsuarioCarga INT NULL;
IF COL_LENGTH('dbo.Rel_BalancePpto','FechaCarga') IS NULL
    ALTER TABLE dbo.Rel_BalancePpto ADD FechaCarga DATETIME NULL;
IF COL_LENGTH('dbo.Rel_BalancePpto','IdUsuarioModificacion') IS NULL
    ALTER TABLE dbo.Rel_BalancePpto ADD IdUsuarioModificacion INT NULL;
IF COL_LENGTH('dbo.Rel_BalancePpto','FechaModificacion') IS NULL
    ALTER TABLE dbo.Rel_BalancePpto ADD FechaModificacion DATETIME NULL;
GO

-- 2. Respaldo (una sola vez) ---------------------------------------------
IF OBJECT_ID('dbo.Rel_BalancePpto_Respaldo_038','U') IS NULL
    SELECT * INTO dbo.Rel_BalancePpto_Respaldo_038 FROM dbo.Rel_BalancePpto;
GO

-- 3. Asignar lo existente a ULTRA y clonar la estructura (Valor NULL) al resto
DECLARE @IdUltra INT, @n INT;
SELECT @n = COUNT(*) FROM dbo.Empresas WHERE EmpNombre LIKE 'Ultra%';
IF @n = 1
    SELECT @IdUltra = EmpId FROM dbo.Empresas WHERE EmpNombre LIKE 'Ultra%';

IF EXISTS (SELECT 1 FROM dbo.Rel_BalancePpto WHERE IdEmpresa IS NULL)
BEGIN
    IF @IdUltra IS NULL
    BEGIN
        RAISERROR('No se encontró una única empresa "Ultra%%" (encontradas: %d). Asigne @IdUltra manualmente y vuelva a ejecutar.', 16, 1, @n);
        RETURN;
    END

    UPDATE dbo.Rel_BalancePpto
    SET    IdEmpresa = @IdUltra, FechaCarga = ISNULL(FechaCarga, GETDATE())
    WHERE  IdEmpresa IS NULL;
END

-- Columnas de "estructura" = todas menos Valor, identity y las de trazabilidad
DECLARE @cols NVARCHAR(MAX) = STUFF((
    SELECT ', ' + QUOTENAME(c.COLUMN_NAME)
    FROM   INFORMATION_SCHEMA.COLUMNS c
    WHERE  c.TABLE_NAME = 'Rel_BalancePpto' AND c.TABLE_SCHEMA = 'dbo'
      AND  c.COLUMN_NAME NOT IN ('IdEmpresa','IdUsuarioCarga','FechaCarga','IdUsuarioModificacion','FechaModificacion','Valor')
      AND  COLUMNPROPERTY(OBJECT_ID('dbo.Rel_BalancePpto'), c.COLUMN_NAME, 'IsIdentity') = 0
      AND  COLUMNPROPERTY(OBJECT_ID('dbo.Rel_BalancePpto'), c.COLUMN_NAME, 'IsComputed') = 0
    ORDER BY c.ORDINAL_POSITION
    FOR XML PATH('')), 1, 2, '');

DECLARE @ref INT = (SELECT TOP 1 IdEmpresa FROM dbo.Rel_BalancePpto WHERE IdEmpresa IS NOT NULL ORDER BY CASE WHEN IdEmpresa = @IdUltra THEN 0 ELSE 1 END, IdEmpresa);
DECLARE @sql NVARCHAR(MAX) =
    N'INSERT INTO dbo.Rel_BalancePpto (' + @cols + N', Valor, IdEmpresa, FechaCarga)
      SELECT ' + @cols + N', NULL, E.EmpId, GETDATE()
      FROM   dbo.Rel_BalancePpto R
             CROSS JOIN dbo.Empresas E
      WHERE  R.IdEmpresa = @ref
        AND  NOT EXISTS (SELECT 1 FROM dbo.Rel_BalancePpto X WHERE X.IdEmpresa = E.EmpId);';
IF @ref IS NOT NULL
    EXEC sp_executesql @sql, N'@ref INT', @ref = @ref;
GO

-- 4. NOT NULL + índices ---------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Rel_BalancePpto WHERE IdEmpresa IS NULL)
   AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Rel_BalancePpto' AND COLUMN_NAME='IdEmpresa' AND IS_NULLABLE='YES')
    ALTER TABLE dbo.Rel_BalancePpto ALTER COLUMN IdEmpresa INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Rel_BalancePpto_Empresa_Id_Mes' AND object_id=OBJECT_ID('dbo.Rel_BalancePpto'))
    CREATE UNIQUE INDEX UX_Rel_BalancePpto_Empresa_Id_Mes ON dbo.Rel_BalancePpto (IdEmpresa, Id, Mes);
GO

-- 5. Inicialización de una empresa (estructura con Valor NULL) ---------------
IF OBJECT_ID('dbo.sp_InicializarSupuestosBalancePpto','P') IS NOT NULL DROP PROCEDURE dbo.sp_InicializarSupuestosBalancePpto;
GO
CREATE PROCEDURE dbo.sp_InicializarSupuestosBalancePpto
    @IdEmpresa INT, @IdUsuario INT = NULL, @Filas INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Filas = 0;
    IF EXISTS (SELECT 1 FROM dbo.Rel_BalancePpto WHERE IdEmpresa = @IdEmpresa) RETURN;  -- ya tiene

    DECLARE @ref INT = (SELECT TOP 1 IdEmpresa FROM dbo.Rel_BalancePpto WHERE IdEmpresa <> @IdEmpresa ORDER BY IdEmpresa);
    IF @ref IS NULL RETURN;

    DECLARE @cols NVARCHAR(MAX) = STUFF((
        SELECT ', ' + QUOTENAME(c.COLUMN_NAME)
        FROM   INFORMATION_SCHEMA.COLUMNS c
        WHERE  c.TABLE_NAME = 'Rel_BalancePpto' AND c.TABLE_SCHEMA = 'dbo'
          AND  c.COLUMN_NAME NOT IN ('IdEmpresa','IdUsuarioCarga','FechaCarga','IdUsuarioModificacion','FechaModificacion','Valor')
          AND  COLUMNPROPERTY(OBJECT_ID('dbo.Rel_BalancePpto'), c.COLUMN_NAME, 'IsIdentity') = 0
          AND  COLUMNPROPERTY(OBJECT_ID('dbo.Rel_BalancePpto'), c.COLUMN_NAME, 'IsComputed') = 0
        ORDER BY c.ORDINAL_POSITION
        FOR XML PATH('')), 1, 2, '');

    DECLARE @sql NVARCHAR(MAX) =
        N'INSERT INTO dbo.Rel_BalancePpto (' + @cols + N', Valor, IdEmpresa, IdUsuarioCarga, FechaCarga)
          SELECT ' + @cols + N', NULL, @e, @u, GETDATE()
          FROM   dbo.Rel_BalancePpto WHERE IdEmpresa = @ref;
          SET @n = @@ROWCOUNT;';
    EXEC sp_executesql @sql, N'@e INT, @u INT, @ref INT, @n INT OUTPUT', @e = @IdEmpresa, @u = @IdUsuario, @ref = @ref, @n = @Filas OUTPUT;
END
GO

-- 6. Opción de menú ----------------------------------------------------------
-- Verifique los nombres de columna de MenuOpciones en su BD (los de CLAUDE.md / PermisosService).
IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_CONFIG_BALANCE_PPTO')
BEGIN
    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo,
         IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    VALUES
        ('ADMIN_CONFIG_BALANCE_PPTO', N'Supuestos del balance del ppto', N'Budget balance sheet assumptions',
         N'Supuestos por empresa del balance presupuestado', 'fas fa-sliders-h', 95, 'SupuestosBalancePpto', 'Index', 1,
         NULL, (SELECT TOP 1 Id FROM dbo.CategoriasMenu WHERE Nombre = N'Configuración'), 0, 1, 0);
END
GO
SET NOEXEC OFF;
GO
