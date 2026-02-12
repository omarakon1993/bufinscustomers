-- ============================================================
-- MIGRACIÓN: Sidebar Dinámico basado en MenuOpciones
-- Fecha: 2026-02-11
-- Descripción: Agrega columnas para renderizado del sidebar,
--              actualiza las 11 opciones, y crea SPs nuevos.
-- ============================================================

-- ========== PASO 1A: Agregar columnas para renderizado del sidebar ==========

-- Verificar y agregar columna NombreGrupo
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MenuOpciones') AND name = 'NombreGrupo')
BEGIN
    ALTER TABLE MenuOpciones ADD NombreGrupo NVARCHAR(100) NULL;
END
GO

-- Verificar y agregar columna IconoGrupo
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MenuOpciones') AND name = 'IconoGrupo')
BEGIN
    ALTER TABLE MenuOpciones ADD IconoGrupo NVARCHAR(100) NULL;
END
GO

-- Verificar y agregar columna IconoCategoria
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MenuOpciones') AND name = 'IconoCategoria')
BEGIN
    ALTER TABLE MenuOpciones ADD IconoCategoria NVARCHAR(100) NULL;
END
GO

-- Verificar y agregar columna OrdenCategoria
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MenuOpciones') AND name = 'OrdenCategoria')
BEGIN
    ALTER TABLE MenuOpciones ADD OrdenCategoria INT DEFAULT 0;
END
GO

-- ========== PASO 1B: Actualizar las 11 opciones con datos del sidebar ==========

-- 1. DATOS_PLANTILLA_CARGUE
UPDATE MenuOpciones SET
    NombreGrupo = 'Plantilla',
    IconoGrupo = 'fas fa-file-excel',
    IconoCategoria = 'fas fa-database',
    OrdenCategoria = 1
WHERE Codigo = 'DATOS_PLANTILLA_CARGUE';

-- 2. DATOS_MODELO_EJECUCION
UPDATE MenuOpciones SET
    NombreGrupo = 'Modelo',
    IconoGrupo = 'fas fa-cogs',
    IconoCategoria = 'fas fa-database',
    OrdenCategoria = 1
WHERE Codigo = 'DATOS_MODELO_EJECUCION';

-- 3. INFORMES_REPORTES_PBI
UPDATE MenuOpciones SET
    NombreGrupo = 'Reportes',
    IconoGrupo = 'fas fa-chart-column',
    IconoCategoria = 'fas fa-chart-line',
    OrdenCategoria = 2
WHERE Codigo = 'INFORMES_REPORTES_PBI';

-- 4. INFORMES_AUDITORIA_CARGUES
UPDATE MenuOpciones SET
    NombreGrupo = 'Informes',
    IconoGrupo = 'fas fa-table',
    IconoCategoria = 'fas fa-chart-line',
    OrdenCategoria = 2
WHERE Codigo = 'INFORMES_AUDITORIA_CARGUES';

-- 5. INFORMES_TABLAS_DATOS
UPDATE MenuOpciones SET
    NombreGrupo = 'Informes',
    IconoGrupo = 'fas fa-table',
    IconoCategoria = 'fas fa-chart-line',
    OrdenCategoria = 2
WHERE Codigo = 'INFORMES_TABLAS_DATOS';

-- 6. INFORMES_RELACIONAMIENTOS
UPDATE MenuOpciones SET
    NombreGrupo = 'Informes',
    IconoGrupo = 'fas fa-table',
    IconoCategoria = 'fas fa-chart-line',
    OrdenCategoria = 2
WHERE Codigo = 'INFORMES_RELACIONAMIENTOS';

-- 7. ADMIN_USUARIOS_GESTOR
UPDATE MenuOpciones SET
    NombreGrupo = 'Usuarios',
    IconoGrupo = 'fas fa-user-plus',
    IconoCategoria = 'fas fa-cog',
    OrdenCategoria = 3
WHERE Codigo = 'ADMIN_USUARIOS_GESTOR';

-- 8. ADMIN_EMPRESAS_GESTOR - Hacer asignable (quitar SoloSuperAdmin)
UPDATE MenuOpciones SET
    NombreGrupo = 'Empresas',
    IconoGrupo = 'fas fa-building',
    IconoCategoria = 'fas fa-cog',
    OrdenCategoria = 3,
    SoloSuperAdmin = 0,
    NivelMinimo = 0
WHERE Codigo = 'ADMIN_EMPRESAS_GESTOR';

-- 9. ADMIN_REPORTES_GESTOR - Hacer asignable (quitar SoloSuperAdmin)
UPDATE MenuOpciones SET
    NombreGrupo = 'Reportes',
    IconoGrupo = 'fas fa-file-circle-plus',
    IconoCategoria = 'fas fa-cog',
    OrdenCategoria = 3,
    SoloSuperAdmin = 0,
    NivelMinimo = 0
WHERE Codigo = 'ADMIN_REPORTES_GESTOR';

-- 10. ADMIN_CONFIG_EMPRESAS
UPDATE MenuOpciones SET
    NombreGrupo = N'Configuración',
    IconoGrupo = 'fas fa-cogs',
    IconoCategoria = 'fas fa-cog',
    OrdenCategoria = 3
WHERE Codigo = 'ADMIN_CONFIG_EMPRESAS';

-- 11. ADMIN_CONFIG_RELACIONAMIENTOS
UPDATE MenuOpciones SET
    NombreGrupo = N'Configuración',
    IconoGrupo = 'fas fa-cogs',
    IconoCategoria = 'fas fa-cog',
    OrdenCategoria = 3
WHERE Codigo = 'ADMIN_CONFIG_RELACIONAMIENTOS';
GO

-- ========== PASO 1C: SP sp_ObtenerMenuUsuario ==========
-- Una sola consulta para obtener todas las opciones del sidebar del usuario

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
            mo.Icono, mo.Orden, mo.Controller, mo.[Action], mo.URL,
            mo.TienePadre, mo.IdPadre, mo.NivelMinimo, mo.SoloSuperAdmin, mo.Activo,
            mo.NombreGrupo, mo.IconoGrupo, mo.IconoCategoria, mo.OrdenCategoria
        FROM MenuOpciones mo
        WHERE mo.Activo = 1
        ORDER BY mo.OrdenCategoria, mo.Orden;
    END
    ELSE
    BEGIN
        -- Admin 0/1: devuelve solo las asignadas en UsuarioMenuPermisos
        SELECT
            mo.Id, mo.Codigo, mo.Nombre, mo.Descripcion, mo.Categoria,
            mo.Icono, mo.Orden, mo.Controller, mo.[Action], mo.URL,
            mo.TienePadre, mo.IdPadre, mo.NivelMinimo, mo.SoloSuperAdmin, mo.Activo,
            mo.NombreGrupo, mo.IconoGrupo, mo.IconoCategoria, mo.OrdenCategoria
        FROM MenuOpciones mo
        INNER JOIN UsuarioMenuPermisos ump ON mo.Id = ump.IdMenuOpcion
        WHERE ump.IdUsuario = @IdUsuario
          AND mo.Activo = 1
        ORDER BY mo.OrdenCategoria, mo.Orden;
    END
END
GO

-- ========== PASO 1D: SP sp_ObtenerCodigosPermisosUsuario ==========
-- Para cachear en sesión: lista de códigos de permisos asignados al usuario

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
      AND mo.Activo = 1;
END
GO

PRINT 'Migración SidebarDinamico_MenuOpciones completada exitosamente.';
GO
