-- ============================================================
-- MIGRACION: Gestor de Opciones de Menu + SoloSuperAdmin
-- Fecha: 2026-02-11
-- Descripcion: Agrega columna SoloSuperAdmin si no existe,
--              inserta opcion ADMIN_CONFIG_MENU, y recrea 3 SPs
--              para filtrar opciones SoloSuperAdmin para Admin 0/1.
-- ============================================================

USE bufinscustomers;
GO

-- ========== PASO 1: Agregar columna SoloSuperAdmin (si no existe) ==========

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MenuOpciones') AND name = 'SoloSuperAdmin')
BEGIN
    ALTER TABLE MenuOpciones ADD SoloSuperAdmin BIT NOT NULL DEFAULT 0;
    PRINT 'Columna SoloSuperAdmin agregada a MenuOpciones';
END
ELSE
BEGIN
    PRINT 'Columna SoloSuperAdmin ya existe en MenuOpciones';
END
GO

-- ========== PASO 2: Insertar opcion ADMIN_CONFIG_MENU ==========

IF NOT EXISTS (SELECT 1 FROM MenuOpciones WHERE Codigo = 'ADMIN_CONFIG_MENU')
BEGIN
    INSERT INTO MenuOpciones (Codigo, Nombre, Descripcion, Categoria, Icono, Orden, Controller, [Action], Activo, NombreGrupo, IconoGrupo, IconoCategoria, OrdenCategoria, SoloSuperAdmin)
    VALUES (
        'ADMIN_CONFIG_MENU',
        N'Gestor de Menú',
        N'Administración de opciones del menú del sistema',
        'ADMINISTRACION',
        'fas fa-bars',
        30,
        'MenuOpciones',
        'Index',
        1,
        N'Configuración',
        'fas fa-cogs',
        'fas fa-cog',
        3,
        1
    );
    PRINT 'Opcion ADMIN_CONFIG_MENU insertada';
END
ELSE
BEGIN
    PRINT 'Opcion ADMIN_CONFIG_MENU ya existe';
END
GO

-- ========== PASO 3: Recrear sp_ObtenerMenuUsuario ==========
-- Admin 2: todas las opciones activas (incluye SoloSuperAdmin)
-- Admin 0/1: solo asignadas Y SoloSuperAdmin = 0

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'sp_ObtenerMenuUsuario')
    DROP PROCEDURE sp_ObtenerMenuUsuario;
GO

CREATE PROCEDURE sp_ObtenerMenuUsuario
    @IdUsuario INT,
    @NivelAdmin TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    IF @NivelAdmin = 2
    BEGIN
        -- Super Admin: devuelve TODAS las opciones activas
        SELECT
            mo.Id, mo.Codigo, mo.Nombre, mo.Descripcion, mo.Categoria,
            mo.Icono, mo.Orden, mo.Controller, mo.[Action],
            mo.NombreGrupo, mo.IconoGrupo, mo.IconoCategoria, mo.OrdenCategoria,
            mo.SoloSuperAdmin
        FROM MenuOpciones mo
        WHERE mo.Activo = 1
        ORDER BY mo.OrdenCategoria, mo.Orden;
    END
    ELSE
    BEGIN
        -- Admin 0/1: solo asignadas y NO SoloSuperAdmin
        SELECT
            mo.Id, mo.Codigo, mo.Nombre, mo.Descripcion, mo.Categoria,
            mo.Icono, mo.Orden, mo.Controller, mo.[Action],
            mo.NombreGrupo, mo.IconoGrupo, mo.IconoCategoria, mo.OrdenCategoria,
            mo.SoloSuperAdmin
        FROM MenuOpciones mo
        INNER JOIN UsuarioMenuPermisos ump ON mo.Id = ump.IdMenuOpcion
        WHERE ump.IdUsuario = @IdUsuario
          AND mo.Activo = 1
          AND mo.SoloSuperAdmin = 0
        ORDER BY mo.OrdenCategoria, mo.Orden;
    END
END
GO

PRINT 'SP sp_ObtenerMenuUsuario recreado con filtro SoloSuperAdmin';
GO

-- ========== PASO 4: Recrear sp_ObtenerOpcionesMenuUsuarioConEstado ==========
-- Excluye SoloSuperAdmin = 1 de opciones asignables (solo Admin 0/1 se gestionan)

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'sp_ObtenerOpcionesMenuUsuarioConEstado')
    DROP PROCEDURE sp_ObtenerOpcionesMenuUsuarioConEstado;
GO

CREATE PROCEDURE sp_ObtenerOpcionesMenuUsuarioConEstado
    @IdUsuario INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        mo.Id,
        mo.Codigo,
        mo.Nombre,
        mo.Descripcion,
        mo.Categoria,
        mo.Icono,
        mo.Controller,
        mo.[Action],
        mo.NombreGrupo,
        mo.IconoGrupo,
        mo.IconoCategoria,
        mo.OrdenCategoria,
        CAST(CASE WHEN ump.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS Asignado
    FROM MenuOpciones mo
    LEFT JOIN UsuarioMenuPermisos ump ON mo.Id = ump.IdMenuOpcion AND ump.IdUsuario = @IdUsuario
    WHERE mo.Activo = 1
      AND mo.SoloSuperAdmin = 0
    ORDER BY mo.Categoria, mo.Orden;
END
GO

PRINT 'SP sp_ObtenerOpcionesMenuUsuarioConEstado recreado con filtro SoloSuperAdmin';
GO

-- ========== PASO 5: Recrear sp_ObtenerCodigosPermisosUsuario ==========
-- Excluye SoloSuperAdmin = 1 (Admin 2 nunca llama este SP, usa bypass)

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'sp_ObtenerCodigosPermisosUsuario')
    DROP PROCEDURE sp_ObtenerCodigosPermisosUsuario;
GO

CREATE PROCEDURE sp_ObtenerCodigosPermisosUsuario
    @IdUsuario INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT mo.Codigo
    FROM MenuOpciones mo
    INNER JOIN UsuarioMenuPermisos ump ON mo.Id = ump.IdMenuOpcion
    WHERE ump.IdUsuario = @IdUsuario
      AND mo.Activo = 1
      AND mo.SoloSuperAdmin = 0;
END
GO

PRINT 'SP sp_ObtenerCodigosPermisosUsuario recreado con filtro SoloSuperAdmin';
GO

PRINT '';
PRINT '=== Migracion GestorMenuOpciones completada ===';
GO
